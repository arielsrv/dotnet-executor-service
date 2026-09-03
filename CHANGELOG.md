# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `IExecutor` and `IExecutorService` abstractions mirroring `java.util.concurrent`.
- `ThreadPoolExecutor`: fixed-size pool of dedicated worker threads over an unbounded FIFO queue.
- `Executors.NewFixedThreadPool` and `Executors.NewSingleThreadExecutor` factories.
- `ThreadPoolExecutorOptions` for thread name prefix, background flag and priority.
- `RejectedExecutionException`, thrown when submitting after shutdown.
- Lifecycle: `Shutdown`, `ShutdownNow`, `AwaitTermination`, `AwaitTerminationAsync`, `IsShutdown`, `IsTerminated`.
- `IDisposable` / `IAsyncDisposable` semantics equivalent to Java's `close()`.

[Unreleased]: https://github.com/arielsrv/dotnet-executor-service/compare/main...HEAD
