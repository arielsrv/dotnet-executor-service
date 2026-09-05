using BenchmarkDotNet.Attributes;

namespace ExecutorService.Benchmarks;

/// <summary>
///     What one submission costs, in time and in allocations, measured in batches so thread wake-ups are
///     amortised the way they are under real load.
/// </summary>
/// <remarks>
///     <see cref="TaskRun" /> is the baseline, not a rival: it runs on the process-wide
///     <see cref="ThreadPool" />, which grows under load and offers neither FIFO order nor a bound on
///     concurrency. It is here because it is what the same code looks like without this library, so the
///     numbers say what the executor's guarantees cost.
/// </remarks>
[MemoryDiagnoser]
public class SubmissionBenchmarks
{
    private const int Batch = 1_000;

    private static readonly Action NoOp = static () => { };

    private Task[] _drain = null!;
    private IExecutorService _executor = null!;
    private Task[] _tasks = null!;

    /// <summary>Gets or sets the pool width under test: strictly sequential, or a small fixed pool.</summary>
    [Params(1, 4)]
    public int Threads { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _executor = Executors.NewFixedThreadPool(Threads);
        _tasks = new Task[Batch];
        _drain = new Task[Threads];
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _executor.Dispose();
    }

    [Benchmark(Baseline = true, Description = "Task.Run x1000")]
    public void TaskRun()
    {
        for (int i = 0; i < Batch; i++)
        {
            _tasks[i] = Task.Run(NoOp);
        }

        Task.WaitAll(_tasks);
    }

    [Benchmark(Description = "Submit x1000")]
    public void Submit()
    {
        for (int i = 0; i < Batch; i++)
        {
            _tasks[i] = _executor.Submit(NoOp);
        }

        Task.WaitAll(_tasks);
    }

    /// <summary>
    ///     The cheapest path the library has: no future is handed back, so nothing but the queue entry and
    ///     the captured <see cref="ExecutionContext" /> is allocated per call.
    /// </summary>
    [Benchmark(Description = "Execute x1000 (fire and forget)")]
    public void Execute()
    {
        for (int i = 0; i < Batch; i++)
        {
            _executor.Execute(NoOp);
        }

        // One sentinel per worker: FIFO means every one of them is dequeued only after the whole batch
        // is, so nothing from the batch is still running when the measurement stops. Without this the
        // benchmark would stop timing with the queue still full.
        for (int i = 0; i < Threads; i++)
        {
            _drain[i] = _executor.Submit(NoOp);
        }

        Task.WaitAll(_drain);
    }
}
