module WebScrape.DataProviders

open System
open FSharp.Data
open FSharp.Data.Runtime

type ParsingContext = 
    { Headings : Map<string, string> 
      Rows : list<string[]> }

type ExploreTree = CsvProvider<"Heading 2, Heading 3, Item">
type ExploreDate = CsvProvider<"Type, Date, Entry">

let getTree url =
  let page = Http.RequestString(url, responseEncodingOverride="UTF-8")
  let doc = HtmlDocument.Parse(page)

  let specialHeadings = set ["h2";"h3"]
  let leafElements = set ["li"]

  let rec visitElement ctx (el:HtmlNode) =
    let name = el.Name()
    if specialHeadings.Contains name then
      { ctx with Headings = Map.add name (el.InnerText()) ctx.Headings }
    elif leafElements.Contains name then
      let row = [ for head in specialHeadings -> defaultArg (ctx.Headings.TryFind(head)) "" ]
      let row = row @ [ el.InnerText() ] |> Array.ofList
      { ctx with Rows = row::ctx.Rows }
    else
      el.Elements() |> Seq.fold visitElement ctx

  let emptyCtx = { Headings = Map.empty; Rows = [] }
  doc.Elements() |> Seq.fold visitElement emptyCtx 

let getWikiTree url =
  let page = Http.RequestString(url, responseEncodingOverride="UTF-8")
  let doc = HtmlDocument.Parse(page)

  let specialHeadings = set ["h2";"h3"]
  let leafElements = set ["li"]

  let rec visitElement ctx (el:HtmlNode) =
    let name = el.Name()
    if specialHeadings.Contains name then
      { ctx with Headings = Map.add name (el.InnerText()) ctx.Headings }
    elif leafElements.Contains name then
      let els = el.Descendants() |> Seq.filter (fun d -> d.Name() = "li")
      if Seq.isEmpty els then 
        // There are no child <li> elements
        let row = [ for head in specialHeadings -> defaultArg (ctx.Headings.TryFind(head)) "" ]
        let row = row @ [ el.InnerText() ] |> Array.ofList
        { ctx with Rows = row::ctx.Rows }
      else
        // There are some more nested <li> elements
        let prefix = 
          el.Elements() 
          |> Seq.filter (fun d -> d.Name() = "")
          |> Seq.map (fun d -> d.InnerText())
          |> String.concat " "

        els |> Seq.fold (fun ctx el ->
          let row = [ for head in specialHeadings -> defaultArg (ctx.Headings.TryFind(head)) "" ]
          let row = row @ [ prefix + " " + el.InnerText() ] |> Array.ofList
          { ctx with Rows = row::ctx.Rows }) ctx
    else
      el.Elements() |> Seq.fold visitElement ctx

  let emptyCtx = { Headings = Map.empty; Rows = [] }
  doc.Elements() |> Seq.fold visitElement emptyCtx 
  
let getAllEntries (url:string) =
  let resCtx = getTree url
  let csv = 
    [ for row in resCtx.Rows -> ExploreTree.Row(row.[0], row.[1], row.[2]) ]
    |> ExploreTree.GetSample().Append
  csv

let isMonth (monthName:string) =
    let months = ["January";"February";"March";"April";"May";"June";"July";"August";"September";"October";"November";"December"]
    let matchedMonth = months |> List.filter(fun x -> x.StartsWith(monthName))
    if (matchedMonth.IsEmpty) then
      None
    else Some(matchedMonth.[0]) 

let splitString (str:string) = 
  let rec loop i acc parts = 
    let stringify chars = System.String(Array.ofSeq (List.rev chars))
    if i = str.Length then  
      if List.isEmpty acc then parts else stringify acc::parts
    elif Seq.length parts = 2 then 
      let rest = str.[i..]
      if List.isEmpty acc then rest::parts else rest::stringify acc::parts
    elif Char.IsLetterOrDigit str.[i] then loop (i+1) (str.[i]::acc) parts
    else loop (i+1) [] (stringify acc::parts)
  loop 0 [] [] |> List.rev

let getDated yyyy (entries:string[] list) = 
  entries |> List.collect (fun entry -> 
    let listOfWords = splitString entry.[2]
    if Seq.length listOfWords >= 3 then
      let could1, option1 = System.Int32.TryParse(listOfWords.[0])
      if could1 then
        let dd = listOfWords.[0]
        let monthName = isMonth listOfWords.[1]
        match monthName with
        | Some x ->
          let mm = x
          let description = listOfWords.[2]
          let entryDateString = sprintf "%s/%s/%s" dd mm yyyy
          let could, entryDate = System.DateTime.TryParse(entryDateString)
          if could then 
            [entry.[0], entryDate.ToString(), description]
          else 
            []
        | None -> 
            []
      else 
        let could2, option2 = System.Int32.TryParse(listOfWords.[1]) 
        if could2 then
          let dd = listOfWords.[1]
          let monthName = isMonth listOfWords.[0]
          match monthName with
          | Some x ->
            let mm = x
            let description = listOfWords.[2]
            let entryDateString = sprintf "%s/%s/%s" dd mm yyyy
            let could, entryDate = System.DateTime.TryParse(entryDateString)
            if could then 
              [entry.[0], entryDate.ToString(), description]
            else 
              []
          | None -> 
              []
        else
          []
    else 
      []
  )
  
let getDatedEntries year (url:string) =
  let resCtx = getWikiTree url
  let datedEntries = getDated year resCtx.Rows
  let csv = 
    [ for typ, dt, e in datedEntries -> ExploreDate.Row(typ, dt, Regex.replace "\[[0-9]+\]" "" e)]
    |> ExploreDate.GetSample().Append
  csv
