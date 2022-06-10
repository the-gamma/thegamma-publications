module Gallery.CsvService.Listing
open Gallery.CsvService

open Suave
open Suave.Filters
open Suave.Operators
open System
open Gallery.CsvService.Storage

let getTagId (s:string) =
  let rec loop (sb:System.Text.StringBuilder) dash i = 
    if i = s.Length then sb.ToString()
    elif Char.IsLetterOrDigit s.[i] then loop (sb.Append(Char.ToLower s.[i])) false (i+1)
    elif dash then loop sb true (i+1)
    else loop (sb.Append '-') true (i+1)
  loop (System.Text.StringBuilder()) true 0

let handleRequest root (files:UploadedCsvFile[]) = 
  let files = files |> Seq.filter (fun f -> not f.hidden) 
  let tags = files |> Seq.collect (fun f -> f.tags) |> Seq.map (fun t -> getTagId t, t) |> dict
  let dates = files |> Seq.map (fun f -> f.date.Year, f.date.Month) |> Seq.distinct |> Seq.sort
  choose [
    path "/providers/listing/" >=> request (fun r ->
      Serializer.returnMembers [        
        Member.Member("by date", None, Result.Nested("date/"), [], [])
        Member.Member("by tag", None, Result.Nested("tag/"), [], [])
      ])
    path "/providers/listing/date/" >=> request (fun r ->
      Serializer.returnMembers [
        for y, m in dates ->
          let name = System.Globalization.DateTimeFormatInfo.InvariantInfo.GetMonthName(m)
          Member.Member(name + " " + string y, None, Result.Nested("date/" + string y + "-" + string m + "/"), [], [])
      ])
    path "/providers/listing/tag/" >=> request (fun r ->
      Serializer.returnMembers [        
        for (KeyValue(tid, t)) in tags ->
          Member.Member(t, None, Result.Nested("tag/" + tid + "/"), [], [])
      ])
    pathScan "/providers/listing/date/%d-%d/" (fun (y, m) ->
      Serializer.returnMembers [
        for file in files do
          let show = file.date.Year = y && file.date.Month = m
          if show then yield Member.Member(file.title, None, Result.Provider("pivot", root + "/providers/csv/" + file.id), [], [])
      ])
    pathScan "/providers/listing/tag/%s/" (fun tid ->
      Serializer.returnMembers [
        for file in files do
          let show = file.tags |> Seq.exists (fun t -> getTagId t = tid)
          if show then yield Member.Member(file.title, None, Result.Provider("pivot", root + "/providers/csv/" + file.id), [], [])
      ])
  ]