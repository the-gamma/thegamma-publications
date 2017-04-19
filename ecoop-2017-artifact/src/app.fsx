#if INTERACTIVE
#r "../packages/Suave/lib/net40/Suave.dll"
#r "../packages/FSharp.Data/lib/net40/FSharp.Data.dll"
#load "serializer.fs" "pivot.fs"
#else
module Gallery.App
#endif
open System
open System.IO

open Suave
open Suave.Filters
open Suave.Writers
open Suave.Operators
open FSharp.Data
open Gallery.CsvService

// --------------------------------------------------------------------------------------
// Server with /upload endpoint and /csv for Pivot type provider
// --------------------------------------------------------------------------------------

let meta, data = 
  let data = File.ReadAllText(__SOURCE_DIRECTORY__ + "/medals.csv")
  Pivot.readCsvFile data

let app =
  choose [
    GET >=> path "/olympics" >=> request (fun r ->
      Pivot.handleRequest meta data (List.map fst r.query) ) 
    GET >=> path "/" >=> Files.browseFileHome "index.html" 
    Files.browseHome
  ]

// When port was specified, we start the app (in Azure), 
// otherwise we do nothing (it is hosted by 'build.fsx')
let serverConfig =
  { Web.defaultConfig with
      homeFolder = Some(Path.GetFullPath(__SOURCE_DIRECTORY__ + "/../web"))
      logger = Logging.Loggers.saneDefaultsFor Logging.LogLevel.Debug
      bindings = [ HttpBinding.mkSimple HTTP "0.0.0.0" 80 ] }
async { Web.startWebServer serverConfig app } |> Async.Start
