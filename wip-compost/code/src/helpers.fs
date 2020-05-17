module Demo.Helpers

open Fable.Import.Browser
open Compost


let render out viz = 
  let el = document.getElementById(out)
  let svg = Compost.createSvg false false (el.clientWidth, el.clientHeight) viz
  svg |> Html.renderTo el

let renderAnim id init render update =
  Html.createVirtualDomApp id init render update

let svg id shape =
  let el = document.getElementById(id)
  Compost.createSvg false false (el.clientWidth, el.clientHeight) shape

let series d = Array.ofList [ for x, y in d -> unbox x, unbox y ]
let rnd = System.Random()
let inline numv v = COV(CO (float v))
let catv n s  = CAR(CA s, n)
let inline co v = CO(float v)
let ca s = CA(s)
