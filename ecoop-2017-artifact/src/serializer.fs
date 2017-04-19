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
  | Nested of endpoint:string

type Member = 
  | Property of name:string * returns:Result * trace:seq<string>

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

  let serializeMember = function
    | Property(n, r, t) ->
        [| "name", JsonValue.String n
           "returns", serializeResult r
           "trace", JsonValue.Array [| for s in t -> JsonValue.String s |] |] 
        |> JsonValue.Record

  let returnMembers members = 
    let json = [| for m in members -> serializeMember m |] |> JsonValue.Array
    json.ToString() |> Suave.Successful.OK
