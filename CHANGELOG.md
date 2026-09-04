# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `IExecutorService.ShutdownToken`, canceled by `ShutdownNow` so tasks already running can stop
  cooperatively. `Shutdown` never cancels it. Throwing `OperationCanceledException` from a task
  (e.g. via `ThrowIfCancellationRequested`) transitions its `Task` to `Canceled`.
- Metrics published through `System.Diagnostics.Metrics` (the in-box OpenTelemetry API, no new dependency):
  queue depth, thread count, submitted / completed / rejected counters, and histograms for queue latency and
  execution time. Wire them up with `AddMeter(ThreadPoolExecutor.MeterName)`.
- `ThreadPoolExecutorOptions.Meter`, to publish metrics to a caller-supplied `Meter` (for example one from
  `IMeterFactory`) instead of the one the executor creates and owns.

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

[Unreleased]: https://github.com/arielsrv/dotnet-executor-service/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/arielsrv/dotnet-executor-service/releases/tag/v0.2.0
