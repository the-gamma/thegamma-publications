namespace Gallery.CsvService

open System
open System.IO
open System.Collections.Generic
open FSharp.Data

// ------------------------------------------------------------------------------------------------
//
// ------------------------------------------------------------------------------------------------

type Type =
  | Named of string
  | Seq of Type
  | Tuple of Type * Type
  | Record of (string * Type) list

type Result = 
  | Primitive of typ:Type * endpoint:string
  | Provider of kind:string * endpoint:string
  | Nested of endpoint:string

type Parameter = 
  | Parameter of name:string * typ:Type * optional:bool * kind:ParameterKind

and ParameterKind = 
  | Static of cookieName:string
  | Dynamic of traceKey:string

type Schema = 
  | Schema of context:string * typ:string * props:(string * JsonValue) list

type Member = 
  | Member of name:string * pars:Parameter list option * returns:Result * trace:seq<string> * schema:Schema list

module Serializer = 
  let rec serializeType = function
    | Type.Named n -> JsonValue.String n
    | Type.Record(flds) ->
        [| "name", JsonValue.String "record"
           "fields", JsonValue.Array [| 
              for n, t in flds -> 
                JsonValue.Record [|
                  "name", JsonValue.String n
                  "type", serializeType t |] |] |]
        |> JsonValue.Record
    | Type.Tuple(t1, t2) ->
        [| "name", JsonValue.String "tuple"
           "params", JsonValue.Array [| serializeType t1; serializeType t2 |] |]
        |> JsonValue.Record
    | Type.Seq(t) -> 
        [| "name", JsonValue.String "seq"
           "params", JsonValue.Array [| serializeType t |] |]
        |> JsonValue.Record
  
  let serializeResult = function
    | Result.Primitive(t, e) -> 
        [| "kind", JsonValue.String "primitive"
           "type", serializeType t
           "endpoint", JsonValue.String e |]
        |> JsonValue.Record
    | Result.Nested(e) -> 
        [| "kind", JsonValue.String "nested"
           "endpoint", JsonValue.String e |]
        |> JsonValue.Record
    | Result.Provider(kind, e) -> 
        [| "kind", JsonValue.String "provider"
           "provider", JsonValue.String kind
           "endpoint", JsonValue.String e |]
        |> JsonValue.Record

  let serializeParameter = function
    | Parameter(n, t, o, k) ->
        [| "name", JsonValue.String n
           "optional", JsonValue.Boolean o
           "kind", JsonValue.String (match k with Static _ -> "static" | Dynamic _ -> "dynamic")
           ( match k with Static ck -> "cookie" , JsonValue.String ck | Dynamic tk -> "trace", JsonValue.String tk )
           "type", serializeType t |]
        |> JsonValue.Record

      
  let serializeMember = function
    | Member(n, pars, r, t, schem) ->
        [| yield "name", JsonValue.String n
           yield "returns", serializeResult r
           match pars with
           | Some pars -> yield "parameters", JsonValue.Array (Array.map serializeParameter (Array.ofSeq pars))
           | _ -> ()
           match schem with 
           | [] -> ()
           | _ ->
              let schemas = schem |> Seq.map (fun (Schema(ctx, typ, props)) -> 
                JsonValue.Record
                  [| yield "@context", JsonValue.String ctx
                     yield "@type", JsonValue.String typ
                     yield! props |]) |> Array.ofSeq
              yield "schema", JsonValue.Array schemas
           yield "trace", JsonValue.Array [| for s in t -> JsonValue.String s |] |] 
        |> JsonValue.Record

  let returnMembers members = 
    let json = [| for m in members -> serializeMember m |] |> JsonValue.Array
    json.ToString() |> Suave.Successful.OK
