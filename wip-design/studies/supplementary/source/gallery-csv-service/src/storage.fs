module Gallery.CsvService.Storage

open System
open System.Collections.Generic
open Newtonsoft.Json
open Microsoft.WindowsAzure.Storage
open Gallery.CsvService.Pivot

// --------------------------------------------------------------------------------------
// Saving and reading CSV files
// --------------------------------------------------------------------------------------

#if INTERACTIVE
let createCloudBlobClient() = 
  let account = CloudStorageAccount.Parse(Config.TheGammaGalleryCsvStorage)
  account.CreateCloudBlobClient()
#else
let createCloudBlobClient() = 
  let account = CloudStorageAccount.Parse(Environment.GetEnvironmentVariable("CUSTOMCONNSTR_THEGAMMACSV_STORAGE"))
  account.CreateCloudBlobClient()
#endif

let serializer = JsonSerializer.Create()

let toJson value = 
  let sb = System.Text.StringBuilder()
  use tw = new System.IO.StringWriter(sb)
  serializer.Serialize(tw, value)
  sb.ToString() 

let fromJson<'R> str : 'R = 
  use tr = new System.IO.StringReader(str)
  serializer.Deserialize(tr, typeof<'R>) :?> 'R

let generateId (date:System.DateTime) i = 
  sprintf "%s/file_%d.csv" (date.ToString("yyyy-MM-dd")) i

let uploadCsv container id data =
  printfn "upload CSV: %A %A (%d)" container id (Seq.length data)
  let container = createCloudBlobClient().GetContainerReference(container)
  if container.Exists() then
    let blob = container.GetBlockBlobReference(id)
    if blob.Exists() then blob.Delete() // failwithf "Blob '%s' already exists!" name
    blob.UploadText(data, System.Text.Encoding.UTF8) 
    id
  else failwith "Container 'uploads' not found"

let downloadCsv container id =
  printfn "download CSV: %A %A" container id
  let container = createCloudBlobClient().GetContainerReference(container)
  if container.Exists() then
    let blob = container.GetBlockBlobReference(id)
    if not (blob.Exists()) then None
    else Some(blob.DownloadText(System.Text.Encoding.UTF8))
  else failwith "Container 'uploads' not found"

let readMetadata<'T> container =
  let container = createCloudBlobClient().GetContainerReference(container)
  if container.Exists() then
    let blob = container.GetBlockBlobReference("files.json")
    if blob.Exists() then 
      blob.DownloadText(System.Text.Encoding.UTF8) |> fromJson<'T[]> 
    else [||]
  else failwith "Container 'uploads' not found" 

let writeMetadata container (files:'T[]) = 
  let json = files |> toJson
  let container = createCloudBlobClient().GetContainerReference(container)
  if container.Exists() then
    let blob = container.GetBlockBlobReference("files.json")
    blob.UploadText(json, System.Text.Encoding.UTF8)
  else failwith "container 'uploads' not found" 

// --------------------------------------------------------------------------------------
// Keep list of CSV files and cache recently accessed
// --------------------------------------------------------------------------------------

type ParsedFile = (string * string)[] * (string * Value)[][]

type Message<'T> = 
  | UploadFile of (string -> 'T) * string * AsyncReplyChannel<'T>
  | FetchFile of string * AsyncReplyChannel<option<ParsedFile>>
  | UpdateRecord of 'T
  | GetRecords of AsyncReplyChannel<'T[]>

let createCacheAgent<'T> container getId getPass : MailboxProcessor<Message<'T>> = MailboxProcessor.Start(fun inbox ->
  let worker () = async {
    let cache = new Dictionary<_, DateTime * _>()
    let files = new Dictionary<_, _>()
    for f in readMetadata container do files.Add(getId f, f)

    while true do
      let! msg = inbox.TryReceive(timeout=1000*60)
      let remove = [ for (KeyValue(k, (t, _))) in cache do if (DateTime.Now - t).TotalMinutes > 5. then yield k ]
      for k in remove do cache.Remove(k) |> ignore
      match msg with
      | None -> ()
      | Some(GetRecords ch) ->
          ch.Reply (Array.ofSeq files.Values)

      | Some(UpdateRecord(file)) ->
          if files.ContainsKey(getId file) && getPass (files.[getId file]) = (getPass file : string) then
            files.[getId file] <- file
            writeMetadata container (Array.ofSeq files.Values)

      | Some(UploadFile(createMeta, data, repl)) ->
          let meta = Seq.initInfinite (generateId DateTime.Today) |> Seq.filter (files.ContainsKey >> not) |> Seq.head |> createMeta
          if files.ContainsKey (getId meta) then repl.Reply(files.[getId meta]) else
          let csv = uploadCsv container (getId meta) data |> createMeta
          files.Add(getId csv, csv)
          writeMetadata container (Array.ofSeq files.Values)
          repl.Reply(csv)

      | Some(FetchFile(id, repl)) ->
          if not (files.ContainsKey id) then repl.Reply(None) else
          if not (cache.ContainsKey id) then
              match downloadCsv container id with
              | Some data -> cache.Add(id, (DateTime.Now, readCsvFile data))
              | None -> ()
          match cache.TryGetValue id with
          | true, (_, res) -> 
              cache.[id] <- (DateTime.Now, res)
              repl.Reply(Some res)
          | _ -> repl.Reply None }
  async { 
    while true do
      try return! worker ()
      with e -> printfn "Agent failed: %A" e })

// --------------------------------------------------------------------------------------
// Uploaded CSV file handling
// --------------------------------------------------------------------------------------

open Suave

type UploadedCsvFile = 
  { id : string 
    hidden : bool 
    date : DateTime
    source : string
    title : string
    description : string
    tags : string[] 
    passcode : string }
  static member Create(id) = 
    { id = id; hidden = true; date = DateTime.Today
      title = ""; source = ""; description = ""; tags = [||]; 
      passcode = System.Guid.NewGuid().ToString("N") }

let uploads = createCacheAgent "uploads" (fun csv -> csv.id) (fun csv -> csv.passcode)

module Uploads = 
  let fetchFile source = 
    uploads.PostAndAsyncReply(fun ch -> FetchFile(source, ch))

  let getRecords () =
    uploads.PostAndAsyncReply(GetRecords) 

  let updateRecord = request (fun r ->
    let file = fromJson<UploadedCsvFile> (System.Text.Encoding.UTF8.GetString(r.rawForm))
    uploads.Post(UpdateRecord file)
    Successful.OK "" )

  let uploadFile = (fun ctx -> async { 
    let data = System.Text.Encoding.UTF8.GetString(ctx.request.rawForm)
    try 
      ignore (readCsvFile data) 
      let! file = uploads.PostAndAsyncReply(fun ch -> UploadFile(UploadedCsvFile.Create, data, ch))
      return! Successful.OK (toJson file) ctx
    with ParseError msg -> 
      return! RequestErrors.BAD_REQUEST msg ctx })

// --------------------------------------------------------------------------------------
// Cached CSV file handling
// --------------------------------------------------------------------------------------

open Suave

type CachedCsvFile = 
  { id : string  
    url : string }

let cache = createCacheAgent "cache" (fun csv -> csv.id) (fun csv -> "")

module Cache = 
  let sha256 = System.Security.Cryptography.SHA256.Create()
  let hash url = 
    Uri(url).Host.Replace(".", "-") + "-" +
    ( sha256.ComputeHash(System.Text.UTF8Encoding.UTF8.GetBytes url) 
      |> Seq.map (fun s -> s.ToString("x2"))
      |> String.concat "" )

  let fetchFile id = 
    cache.PostAndAsyncReply(fun ch -> FetchFile(id, ch))

  let uploadFile url data kind = async { 
    try 
      ignore (readCsvFile data) 
      let mkmeta _ = { id = ((hash url)+kind); url = url ;}
      let! file = cache.PostAndAsyncReply(fun ch -> UploadFile(mkmeta, data, ch))
      return Choice1Of2 ((hash url)+kind)
    with ParseError msg -> 
      return Choice2Of2(msg) }
