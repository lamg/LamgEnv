module Lamg.Env.Env

open System

let getEnv s =
  Environment.GetEnvironmentVariable s |> Option.ofObj

let getEnvF s =
  match getEnv s with
  | Some v -> v
  | None -> failwith $"environment variable {s} not found"

/// Require an environment variable.
let requireEnv (name: string) : Result<string, string> =
  match getEnv name with
  | Some v -> Ok v
  | None -> Error $"environment variable '{name}' is missing or empty"

/// Require all named environment variables; returns name → value map.
let requireEnvs (names: string list) : Result<Map<string, string>, string> =
  let missing = names |> List.filter (fun name -> getEnv name |> Option.isNone)

  if missing.IsEmpty then
    names
    |> List.map (fun name -> name, Option.get (getEnv name))
    |> Map.ofList
    |> Ok
  else
    let missingList = String.concat ", " missing
    Error $"environment variables missing or empty: {missingList}"

let setEnv (var, value) =
  Environment.SetEnvironmentVariable(var, value)

let nowRFC3339 () =
  DateTimeOffset.UtcNow.ToString "yyyy-MM-dd'T'HH:mm:ssK"

let nowUnix () =
  DateTimeOffset(DateTime.UtcNow).ToUnixTimeMilliseconds()

module Tests =
  open Swensen.Unquote.Assertions

  let private uniqueName () = $"LAMG_ENV_TEST_{Guid.NewGuid():N}"

  let ``getEnv returns None when missing`` () =
    let name = uniqueName ()
    Environment.SetEnvironmentVariable(name, null)
    test <@ getEnv name = None @>

  let ``getEnvF fails when variable is missing`` () =
    let name = uniqueName ()
    Environment.SetEnvironmentVariable(name, null)

    try
      getEnvF name |> ignore
      failwith "expected getEnvF to throw"
    with ex ->
      test <@ ex.Message.Contains name @>

  let ``setEnv and getEnv round-trip`` () =
    let name = uniqueName ()

    try
      setEnv (name, "value")
      test <@ getEnv name = Some "value" @>
    finally
      Environment.SetEnvironmentVariable(name, null)

  let ``requireEnvs collects values or lists missing`` () =
    let a = uniqueName ()
    let b = uniqueName ()

    try
      Environment.SetEnvironmentVariable(a, "one")
      Environment.SetEnvironmentVariable(b, "two")

      match requireEnvs [ a; b ] with
      | Ok map ->
        test <@ map[a] = "one" @>
        test <@ map[b] = "two" @>
      | Error e -> failwith e

      Environment.SetEnvironmentVariable(b, null)

      match requireEnvs [ a; b ] with
      | Error msg -> test <@ msg.Contains b @>
      | Ok _ -> failwith "expected missing env error"
    finally
      Environment.SetEnvironmentVariable(a, null)
      Environment.SetEnvironmentVariable(b, null)
