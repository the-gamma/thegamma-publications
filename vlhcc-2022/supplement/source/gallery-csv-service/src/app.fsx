#if INTERACTIVE
#r "../packages/Suave/lib/net40/Suave.dll"
#r "../packages/FSharp.Data/lib/net40/FSharp.Data.dll"
#r "../packages/Newtonsoft.Json/lib/net40/Newtonsoft.Json.dll"
#load "../packages/FSharp.Azure.StorageTypeProvider/StorageTypeProvider.fsx"
#load "config.fs" "common/serializer.fs" "common/webscrape.fs" "pivot.fs" "storage.fs" "listing.fs" "data.fs" 
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
open WebScrape.DataProviders
// --------------------------------------------------------------------------------------
// Server with /upload endpoint and /csv for Pivot type provider
// --------------------------------------------------------------------------------------

#if INTERACTIVE
let root = "http://localhost:8897"
#else
let root = "https://gallery-csv-service.azurewebsites.net"
#endif

let app =
  choose [
    GET >=> path "/" >=> Successful.OK "Service is running..." 
    POST >=> path "/update" >=> Storage.Uploads.updateRecord
    POST >=> path "/upload" >=> Storage.Uploads.uploadFile
    GET >=> path "/tags" >=> request (fun _ ctx -> async {
        let! files = Storage.Uploads.getRecords ()
        let tags = 
          files 
          |> Seq.collect (fun f -> f.tags) 
          |> Seq.distinct
          |> Seq.map (fun t -> JsonValue.Array [| JsonValue.String (Listing.getTagId t); JsonValue.String t |])
          |> Array.ofSeq
          |> JsonValue.Array
        return! Successful.OK (tags.ToString()) ctx })
    setHeader  "Access-Control-Allow-Origin" "*"
    >=> setHeader "Access-Control-Allow-Headers" "content-type,x-cookie"
    >=> choose [
      OPTIONS >=> Successful.OK "CORS approved"
      GET >=> pathScan "/providers/data%s" (fun s -> 
        DataProviders.handleRequest root)
      GET >=> pathScan "/providers/listing%s" (fun _ ctx -> async { 
        let! files = Storage.Uploads.getRecords ()
        return! Listing.handleRequest root files ctx })
      GET >=> pathScan "/providers/csv/%s" (fun source ctx -> async {
        let! file = Storage.Uploads.fetchFile source
        match file with 
        | None -> return! RequestErrors.BAD_REQUEST (sprintf "File with id '%s' does not exist!" source) ctx
        | Some (meta, data) -> return! Pivot.handleRequest meta data (List.map fst ctx.request.query) ctx }) ]
  ]

// When port was specified, we start the app (in Azure), 
// otherwise we do nothing (it is hosted by 'build.fsx')
match System.Environment.GetCommandLineArgs() |> Seq.tryPick (fun s ->
    if s.StartsWith("port=") then Some(int(s.Substring("port=".Length)))
    else None ) with
| Some port ->
    let serverConfig =
      { Web.defaultConfig with
          maxContentLength = 1024 * 1024 * 50
          bindings = [ HttpBinding.createSimple HTTP "127.0.0.1" port ] }
    Web.startWebServer serverConfig app
| _ -> ()



