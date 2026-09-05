# ExecutorService

[![CI](https://github.com/arielsrv/dotnet-executor-service/actions/workflows/ci.yml/badge.svg)](https://github.com/arielsrv/dotnet-executor-service/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/ExecutorService.svg)](https://www.nuget.org/packages/ExecutorService)
[![NuGet Downloads](https://img.shields.io/nuget/dt/ExecutorService.svg)](https://www.nuget.org/packages/ExecutorService)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A .NET port of Java's
[`java.util.concurrent.ExecutorService`](https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/util/concurrent/ExecutorService.html):
fixed-size pools of **dedicated threads**, an explicit **lifecycle** (`Shutdown`, `ShutdownNow`,
`AwaitTermination`), and `Task`-based futures.

## Why?

The .NET `ThreadPool` is a single, process-wide, elastic pool. That is the right default for async I/O, but sometimes
you want what Java developers reach for with `Executors.newFixedThreadPool(n)`:

- A **fixed number of threads** for a specific workload, isolated from the rest of the process.
- Safe **blocking work** without starving the shared pool.
- A **deterministic shutdown**: stop accepting work, drain or drop the queue, wait for termination.
- **Strict FIFO** and, with one thread, **strictly sequential** execution.

## Installation

```shell
dotnet add package ExecutorService
```

Targets **.NET 8** and **.NET 10**, with no dependencies on either.

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

[`samples/ExecutorService.QuickStart.Sample`](samples/ExecutorService.QuickStart.Sample) runs all of this as a
console smoke test against the package as published on nuget.org (`task quickstart`).

Disposing the executor is equivalent to Java's `close()`: it calls `Shutdown()` and waits for termination.
`await using` does the same without blocking.

### Submitting asynchronous work

Pass the `async` delegate directly and the returned task tracks the work, not just its start:

```csharp
Task<int> future = executor.Submit(async () =>
{
    await using var connection = await OpenAsync();
    return await connection.QueryAsync();
});
```

The worker thread stays occupied until that work completes, which is the reason to route async work through
an executor at all: **the thread count becomes a concurrency limit**. A four-thread pool runs at most four of
these at a time, no matter how many you submit — useful for rate-limiting calls to a dependency that would
otherwise be hammered by unbounded `Task.Run`.

Note that `Execute` takes an `Action`, so `executor.Execute(() => WorkAsync())` starts the work and forgets
it: nothing waits for it and nothing observes its exceptions. Use `Submit` for async work.

### Dropping pending work

```csharp
IReadOnlyList<Task> neverStarted = executor.ShutdownNow();
// each Task in `neverStarted` is in the Canceled state
```

Tasks that are already running are **not** interrupted, because .NET has no thread interruption. They can stop
cooperatively by observing `ShutdownToken`, which `ShutdownNow()` cancels:

```csharp
var executor = Executors.NewFixedThreadPool(4);

executor.Submit(() =>
{
    while (!executor.ShutdownToken.IsCancellationRequested)
    {
        ProcessNextBatch();
    }

    executor.ShutdownToken.ThrowIfCancellationRequested();   // the Task ends up Canceled
});
```

The graceful `Shutdown()` never cancels that token: queued tasks are allowed to finish.

### Configuring worker threads

```csharp
var executor = Executors.NewFixedThreadPool(2, new ThreadPoolExecutorOptions
{
    ThreadNamePrefix = "image-resizer",   // threads are named image-resizer-0, image-resizer-1
    IsBackground = false,                 // keep the process alive while work is pending
    Priority = ThreadPriority.BelowNormal,
});
```

## Metrics

The executor publishes metrics through `System.Diagnostics.Metrics`, the in-box OpenTelemetry metrics API.
**No extra package reference is needed** — and none is imposed on you: this library takes no dependency on any
telemetry SDK, so your application picks the exporter.

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter(ThreadPoolExecutor.MeterName));
```

That is all it takes to reach Prometheus / Grafana, Azure Monitor, or any OTLP backend such as New Relic,
Datadog or Honeycomb. To look without any pipeline at all:

```shell
dotnet-counters monitor --process-id <pid> --counters ExecutorService
```

| Instrument                          | Kind      | Unit       | Meaning                                         |
|-------------------------------------|-----------|------------|-------------------------------------------------|
| `executor.tasks.queued`             | Gauge     | `{task}`   | Tasks waiting to be executed                    |
| `executor.threads`                  | Gauge     | `{thread}` | Worker threads owned by the executor            |
| `executor.tasks.submitted`          | Counter   | `{task}`   | Tasks accepted for execution                    |
| `executor.tasks.completed`          | Counter   | `{task}`   | Terminal tasks, tagged by outcome               |
| `executor.tasks.rejected`           | Counter   | `{task}`   | Submissions refused after shutdown              |
| `executor.task.queue.duration`      | Histogram | `s`        | Time a task waited in the queue before starting |
| `executor.task.execution.duration`  | Histogram | `s`        | Time a task spent executing                     |

Every measurement carries an `executor.name` tag, taken from `ThreadNamePrefix`, so several executors in one
process stay apart. `executor.tasks.completed` adds `executor.task.status` with `success`, `faulted` or
`canceled`.

Watch `executor.task.queue.duration` above all: a fixed pool over an unbounded queue absorbs overload silently,
and queue latency is what tells you the pool is undersized before anything downstream times out.

Histograms are only timestamped while something is listening, so the cost of leaving metrics unobserved is one
boolean read per task. To scope metrics to a dependency injection container, hand the executor a meter of your
own:

```csharp
new ThreadPoolExecutorOptions { Meter = meterFactory.Create(ThreadPoolExecutor.MeterName) }
```

A supplied meter is never disposed by the executor; the one it creates for itself is released when it terminates.

### Seeing it in action

[`samples/ExecutorService.Metrics.Sample`](samples/ExecutorService.Metrics.Sample) drives an executor under
synthetic load until every instrument has moved — including the rejected and canceled paths, which a healthy
workload never reaches:

```shell
task metrics            # OpenTelemetry console exporter, 30 seconds
task metrics:counters   # live dotnet-counters display, until Ctrl+C
```

One caveat it makes concrete: both duration instruments are in seconds, while OpenTelemetry's default
histogram buckets span 0 to 10000 and are sized for milliseconds. Without a view supplying second-scaled
boundaries, every measurement lands in the first bucket. See the
[sample README](samples/ExecutorService.Metrics.Sample/README.md) for the configuration.

## Ambient context

Submitted work runs under the caller's `ExecutionContext`, captured per submission, so `AsyncLocal<T>` values
reach it exactly as they would through `Task.Run`. That includes `Activity.Current`, which means spans started
inside a task are parented correctly and traces stay connected:

```csharp
using var parent = source.StartActivity("import");
executor.Submit(() =>
{
    using var child = source.StartActivity("import-row");   // child of "import"
    ImportRow();
});
```

To opt out — the same way you would for any other .NET scheduling primitive — suppress the flow around the
submission:

```csharp
using (ExecutionContext.SuppressFlow())
{
    executor.Submit(Work);   // runs with a clean context
}
```

## Java to .NET mapping

| Java                                    | ExecutorService (.NET)                                                              |
|-----------------------------------------|-------------------------------------------------------------------------------------|
| `Executor`                              | `IExecutor`                                                                         |
| `ExecutorService`                       | `IExecutorService`                                                                  |
| `Executors.newFixedThreadPool(n)`       | `Executors.NewFixedThreadPool(n)`                                                   |
| `Executors.newSingleThreadExecutor()`   | `Executors.NewSingleThreadExecutor()`                                               |
| `execute(Runnable)`                     | `Execute(Action)`                                                                   |
| `submit(Runnable)` / `submit(Callable)` | `Submit(Action)` / `Submit<T>(Func<T>)`                                             |
| `Future<T>`                             | `Task<T>`                                                                           |
| *(no equivalent)*                       | `Submit(Func<Task>)` / `Submit<T>(Func<Task<T>>)`                                   |
| `shutdown()` / `shutdownNow()`          | `Shutdown()` / `ShutdownNow()`                                                      |
| `awaitTermination(timeout, unit)`       | `AwaitTermination(TimeSpan)` / `AwaitTerminationAsync(TimeSpan, CancellationToken)` |
| `isShutdown()` / `isTerminated()`       | `IsShutdown` / `IsTerminated`                                                       |
| `close()`                               | `Dispose()` / `DisposeAsync()`                                                      |
| `RejectedExecutionException`            | `RejectedExecutionException`                                                        |

## Roadmap

See [CHANGELOG.md](CHANGELOG.md) for released features. Planned:

- `InvokeAll` / `InvokeAny`
- Cancellation-aware overloads (`Action<CancellationToken>`, `Func<CancellationToken, T>`)
- Bounded queues with rejection policies (`Abort`, `CallerRuns`, `Discard`, `DiscardOldest`)
- `Executors.NewCachedThreadPool()` with core / max pool size and keep-alive
- `IScheduledExecutorService` (`Schedule`, `ScheduleAtFixedRate`, `ScheduleWithFixedDelay`)
- `TaskScheduler` adapter so `Task.Factory.StartNew` can target an executor

## Contributing

Contributions are welcome. Please read [CONTRIBUTING.md](CONTRIBUTING.md) and the [Code of Conduct](CODE_OF_CONDUCT.md).

## License

[MIT](LICENSE)
