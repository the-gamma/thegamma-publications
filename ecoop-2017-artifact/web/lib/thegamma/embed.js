if (!thegammaInit) { var thegammaInit = false; }

function openDialog(id) {
  document.getElementById("thegamma-" + id + "-dialog").style.display="block";
  setTimeout(function() { 
    document.getElementById("thegamma-" + id + "-dialog").style.opacity=1;
    document.getElementById("thegamma-" + id + "-dialog-window").style.top="0px";
  },1);
}
function closeDialog(id) {
  document.getElementById("thegamma-" + id + "-dialog").style.opacity=0;
  document.getElementById("thegamma-" + id + "-dialog-window").style.top="-500px";
  setTimeout(function() { 
    document.getElementById("thegamma-" + id + "-dialog").style.display="none";
  },400)
}

function loadTheGamma() {
  require.config({
    paths:{'vs':'https://thegamma.net/lib/thegamma/vs'},
    map:{ "*":{"monaco":"vs/editor/editor.main"}}
  });
  require(["monaco", "https://thegamma.net/lib/thegamma/thegamma.js"], function (_, g) {      
    thegamma.forEach(function (id) {
      var services = "https://thegamma-services.azurewebsites.net/";      
      var providers = 
        g.providers.createProviders({ 
          "worldbank": g.providers.rest(services + "worldbank"),
          "libraries": g.providers.library("https://thegamma.net/lib/thegamma/libraries.json"),
          "olympics": g.providers.pivot(services + "pdata/olympics") });
        
      // Create context and setup error handler
      var ctx = g.gamma.createContext(providers);
      ctx.errorsReported(function (errs) { 
        var lis = errs.slice(0, 5).map(function (e) { 
          return "<li><span class='err'>error " + e.number + "</span>" +
            "<span class='loc'>at line " + e.startLine + " col " + e.startColumn + "</span>: " +
            e.message;
        });        
        var ul = "<ul>" + lis + "</ul>";
        document.getElementById("thegamma-" + id + "-errors").innerHTML = ul;
      });
      
      // Get and run default code, setup update handler
      var editor;
      var code = document.getElementById(id + "-code").innerHTML;
      ctx.evaluate(code, "thegamma-" + id + "-out");
      document.getElementById("thegamma-" + id + "-update").onclick = function() {
        ctx.evaluate(editor.getValue(), "thegamma-" + id + "-out");
        closeDialog(id);
        return false;
      }

      // Specify options and create the editor
      var opts =
        { height: document.getElementById("thegamma-" + id + "-sizer").clientHeight-130,
          width: document.getElementById("thegamma-" + id + "-sizer").clientWidth-20,
          monacoOptions: function(m) {
            m.fontFamily = "Inconsolata";
            m.fontSize = 15;
            m.lineHeight = 20;
            m.lineNumbers = false;
          } };
      editor = ctx.createEditor("thegamma-" + id + "-ed", code, opts);
    });
  });
}

function initTheGamma() {
  thegamma.forEach(function(id) {
    var el = document.getElementById(id);  
    el.innerHTML = 
      ('<div id="thegamma-[ID]-out"><p class="placeholder">Loading the visualization...</p></div>' +
      "<button onclick='openDialog(\"[ID]\")'>Edit source code</button>" +
      '<div id="thegamma-[ID]-sizer" class="thegamma-sizer"></div>' +
      '<div id="thegamma-[ID]-dialog" class="thegamma-dialog">' +
      '  <div id="thegamma-[ID]-dialog-window" class="thegamma-dialog-window">' +
      '  <div class="header"><a href="javascript:closeDialog(\'[ID]\');">&times;</a><span>Edit source code</span></div>' +
      '  <div class="body"><div id="thegamma-[ID]-ed"></div><div id="thegamma-[ID]-errors" class="errors"></div>' +
      '    <button id="thegamma-[ID]-update">Update page</button></div>' +
      '</div></div>').replace(/\[ID\]/g, id);
  });
  loadTheGamma();
}

if (!thegammaInit) { 
  thegammaInit=true; 
  var ol = window.onload;
  window.onload = function() { initTheGamma(); if (ol) ol(); };
  var link = '<link href="https://thegamma.net/lib/thegamma/thegamma.css" rel="stylesheet">';
  var heads = document.getElementsByTagName("head");
  if (heads.length > 0) heads[0].innerHTML += link;
  else document.write(link);
}
