const neo4j = require('neo4j-driver').v1;
const fs = require('fs');
const _ = require('underscore');

var config = null;
try {
  config = JSON.parse(fs.readFileSync("config.json"))
} catch {
  config = {
    NEO4J_PASSWORD: process.env.NEO4J_PASSWORD,
    NEO4J_URL: process.env.NEO4J_URL,
    NEO4J_USER: process.env.NEO4J_USER,
    GRAPH_SERVICE: process.env.GRAPH_SERVICE,
    GALLERY_SERVICE: process.env.GALLERY_SERVICE }
}
const driver = neo4j.driver(config.NEO4J_URL, neo4j.auth.basic(config.NEO4J_USER, config.NEO4J_PASSWORD));
const session = driver.session();

function constructDataQuery(traceString) {
  const trace = traceString.split('&')
  var conditions = "n1.label = $label";
  var returns = "n1";
  var query = "(n1)";
  var args =
    { label:trace[0],
    }

  var numNodes = trace.length/2;
  for(var n = 1; n <= numNodes; n++)
  {
    var nodeName = trace[(n-1)*2+1];
    if (n > 1)
    {
      returns += ", n" + n;
      query += "-[r" + (n-1) + "]->"
      query += "(n" + n + ")";

      conditions += " AND type(r" + (n-1) + ") = $r" + (n-1) + "type"
      args["r" + (n-1) + "type"] = trace[(n-1)*2];

    }
    if (nodeName != "[any]") {
      conditions += " AND n" + n + ".name = $n" + n + "name"
      args["n" + n + "name"] = nodeName;
    }
  }
  return ['MATCH ' + query + ' WHERE ' + conditions + ' RETURN ' + returns, args];
}



function list_of_key_and_type(traceString){
  var [query, args] = constructDataQuery(traceString)
  var key = [];
  var type = [];
  const resultPromise = session.run(query, args);
  return resultPromise.then(result => {
    session.close();

    for(var res of result.records) {
      for(var i = 0; i < res._fields.length; i++) {
        for(var prop of Object.keys(res._fields[i].properties)) {
          key.push("node" + (i+1) + "." + prop);
          type.push(typeof(res._fields[i].properties[prop]));

        }
      }
    }
    type.forEach(function(item, i) { if (item == "object") type[i] = "float"; });
    let unique_key = [...new Set(key)];
    var unique_type = []
    for(var k of unique_key){
      unique_type.push(type[key.indexOf(k)]);
    }
    return [unique_key , unique_type];
  });


}


function add_label(){
  const resultPromise = session.run(
    'MATCH (n)  SET n.label = labels(n)[0]' ,
  );
}


function All_nodes(){
//--list all types of nodes
  const resultPromise = session.run(
    'MATCH (n) RETURN n',
  );

  return resultPromise.then(result => {
    session.close();
    var jsonString = ""
    for (var i = 0; i < result.records.length ; i++) {
      const singleRecord = result.records[i];
      const node = singleRecord.get(0);
      const node_obj = {
        name: node.labels[0],
        trace:[node.labels[0]],
        returns:{"kind":"nested","endpoint": "/" + node.labels[0] + "/nodes_of_type/"+node.labels},

      }
      jsonString += JSON.stringify(node_obj) + ";";

    }
    var arr =  jsonString.slice(0, jsonString.length-1);
    arr = arr.split(";");
    let unique = [...new Set(arr)];
    var nl = ""
    for (var i = 1; i < unique.length ; i++) {
      nl += unique[i] + ",";
    }
    jsonString = "[" + nl.slice(0, nl.length-1) + "]";
    return jsonString;
  }).catch(
       (reason) => {
            console.log('Handle rejected promise ('+reason+') here.');
        });
}



function nodes_of_type(label_type, trace){
//--list all nodes of type
  add_label();
  const resultPromise = session.run(
    'MATCH (n) WHERE n.label = $label RETURN n',
    {label: label_type}
  );
  return resultPromise.then(result => {
    session.close();
    var jsonArray = result.records.map(record => {
      const node = record.get(0);
      return {
        name: node.properties.name,
        returns:{"kind":"nested","endpoint":trace +"&" + node.properties.name + "/links_from_node/"+node.properties.name},
        trace:[node.properties.name]
      } });

    var any = {
      name: "[any]",
      returns:{"kind":"nested","endpoint":trace + "&[any]" +"/links_from_any_node/" + label_type},
      trace:["[any]"]
    };
    jsonArray.push(any);
    var jsonString = JSON.stringify(jsonArray);
    return jsonString;
  }).catch(
       (reason) => {
            console.log('Handle rejected promise ('+reason+') here.');
        });
}

function links_from_any_node(label_type, traceString){
  add_label();
  return list_of_key_and_type(traceString).then(res => {
    const key = res[0];
    const type = res[1];
    const resultPromise = session.run(
      'MATCH  (a {label: $label}) OPTIONAL MATCH (a {label:$label })-[r]-(b) return type(r), properties(a)',
      {label: label_type}
    );
    return resultPromise.then(result => {
      session.close();
      var propertie_string = [];
      for (var i = 0; i < key.length; i++) {
        obj = {
            name: key[i],
            type: type[i]
        };
        propertie_string.push(obj);
      }
      var jsonString = "";
      const prop_obj = {
        name: "get_properties",
        trace:[],
        returns:{
          kind:"primitive",
          type: {name:"seq", params:[
            { name:"record",
              fields: propertie_string
              } ]},
          endpoint:"/get_properties_of_node" }
      }
      jsonString += JSON.stringify(prop_obj) + ";";

      const url = config.GRAPH_SERVICE + "/drWho/" + traceString + "/get_propertiescsv"
      const explore_prop_obj = {
        name: "explore_properties",
        trace:[],
        returns:{
          kind:"nested",
          endpoint: config.GALLERY_SERVICE + "/providers/data/upload/" + encodeURIComponent(url) }
      }
      jsonString += JSON.stringify(explore_prop_obj) + ";";


        for (var i = 0; i < result.records.length; i++) {
          const singleRecord = result.records[i];
          const node = singleRecord.get(0);
          if (node != null) {
            const node_obj = {
              name: node,
              trace:[node],
              returns:{"kind":"nested","endpoint":traceString + "&" + node + "/linked_from_node/"+ "any" + "/"+ node},
            }
            jsonString += JSON.stringify(node_obj) + ";";
          }
        }


      var arr =  jsonString.slice(0, jsonString.length-1);
      arr = arr.split(";");
      let unique = [...new Set(arr)];
      var nl = "";
      for (var i = 0; i < unique.length; i++) {
        nl += unique[i] + ",";
      }
      jsonString = "[" + nl.slice(0, nl.length-1) + "]";
      return jsonString;
    }).catch(
         (reason) => {
              console.log('Handle rejected promise ('+reason+') here.');
          });

  });
}

function links_from_node(node_id, traceString){
  //--list all relations linking node with with something
  add_label();
  return list_of_key_and_type(traceString).then(res => {
    const key = res[0];
    const type = res[1];
    const resultPromise = session.run(
      'MATCH  (a {name: $name}) OPTIONAL MATCH (a {name:$name })-[r]-(b) return type(r), properties(a)',
      {name: node_id}
    );
    return resultPromise.then(result => {
      session.close();
      var propertie_string = [];
      for (var i = 0; i < Object.keys(result.records[0].get(1)).length; i++) {
        obj = {
            name: key[i],
            type: type[i]
        }
        propertie_string.push(obj);
      }
      var jsonString = "";
      const prop_obj = {
        name: "get_properties",
        trace:[],
        returns:{
          kind:"primitive",
          type: {name:"seq", params:[
            { name:"record",
              fields: propertie_string
              } ]},
          endpoint:"/get_properties_of_node" }
      }
      jsonString += JSON.stringify(prop_obj) + ";";

      const url = config.GRAPH_SERVICE + "/drWho/" + traceString + "/get_propertiescsv"
      const explore_prop_obj = {
        name: "explore_properties",
        trace:[],
        returns:{
          kind:"nested",
          endpoint:config.GALLERY_SERVICE + "/providers/data/upload/" + encodeURIComponent(url) }
      }
      jsonString += JSON.stringify(explore_prop_obj) + ";";


        for (var i = 0; i < result.records.length; i++) {
          const singleRecord = result.records[i];
          const node = singleRecord.get(0);
          if (node != null) {
            const node_obj = {
              name: node,
              trace:[node],
              returns:{"kind":"nested","endpoint":traceString + "&" + node +"/linked_from_node/"+ node_id + "/"+ node},
            }
            jsonString += JSON.stringify(node_obj) + ";";
          }
        }


      var arr =  jsonString.slice(0, jsonString.length-1);
      arr = arr.split(";");
      let unique = [...new Set(arr)];
      var nl = "";
      for (var i = 0; i < unique.length; i++) {
        nl += unique[i] + ",";
      }
      jsonString = "[" + nl.slice(0, nl.length-1) + "]";
      return jsonString;
    }).catch(
         (reason) => {
              console.log('Handle rejected promise ('+reason+') here.');
          });
  })


}

// linked from node marche pas a cause promise


function linked_any(link, trace){
  add_label();
  const resultPromise = session.run(
      'MATCH (a)-[r]-(b) WHERE type(r) = $link RETURN b, a.label',
      {link: link}
  );
  return resultPromise.then(result => {
    session.close();
    var jsonArray = result.records.map(record => {
      const node = record.get(0);
      return {
        id: node.identity.toNumber(),
        name: node.properties.name,
        properties: node.properties,
        trace:[node.properties.name],
        returns:{"kind":"nested","endpoint":trace + "&"+ node.properties.name + "/links_from_node/"+node.properties.name},
    } });
    var any = {
      name: "[any]",
      returns:{"kind":"nested","endpoint":trace + "&"+ "[any]" + "/links_from_any_node/" + result.records[0].get(1)},
      trace:["[any]"]
    };
    jsonArray.push(any);
    var cache = {};
    jsonArray = jsonArray.filter(function(elem,index,array){
	     return cache[elem.id]?0:cache[elem.id]=1;
     });
    var jsonString = JSON.stringify(jsonArray);
    return jsonString;
  }).catch(
       (reason) => {
            console.log('Handle rejected promise ('+reason+') here.');
        });
}



function linked_from_node(name, link, trace){
  //--list all nodes connected to by relation
  add_label();
  const resultPromise = session.run(
    'MATCH (a)-[r]-(b) WHERE type(r) = $link and a.name = $name RETURN b, a.label',
    {name: name, link: link}
  );
  return resultPromise.then(result => {
    session.close();
    var jsonArray = result.records.map(record => {
      const node = record.get(0);
      return {
        id: node.identity.toNumber(),
        name: node.properties.name,
        properties: node.properties,
        trace:[node.properties.name],
        returns:{"kind":"nested","endpoint":trace + "&" + node.properties.name + "/links_from_node/"+node.properties.name},
      } });
      var any = {
        name: "[any]",
        returns:{"kind":"nested","endpoint":trace + "&" + "[any]" +"/links_from_any_node/" + result.records[0].get(1)},
        trace:["[any]"]
      };
    jsonArray.push(any);
    var cache = {};
    jsonArray = jsonArray.filter(function(elem,index,array){
	     return cache[elem.id]?0:cache[elem.id]=1;
     });
    const jsonString = JSON.stringify(jsonArray);
    return jsonString;
  }).catch(
       (reason) => {
            console.log('Handle rejected promise ('+reason+') here.');
        });
}



function get_properties(traceString){
  return list_of_key_and_type(traceString).then(res => {
    const key = res[0];
    var [query, args] = constructDataQuery(traceString);
    const resultPromise = session.run(query, args);
    return resultPromise.then(result => {
      session.close();
      var key_label = [];
      for (var i = 0; i < key.length; i++) {
        key_label.push(key[i].split('.'));
      }
      var key_label_obj = [];
      for (var i = 0; i < key.length; i++) {
        var temp_obj = {
          'node' : key_label[i][0],
          'key' : key_label[i][1],
        };
        key_label_obj.push(temp_obj);
      }
      var grouped = _.mapObject(_.groupBy(key_label_obj, 'node'),
                          clist => clist.map(a => _.omit(a, 'node')));
      key_label = [];
      for (var i = 0; i < Object.keys(grouped).length; i++) {
        var temp_arr = [];
        for (var j = 0; j < grouped[Object.keys(grouped)[i]].length; j++) {
          temp_arr.push(grouped[Object.keys(grouped)[i]][j].key);
        }
        key_label.push(temp_arr);

      }


      var jsonArray = result.records.map(record => {
        var obj = {};
        for (var j = 0; j < record.keys.length; j++) {

          for (var n in key_label[j]) {
            var prop_name_list = Object.keys(record._fields[j].properties);
            var arr = key_label[j];
            for(var i = 0, len = arr.length; i < len; i++) {
              arr[i] = arr[i].replace(record.keys[j].replace(/n/g, "node") + ".", '');
            }
            if (prop_name_list.includes(arr[n])) {
              if (typeof(record._fields[j].properties[arr[n]]) == typeof({})) {
                obj[j+"-"+ key_label[j][n]] = record._fields[j].properties[arr[n]].toNumber();
              } else {
                obj[j+"-"+key_label[j][n]] = record._fields[j].properties[arr[n]];

              }
            } else {
              obj[j+"-"+key_label[j][n]] = 0 ;

            }
          }
        }

        return obj;
    });
      //fs.writeFile("test2.json", JSON.stringify(jsonArray), function(err) { });
      return [JSON.stringify(jsonArray), Object.keys(jsonArray[0])];
;
  });
});

}



exports.linked_any = linked_any;
exports.links_from_any_node = links_from_any_node;
exports.get_properties = get_properties;
exports.All_nodes = All_nodes;
exports.nodes_of_type = nodes_of_type;
exports.links_from_node = links_from_node;
exports.linked_from_node = linked_from_node;
