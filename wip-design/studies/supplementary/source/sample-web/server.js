var express = require('express');
var app = express();
var port = process.env.port || 8089;

app.use('/', express.static(__dirname + '/web'));
var exposedNodeModules = [ 
  '/node_modules/requirejs',
  '/node_modules/monaco-editor',
  '/node_modules/thegamma-script',
  '/node_modules/babel-standalone' ];
  
for(var i=0; i<exposedNodeModules.length; i++) {
  var subdir = exposedNodeModules[i];
  app.use(subdir, express.static(__dirname + subdir));
}  
  
app.listen(port);

console.log('Listening on port: ', port);
