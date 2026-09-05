# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.6.1] - 2026-09-05

### Added

- A metrics sample under `samples/ExecutorService.Metrics.Sample`, driving an executor under synthetic load
  until every instrument has moved — the rejected and canceled paths included. Watch it with the
  OpenTelemetry console exporter (`task metrics`) or live with `dotnet-counters` (`task metrics:counters`).

### Fixed

- `DisposeAsync` deadlocked when called from one of the executor's own worker threads. `Submit(Func<Task>)`
  blocks its worker until the returned task completes, so awaiting termination there kept alive the very
  worker whose exit termination waits for. It now skips the wait on a worker thread, matching `Dispose`.

## [0.6.0] - 2026-09-04

### Added

- `Submit(Func<Task>)` and `Submit<TResult>(Func<Task<TResult>>)`, for asynchronous work. The returned task
  completes when the work finishes, and the worker thread stays occupied until then, so `ThreadCount` bounds
  how many run concurrently.

### Fixed

- Submitting async work used to be a silent trap: `Submit(() => WorkAsync())` bound to
  `Submit<TResult>(Func<TResult>)` with `TResult` inferred as `Task`, so it returned a `Task<Task>` that
  completed as soon as the work *started*. The worker was released immediately, so the pool bounded nothing
  and exceptions surfaced on the inner task nobody awaited. The new overloads now win overload resolution.

### Changed

- Source-breaking in one corner: a throw-only lambda with an explicit type argument, such as
  `Submit<int>(() => throw new Exception())`, is now ambiguous, because such a lambda has no return type and
  fits both `Func<int>` and `Func<Task<int>>`. `Task.Run` has the same corner. Give the delegate a type — a
  local function or a cast — to disambiguate.

## [0.5.1] - 2026-09-04

### Fixed

- Submitted work now runs under the caller's `ExecutionContext`, captured per submission, so `AsyncLocal<T>`
  values — `Activity.Current` among them — flow into tasks the way they do through `Task.Run`. Previously the
  worker threads captured the context once, when the executor was constructed, and served that frozen copy to
  every task: ambient values set after construction never arrived, spans were parented to whatever was current
  at construction time, and that context's object graph was kept alive for the executor's lifetime. Callers opt
  out with the standard `ExecutionContext.SuppressFlow()`.

## [0.5.0] - 2026-09-04

### Added

- Metrics published through `System.Diagnostics.Metrics` (the in-box OpenTelemetry API, no new dependency):
  queue depth, thread count, submitted / completed / rejected counters, and histograms for queue latency and
  execution time. Wire them up with `AddMeter(ThreadPoolExecutor.MeterName)`.
- `ThreadPoolExecutorOptions.Meter`, to publish metrics to a caller-supplied `Meter` (for example one from
  `IMeterFactory`) instead of the one the executor creates and owns.
- A package icon.

### Changed

- The package now targets **net8.0** as well as net10.0. No source changes were needed: nothing in the library
  depends on APIs newer than .NET 8. The test suite runs against both targets; the coverage gate measures
  net10.0 only, since test completeness does not differ per target framework.
- Corrected the package description, which called the executors "bounded" although the queue is unbounded.
  Only the thread count is fixed.

## [0.4.0] - 2026-09-03

### Added

- `IExecutorService.ShutdownToken`, canceled by `ShutdownNow` so tasks already running can stop
  cooperatively. `Shutdown` never cancels it. Throwing `OperationCanceledException` from a task
  (e.g. via `ThrowIfCancellationRequested`) transitions its `Task` to `Canceled`.

## [0.3.0] - 2026-09-03

### Fixed

- `task ci` measured coverage in Debug while building and testing in Release: Task does not pass a task's
  vars down to its dependencies, so the `coverage` dependency fell back to the default configuration.

## [0.2.0] - 2026-09-03

### Added

- `IExecutor` and `IExecutorService` abstractions mirroring `java.util.concurrent`.
- `ThreadPoolExecutor`: fixed-size pool of dedicated worker threads over an unbounded FIFO queue.
- `Executors.NewFixedThreadPool` and `Executors.NewSingleThreadExecutor` factories.
- `ThreadPoolExecutorOptions` for thread name prefix, background flag and priority.
- `RejectedExecutionException`, thrown when submitting after shutdown.
- Lifecycle: `Shutdown`, `ShutdownNow`, `AwaitTermination`, `AwaitTerminationAsync`, `IsShutdown`, `IsTerminated`.
- `IDisposable` / `IAsyncDisposable` semantics equivalent to Java's `close()`.
- `Taskfile.yml` with build, test, coverage (100% gate), format and pack commands.

### Fixed

- `ThreadPoolExecutor.ShutdownNow` and `QueuedCount` threw `ObjectDisposedException` when called after the
  executor had terminated (or while the last worker was exiting), because the worker disposed the queue.

## [0.0.3] - 2026-09-03

Released out of order: this version is numbered below 0.2.0 but contains later code. Use 0.3.0 or newer.

### Added

- `Microsoft.CodeAnalysis.PublicApiAnalyzers` wired up, with the public surface recorded in
  `PublicAPI.Shipped.txt`, so accidental API changes break the build.

### Changed

- `ThreadPoolExecutor.IsWorkerThread` no longer allocates while checking the current thread.

[Unreleased]: https://github.com/arielsrv/dotnet-executor-service/compare/v0.6.1...HEAD
[0.6.1]: https://github.com/arielsrv/dotnet-executor-service/releases/tag/v0.6.1
[0.6.0]: https://github.com/arielsrv/dotnet-executor-service/releases/tag/v0.6.0
[0.5.1]: https://github.com/arielsrv/dotnet-executor-service/releases/tag/v0.5.1
[0.5.0]: https://github.com/arielsrv/dotnet-executor-service/releases/tag/v0.5.0
[0.4.0]: https://github.com/arielsrv/dotnet-executor-service/releases/tag/v0.4.0
[0.3.0]: https://github.com/arielsrv/dotnet-executor-service/releases/tag/v0.3.0
[0.2.0]: https://github.com/arielsrv/dotnet-executor-service/releases/tag/v0.2.0
[0.0.3]: https://github.com/arielsrv/dotnet-executor-service/releases/tag/v0.0.3
