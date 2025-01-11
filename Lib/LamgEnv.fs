module LamgEnv

open System

let getEnv s =
  Environment.GetEnvironmentVariable s |> Option.ofObj

let getEnvF s =
   match getEnv s with
   | Some v -> v
   | None -> failwith $"environment variable {s} not found"

let setEnv (var, value) =
  Environment.SetEnvironmentVariable(var, value)

let nowRFC3339 () =
  DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fffK")

let nowUnix () =
  DateTimeOffset(DateTime.UtcNow).ToUnixTimeMilliseconds()

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
