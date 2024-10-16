#if INTERACTIVE
#I "../packages"
#r "Suave/lib/net40/Suave.dll"
#r "FSharp.Data/lib/net40/FSharp.Data.dll"
#r "DotLiquid/lib/net451/DotLiquid.dll"
#r "Suave.DotLiquid/lib/net40/Suave.DotLiquid.dll"
#r "Newtonsoft.Json/lib/net40/Newtonsoft.Json.dll"
#else
module OlympicsWeb
#endif

open Suave
open System
open System.Text.RegularExpressions
open Suave.Filters
open Suave.Operators
open Newtonsoft.Json
open FSharp.Data

#if INTERACTIVE
#load "config.fs"
let recaptchaSecret = Config.TheGammaRecaptcha
#else
let recaptchaSecret = Environment.GetEnvironmentVariable("RECAPTCHA_SECRET")
#endif

let (</>) a b = IO.Path.Combine(a, b)

let asm, debug = 
  if System.Reflection.Assembly.GetExecutingAssembly().IsDynamic then __SOURCE_DIRECTORY__, true
  else IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), false
let root = IO.Path.GetFullPath(asm </> ".." </> "web")
let templ = IO.Path.GetFullPath(asm </> ".." </> "templates")

// --------------------------------------------------------------------------------------
// Filters for the DotLiquid engine
// --------------------------------------------------------------------------------------

module Filters = 
  open System.Text.RegularExpressions

  let isHome (obj:System.Collections.IEnumerable) =
    (obj |> Seq.cast<obj> |> Seq.length) <= 8
  let jsEncode (s:string) = 
    System.Web.HttpUtility.JavaScriptStringEncode s
  let htmlEncode (s:string) = 
    System.Web.HttpUtility.HtmlEncode s
  let urlEncode (url:string) =
    System.Web.HttpUtility.UrlEncode(url)
  let mailEncode (url:string) =
    urlEncode(url).Replace("+", "%20")
  let cleanTitle (title:string) = 
    let t = Regex.Replace(title.ToLower(), "[^a-z0-9 ]", "")
    let t = Regex.Replace(t, " +", "-")
    urlEncode t
  let modTwo (n:int) = n % 2

  let niceDate (dt:DateTime) =
    let ts = DateTime.UtcNow - dt
    if ts.TotalSeconds < 0.0 then "just now"
    elif ts.TotalSeconds < 60.0 then sprintf "%d secs ago" (int ts.TotalSeconds)
    elif ts.TotalMinutes < 60.0 then sprintf "%d mins ago" (int ts.TotalMinutes)
    elif ts.TotalHours < 24.0 then sprintf "%d hours ago" (int ts.TotalHours)
    elif ts.TotalHours < 48.0 then sprintf "yesterday"
    elif ts.TotalDays < 30.0 then sprintf "%d days ago" (int ts.TotalDays)
    elif ts.TotalDays < 365.0 then sprintf "%d months ago" (int ts.TotalDays / 30)
    else sprintf "%d years ago" (int ts.TotalDays / 365)


// --------------------------------------------------------------------------------------
// Generally useful helpers
// --------------------------------------------------------------------------------------

let serializer = JsonSerializer.Create()

let fromJson<'R> str : 'R = 
  use tr = new System.IO.StringReader(str)
  serializer.Deserialize(tr, typeof<'R>) :?> 'R

let toJson value = 
  let sb = System.Text.StringBuilder()
  use tw = new System.IO.StringWriter(sb)
  serializer.Serialize(tw, value)
  sb.ToString() 
  
let (|Let|) v input = v, input

let (|Lookup|_|) k map = Map.tryFind k map
let (|NonEmpty|_|) v = if String.IsNullOrWhiteSpace(v) then None else Some v

// --------------------------------------------------------------------------------------
// Saving and loading snippets
// --------------------------------------------------------------------------------------

type Snippet = 
  { id : int
    likes : int
    posted : DateTime
    title : string
    description : string
    author : string
    twitter : string
    link : string
    code : string 
    version : string
    config : string
    hidden : bool }

type NewSnippet = 
  { title : string
    description : string
    author : string
    twitter : string
    link : string
    compiled : string
    code : string 
    hidden : bool
    config : string
    version : string }

let mutable currentVersion = 
  Http.RequestString("http://thegamma.net/lib/latest.txt")

let updateCurrentVersion ctx = async {
  let! v = Http.AsyncRequestString("http://thegamma.net/lib/latest.txt")
  currentVersion <- v 
  return Some ctx }

let readSnippets () = async {
  let! json = Http.AsyncRequestString("http://thegamma-snippets.azurewebsites.net/thegamma")
  let snips = 
    fromJson<Snippet[]> json 
    |> Array.sortByDescending (fun s -> s.posted) 
    |> Array.map (fun s -> 
      { s with 
            twitter = if String.IsNullOrWhiteSpace s.twitter then null else s.twitter
            link = if String.IsNullOrWhiteSpace s.link then null else s.link })
  return snips }

let postSnippet (snip:NewSnippet) = async {
  let! id = 
    Http.AsyncRequestString
      ( "http://thegamma-snippets.azurewebsites.net/thegamma", 
        httpMethod="POST",
        body=HttpRequestBody.TextRequest(toJson snip) )
  return 
    { Snippet.id = int id; likes = 0; posted = DateTime.UtcNow;
      title = snip.title; description = snip.description; 
      link = if String.IsNullOrWhiteSpace snip.link then null else snip.link
      twitter = if String.IsNullOrWhiteSpace snip.twitter then null else snip.twitter
      author = snip.author; code = snip.code; config = snip.config; 
      version = snip.version; hidden = snip.hidden } }

type SnippetMessage = 
  | GetSnippet of int * AsyncReplyChannel<Snippet option>
  | ListSnippets of int * AsyncReplyChannel<Snippet[]>
  | InsertSnippet of NewSnippet * AsyncReplyChannel<int>

let snippetAgent = MailboxProcessor.Start(fun inbox ->
  let rec loop snips = async {
    let! msg = inbox.Receive()
    match msg with
    | GetSnippet(id, ch) -> 
        ch.Reply(snips|> Array.tryFind (fun s -> s.id = id))
        return! loop snips
    | ListSnippets(max, ch) ->
        ch.Reply(snips |> Array.truncate max)
        return! loop snips
    | InsertSnippet(snip, ch) ->
        let! snip = postSnippet snip
        ch.Reply(snip.id)
        return! loop (Array.append [| snip |] snips) }
  async { 
    while true do
      try
        let! snips = readSnippets() 
        return! loop snips
      with e -> printfn "Agent has failed: %A" e })

// --------------------------------------------------------------------------------------
// Uploading data using the CSV service
// --------------------------------------------------------------------------------------

type CsvFile = 
  { id : string 
    hidden : bool 
    date : DateTime
    source : string
    title : string
    description : string
    tags : string[] 
    passcode : string }

let tags = MailboxProcessor.Start(fun inbox ->
  let rec loop (time:DateTime) tags = async {
    if (DateTime.Now - time).TotalSeconds > 300. then
      let! tags = Http.AsyncRequestString("http://gallery-csv-service.azurewebsites.net/tags", httpMethod="GET")
      let tags = fromJson<string[][]> tags |> Array.map Array.last
      return! loop DateTime.Now tags
    else
      let! (repl:AsyncReplyChannel<_>) = inbox.Receive()
      repl.Reply(tags)
      return! loop time tags }
  async {
    while true do 
      try return! loop DateTime.MinValue [||]
      with e -> printfn "Agent failed %A" e })

let updateFileInfo (csv:CsvFile) = 
  let data = toJson csv
  Http.RequestString
    ( "http://gallery-csv-service.azurewebsites.net/update", 
      httpMethod="POST", body=HttpRequestBody.TextRequest data,
      headers = [HttpRequestHeaders.ContentType "charset=utf-8"] ) |> ignore
  
let uploadData data =
  try
    Http.RequestString
      ( "http://gallery-csv-service.azurewebsites.net/upload", 
        httpMethod="POST", body=HttpRequestBody.TextRequest data, 
        headers = [HttpRequestHeaders.ContentType "charset=utf-8"] )
    |> fromJson<CsvFile>
    |> Choice1Of2
  with :? System.Net.WebException as e ->
    let idx = e.Message.IndexOf("upload:")
    Choice2Of2(if idx > -1 then e.Message.Substring(idx+7).Trim() else e.Message)

// --------------------------------------------------------------------------------------
// Insert page
// --------------------------------------------------------------------------------------

type RecaptchaResponse = JsonProvider<"""{"success":true}""">

/// Validates that reCAPTCHA has been entered properly
let validateRecaptcha form = async {
  let response = match form with Lookup "g-recaptcha-response" re -> re | _ -> ""
  let! response = 
      Http.AsyncRequestString
        ( "https://www.google.com/recaptcha/api/siteverify", httpMethod="POST", 
          body=HttpRequestBody.FormValues ["secret", recaptchaSecret; "response", response])
  return RecaptchaResponse.Parse(response).Success }

let error msg = 
  DotLiquid.page "error.html" msg >=> Writers.setStatus HttpCode.HTTP_400
    
let insertSnippetHandler form ctx = async {
  // Give up early if the reCAPTCHA was not correct
  let! valid = validateRecaptcha form
  if not valid then return! error """Human validation using ReCaptcha failed. Please ensure
    that your browser supports ReCaptcha and checkt the checkbox to verify you are a human.""" ctx
  else
    match form with
    | Lookup "title" (NonEmpty title) &
      Lookup "description" (NonEmpty descr) &
      Lookup "source" (NonEmpty source) &
      Lookup "author" (NonEmpty author) &
      Lookup "link" link & Lookup "twitter" twitter ->
        let version = defaultArg (form.TryFind("version")) currentVersion
        let newSnip = 
          { title = title; description = descr; author = author;
            twitter = twitter.TrimStart('@'); link = link; compiled = ""; code = source;
            hidden = false; config = defaultArg (form.TryFind "config") ""; version = version } 
        let! id = snippetAgent.PostAndAsyncReply(fun ch -> InsertSnippet(newSnip, ch))
        let url = sprintf "/%d/%s" id (Filters.cleanTitle title)
        return! Redirection.FOUND url ctx
    | _ ->
        return! error """Some of the inputs for the snippet were not valid, but the client-side 
          checking did not catch that. If you have JavaScript enabled and did not try to trick us,
          please consider opening an issue!""" ctx }

let insertPage ctx = async {
  let form = Map.ofSeq [ for k, v in ctx.request.form do if v.IsSome then yield k.ToLower(), v.Value ] 
  return! insertSnippetHandler form ctx }

// --------------------------------------------------------------------------------------
// Create pages
// --------------------------------------------------------------------------------------

type CreateModel =
  { PastedData : string 
    TransformSource : string
    DataTags : string[]
    VizSource : string
    ChartType : string
    UploadId : string
    UploadPasscode : string
    UploadError : string 
    CurrentVersion : string }

let createPage = updateCurrentVersion >=> request (fun r ->
  let inputs = 
    [ for f in r.files -> f.fieldName, System.IO.File.ReadAllText(f.tempFilePath) ]
    |> Seq.append r.multiPartFields
    |> Map.ofSeq 
    
  let model = 
    { DataTags = [||]
      VizSource = defaultArg (inputs.TryFind("viz-source")) "" 
      TransformSource = defaultArg (inputs.TryFind("transform-source")) ""
      ChartType = defaultArg (inputs.TryFind("chart-type")) "" 
      PastedData = defaultArg (inputs.TryFind("pasted-data")) ""
      UploadId = defaultArg (inputs.TryFind("upload-id")) ""
      UploadPasscode = defaultArg (inputs.TryFind("upload-passcode")) ""
      UploadError = defaultArg (inputs.TryFind("upload-error")) ""
      CurrentVersion = currentVersion  }

  match inputs with
  // Step 5: Submit a snippet!
  | Lookup "nextstep" "step5" ->
      let dstags = r.multiPartFields |> List.choose (function ("dstags", t) -> Some t | _ -> None) 
      let settings, source = 
        match inputs, dstags, model.UploadId, model.UploadPasscode with 
        | Lookup "dstitle" (NonEmpty title) &
          Lookup "dssource" (NonEmpty source) &
          Lookup "dsdescription" (NonEmpty description), (NonEmpty _ :: _), 
            NonEmpty id, NonEmpty passcode ->
            let csv = 
              { id = id; hidden = false; date = DateTime.UtcNow; source = source; passcode = passcode
                title = title.Replace("'", ""); description = description; tags = Array.ofList dstags }
            updateFileInfo csv
            let month = DateTime.UtcNow.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture)
            "", (model.VizSource.Replace("uploaded", sprintf "shared.'by date'.'%s'.'%s'" month csv.title))
        | _, _, NonEmpty uploadId, _ -> 
            sprintf """{ "providers": [ ["uploaded", "pivot", "https://gallery-csv-service.azurewebsites.net/providers/csv/%s"] ] }""" uploadId,
            model.VizSource
        | _ -> "", model.VizSource      
      inputs 
      |> Map.add "source" source
      |> Map.add "config" settings
      |> insertSnippetHandler

  // Step 4: Display page asking for snippet details
  | Lookup "nextstep" "step4" -> fun ctx -> async {
      let! tags = tags.PostAndAsyncReply id
      return! DotLiquid.page "create-step4.html" { model with DataTags = tags } ctx }
  
  // Step 3: Generate chart based on Step 2, or leave it as is when going back
  | Lookup "nextstep" "step3" ->
      let op = 
        match model.ChartType with 
        | "bar-chart" -> 
            "chart.bar(data)\n" + 
            "  .set(title=\"Enter chart title\", colors=[\"#77aae0\"])\n" + 
            "  .set(fontName=\"Roboto\", fontSize=13)\n" + 
            "  .legend(position=\"none\")"
        | "line-chart" -> 
            "chart.line(data.setProperties(seriesName=\"Enter data series description\"))\n" + 
            "  .set(title=\"Enter chart title\", colors=[\"#5588e0\"])\n" + 
            "  .set(fontName=\"Roboto\", fontSize=13)\n" +
            "  .legend(position=\"bottom\")"
        | _ -> 
            "table.create(data)"
      let model = 
        if not (inputs.ContainsKey "gotoviz") && not (String.IsNullOrWhiteSpace model.VizSource) then model
        else
          let src = "let data =" + Regex.Replace("\n" + model.TransformSource.Trim(), "[\r\n]+", "\n  ") + "\n\n" + op
          { model with VizSource = src }
      DotLiquid.page "create-step3.html" model

  // Step 2: Upload data from step 1 and display editor 
  | Lookup "nextstep" "step2" ->
      let skip, model = 
        match inputs with
        | Let true (uploaded, Lookup "uploadcsv" (NonEmpty data)) 
        | Let false (uploaded, Lookup "usepasted" _ & Lookup "uploaddata" (NonEmpty data)) -> 
            match uploadData data with
            | Choice2Of2 error -> 
                false,
                { model with 
                    PastedData = data
                    UploadError = error }
            | Choice1Of2 csv ->
                false,
                { model with 
                    PastedData = if uploaded then "" else data
                    TransformSource = "uploaded\n  .'drop columns'.then\n  .'get the data'" 
                    UploadId = csv.id; UploadPasscode = csv.passcode }                    
        | Lookup "usesample" _ & Lookup "samplesource" (NonEmpty sample) -> 
            false, { model with TransformSource = sample }
        | Lookup "skipsample" _ ->
            true, { model with VizSource = "chart.line(series.values([1,2,0]))" }
        | _ -> false, model
      if skip then DotLiquid.page "create-step3.html" model 
      elif model.UploadError = "" then DotLiquid.page "create-step2.html" model 
      else DotLiquid.page "create-step1.html" model 
      
  // Step 1: Display page for choosing inputs
  | _ -> DotLiquid.page "create-step1.html" model )

// --------------------------------------------------------------------------------------
// Web server
// --------------------------------------------------------------------------------------

let inits = Lazy.Create(fun () ->
  DotLiquid.setTemplatesDir templ 
  DotLiquid.setCSharpNamingConvention()
  System.Reflection.Assembly.GetExecutingAssembly().GetTypes()
  |> Seq.find (fun ty -> ty.Name = "Filters")
  |> DotLiquid.registerFiltersByType )  

let asyncPage page model ctx = async {
  let! model = model
  return! ctx |> DotLiquid.page page model }

let app = request (fun _ ->
  inits.Value
  choose [
    path "/" >=> asyncPage "home.html" (snippetAgent.PostAndAsyncReply(fun ch -> ListSnippets(8, ch)))
    path "/all" >=> asyncPage "home.html" (snippetAgent.PostAndAsyncReply(fun ch -> ListSnippets(Int32.MaxValue, ch)))
    path "/create" >=> createPage
    path "/insert" >=> insertPage
    pathScan "/%d/%s/embed" (fun (id, _) ctx -> async {
      let! snip = snippetAgent.PostAndAsyncReply(fun ch -> GetSnippet(id, ch))
      match snip with 
      | Some snip -> return! ctx |> DotLiquid.page "embed.html" snip
      | _ -> return! ctx |> (DotLiquid.page "404.html" null >=> Writers.setStatus HttpCode.HTTP_404) })
    pathScan "/%d/%s" (fun (id, _) ctx -> async {
      let! snip = snippetAgent.PostAndAsyncReply(fun ch -> GetSnippet(id, ch))
      match snip with 
      | Some snip -> return! ctx |> DotLiquid.page "snippet.html" snip
      | _ -> return! ctx |> (DotLiquid.page "404.html" null >=> Writers.setStatus HttpCode.HTTP_404) })
    Files.browse root ])
