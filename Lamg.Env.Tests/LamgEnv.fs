module LamgEnvTests

open Expecto

let tests =
  testList
    "Lamg.Env"
    [ testList
        "Result"
        [ testCase "result bind short-circuits on Error" (fun _ ->
            Lamg.Env.Result.Tests.``result bind short-circuits on Error`` ())

          testCase "result bind continues on Ok" (fun _ -> Lamg.Env.Result.Tests.``result bind continues on Ok`` ())

          testCase "taskResult binds Result and Task Result" (fun _ ->
            Lamg.Env.Result.Tests.``taskResult binds Result and Task Result`` ())

          testCase "taskResult short-circuits on Error" (fun _ ->
            Lamg.Env.Result.Tests.``taskResult short-circuits on Error`` ()) ]

      testList
        "Secrets"
        [ testCase "fieldsByLabel parses OpField array JSON" (fun _ ->
            Lamg.Env.Secrets.Tests.``fieldsByLabel parses OpField array JSON`` ())

          testCase "fieldsByLabel rejects invalid JSON" (fun _ ->
            Lamg.Env.Secrets.Tests.``fieldsByLabel rejects invalid JSON`` ())

          testCase "requireFields reports missing and blank labels" (fun _ ->
            Lamg.Env.Secrets.Tests.``requireFields reports missing and blank labels`` ()) ]

      testList
        "Env"
        [ testCase "getEnv returns None when missing" (fun _ ->
            Lamg.Env.Env.Tests.``getEnv returns None when missing`` ())

          testCase "getEnv returns None when empty or whitespace" (fun _ ->
            Lamg.Env.Env.Tests.``getEnv returns None when empty or whitespace`` ())

          testCase "getEnv trims surrounding whitespace" (fun _ ->
            Lamg.Env.Env.Tests.``getEnv trims surrounding whitespace`` ())

          testCase "getEnvF fails when variable is missing" (fun _ ->
            Lamg.Env.Env.Tests.``getEnvF fails when variable is missing`` ())

          testCase "setEnv and getEnv round-trip" (fun _ -> Lamg.Env.Env.Tests.``setEnv and getEnv round-trip`` ())

          testCase "requireEnvs collects values or lists missing" (fun _ ->
            Lamg.Env.Env.Tests.``requireEnvs collects values or lists missing`` ()) ]

      testList
        "Sqlite"
        [ testCase "read-write Run calls submitUpdates once on success" (fun _ ->
            Lamg.Env.Sqlite.Tests.``read-write Run calls submitUpdates once on success`` ())

          testCase "read-only Run does not submit" (fun _ -> Lamg.Env.Sqlite.Tests.``read-only Run does not submit`` ())

          testCase "nested write under write reuses context and submits once" (fun _ ->
            Lamg.Env.Sqlite.Tests.``nested write under write reuses context and submits once`` ())

          testCase "nested write under read-only returns ReadWriteInsideReadOnly" (fun _ ->
            Lamg.Env.Sqlite.Tests.``nested write under read-only returns ReadWriteInsideReadOnly`` ())

          testCase "exception from operation yields DbError.Sql" (fun _ ->
            Lamg.Env.Sqlite.Tests.``exception from operation yields DbError.Sql`` ()) ] ]

[<EntryPoint>]
let main argv = runTestsWithCLIArgs [] argv tests
