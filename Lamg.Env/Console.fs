module Lamg.Env.Console

open System

let printColor (print: string -> unit) color s =
  let original = Console.ForegroundColor
  Console.ForegroundColor <- color
  print s
  Console.ForegroundColor <- original

let stdPrint x = printfn $"{x}"
let errPrint x = eprintfn $"{x}"

let printError s = printColor errPrint ConsoleColor.Red s

let printOk s =
  printColor stdPrint ConsoleColor.Green s

let printWarning s =
  printColor errPrint ConsoleColor.Yellow s

let printYellowIntro intro text =
  printColor (fun s -> printf $"{s}: ") ConsoleColor.Yellow intro
  printfn $"{text}"
