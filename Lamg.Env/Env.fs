module Lamg.Env.Env

open System

/// <summary>
/// Read an environment variable with normalized value.
/// </summary>
/// <remarks>
/// Always trims surrounding whitespace. Returns <c>None</c> when the variable is unset,
/// empty, or whitespace-only. When <c>Some</c>, the string is already trimmed — do not
/// call <c>Trim()</c> or re-check for blank values after <c>getEnv</c>.
/// </remarks>
let getEnv s =
  match Environment.GetEnvironmentVariable s with
  | null -> None
  | v ->
    let t = v.Trim()

    if t.Length = 0 then None else Some t

/// <summary>
/// Like <see cref="getEnv"/> but fails with an exception if the variable is missing or blank.
/// </summary>
/// <remarks>
/// The returned string is already trimmed (see <see cref="getEnv"/>).
/// </remarks>
let getEnvF s =
  match getEnv s with
  | Some v -> v
  | None -> failwith $"environment variable {s} not found"

/// <summary>
/// Require an environment variable; returns <c>Error</c> if missing or blank after trim.
/// </summary>
/// <remarks>
/// On <c>Ok</c>, the value is already trimmed (see <see cref="getEnv"/>). Callers should not
/// trim again or test for empty/whitespace.
/// </remarks>
let requireEnv (name: string) : Result<string, string> =
  match getEnv name with
  | Some v -> Ok v
  | None -> Error $"environment variable '{name}' is missing or empty"

/// <summary>
/// Require all named environment variables; returns name → value map.
/// </summary>
/// <remarks>
/// Values are already trimmed (via <see cref="getEnv"/>). Missing or blank names appear
/// in the <c>Error</c> message. Do not re-trim map values at the call site.
/// </remarks>
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

/// Format a timestamp as RFC3339 without fractional seconds (e.g. 2026-07-23T12:00:00+00:00).
let toRFC3339 (value: DateTimeOffset) =
  value.ToString("yyyy-MM-dd'T'HH:mm:ssK", Globalization.CultureInfo.InvariantCulture)

/// Current UTC time as RFC3339 (see <see cref="toRFC3339"/>). Prefer <c>toRFC3339</c> when the clock is injected.
let nowRFC3339 () = toRFC3339 DateTimeOffset.UtcNow

let nowUnix () =
  DateTimeOffset(DateTime.UtcNow).ToUnixTimeMilliseconds()

module Tests =
  open Swensen.Unquote.Assertions

  let private uniqueName () = $"LAMG_ENV_TEST_{Guid.NewGuid():N}"

  let ``getEnv returns None when missing`` () =
    let name = uniqueName ()
    Environment.SetEnvironmentVariable(name, null)
    test <@ getEnv name = None @>

  let ``getEnv returns None when empty or whitespace`` () =
    let name = uniqueName ()

    try
      Environment.SetEnvironmentVariable(name, "")
      test <@ getEnv name = None @>
      Environment.SetEnvironmentVariable(name, "   ")
      test <@ getEnv name = None @>
    finally
      Environment.SetEnvironmentVariable(name, null)

  let ``getEnv trims surrounding whitespace`` () =
    let name = uniqueName ()

    try
      Environment.SetEnvironmentVariable(name, "  value  ")
      test <@ getEnv name = Some "value" @>
    finally
      Environment.SetEnvironmentVariable(name, null)

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
