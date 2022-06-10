# The Gamma sample web

This project shows how to use [thegamma-script](https://www.npmjs.com/package/thegamma-script) 
NPM package to build open and reproducible data-driven visualizations. You can see the result
of running this [sample web live](http://thegamma-sample-web.azurewebsites.net/).

## Running the sample

Running the sample should be easy - it is a pure JavaScript node project, so you just need two commands:

```
npm install
npm run start
```

This should start a local server and open a browser with the sample web site!


## Using The Gamma in JavaScript

After referencing `thegamma-script` and `monaco-editor` (the web-based editor that we are 
extending), you can load all the packages using something like:

```html
<script src="/node_modules/requirejs/require.js"></script>
<script>
  require.config({
    paths:{'vs':'node_modules/monaco-editor/min/vs'},
    map:{ "*":{"monaco":"vs/editor/editor.main"}}
  });
  require(["monaco","node_modules/thegamma-script/dist/thegamma.js"], function (_,g) {
    // (...)
  });
</script>  
```

In practice, you can do something more clever (bundling etc.), but this is the simplest thing
that will work for now. Once The Gamma is loaded - in the `(...)` part - you can configure what
data sources are available, run The Gamma scripts and create the editor component.

In the sample, we use a service that provides Olympic medal data. We also specify 
libraries that are available to use in the user code:

```javascript
var services = "http://thegamma-services.azurewebsites.net/";      
var providers = 
  g.providers.createProviders({ 
    "libraries": g.providers.library("/node_modules/thegamma-script/dist/libraries.json"),
    "olympics": g.providers.pivot(services + "pdata/olympics") });
          
var ctx = g.gamma.createContext(providers);
```

The `g.providers` API lets you define two kinds of "type providers" that define what code
can users write in The Gamma editor:

 - The `library` provider takes a JSON that specifies the types and structure of JavaScript 
   libraries - the `thegamma-script` package comes with a couple of wrappers for Google Charts
   and for generating tables that you can see in the [Olympic Medallists demo](http://rio2016.thegamma.net/).
   You can create your own too, but it's not documented yet...
   
 - The `pivot` provider takes a service that can evaluate "data aggregation" requests.
   The above uses a [sample implementation](https://github.com/the-gamma/thegamma-services/blob/master/src/pdata/server.fsx).
   The service reports the columns available in the data source, so you do not 
   need to provide those explicitly. The provider then lets you write data 
   aggregations and transformations using `.` as in the example below.
   
Here, the first (larger) block uses the `pivot` provider and the second one uses the `library` provider (`chart`):

```
let data = olympics
  .'group data'.'by Athlete'.'sum Gold'.then
  .'sort data'.'by Gold descending'.then
  .paging.take(10)
  .'get series'.'with key Athlete'.'and value Gold'
  
chart.column(data)
```

Before we run any code, it is a good idea to register an error handler that will be called when
the executed script contains errors. You can use this to display code errors to the user:

```javascript
ctx.errorsReported(function (errs) { 
  for(var i = 0; i < errs.length; i++) {
    var e = errs[i];
    console.log("error " + e.number + " at line " +
      e.startLine + " col " + e.startColumn + ": " +
      e.message);
  }
});
```

Once we configured The Gamma providers, we can run the code - assuming `code` contains the above 
snippet and `out1` is an ID of an element on a page, the following runs the code and renders
chart into `out1`:

```javascript
ctx.evaluate(code, "out1");
```

The other feature that is currently exposed is creating an editor that lets users modify code snippets.
To create the editor, we first need to provide options. The available options are [defined in this F#
record](https://github.com/the-gamma/thegamma-script/blob/master/src/main/main.fsx#L465). Then you just
need to call `createEditor` function and give it an ID of a HTML element to use:

```javascript
var opts =
  { height: document.getElementById("sizer").clientHeight - 100,
    width: document.getElementById("sizer").clientWidth - 40,
    monacoOptions: function(m) {
      m.fontFamily = "Inconsolata";
      m.fontSize = 15;
      m.lineHeight = 20;
      m.lineNumbers = false;
    } };

ctx.createEditor("ed1", code, opts);
```
