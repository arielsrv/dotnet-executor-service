using System.Diagnostics;
using System.Globalization;

using ExecutorService;

using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

// Two ways to watch the same instruments, selected with --exporter:
//
//   console  (default)  OpenTelemetry prints every metric to stdout on a fixed interval.
//   none                Nothing is printed, so you can attach dotnet-counters to this process
//                       without the two fighting over the terminal.
//
// Both work because ThreadPoolExecutor publishes through System.Diagnostics.Metrics under the
// meter named ThreadPoolExecutor.MeterName; nothing here is specific to OpenTelemetry.
SampleOptions options = SampleOptions.Parse(args);

using MeterProvider? provider = options.UseConsoleExporter
    ? Sdk.CreateMeterProviderBuilder()
        .ConfigureResource(resource => resource.AddService("executor-sample"))
        .AddMeter(ThreadPoolExecutor.MeterName)
        // Both duration instruments are in seconds, and OpenTelemetry's default explicit buckets run
        // from 0 to 10000 — sized for milliseconds, so every measurement would land in the first
        // bucket. These boundaries span a tenth of a millisecond to ten seconds instead.
        .AddView("executor.task.queue.duration", DurationBuckets())
        .AddView("executor.task.execution.duration", DurationBuckets())
        .AddConsoleExporter((_, readerOptions) =>
            readerOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds =
                (int)options.ExportInterval.TotalMilliseconds)
        .Build()
    : null;

using CancellationTokenSource stopping = new();

// Kept in a variable so it can be unsubscribed below: the handler holds on to `stopping`, and a
// Ctrl+C arriving after this scope disposes the source would call Cancel() on a disposed object.
// The unsubscribe is what makes that safe, and it is also what the analyzer cannot see.
// ReSharper disable once AccessToDisposedClosure
ConsoleCancelEventHandler stopOnCancelKey = (_, e) =>
{
    // Let the scenario wind down through its own shutdown path instead of killing the process.
    e.Cancel = true;
    stopping.Cancel();
};
Console.CancelKeyPress += stopOnCancelKey;

Report($"pid {Environment.ProcessId} — meter '{ThreadPoolExecutor.MeterName}'");
if (!options.UseConsoleExporter)
{
    Report($"attach with: dotnet counters monitor --counters {ThreadPoolExecutor.MeterName} --process-id {Environment.ProcessId}");
}

Report(options.Duration == Timeout.InfiniteTimeSpan
    ? "running until Ctrl+C"
    : $"running for {options.Duration.TotalSeconds:0}s (Ctrl+C to stop early)");

try
{
    await RunAsync(options, stopping.Token).ConfigureAwait(false);
}
finally
{
    Console.CancelKeyPress -= stopOnCancelKey;
}

// Dispose would flush too, but an explicit flush prints the final totals before the process exits
// instead of racing it.
provider?.ForceFlush();
return 0;

static async Task RunAsync(SampleOptions options, CancellationToken stopping)
{
    ThreadPoolExecutor executor = new(options.ThreadCount, new ThreadPoolExecutorOptions
    {
        ThreadNamePrefix = "sample-worker"
    });

    long submitted = 0;

    // Phase 1 — steady load.
    try
    {
        await ProduceAsync().ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
        // Ctrl+C during the load phase: fall through to the shutdown phases below.
    }

    Report($"submitted {submitted} tasks, {executor.QueuedCount} still queued");

    // Phase 2 — Shutdown() closes the queue. The next submission is refused, which is the only way
    // executor.tasks.rejected ever moves.
    executor.Shutdown();
    try
    {
        executor.Execute(() => { });
        Report("expected the post-shutdown submission to be rejected");
    }
    catch (RejectedExecutionException)
    {
        Report("post-shutdown submission rejected (executor.tasks.rejected +1)");
    }

    // Phase 3 — ShutdownNow() cancels whatever is still queued, so those tasks land on
    // executor.tasks.completed with executor.task.status=canceled rather than success.
    IReadOnlyList<Task> abandoned = executor.ShutdownNow();
    Report($"drained {abandoned.Count} queued tasks as canceled");

    await executor.AwaitTerminationAsync(TimeSpan.FromSeconds(30), CancellationToken.None).ConfigureAwait(false);
    Report($"terminated: {executor.IsTerminated}");

    async Task ProduceAsync()
    {
        long deadline = options.Duration == Timeout.InfiniteTimeSpan
            ? long.MaxValue
            : Stopwatch.GetTimestamp() + (long)(options.Duration.TotalSeconds * Stopwatch.Frequency);

        // Deterministic on purpose: the point of the sample is a reproducible shape in the histograms,
        // not real randomness.
        int step = 0;
        while (!stopping.IsCancellationRequested && Stopwatch.GetTimestamp() < deadline)
        {
            step++;
            submitted++;

            switch (step % 10)
            {
                case 0:
                    // One in ten faults, so executor.task.status=faulted is not always zero.
                    _ = Observe(executor.Submit(() => throw new InvalidOperationException("synthetic failure")));
                    break;
                case 3 or 7:
                    // Slow tasks stretch executor.task.execution.duration and grow the backlog.
                    _ = Observe(executor.Submit(() => Thread.Sleep(250)));
                    break;
                default:
                    _ = Observe(executor.Submit(() => Thread.Sleep(20)));
                    break;
            }

            // Submit faster than the pool can drain until the backlog reaches the target depth, then
            // back off below capacity so it drains again. The result oscillates around the target
            // instead of growing without bound, which matters for a sample left running for minutes
            // under dotnet-counters: executor.tasks.queued settles into a band and
            // executor.task.queue.duration stops climbing forever.
            TimeSpan pause = executor.QueuedCount > options.TargetQueueDepth
                ? TimeSpan.FromMilliseconds(40)
                : TimeSpan.FromMilliseconds(5);
            await Task.Delay(pause, CancellationToken.None).ConfigureAwait(false);
        }
    }
}

// Faults are already recorded by the executor's metrics; observing them here only keeps
// TaskScheduler.UnobservedTaskException quiet.
static Task Observe(Task task)
{
    return task.ContinueWith(
        static t => _ = t.Exception,
        CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
        TaskScheduler.Default);
}

// Local function rather than a field: top-level statements cannot declare members, and each view
// needs its own configuration instance.
static ExplicitBucketHistogramConfiguration DurationBuckets()
{
    return new ExplicitBucketHistogramConfiguration
    {
        Boundaries = [0.000_1, 0.001, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10]
    };
}

static void Report(string message)
{
    Console.WriteLine($"[sample] {message}");
}

internal sealed record SampleOptions(
    bool UseConsoleExporter,
    TimeSpan Duration,
    TimeSpan ExportInterval,
    int ThreadCount,
    int TargetQueueDepth)
{
    public static SampleOptions Parse(string[] args)
    {
        bool console = true;
        TimeSpan duration = TimeSpan.FromSeconds(30);
        TimeSpan interval = TimeSpan.FromSeconds(5);
        int threads = 4;
        int queueDepth = 50;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--exporter" when i + 1 < args.Length:
                    console = !string.Equals(args[++i], "none", StringComparison.OrdinalIgnoreCase);
                    break;
                case "--duration" when i + 1 < args.Length:
                    double seconds = double.Parse(args[++i], CultureInfo.InvariantCulture);
                    duration = seconds <= 0 ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(seconds);
                    break;
                case "--interval" when i + 1 < args.Length:
                    interval = TimeSpan.FromSeconds(double.Parse(args[++i], CultureInfo.InvariantCulture));
                    break;
                case "--threads" when i + 1 < args.Length:
                    threads = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--queue-depth" when i + 1 < args.Length:
                    queueDepth = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                default:
                    throw new ArgumentException($"unrecognised argument '{args[i]}'", nameof(args));
            }
        }

        return new SampleOptions(console, duration, interval, threads, queueDepth);
    }
}
