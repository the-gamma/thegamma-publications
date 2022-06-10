var app = require('express')();
var http = require('http').createServer(app);
var api = require('./neo4j');
const fs = require('fs');

function ConvertToCSV(objArray,keys) {
  var array = typeof objArray != 'object' ? JSON.parse(objArray) : objArray;
  var str = '';
  var lines = '';
  for (var key in keys) {
    lines += keys[key] +','
  }
  lines = lines.substring(0, lines.length - 1);
  str += lines + '\r\n';
  for (var i = 0; i < array.length; i++) {
      var line = '';
      for (var index in array[i]) {
          if (line != '') line += ','
          if (typeof(array[i][index]) == typeof("") && array[i][index].includes(",") ) {
            line += array[i][index].replace(/,/g, ";"); // to fix the issue with comma in string, I replace all the comma by ;
          }else {
            line += array[i][index];
          }

      }

        str += line + '\r\n';
      }
  return str;
}


app.use(function(req, res, next) {
  res.header("Access-Control-Allow-Origin", "*");
  res.header("Access-Control-Allow-Headers", "Origin, X-Requested-With, Content-Type, Accept");
  next();
});

app.get('/drWho', function(req, res) {
    api.All_nodes().then(resultJson => {
      res.setHeader('Content-Type', 'text/plain');
      res.end(resultJson);
    });
});

app.get('/drWho/:trace/nodes_of_type/:type', function(req, res) {
    api.nodes_of_type(req.params.type, req.params.trace).then(resultJson => {
      res.setHeader('Content-Type', 'text/plain');
      res.end(resultJson);
    });
});

app.get('/drWho/:trace/links_from_node/:node_id', function(req, res) {
  api.links_from_node(req.params.node_id,  req.params.trace).then(resultJson => {
    res.setHeader('Content-Type', 'text/plain');
    res.end(resultJson);
  });
});

app.get('/drWho/:trace/links_from_any_node/:label_type', function(req, res) {
    api.links_from_any_node(req.params.label_type, req.params.trace).then(resultJson => {
      res.setHeader('Content-Type', 'text/plain');
      res.end(resultJson);
    });

});

app.get('/drWho/:trace/linked_from_node/:node_id/:relation', function(req, res) {
  if (req.params.node_id == 'any') {
    api.linked_any(req.params.relation, req.params.trace).then(resultJson => {
      res.setHeader('Content-Type', 'text/plain');
      res.end(resultJson);
    });
  }else {
    api.linked_from_node(req.params.node_id, req.params.relation, req.params.trace).then(resultJson => {
      res.setHeader('Content-Type', 'text/plain');
      res.end(resultJson);
    });
  }
});

app.get('/drWho/:trace/get_propertiescsv', function(req, res) {
  api.get_properties(req.params.trace).then(resultJson => {
    res.setHeader('Content-Type', 'text/plain');
    res.end(ConvertToCSV(resultJson[0], resultJson[1]));
  });
});




app.all('/drWho/get_properties_of_node', function(req, res) {
  var trace = "";
  req.setEncoding('utf8');
  req.on("data", function(chunk) { trace += chunk });
  req.on('end', function() {
    api.get_properties(trace).then(resultJson => {
      res.setHeader('Content-Type', 'text/plain');
      res.end(resultJson[0]);
    });
  });
});



var port = process.env.port || 8000;
http.listen(port, function(){
  console.log('Listening on port: ', port);
});
