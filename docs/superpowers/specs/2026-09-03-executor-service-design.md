# ExecutorService for .NET: design

**Date:** 2026-09-03
**Status:** approved for v0.x (basic implementation), later phases open for adjustment

## Goal

Publish an open-source NuGet package, `ExecutorService`, that ports Java's
`java.util.concurrent.ExecutorService` family to .NET 10 with idiomatic .NET types
(`Task`, `TimeSpan`, `IDisposable`) while preserving Java's lifecycle semantics.

## Non-goals (for now)

- Replacing or wrapping the .NET `ThreadPool`; this library provides *dedicated* threads.
- Thread interruption. .NET has none; `ShutdownNow` cancels queued work only.
- Scheduling (`ScheduledExecutorService`), bounded queues, `InvokeAll`/`InvokeAny`: roadmap items.

## Decisions

| Topic | Decision | Reason |
|---|---|---|
| Package id / root namespace | `ExecutorService` | Free on nuget.org, mirrors Java, no type named exactly `ExecutorService` so no namespace clash. |
| Target framework | `net10.0` only | Requested. Multi-targeting `net8.0` is a one-line change later. |
| Future type | `Task` / `Task<T>` | Idiomatic; composes with `await`, `WhenAll`, `WaitAsync`. |
| Runnable / Callable | `Action` / `Func<T>` | Direct mapping. Async and token-aware overloads are roadmap. |
| Work queue | `BlockingCollection<WorkItem>` over `ConcurrentQueue` | Unbounded FIFO, `CompleteAdding` gives clean shutdown semantics with synchronous workers. |
| Workers | Eager `Thread[]`, named `{prefix}-{i}`, background by default | Java's fixed pool semantics; names help diagnostics. |
| Task exceptions | Captured into the `Task`; never escape the worker | An unhandled exception on a dedicated thread would kill the process. |
| `Execute` failures | Swallowed (observed via internal Task) | Java routes them to `UncaughtExceptionHandler`; a hook can be added later. |
| `close()` | `Dispose()` = `Shutdown()` + blocking wait; `DisposeAsync()` awaits | Matches Java 19+ `AutoCloseable`. `Dispose` skips the wait when called from a worker thread to avoid deadlock. |
| `ShutdownNow` return | `IReadOnlyList<Task>` of canceled tasks | Java returns `List<Runnable>`; returning the tasks lets callers observe cancellation. |
| Versioning | MinVer from `v*` tags | Zero-config, community standard for small OSS packages. |
| Tests | xunit v3 on Microsoft.Testing.Platform | Current xunit line; MTP is the .NET 10 default runner. |
| Package hygiene | SourceLink (SDK built-in), snupkg, deterministic + CI builds, README in package, MIT, XML docs, AOT-compatible | NuGet.org best-practice checklist. |
| Feeds | Repo-level `nuget.config` with `<clear/>` + nuget.org only | Isolates contributors from machine-level private feeds; required by Central Package Management. |

## Architecture

```
IExecutor                    Execute(Action)
  └─ IExecutorService        Submit, Shutdown, ShutdownNow, AwaitTermination*, IsShutdown, IsTerminated, Dispose*
       └─ ThreadPoolExecutor  fixed N dedicated threads + BlockingCollection<WorkItem>
Executors                    static factories → ThreadPoolExecutor
ThreadPoolExecutorOptions    thread name prefix, IsBackground, Priority
RejectedExecutionException   thrown on submit after shutdown
Internal/WorkItem            ActionWorkItem, FuncWorkItem<T>: delegate → TaskCompletionSource bridge
```

### State machine

`Running → ShuttingDown → Stopped`, stored in an `int` with `Interlocked`.

- `Shutdown`: `Running → ShuttingDown`, `CompleteAdding()`. Workers drain the queue then exit.
- `ShutdownNow`: any → `Stopped`, `CompleteAdding()` if needed, drain queue canceling each item. Workers cancel anything they pick up while `Stopped`.
- Termination: last worker to exit disposes the queue and completes the `_terminated` TCS.
- Submit race: state check, then `Add`; an `InvalidOperationException` from a completed queue is translated to `RejectedExecutionException`.

## Error handling

- `ArgumentNullException` for null delegates, `ArgumentOutOfRangeException` for `threadCount < 1`.
- `RejectedExecutionException : InvalidOperationException` after shutdown.
- `OperationCanceledException` thrown by a task cancels its `Task`; any other exception faults it.

## Testing strategy

Unit tests cover: result propagation, exception propagation, worker naming, concurrency bound,
shutdown rejection with queued completion, `ShutdownNow` cancellation and return value,
`AwaitTermination` timeout and success (sync and async), `Dispose`/`DisposeAsync`,
dispose from a worker thread, idempotent shutdown, factories, sequential ordering on a single thread.
All waits use a 5 s timeout and the xunit test cancellation token so a bug cannot hang CI.
