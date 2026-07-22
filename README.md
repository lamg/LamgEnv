# Lamg.Env

F# library for environment/secret loading, `result` / `taskResult` computation expressions, and generic SQLite transaction helpers.

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
| `Lamg.Env.Sqlite` | Generic `dbTxn` / nested CE for SQLProvider (or any) contexts |

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

## SQLite / SQLProvider transactions (`Lamg.Env.Sqlite`)

No SQLProvider or SQLite package dependency — supply a `DbSession<'ctx>` that knows how to open and commit your app context:

```fsharp
open Lamg.Env.Sqlite

// Example with a SQLProvider-generated DataContext:
let session: DbSession<MyDb.dataContext> =
  { connect = fun path -> MyDb.GetDataContext($"Data Source={path};Foreign Keys=True")
    submitUpdates = fun ctx -> ctx.SubmitUpdates() }

let db = dbTxn session "app.db"
let readDb = readOnlyDbTxn session "app.db"

let! rows = db.Run(fun ctx ->
  // stage writes on ctx; outermost successful write Run calls submitUpdates
  ...)
```

Semantics:

- Nested `Run` on the same DB path reuses the active context when modes allow
- Read-only may nest under write; write under read-only → `DbError.ReadWriteInsideReadOnly`
- Outermost successful **read-write** run calls `submitUpdates`
- Per-path write gate serializes outermost write transactions

## Develop

```sh
dotnet build Lamg.Env.slnx
dotnet run --project Lamg.Env.Tests
dotnet pack Lamg.Env/Lamg.Env.fsproj -c Release
```
