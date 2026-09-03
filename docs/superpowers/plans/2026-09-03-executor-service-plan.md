# ExecutorService for .NET: implementation plan

Spec: [2026-09-03-executor-service-design.md](../specs/2026-09-03-executor-service-design.md)

Each phase ships independently and gets its own CHANGELOG entry. Phase 0 is done.

## Phase 0: repository skeleton and core executor (done)

- [x] Solution (`.slnx`), `src/ExecutorService`, `tests/ExecutorService.Tests`
- [x] `Directory.Build.props` (analyzers, warnings as errors, deterministic, package metadata)
- [x] `Directory.Packages.props` (Central Package Management), `nuget.config`, `global.json` (SDK + MTP runner)
- [x] `IExecutor`, `IExecutorService`, `ThreadPoolExecutor`, `Executors`, `ThreadPoolExecutorOptions`, `RejectedExecutionException`
- [x] 21 unit tests, xunit v3
- [x] Community files: README, LICENSE (MIT), CHANGELOG, CONTRIBUTING, CODE_OF_CONDUCT, SECURITY, `.editorconfig`, `.gitattributes`
- [x] GitHub: CI matrix (Linux/Windows/macOS), release on tag, Dependabot, CODEOWNERS, issue and PR templates

## Phase 1: publish v0.1.0

1. Create the GitHub repo `arielsrv/dotnet-executor-service`, push `main`.
2. Enable Discussions (linked from issue template config) and private vulnerability reporting.
3. Create a NuGet.org API key scoped to push `ExecutorService`; store as `NUGET_API_KEY` in a GitHub environment named `nuget`.
4. Optionally add `icon.png` (128x128) and `<PackageIcon>` in the csproj.
5. Move `[Unreleased]` to `## [0.1.0] - <date>` in CHANGELOG, tag `v0.1.0`, push the tag.
6. Verify the package page on nuget.org shows README, license, source link and symbols.

## Phase 2: complete the ExecutorService surface

1. `InvokeAll<T>(IEnumerable<Func<T>>, TimeSpan? timeout)` returning completed `Task<T>` list; `InvokeAny<T>` returning the first successful result and canceling the rest.
2. Cancellation-aware overloads: `Submit<T>(Func<CancellationToken, T>)`, `Submit(Action<CancellationToken>)`. `ShutdownNow` cancels the executor-wide token so running tasks can cooperate.
3. Async overloads: `Submit(Func<Task>)`, `Submit<T>(Func<Task<T>>)`. Decide whether continuations run on the pool thread (custom `SynchronizationContext`) or on the .NET pool; document it.
4. `UnhandledException` event on `ThreadPoolExecutor` for `Execute` failures (Java's `UncaughtExceptionHandler`).
5. Public API tracking with `Microsoft.CodeAnalysis.PublicApiAnalyzers` (`PublicAPI.Shipped.txt` / `PublicAPI.Unshipped.txt`), already pinned in `Directory.Packages.props`.

## Phase 3: pool configuration like Java's `ThreadPoolExecutor`

1. Bounded queue (`BlockingCollection` capacity) and `IRejectedExecutionHandler` with `Abort`, `CallerRuns`, `Discard`, `DiscardOldest`.
2. Core / maximum pool size with keep-alive for idle threads; `Executors.NewCachedThreadPool()`.
3. Metrics: `ActiveCount`, `CompletedTaskCount`, `LargestPoolSize`. Consider `System.Diagnostics.Metrics` counters.
4. `IThreadFactory` abstraction replacing `ThreadPoolExecutorOptions` if configuration keeps growing.

## Phase 4: scheduling

1. `IScheduledExecutorService` with `Schedule`, `ScheduleAtFixedRate`, `ScheduleWithFixedDelay` returning a cancelable `IScheduledTask`.
2. `ScheduledThreadPoolExecutor` on a timer heap plus the existing worker pool.
3. `Executors.NewScheduledThreadPool(n)`, `Executors.NewSingleThreadScheduledExecutor()`.

## Phase 5: interop and polish

1. `ExecutorTaskScheduler : TaskScheduler` adapter so `Task.Factory.StartNew(..., scheduler)` targets an executor.
2. Multi-target `net8.0;net9.0;net10.0` if users ask for it.
3. Benchmarks project (`benchmarks/`, BenchmarkDotNet) comparing against `ThreadPool` and `Channel<T>` based pools.
4. `samples/` console app and a docs site (DocFX) if the API grows beyond the README.

## Definition of done per phase

- Tests for every new public member, including races (shutdown during submit, cancel during run).
- XML docs on every public member; `dotnet build` clean with warnings as errors.
- CHANGELOG entry under `[Unreleased]`.
- Java deviation, if any, documented in the member's `<remarks>`.
