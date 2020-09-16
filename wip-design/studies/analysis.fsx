#load "C:/Temp/nuget/packages/FsLab/FsLab.fsx"
open Deedle

System.Environment.CurrentDirectory <- __SOURCE_DIRECTORY__

// Drop personal data (used to link answers to participants)
let dfn = Frame.ReadCsv("questionnaire.csv")
dfn.DropColumn("Username")
dfn.SaveCsv("questionnaire.csv", includeRowKeys=false)

// Convert to correct Likert scale (1=strongly disagree, 5=strongly agree)
let df = Frame.ReadCsv("questionnaire.csv")
let dfl = df |> Frame.mapValues (fun (n:int) -> 6-n)

let results = frame [
  "Mean" => (dfl |> Stats.mean)
  "Sdv" => (dfl |> Stats.stdDev) ]
let rounded = results |> Frame.mapValues (fun (v:float) -> System.Math.Round(v, 2) )
rounded.SaveCsv("results.csv", keyNames=["Question"])
