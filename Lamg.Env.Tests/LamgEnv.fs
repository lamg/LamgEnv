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

          testCase "getEnvF fails when variable is missing" (fun _ ->
            Lamg.Env.Env.Tests.``getEnvF fails when variable is missing`` ())

          testCase "setEnv and getEnv round-trip" (fun _ -> Lamg.Env.Env.Tests.``setEnv and getEnv round-trip`` ())

          testCase "requireEnvs collects values or lists missing" (fun _ ->
            Lamg.Env.Env.Tests.``requireEnvs collects values or lists missing`` ()) ] ]

[<EntryPoint>]
let main argv = runTestsWithCLIArgs [] argv tests
