module Lamg.Env.Secrets

open System
open System.Diagnostics
open System.Text.Json
open System.Threading.Tasks
open Lamg.Env.Env
open Lamg.Env.Result

[<Literal>]
let serviceAccountTokenKey = "OP_SERVICE_ACCOUNT_TOKEN"

/// Shape of one field on a 1Password item (`op item get --format json`).
[<CLIMutable>]
type OpField =
  { id: string
    label: string
    value: string
    reference: string }

/// Shape of `op item get --vault <vault> <name> --format json`.
[<CLIMutable>]
type OpItem = { fields: OpField[] }

/// Load `.env` into the process environment (dotenv.net).
let loadDotEnv () = dotenv.net.DotEnv.Load()

let private truncateRaw (json: string) =
  if json.Length > 300 then
    json.Substring(0, 300) + "..."
  else
    json

let private fieldsToMap (fields: OpField[]) : Map<string, string> =
  fields
  |> Option.ofObj
  |> Option.defaultValue Array.empty
  |> Array.filter (fun field -> not (String.IsNullOrWhiteSpace field.label))
  |> Array.map (fun field -> field.label, field.value)
  |> Map.ofArray

/// Parse `op item get --fields … --format json` output (`OpField[]`) into label → value.
let fieldsByLabel (json: string) : Result<Map<string, string>, string> =
  result {
    try
      let fields =
        JsonSerializer.Deserialize<OpField[]> json
        |> Option.ofObj
        |> Option.defaultValue Array.empty

      return fieldsToMap fields
    with ex ->
      let raw = truncateRaw (if isNull json then "" else json)
      return! Error $"Invalid 1Password JSON\n{raw}\nerror: {ex.Message}"
  }

/// Ensure required labels exist and are non-blank.
let requireFields (required: string list) (fields: Map<string, string>) : Result<Map<string, string>, string> =
  let missing =
    required
    |> List.choose (fun label ->
      match fields |> Map.tryFind label |> Option.filter (String.IsNullOrWhiteSpace >> not) with
      | Some _ -> None
      | None -> Some label)

  if missing.IsEmpty then
    // Trim values so callers match getEnv semantics (no extra Trim needed).
    fields |> Map.map (fun _ v -> if isNull v then v else v.Trim()) |> Ok
  else
    let missingList = String.concat ", " missing
    Error $"1Password fields not found or empty: {missingList}"

/// Run `op item get --vault <vault> <item> --format json` with optional `--fields label=…`.
/// Uses OP_SERVICE_ACCOUNT_TOKEN when set. Returns raw stdout JSON.
let getItem (vault: string) (item: string) (fields: string list option) : Task<Result<string, string>> =
  taskResult {
    let startInfo = ProcessStartInfo()
    startInfo.FileName <- "op"
    startInfo.UseShellExecute <- false
    startInfo.RedirectStandardOutput <- true
    startInfo.RedirectStandardError <- true
    startInfo.CreateNoWindow <- true
    startInfo.WorkingDirectory <- Environment.CurrentDirectory
    startInfo.ArgumentList.Add "item"
    startInfo.ArgumentList.Add "get"
    startInfo.ArgumentList.Add "--vault"
    startInfo.ArgumentList.Add vault
    startInfo.ArgumentList.Add item
    startInfo.ArgumentList.Add "--format"
    startInfo.ArgumentList.Add "json"

    match fields with
    | Some labels when not labels.IsEmpty ->
      let fieldSpec =
        labels |> List.map (fun label -> $"label={label}") |> String.concat ","

      startInfo.ArgumentList.Add "--fields"
      startInfo.ArgumentList.Add fieldSpec
    | _ -> ()

    // ProcessStartInfo inherits the parent env (including DotEnv.Load).
    // Use indexer assignment so a pre-existing key does not throw ArgumentException.
    match getEnv serviceAccountTokenKey with
    | Some token -> startInfo.EnvironmentVariables.[serviceAccountTokenKey] <- token
    | None -> ()


    use proc = new Process(StartInfo = startInfo)

    if not (proc.Start()) then
      return! Error "failed to start 1Password CLI (op)"

    let outTask = proc.StandardOutput.ReadToEndAsync()
    let errTask = proc.StandardError.ReadToEndAsync()
    do! task { do! proc.WaitForExitAsync() }

    let! output = outTask
    let! errorOutput = errTask

    if proc.ExitCode <> 0 then
      let msg =
        if String.IsNullOrWhiteSpace errorOutput then
          output
        else
          errorOutput

      return!
        Error
          $"1Password CLI failed (exit {proc.ExitCode}) loading '{item}' from vault '{vault}'. Ensure {serviceAccountTokenKey} is set or run 'op signin'. {msg}"
    else
      return output
  }

/// Prefer process env for `names`; if any are missing, load the same labels from 1Password.
/// Env var names and 1Password field labels must match.
let resolveFromEnvOrOnePassword
  (names: string list)
  (vault: string)
  (item: string)
  : Task<Result<Map<string, string>, string>> =
  taskResult {
    match requireEnvs names with
    | Ok map -> return map
    | Error _ ->
      let! json = getItem vault item (Some names)
      let! fields = fieldsByLabel json
      return! requireFields names fields
  }

module Tests =
  open Swensen.Unquote.Assertions

  let ``fieldsByLabel parses OpField array JSON`` () =
    let json =
      """[{"id":"","label":"X","value":"0","reference":""},{"id":"","label":"Y","value":"1","reference":""}]"""

    match fieldsByLabel json with
    | Ok map ->
      test <@ map["X"] = "0" @>
      test <@ map["Y"] = "1" @>
    | Error e -> failwith e

  let ``fieldsByLabel rejects invalid JSON`` () =
    match fieldsByLabel "not-json" with
    | Error msg -> test <@ msg.Contains "Invalid 1Password JSON" @>
    | Ok _ -> failwith "expected error for invalid JSON"

  let ``requireFields reports missing and blank labels`` () =
    let fields = Map.ofList [ "A", "ok"; "B", "  "; "C", "value" ]

    match requireFields [ "A"; "C" ] fields with
    | Ok m -> test <@ m["A"] = "ok" @>
    | Error e -> failwith e

    match requireFields [ "A"; "B"; "MISSING" ] fields with
    | Error msg ->
      test <@ msg.Contains "B" @>
      test <@ msg.Contains "MISSING" @>
    | Ok _ -> failwith "expected missing fields error"
