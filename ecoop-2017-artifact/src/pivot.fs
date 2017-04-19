module Pivot

open System
open FSharp.Data
open Suave

// ------------------------------------------------------------------------------------------------
// Types for values and transformations
// ------------------------------------------------------------------------------------------------

type Value = 
  | String of string
  | Number of decimal

type Aggregation = 
  | GroupKey
  | CountAll
  | CountDistinct of string
  | ReturnUnique of string
  | ConcatValues of string
  | Sum of string
  | Mean of string

type SortDirection =
  | Ascending
  | Descending 

type Paging =
  | Take of int
  | Skip of int
  
type Transformation = 
  | DropColumns of string list
  | SortBy of (string * SortDirection) list
  | GroupBy of string list * Aggregation list
  | FilterBy of (string * bool * string) list
  | Paging of Paging list
  | Empty

type Action = 
  | Metadata
  | GetSeries of string * string
  | GetTheData 
  | GetRange of string

type Query = 
  { Transformations : Transformation list
    Action : Action }


// ------------------------------------------------------------------------------------------------
// Parsing transformations
// ------------------------------------------------------------------------------------------------

module Transform = 

  let ops = 
    [ "count-dist", CountDistinct; "unique", ReturnUnique; 
      "concat-vals", ConcatValues; "sum", Sum; "mean", Mean ]

  let trimIdent (s:string) = 
    if s.StartsWith("'") && s.EndsWith("'") then s.Substring(1, s.Length-2)
    else s

  let parseAggOp op =
    if op = "key" then GroupKey
    elif op = "count-all" then CountAll
    else
      let parsed = ops |> List.tryPick (fun (k, f) ->
        if op.StartsWith(k) then Some(f(trimIdent(op.Substring(k.Length + 1))))
        else None)
      if parsed.IsSome then parsed.Value else failwith "Unknonw operation"

  let parseAction (op, args) = 
    match op, args with
    | "metadata", [] -> Metadata, true
    | "series", [k; v] -> GetSeries(k, v), true
    | "range", [f] -> GetRange(f), true
    | _ -> GetTheData, false

  let parseCondition (cond:string) = 
    let cond = cond.Trim()
    let start = if cond.StartsWith("'") then cond.IndexOf('\'', 1) else 0
    let neq, eq = cond.IndexOf(" neq ", start), cond.IndexOf(" eq ", start)
    if neq <> -1 then trimIdent (cond.Substring(0, neq)), false, cond.Substring(neq + 5)
    elif eq <> -1 then trimIdent (cond.Substring(0, eq)), true, cond.Substring(eq + 4)
    else failwith "Incorrectly formatted condition"

  let parseTransform (op, args) = 
    match op, args with
    | "drop", columns -> DropColumns(List.map trimIdent columns)
    | "sort", columns -> SortBy(columns |> List.map (fun col -> 
        if col.EndsWith(" asc") then trimIdent (col.Substring(0, col.Length-4)), Ascending
        elif col.EndsWith(" desc") then trimIdent (col.Substring(0, col.Length-5)), Descending
        else trimIdent col, Ascending))
    | "filter", conds -> FilterBy(List.map parseCondition conds)
    | "groupby", ops ->
        let keys = ops |> List.takeWhile (fun s -> s.StartsWith "by ") |> List.map (fun s -> trimIdent (s.Substring(3)))
        let aggs = ops |> List.skipWhile (fun s -> s.StartsWith "by ") |> List.map parseAggOp
        GroupBy(keys, aggs)
    | "take", [n] -> Paging [Take (int n)]
    | "skip", [n] -> Paging [Skip (int n)]
    | _ -> failwith "Unsupported transformation"

  let parseArgs (s:string) = 
    let rec loop i quoted current acc = 
      let parseCurrent () = System.String(Array.ofList (List.rev current))
      if i = s.Length then List.rev (parseCurrent()::acc) else
      let c = s.[i]
      if c = '\'' && quoted then loop (i + 1) false (c::current) acc
      elif c = '\'' && not quoted then loop (i + 1) true (c::current) acc
      elif c = ',' && not quoted then loop (i + 1) quoted [] (parseCurrent()::acc)
      else loop (i + 1) quoted (c::current) acc
    loop 0 false [] [] 

  let parseChunk (s:string) =
    let openPar, closePar = s.IndexOf('('), s.LastIndexOf(')')
    if openPar = -1 || closePar = -1 then s, []
    else s.Substring(0, openPar), parseArgs (s.Substring(openPar + 1, closePar - openPar - 1))
    
  let fromUrl (s:string) = 
    let chunks = 
      System.Web.HttpUtility.UrlDecode(s)
        .Split([|'$'|], StringSplitOptions.RemoveEmptyEntries) 
      |> Array.map parseChunk 
    if chunks.Length = 0 then { Transformations = []; Action = GetTheData }
    else
      let action, explicit = parseAction (chunks.[chunks.Length - 1])
      let chunks = List.ofArray (if explicit then chunks.[0 .. chunks.Length - 2] else chunks)
      { Transformations = List.map parseTransform chunks; Action = action }    


// ----------------------------------------------------------------------------
// Evaluate query
// ----------------------------------------------------------------------------

let inline pickField name obj = 
  Array.pick (fun (n, v) -> if n = name then Some v else None) obj

let inline the s = match List.ofSeq s with [v] -> v | _ -> failwith "Not unique"
let asString = function String s -> s | Number n -> string n
let asDecimal = function String s -> decimal s | Number n -> n

let applyAggregation kvals group = function
 | GroupKey -> kvals
 | CountAll -> [ "count", Number(group |> Seq.length |> decimal) ]
 | CountDistinct(fld) -> [ fld, Number(group |> Seq.distinctBy (pickField fld) |> Seq.length |> decimal) ]
 | ReturnUnique(fld) -> [ fld, group |> Seq.map (pickField fld) |> the ]
 | ConcatValues(fld) -> [ fld, group |> Seq.map(fun obj -> pickField fld obj |> asString) |> Seq.distinct |> String.concat ", " |> String ]
 | Sum(fld) -> [ fld, group |> Seq.sumBy (fun obj -> pickField fld obj |> asDecimal) |> Number ]
 | Mean(fld) -> [ fld, group |> Seq.averageBy (fun obj -> pickField fld obj |> asDecimal) |> Number ]

let compareFields o1 o2 (fld, order) = 
  let reverse = if order = Descending then -1 else 1
  match pickField fld o1, pickField fld o2 with
  | Number d1, Number d2 -> reverse * compare d1 d2
  | String s1, String s2 -> reverse * compare s1 s2
  | _ -> failwith "Cannot compare values"

let transformData (objs:seq<(string * Value)[]>) = function
  | Empty -> objs
  | Paging(pgops) ->
      pgops |> Seq.fold (fun objs -> function
        | Take n -> objs |> Seq.truncate n
        | Skip n -> objs |> Seq.skip n) objs
  | DropColumns(flds) ->
      let dropped = set flds
      objs |> Seq.map (fun obj ->
        obj |> Array.filter (fst >> dropped.Contains >> not))
  | SortBy(flds) ->
      let flds = List.rev flds
      objs |> Seq.sortWith (fun o1 o2 ->
        let optRes = flds |> List.map (compareFields o1 o2) |> List.skipWhile ((=) 0) |> List.tryHead
        defaultArg optRes 0)
  | FilterBy(conds) ->
      conds |> List.fold (fun objs (fld, eq, value) ->
        objs |> Seq.filter (fun o -> 
          match pickField fld o with
          | String v -> v = value
          | Number n -> n = decimal value)) objs
  | GroupBy(flds, aggs) ->
      let aggs = List.rev aggs
      objs 
      |> Seq.groupBy (fun j -> List.map (fun f -> pickField f j) flds)
      |> Seq.map (fun (kvals, group) ->
        aggs 
        |> List.collect (applyAggregation (List.zip flds kvals) group)
        |> Array.ofSeq)

// ----------------------------------------------------------------------------
// Schema inference and CSV file parsing
// ----------------------------------------------------------------------------

exception ParseError of string

let parseError s = raise (ParseError s)
let isNumeric (s:string) = Decimal.TryParse(s) |> fst

let readCsvFile (data:string) =   
  if String.IsNullOrWhiteSpace data then parseError "The specified input was empty."
  let firstNewLine = data.IndexOfAny [|'\n'; '\r' |]
  let firstLine = if firstNewLine > 0 then data.Substring(0, firstNewLine) else data
  let separators = 
    if firstLine.Contains("\t") then "\t"
    elif firstLine.Contains(";") then ";"
    else ","
  let file = try CsvFile.Parse(data,separators) with _ -> parseError "Failed to parse data as a CSV file."
  if Seq.isEmpty file.Rows then parseError "The specified CSV file contains no data."
  let meta = 
    file.Rows 
    |> Seq.truncate 20
    |> Seq.map (fun r -> Array.map isNumeric r.Columns)
    |> Seq.reduce (Array.map2 (&&))
    |> Seq.zip file.Headers.Value
    |> Array.ofSeq
  
  let data = 
    file.Rows
    |> Seq.map (fun row -> 
      Seq.zip meta row.Columns 
      |> Seq.map (fun ((col, isNum), value) -> 
        if isNum then 
          try col, Value.Number(Decimal.Parse value)
          with _ -> parseError (sprintf "Column '%s' was inferred as numeric, but contians non-numeric value '%s'." col value)
        else col, Value.String(value))
      |> Array.ofSeq )
    |> Array.ofSeq

  meta |> Seq.map (fun (n, num) -> n, if num then "number" else "string") |> Array.ofSeq,
  data

// ----------------------------------------------------------------------------
// Pivot service
// ----------------------------------------------------------------------------

let serializeValue = function String s -> JsonValue.String s | Number n -> JsonValue.Number n

let serialize isPreview isSeries data = 
  let data = if isPreview then Array.truncate 10 data else data
  data 
  |> Array.map (fun (fields:_[]) ->
    if isSeries then
      JsonValue.Array [| serializeValue (snd fields.[0]); serializeValue (snd fields.[1]) |]
    else 
      fields |> Array.map (fun (k, v) -> k, serializeValue v) |> JsonValue.Record)
  |> JsonValue.Array

let applyAction isPreview meta objs = function
  | GetSeries(k, v) ->
      objs |> Seq.map (fun obj ->
        let kn, kval = Array.find (fst >> (=) k) obj
        let vn, vval = Array.find (fst >> (=) v) obj
        [| kn, kval; vn, vval |]) |> Array.ofSeq |> serialize isPreview true
  | GetTheData -> 
      objs |> serialize isPreview false
  | Metadata -> 
      JsonValue.Record [| for k, v in meta -> k, JsonValue.String v |]
  | GetRange(fld) ->
      objs |> Array.map (pickField fld) |> Array.distinct |> Array.map serializeValue |> JsonValue.Array

let handleRequest meta source query = 
  let source = source |> Seq.ofArray
  let preview, query = query |> List.partition ((=) "preview")
  let isPreview = not (List.isEmpty preview)
  let query = query |> List.head |> Transform.fromUrl
  let res = query.Transformations |> List.fold transformData source |> Array.ofSeq
  let json = applyAction isPreview meta res query.Action
  Successful.OK (json.ToString())  
