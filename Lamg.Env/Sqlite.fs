/// Generic SQLite / SQLProvider transaction helpers: nested CE, write gates, submit on outermost write.
module Lamg.Env.Sqlite

open System
open System.Collections.Concurrent
open System.IO
open System.Threading
open System.Threading.Tasks

[<RequireQualifiedAccess>]
type DbError =
  | Sql of exn
  | ReadWriteInsideReadOnly

[<RequireQualifiedAccess>]
type TransactionMode =
  | ReadWrite
  | ReadOnly

/// App supplies how to open a context and how to commit staged writes (e.g. SQLProvider SubmitUpdates).
type DbSession<'ctx> =
  { connect: string -> 'ctx
    submitUpdates: 'ctx -> unit }

let private result value = fun _ -> value

let private zero () = result ()

let private returnFrom operation = operation

let private bind operation next = fun ctx -> next (operation ctx) ctx

let private combine operation next = bind operation (fun () -> next)

let private delay factory = fun ctx -> factory () ctx

let private forEach (items: 'a seq) body =
  fun ctx ->
    for item in items do
      body item ctx

let private writeGates = ConcurrentDictionary<string, SemaphoreSlim>()

let private gateFor dbPath =
  let resolvedPath = Path.GetFullPath dbPath
  writeGates.GetOrAdd(resolvedPath, fun _ -> new SemaphoreSlim(1, 1))

type private ActiveTransaction<'ctx> =
  { resolvedDbPath: string
    mode: TransactionMode
    context: 'ctx }

type private Nest<'ctx>() =
  static let active = AsyncLocal<ActiveTransaction<'ctx> option>()
  static member Active = active

let private canReuse requested active =
  match requested, active with
  | TransactionMode.ReadOnly, _ -> true
  | TransactionMode.ReadWrite, TransactionMode.ReadWrite -> true
  | TransactionMode.ReadWrite, TransactionMode.ReadOnly -> false

let private runWithContext (session: DbSession<'ctx>) (dbPath: string) (mode: TransactionMode) (operation: 'ctx -> 'a) =
  task {
    let resolvedDbPath = Path.GetFullPath dbPath
    let nest = Nest<'ctx>.Active
    let previous = nest.Value
    let context = session.connect resolvedDbPath

    nest.Value <-
      Some
        { resolvedDbPath = resolvedDbPath
          mode = mode
          context = context }

    try
      try
        let value = operation context

        match mode with
        | TransactionMode.ReadWrite ->
          session.submitUpdates context
          return Ok value
        | TransactionMode.ReadOnly -> return Ok value
      with ex ->
        return Error(DbError.Sql ex)
    finally
      nest.Value <- previous
  }

let private run (session: DbSession<'ctx>) (dbPath: string) (mode: TransactionMode) (step: 'ctx -> 'a) =
  // transaction model:
  // - nested write steps: staged in the shared context
  // - nested read steps: query immediately against the shared context
  // - outermost successful read-write run: calls session.submitUpdates
  // - SQLite transaction boundary (with SQLProvider): only around submit, not the whole body
  //
  // limitation:
  // A situation like the following causes inner runs to reuse the same active.context in
  // a non-predictable way:
  // db.Run(fun ctx ->
  //  [ task { return! db.Run(writeA) }
  //    task { return! db.Run(writeB) } ]
  //  |> Task.WhenAll
  //  |> _.Result
  //  |> ignore
  // )
  task {
    let resolvedDbPath = Path.GetFullPath dbPath
    let nest = Nest<'ctx>.Active

    match nest.Value with
    | Some active when active.resolvedDbPath = resolvedDbPath ->
      if canReuse mode active.mode then
        try
          return Ok(step active.context)
        with ex ->
          return Error(DbError.Sql ex)
      else
        return Error DbError.ReadWriteInsideReadOnly
    | _ ->
      match mode with
      | TransactionMode.ReadOnly -> return! runWithContext session resolvedDbPath mode step
      | TransactionMode.ReadWrite ->
        let gate = gateFor resolvedDbPath
        do! gate.WaitAsync()

        try
          return! runWithContext session resolvedDbPath mode step
        finally
          gate.Release() |> ignore
  }

type DbTxnBuilder<'ctx> internal (dbPath: string, mode: TransactionMode, session: DbSession<'ctx>) =
  member _.DbPath = dbPath
  member _.Mode = mode
  member _.Session = session
  member _.Run(operation: 'ctx -> 'a) = run session dbPath mode operation
  member _.Zero() = zero ()
  member _.Return(value: 'a) = result value
  member _.ReturnFrom(operation: 'ctx -> 'a) = returnFrom operation
  member _.Bind(operation: 'ctx -> 'a, next: 'a -> 'ctx -> 'b) = bind operation next
  member _.Combine(operation: 'ctx -> unit, next: 'ctx -> 'a) = combine operation next
  member _.Delay(factory: unit -> 'ctx -> 'a) = delay factory
  member _.For(items: 'a seq, body: 'a -> 'ctx -> unit) = forEach items body

type TxnBuilder<'ctx>() =
  member _.Run(operation: 'ctx -> 'a) = operation
  member _.Zero() = zero ()
  member _.Return(value: 'a) = result value
  member _.ReturnFrom(operation: 'ctx -> 'a) = returnFrom operation
  member _.Bind(operation: 'ctx -> 'a, next: 'a -> 'ctx -> 'b) = bind operation next
  member _.Combine(operation: 'ctx -> unit, next: 'ctx -> 'a) = combine operation next
  member _.Delay(factory: unit -> 'ctx -> 'a) = delay factory
  member _.For(items: 'a seq, body: 'a -> 'ctx -> unit) = forEach items body

let dbTxn (session: DbSession<'ctx>) (dbPath: string) =
  DbTxnBuilder(dbPath, TransactionMode.ReadWrite, session)

let readOnlyDbTxn (session: DbSession<'ctx>) (dbPath: string) =
  DbTxnBuilder(dbPath, TransactionMode.ReadOnly, session)

let txn<'ctx> = TxnBuilder<'ctx>()

module Tests =
  open Swensen.Unquote.Assertions

  type private FakeCtx =
    { mutable submitted: int
      mutable ops: int
      id: int }

  let private await (t: Task<'a>) = t.GetAwaiter().GetResult()

  let private dbPath () =
    Path.Combine(Path.GetTempPath(), $"lamg-env-sqlite-test-{Guid.NewGuid():N}.db")

  let ``read-write Run calls submitUpdates once on success`` () =
    let mutable submits = 0

    let s: DbSession<FakeCtx> =
      { connect = fun _ -> { submitted = 0; ops = 0; id = 1 }
        submitUpdates =
          fun c ->
            c.submitted <- c.submitted + 1
            submits <- submits + 1 }

    let db = dbTxn s (dbPath ())

    match
      await (
        db.Run(fun c ->
          c.ops <- c.ops + 1
          c.ops)
      )
    with
    | Ok n ->
      test <@ n = 1 @>
      test <@ submits = 1 @>
    | Error e -> failwithf "expected Ok, got %A" e

  let ``read-only Run does not submit`` () =
    let mutable submits = 0

    let s: DbSession<FakeCtx> =
      { connect = fun _ -> { submitted = 0; ops = 0; id = 1 }
        submitUpdates = fun _ -> submits <- submits + 1 }

    let db = readOnlyDbTxn s (dbPath ())

    match
      await (
        db.Run(fun c ->
          c.ops <- c.ops + 1
          c.ops)
      )
    with
    | Ok n ->
      test <@ n = 1 @>
      test <@ submits = 0 @>
    | Error e -> failwithf "expected Ok, got %A" e

  let ``nested write under write reuses context and submits once`` () =
    let mutable connects = 0
    let mutable submits = 0

    let s: DbSession<FakeCtx> =
      { connect =
          fun _ ->
            connects <- connects + 1

            { submitted = 0
              ops = 0
              id = connects }
        submitUpdates = fun _ -> submits <- submits + 1 }

    let path = dbPath ()
    let db = dbTxn s path

    match
      await (
        db.Run(fun outer ->
          outer.ops <- outer.ops + 1

          match
            await (
              db.Run(fun inner ->
                // same instance when nested
                test <@ obj.ReferenceEquals(outer, inner) @>
                inner.ops <- inner.ops + 1
                inner.ops)
            )
          with
          | Ok n -> n
          | Error e -> failwithf "inner failed: %A" e)
      )
    with
    | Ok n ->
      test <@ n = 2 @>
      test <@ connects = 1 @>
      test <@ submits = 1 @>
    | Error e -> failwithf "expected Ok, got %A" e

  let ``nested write under read-only returns ReadWriteInsideReadOnly`` () =
    let s: DbSession<FakeCtx> =
      { connect = fun _ -> { submitted = 0; ops = 0; id = 1 }
        submitUpdates = fun _ -> () }

    let path = dbPath ()
    let ro = readOnlyDbTxn s path
    let rw = dbTxn s path

    match
      await (
        ro.Run(fun _ ->
          match
            await (
              rw.Run(fun c ->
                c.ops <- 1
                c.ops)
            )
          with
          | Error DbError.ReadWriteInsideReadOnly -> "ok"
          | other -> failwithf "expected ReadWriteInsideReadOnly, got %A" other)
      )
    with
    | Ok "ok" -> ()
    | other -> failwithf "unexpected %A" other

  let ``exception from operation yields DbError.Sql`` () =
    let s: DbSession<FakeCtx> =
      { connect = fun _ -> { submitted = 0; ops = 0; id = 1 }
        submitUpdates = fun _ -> () }

    let db = dbTxn s (dbPath ())

    match await (db.Run(fun _ -> failwith "boom")) with
    | Error(DbError.Sql ex) -> test <@ ex.Message.Contains "boom" @>
    | other -> failwithf "expected Sql error, got %A" other
