# Metrics sample

A console app that drives a `ThreadPoolExecutor` under synthetic load so every instrument the library
publishes has something to show — without waiting for a real workload.

The executor publishes through `System.Diagnostics.Metrics` under the meter named
`ThreadPoolExecutor.MeterName`, so both ways of watching below observe the *same* instruments. Nothing
in the library is OpenTelemetry-specific; the SDK reference lives in this sample, not in the package.

## Run it

From the repository root:

```shell
task metrics                 # OpenTelemetry console exporter, 30 seconds
task metrics:counters        # live dotnet-counters display, until Ctrl+C
```

Or directly:

```shell
dotnet run --project samples/ExecutorService.Metrics.Sample -- --duration 15
```

## What the workload does

1. **Steady load** — submits a mix of fast (20 ms), slow (250 ms) and faulting tasks, throttled to hold the
   backlog near `--queue-depth`. Four workers cannot keep up with the peak rate, so `executor.tasks.queued`
   and `executor.task.queue.duration` stay meaningfully above zero instead of flatlining.
2. **`Shutdown()`** — closes the queue, then submits once more. The refusal is the only thing that moves
   `executor.tasks.rejected`.
3. **`ShutdownNow()`** — cancels whatever is still queued, so those land on `executor.tasks.completed` with
   `executor.task.status=canceled` rather than `success`.

By the end all five instruments have moved and `executor.tasks.completed` carries all three status values.

## Options

| Flag             | Default   | Meaning                                                              |
|------------------|-----------|----------------------------------------------------------------------|
| `--exporter`     | `console` | `console` prints metrics to stdout; `none` stays quiet for dotnet-counters |
| `--duration`     | `30`      | Seconds of load; `0` runs until Ctrl+C                               |
| `--interval`     | `5`       | Seconds between console exporter flushes                             |
| `--threads`      | `4`       | Worker threads in the pool                                           |
| `--queue-depth`  | `50`      | Backlog the producer throttles towards                               |

## Reading the output

The exporter is configured with explicit histogram buckets. OpenTelemetry's defaults span 0 to 10000, which
suits milliseconds — both duration instruments here are in **seconds**, so with the defaults every
measurement falls into the first bucket and the histogram tells you nothing. Any real pipeline needs the
same view:

```csharp
.AddView("executor.task.queue.duration", new ExplicitBucketHistogramConfiguration
{
    Boundaries = [0.0001, 0.001, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10]
})
```
