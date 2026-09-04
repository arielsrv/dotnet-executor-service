# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

[Unreleased]: https://github.com/arielsrv/dotnet-executor-service/compare/v0.5.1...HEAD
[0.5.1]: https://github.com/arielsrv/dotnet-executor-service/releases/tag/v0.5.1
[0.5.0]: https://github.com/arielsrv/dotnet-executor-service/releases/tag/v0.5.0
[0.4.0]: https://github.com/arielsrv/dotnet-executor-service/releases/tag/v0.4.0
[0.3.0]: https://github.com/arielsrv/dotnet-executor-service/releases/tag/v0.3.0
[0.2.0]: https://github.com/arielsrv/dotnet-executor-service/releases/tag/v0.2.0
[0.0.3]: https://github.com/arielsrv/dotnet-executor-service/releases/tag/v0.0.3
