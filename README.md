# Lamg.Env

F# library for environment/secret loading and `result` / `taskResult` computation expressions.

## Install

```sh
dotnet add package Lamg.Env
```

## Modules

| Module | Purpose |
|---|---|
| `Lamg.Env.Result` | `result` and `taskResult` computation expressions |
| `Lamg.Env.Secrets` | `dotenv.net` load + 1Password CLI (`op`) |
| `Lamg.Env.Console` | Colored console helpers |
| `Lamg.Env.Env` | Env vars (`getEnv`, `requireEnv`, …) + small time helpers |

## Secrets

```fsharp
open Lamg.Env.Result
open Lamg.Env.Secrets

loadDotEnv ()

// Prefer local env; fall back to 1Password (same names as field labels)
let! secrets =
  resolveFromEnvOrOnePassword [ "API_KEY"; "API_SECRET" ] "my-vault" "my-item"

// Or always load from 1Password and map fields yourself
let! json = getItem "my-vault" "my-item" (Some [ "API_KEY" ])
let! fields = fieldsByLabel json
let apiKey = fields["API_KEY"]
```

Set `OP_SERVICE_ACCOUNT_TOKEN` (e.g. in `.env`) or use an interactive `op signin` session.

## Computation expressions

```fsharp
open Lamg.Env.Result

let work = taskResult {
  let! a = Ok 1
  let! b = someAsyncWork ()
  return a + b
}
```

## Develop

```sh
dotnet build Lamg.Env.slnx
dotnet run --project Lamg.Env.Tests
dotnet pack Lamg.Env/Lamg.Env.fsproj -c Release
```
