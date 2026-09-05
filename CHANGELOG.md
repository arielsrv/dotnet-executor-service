# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.7.1] - 2026-09-05

### Changed

- The floor of the two dependencies the netstandard2.0 build ships is now enforced rather than merely
  intended: dependabot no longer proposes major or minor bumps for them, and the build fails if the pins
  move. Consumers inherit that floor through NuGet's upward resolution, so raising it is a change to what
  the package promises, not routine maintenance.
- The quick start sample hands the executor to each check as an argument instead of closing over the
  `using` variable, and the two blocking calls in `ThreadPoolExecutor` that deliberately take no
  cancellation token now say why. No behaviour changed: the library assembly differs only in comments.

### Fixed

- The metrics sample unsubscribes its Ctrl+C handler before the scope disposes the
  `CancellationTokenSource` it captures. A Ctrl+C landing in that window called `Cancel()` on a disposed
  source, which throws.

## [0.7.0] - 2026-09-05

### Added

- **netstandard2.0** target, so .NET Framework 4.6.2+, Mono and Unity can consume the library. It is the only
  target with dependencies — `System.Diagnostics.DiagnosticSource` and `Microsoft.Bcl.AsyncInterfaces`, neither
  of which .NET Framework ships — and the test suite runs against it on `net472` in CI. .NET 8 and .NET 10
  builds are unchanged and still have no dependencies.
- A quick start sample under `samples/ExecutorService.QuickStart.Sample` that consumes the library through a
  `PackageReference` rather than a project reference, so it exercises the package exactly as published on
  nuget.org. Five checks, about a second, non-zero exit on failure (`task quickstart`).
- Benchmarks under `benchmarks/ExecutorService.Benchmarks` measuring submission cost and allocations against
  `Task.Run` as a baseline (`task bench`).
- A native AOT smoke test in CI: the quick start sample is published as a native binary and run, which turns
  `IsAotCompatible` from a declaration into something proven (`task aot`).
- Package validation against the previous release, so a dropped target framework or a broken signature fails
  the pack instead of reaching nuget.org.

### Changed

- The package page on nuget.org now shows the release notes for the version being published, lifted from this
  file at pack time, instead of a link to it.
- Links in the packaged README are absolute. nuget.org strips relative ones, which left `CONTRIBUTING.md`,
  `LICENSE` and the sample links as dead anchors on the package page.
- Workflow actions are pinned by commit SHA, and released packages carry signed build provenance
  (`gh attestation verify <file> --repo arielsrv/dotnet-executor-service`).

## [0.6.2] - 2026-09-05

### Added

- Markdown linting over the repository's documentation with `markdownlint-cli2`, wired into `task lint:md`
  and into CI next to the format check.

### Fixed

- The README shipped inside the package renders the link to Java's `ExecutorService` correctly. A wrapped
  line had left a stray space inside the link text, and one metrics table row was a column wider than the rest.

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

[Unreleased]: https://github.com/arielsrv/dotnet-executor-service/compare/v0.7.1...HEAD
[0.7.1]: https://github.com/arielsrv/dotnet-executor-service/releases/tag/v0.7.1
[0.7.0]: https://github.com/arielsrv/dotnet-executor-service/releases/tag/v0.7.0
[0.6.2]: https://github.com/arielsrv/dotnet-executor-service/releases/tag/v0.6.2
[0.6.1]: https://github.com/arielsrv/dotnet-executor-service/releases/tag/v0.6.1
[0.6.0]: https://github.com/arielsrv/dotnet-executor-service/releases/tag/v0.6.0
[0.5.1]: https://github.com/arielsrv/dotnet-executor-service/releases/tag/v0.5.1
[0.5.0]: https://github.com/arielsrv/dotnet-executor-service/releases/tag/v0.5.0
[0.4.0]: https://github.com/arielsrv/dotnet-executor-service/releases/tag/v0.4.0
[0.3.0]: https://github.com/arielsrv/dotnet-executor-service/releases/tag/v0.3.0
[0.2.0]: https://github.com/arielsrv/dotnet-executor-service/releases/tag/v0.2.0
[0.0.3]: https://github.com/arielsrv/dotnet-executor-service/releases/tag/v0.0.3
