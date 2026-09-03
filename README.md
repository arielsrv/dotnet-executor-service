# ExecutorService

[![CI](https://github.com/arielsrv/dotnet-executor-service/actions/workflows/ci.yml/badge.svg)](https://github.com/arielsrv/dotnet-executor-service/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/ExecutorService.svg)](https://www.nuget.org/packages/ExecutorService)
[![NuGet Downloads](https://img.shields.io/nuget/dt/ExecutorService.svg)](https://www.nuget.org/packages/ExecutorService)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A .NET port of Java's [`java.util.concurrent.ExecutorService`](https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/util/concurrent/ExecutorService.html):
bounded executors backed by **dedicated threads**, an explicit **lifecycle** (`Shutdown`, `ShutdownNow`, `AwaitTermination`),
and `Task`-based futures.

## Why?

The .NET `ThreadPool` is a single, process-wide, elastic pool. That is the right default for async I/O,
but sometimes you want what Java developers reach for with `Executors.newFixedThreadPool(n)`:

- A **fixed number of threads** for a specific workload, isolated from the rest of the process.
- Safe **blocking work** without starving the shared pool.
- A **deterministic shutdown**: stop accepting work, drain or drop the queue, wait for termination.
- **Strict FIFO** and, with one thread, **strictly sequential** execution.

## Installation

```shell
dotnet add package ExecutorService
```

Requires .NET 10 or later.

## Quick start

```csharp
using ExecutorService;

using IExecutorService executor = Executors.NewFixedThreadPool(4);

// Fire and forget
executor.Execute(() => Console.WriteLine("hello from a pool thread"));

// Submit and await a result
Task<int> future = executor.Submit(() => ComputeSomethingExpensive());
int result = await future;

// Orderly shutdown: finish queued work, reject new work
executor.Shutdown();
bool terminated = executor.AwaitTermination(TimeSpan.FromSeconds(10));
```

Disposing the executor is equivalent to Java's `close()`: it calls `Shutdown()` and waits for termination.
`await using` does the same without blocking.

### Dropping pending work

```csharp
IReadOnlyList<Task> neverStarted = executor.ShutdownNow();
// each Task in `neverStarted` is in the Canceled state
```

Tasks that are already running are **not** interrupted. .NET has no thread interruption;
implement cooperative cancellation inside your task if you need it.

### Configuring worker threads

```csharp
var executor = Executors.NewFixedThreadPool(2, new ThreadPoolExecutorOptions
{
    ThreadNamePrefix = "image-resizer",   // threads are named image-resizer-0, image-resizer-1
    IsBackground = false,                 // keep the process alive while work is pending
    Priority = ThreadPriority.BelowNormal,
});
```

## Java to .NET mapping

| Java                                   | ExecutorService (.NET)                         |
|----------------------------------------|------------------------------------------------|
| `Executor`                             | `IExecutor`                                    |
| `ExecutorService`                      | `IExecutorService`                             |
| `Executors.newFixedThreadPool(n)`      | `Executors.NewFixedThreadPool(n)`              |
| `Executors.newSingleThreadExecutor()`  | `Executors.NewSingleThreadExecutor()`          |
| `execute(Runnable)`                    | `Execute(Action)`                              |
| `submit(Runnable)` / `submit(Callable)`| `Submit(Action)` / `Submit<T>(Func<T>)`        |
| `Future<T>`                            | `Task<T>`                                      |
| `shutdown()` / `shutdownNow()`         | `Shutdown()` / `ShutdownNow()`                 |
| `awaitTermination(timeout, unit)`      | `AwaitTermination(TimeSpan)` / `AwaitTerminationAsync(TimeSpan, CancellationToken)` |
| `isShutdown()` / `isTerminated()`      | `IsShutdown` / `IsTerminated`                  |
| `close()`                              | `Dispose()` / `DisposeAsync()`                 |
| `RejectedExecutionException`           | `RejectedExecutionException`                   |

## Roadmap

See [CHANGELOG.md](CHANGELOG.md) for released features. Planned:

- `InvokeAll` / `InvokeAny`
- Cancellation-aware overloads (`Func<CancellationToken, T>`) and `ShutdownNow` propagating a token
- Async task overloads (`Func<Task>`, `Func<Task<T>>`)
- Bounded queues with rejection policies (`Abort`, `CallerRuns`, `Discard`, `DiscardOldest`)
- `Executors.NewCachedThreadPool()` with core / max pool size and keep-alive
- `IScheduledExecutorService` (`Schedule`, `ScheduleAtFixedRate`, `ScheduleWithFixedDelay`)
- `TaskScheduler` adapter so `Task.Factory.StartNew` can target an executor

## Contributing

Contributions are welcome. Please read [CONTRIBUTING.md](CONTRIBUTING.md) and the [Code of Conduct](CODE_OF_CONDUCT.md).

## License

[MIT](LICENSE)
