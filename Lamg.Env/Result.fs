module Lamg.Env.Result

open System
open System.Threading.Tasks

type ResultBuilder() =
  member _.Return(value: 'T) : Result<'T, 'E> = Ok value

  member _.ReturnFrom(result: Result<'T, 'E>) : Result<'T, 'E> = result

  member _.Bind(result: Result<'T, 'E>, binder: 'T -> Result<'U, 'E>) : Result<'U, 'E> =
    match result with
    | Ok v -> binder v
    | Error e -> Error e

  member _.Zero() : Result<unit, 'E> = Ok()

  member _.Combine(result: Result<unit, 'E>, binder: unit -> Result<'T, 'E>) : Result<'T, 'E> =
    match result with
    | Ok() -> binder ()
    | Error e -> Error e

  member _.Delay(f: unit -> Result<'T, 'E>) : unit -> Result<'T, 'E> = f

  member _.Run(f: unit -> Result<'T, 'E>) : Result<'T, 'E> = f ()

  member _.TryWith(body: unit -> Result<'T, 'E>, handler: exn -> Result<'T, 'E>) : Result<'T, 'E> =
    try
      body ()
    with ex ->
      handler ex

  member _.TryFinally(body: unit -> Result<'T, 'E>, compensation: unit -> unit) : Result<'T, 'E> =
    try
      body ()
    finally
      compensation ()

  member this.Using(resource: #IDisposable, body: #IDisposable -> Result<'T, 'E>) : Result<'T, 'E> =
    this.TryFinally(
      (fun () -> body resource),
      (fun () ->
        if not (isNull (box resource)) then
          resource.Dispose())
    )

let result = ResultBuilder()

/// Task-aware Result CE. Awaits via the task CE so faulted/canceled tasks
/// propagate their real exception instead of being rewritten as TaskCanceledException
/// (the previous OnlyOnRanToCompletion + Unwrap path did that).
type TaskResultBuilder() =
  member _.Return(value: 'T) : Task<Result<'T, 'E>> = Task.FromResult(Ok value)

  member _.ReturnFrom(result: Result<'T, 'E>) : Task<Result<'T, 'E>> = Task.FromResult result

  member _.ReturnFrom(taskResult: Task<Result<'T, 'E>>) : Task<Result<'T, 'E>> = taskResult

  member _.Bind(result: Result<'T, 'E>, binder: 'T -> Task<Result<'U, 'E>>) : Task<Result<'U, 'E>> =
    match result with
    | Ok v -> binder v
    | Error e -> Task.FromResult(Error e)

  member _.Bind(taskResult: Task<Result<'T, 'E>>, binder: 'T -> Task<Result<'U, 'E>>) : Task<Result<'U, 'E>> =
    task {
      let! result = taskResult

      match result with
      | Ok v -> return! binder v
      | Error e -> return Error e
    }

  member _.Bind(antecedent: Task<'T>, binder: 'T -> Task<Result<'U, 'E>>) : Task<Result<'U, 'E>> =
    task {
      let! v = antecedent
      return! binder v
    }

  member _.Zero() : Task<Result<unit, 'E>> = Task.FromResult(Ok())

  member _.Combine(result: Result<unit, 'E>, binder: unit -> Task<Result<'T, 'E>>) : Task<Result<'T, 'E>> =
    match result with
    | Ok() -> binder ()
    | Error e -> Task.FromResult(Error e)

  member _.Combine(taskResult: Task<Result<unit, 'E>>, binder: unit -> Task<Result<'T, 'E>>) : Task<Result<'T, 'E>> =
    task {
      let! result = taskResult

      match result with
      | Ok() -> return! binder ()
      | Error e -> return Error e
    }

  member _.Delay(f: unit -> Task<Result<'T, 'E>>) : unit -> Task<Result<'T, 'E>> = f

  member _.Run(f: unit -> Task<Result<'T, 'E>>) : Task<Result<'T, 'E>> = f ()

  member _.TryWith(body: unit -> Task<Result<'T, 'E>>, handler: exn -> Task<Result<'T, 'E>>) : Task<Result<'T, 'E>> =
    task {
      try
        return! body ()
      with ex ->
        return! handler ex
    }

  member _.TryFinally(body: unit -> Task<Result<'T, 'E>>, compensation: unit -> unit) : Task<Result<'T, 'E>> =
    task {
      try
        return! body ()
      finally
        compensation ()
    }

  member this.Using(resource: #IDisposable, body: #IDisposable -> Task<Result<'T, 'E>>) : Task<Result<'T, 'E>> =
    this.TryFinally(
      (fun () -> body resource),
      (fun () ->
        if not (isNull (box resource)) then
          resource.Dispose())
    )

let taskResult = TaskResultBuilder()

module Tests =
  open Swensen.Unquote.Assertions

  let ``result bind short-circuits on Error`` () =
    let r =
      result {
        let! x = Error "boom"
        return x + 1
      }

    test <@ r = Error "boom" @>

  let ``result bind continues on Ok`` () =
    let r =
      result {
        let! x = Ok 2
        let! y = Ok 3
        return x + y
      }

    test <@ r = Ok 5 @>

  let ``taskResult binds Result and Task Result`` () =
    let r =
      taskResult {
        let! a = Ok 1
        let! b = Task.FromResult(Ok 2)
        let! c = Task.FromResult 3
        return a + b + c
      }
      |> _.Result

    test <@ r = Ok 6 @>

  let ``taskResult short-circuits on Error`` () =
    let r =
      taskResult {
        let! _ = Error "nope"
        return 1
      }
      |> _.Result

    test <@ r = Error "nope" @>
