module Demo.Main

open Compost
open Compost.Html
open Demo.Helpers
open Fable.Import.Browser

// ----------------------------------------------------------------------------
// Some *fun* input data about British politics
// ----------------------------------------------------------------------------

// http://www.bankofengland.co.uk/boeapps/iadb/fromshowcolumns.asp?Travel=NIxRSxSUx&FromSeries=1&ToSeries=50&DAT=RNG&FD=1&FM=Jan&FY=2016&TD=1&TM=Jan&TY=2017&VFD=N&csv.x=17&csv.y=24&CSVF=TN&C=C8P&Filter=N

let elections = 
  [ "Conservative", "#1F77B4", 317, 365; "Labour", "#D62728", 262, 202; 
    "LibDem", "#FF7F0E", 12, 11; "SNP", "#BCBD22", 35, 48; 
    "Green", "#2CA02C", 1, 1; "DUP", "#8C564B", 10, 8 ]

let gbpusd = [ 1.3206; 1.3267; 1.312; 1.3114; 1.3116; 1.3122; 1.3085; 1.3211; 1.3175; 1.3136; 1.3286; 1.3231; 1.3323; 1.3215; 1.3186; 1.2987; 1.296; 1.2932; 1.2885; 1.3048; 1.3287; 1.327; 1.3429; 1.3523; 1.3322; 1.3152; 1.3621; 1.4798; 1.4687; 1.467; 1.4694; 1.4293; 1.4064; 1.4196; 1.4114; 1.4282; 1.4334; 1.4465; 1.4552; 1.456; 1.4464; 1.4517; 1.4447; 1.4414 ] |> List.rev
let gbpeur = [ 1.1823; 1.1867; 1.1838; 1.1936; 1.1944; 1.1961; 1.1917; 1.2017; 1.1969; 1.193; 1.2006; 1.1952; 1.1998; 1.1903; 1.1909; 1.1759; 1.1743; 1.168; 1.1639; 1.175; 1.1929; 1.192; 1.2081; 1.2177; 1.2054; 1.1986; 1.2254; 1.3039; 1.3018; 1.3018; 1.296; 1.2709; 1.2617; 1.2634; 1.2589; 1.2639; 1.2687; 1.2771; 1.2773; 1.2823; 1.2726; 1.2814; 1.2947; 1.2898 ] |> List.rev

// ----------------------------------------------------------------------------
// DEMO #1: Creating a bar chart
// ----------------------------------------------------------------------------

// TODO: 'Shape.Layered' of 'Derived.Column' (ca, co) 
// TODO: Add 'Derived.FillColor' and 'Shape.Padding'
// TODO: Add 'Shape.Axes'





// ----------------------------------------------------------------------------
// DEMO #2: Creating a colored line chart
// ----------------------------------------------------------------------------

// TODO: Create a line chart using 'Seq.indexed gbpusd' (numv i, numv v)
// TODO: Use 'Derived.StrokeColor' to make it black
// TODO: Add a 'Shape' (numv 0, numv 1) --> (numv 16, numv 1.8))
// TODO: Make it #aec7e8 using 'Derived.FillColor' 
// DEMO: Add second background box
// DEMO: Add axes using 'Shape.Axes(f, t, t, t, body)'

// DEMO: Create 'title' chart element
// TODO: Align with chart using OuterScale (Some(Continuous(co 0, co 100))





// ----------------------------------------------------------------------------
// DEMO #3: Refactoring
// ----------------------------------------------------------------------------

// TODO: Extract the 'Title' function




// ----------------------------------------------------------------------------
// DEMO #4: Creating a colored line chart
// ----------------------------------------------------------------------------

// TODO: Render simple barchart using 'renderAnim state render update' & 'svg'
// TODO: Define Start and Anim events, 'update' function and add button
// TODO: Scale data based on state, trigger timeout






let Title(text, chart) = 
  let title =
    Shape.InnerScale(Some(Continuous(co 0, co 100)), Some(Continuous(co 0, co 100)),
      Derived.Font("12pt arial", "black",
        Shape.Text(numv 50, numv 50, Middle, 
          Center, 0.0, text) ))

  Shape.Layered [  
    OuterScale(Some(Continuous(co 0, co 100)), Some(Continuous(co 0, co 15)), title)
    OuterScale(Some(Continuous(co 0, co 100)), Some(Continuous(co 15, co 100)), chart)
  ]

let adjust k (hex:string) =
  let r = System.Int32.Parse(hex.Substring(1, 2), System.Globalization.NumberStyles.HexNumber)
  let g = System.Int32.Parse(hex.Substring(3, 2), System.Globalization.NumberStyles.HexNumber)
  let b = System.Int32.Parse(hex.Substring(5, 2), System.Globalization.NumberStyles.HexNumber)
  let f n = min 255 (int (k * float n))
  "#" + ((f r <<< 16) + (f g <<< 8) + (f b)).ToString("X")
  
let partColumn f t x y = 
  Shape [ CAR(x, f), COV y; CAR(x, t), COV y; CAR(x, t), COV (CO f); CAR(x, f), COV (CO t) ]

let bars = 
  Shape.InnerScale(None, Some(Continuous(co 0, co 410)), Shape.Layered [ 
    for p, clr, s17, s19 in elections -> 
      Shape.Padding((0., 10., 0., 10.), 
        Shape.Layered [
          Derived.FillColor(adjust 0.8 clr, partColumn 0.0 0.5 (ca p) (co s17))
          Derived.FillColor(adjust 1.2 clr, partColumn 0.5 1.0 (ca p) (co s19))
        ]          
        ) ])

Title("United Kingdom general elections (2017 vs 2019)", Shape.Axes(false, false, true, true, bars)) |> render "out1"

let line data = 
  Shape.Line [
    for i, v in Seq.indexed data -> numv i, numv v
  ]

let body lo hi data = 
  Shape.Axes(false, true, true, true, Shape.Layered [
    Derived.FillColor("#1F77B460", 
      Shape.Shape [
        (numv 0, numv lo); (numv 16, numv lo); 
        (numv 16, numv hi); (numv 0, numv hi) ] )
    Derived.FillColor("#D6272860",
      Shape.Shape [ 
        (numv (List.length gbpusd - 1), numv lo); (numv 16, numv lo); 
        (numv 16, numv hi); (numv (List.length gbpusd - 1), numv hi) ] )
    Derived.StrokeColor("#202020", line data)
  ])

let chart = 
  Shape.Layered [
    Shape.OuterScale(None, Some(Continuous(co 0, co 50)), body 1.25 1.52 gbpusd)
    Shape.OuterScale(None, Some(Continuous(co 50, co 100)), body 1.15 1.32 gbpeur)
  ]

Title("GBP-USD and GBP-EUR rates (June-July 2016)", chart) |> render "out2"
chart |> render "out2"



let chart1 = 
  Shape.Layered [
    Shape.OuterScale(None, Some(Continuous(co 0, co 50)), 
      Shape.Axes(false, true, true, true, 
        Derived.StrokeColor("#202020", line gbpusd)))
    Shape.OuterScale(None, Some(Continuous(co 50, co 100)), 
      Shape.Axes(false, true, true, true, 
        Derived.StrokeColor("#202020", line gbpeur)))
  ]

chart1 |> render "out1"

(*
let bars1 : Shape<1,1> = 
  Shape.Layered [ 
    for p, clr, s17, s19 in Seq.take 2 elections -> 
      //Shape.Padding((0., 10., 0., 10.), 
      Derived.FillColor(adjust 1.2 clr, partColumn 0. 1. (ca p) (co s19))
        //Shape.Layered [
        //  Derived.FillColor(adjust 0.8 clr, partColumn 0.0 0.5 (ca p) (co s17))
        //  Derived.FillColor(adjust 1.2 clr, partColumn 0.5 1.0 (ca p) (co s19))
        //]          
        //] //) ]
  ]

Shape.Axes(false, false, true, true, bars1) |> render "out1"

let bars3 : Shape<1,1> = 
  Shape.InnerScale(Some(Categorical [|ca "Labour"; ca "Conservative"|]), Some(Continuous(co 0, co 420)), 
    Shape.Layered [ 
      for p, clr, s17, s19 in Seq.take 2 elections -> 
        //Shape.Padding((0., 10., 0., 10.), 
        Derived.FillColor(adjust 1.2 clr, partColumn 0. 1. (ca p) (co s19))
          //Shape.Layered [
          //  Derived.FillColor(adjust 0.8 clr, partColumn 0.0 0.5 (ca p) (co s17))
          //  Derived.FillColor(adjust 1.2 clr, partColumn 0.5 1.0 (ca p) (co s19))
          //]          
          //] //) ]
    ])

Shape.Axes(false, false, true, true, bars3) |> render "out2"

let bars2 : Shape<1,1> = 
  Shape.Layered [ 
    for p, clr, s17, s19 in elections -> 
      Shape.Padding((0., 10., 0., 10.), 
        Shape.Layered [
          Derived.FillColor(adjust 0.8 clr, partColumn 0.0 0.5 (ca p) (co s17))
          Derived.FillColor(adjust 1.2 clr, partColumn 0.5 1.0 (ca p) (co s19))
        ]) ] 
  

//Shape.Axes(false, false, true, true, bars2) |> render "out2"

let body2 lo hi data = 
  Shape.Axes(false, true, true, true, Shape.Layered [
    Derived.StrokeColor("#202020", line data)
  ])

//body2 0 0 gbpusd |> render "out2"




*)