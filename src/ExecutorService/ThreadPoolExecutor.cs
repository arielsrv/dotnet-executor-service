using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

using ExecutorService.Internal;

namespace ExecutorService;

/// <summary>
///     An <see cref="IExecutorService" /> that executes each submitted task using one of a fixed number
///     of dedicated worker threads, fed from an unbounded FIFO queue.
///     Mirrors the fixed-size configuration of <c>java.util.concurrent.ThreadPoolExecutor</c>.
/// </summary>
/// <remarks>
///     <para>
///         Worker threads are created eagerly in the constructor and live until the executor terminates.
///         Because they are dedicated threads (not the shared .NET <see cref="ThreadPool" />), blocking
///         work does not starve the rest of the process.
///     </para>
///     <para>
///         Exceptions thrown by tasks are captured in the returned <see cref="Task" />. For
///         <see cref="Execute" />, which returns nothing, exceptions are silently observed and dropped.
///     </para>
/// </remarks>
public sealed class ThreadPoolExecutor : IExecutorService
{
    private const int Running = 0;
    private const int ShuttingDown = 1;
    private const int Stopped = 2;

    /// <summary>
    ///     The name of the <see cref="Meter" /> this executor publishes to. Pass it to your telemetry
    ///     pipeline, for example <c>AddMeter(ThreadPoolExecutor.MeterName)</c>.
    /// </summary>
    public const string MeterName = "ExecutorService";

    private readonly ExecutorMetrics _metrics;
    private readonly BlockingCollection<WorkItem> _queue = new(new ConcurrentQueue<WorkItem>());

    // Intentionally never disposed, for the same reason as the queue: ShutdownToken stays observable
    // after termination, and disposing the source would make Token throw ObjectDisposedException.
    // No OS handle is held unless a caller asks for the token's WaitHandle.
    private readonly CancellationTokenSource _shutdownNow = new();
    private readonly TaskCompletionSource _terminated = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread[] _workers;
    private int _liveWorkers;
    private int _state = Running;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ThreadPoolExecutor" /> class with a fixed number of threads.
    /// </summary>
    /// <param name="threadCount">Number of worker threads. Must be at least 1.</param>
    /// <param name="options">Thread creation options, or <see langword="null" /> for defaults.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="threadCount" /> is less than 1.</exception>
    public ThreadPoolExecutor(int threadCount, ThreadPoolExecutorOptions? options = null)
    {
        Throw.IfLessThan(threadCount, 1);
        options ??= new ThreadPoolExecutorOptions();

        _workers = new Thread[threadCount];
        _liveWorkers = threadCount;
        _metrics = new ExecutorMetrics(options.ThreadNamePrefix, options.Meter, () => QueuedCount, () => ThreadCount);

        for (int i = 0; i < threadCount; i++)
        {
            Thread worker = new(WorkerLoop)
            {
                Name = $"{options.ThreadNamePrefix}-{i}",
                IsBackground = options.IsBackground,
                Priority = options.Priority
            };
            _workers[i] = worker;
            worker.StartWithoutContextFlow();
        }
    }

    /// <summary>Gets the fixed number of worker threads.</summary>
    public int ThreadCount => _workers.Length;

    /// <summary>Gets the approximate number of tasks waiting to be executed.</summary>
    public int QueuedCount => _queue.Count;

    /// <summary>Gets a task that completes when the executor terminates.</summary>
    public Task Termination => _terminated.Task;

    /// <inheritdoc />
    public bool IsShutdown => Volatile.Read(ref _state) != Running;

    /// <inheritdoc />
    public bool IsTerminated => _terminated.Task.IsCompleted;

    /// <inheritdoc />
    public CancellationToken ShutdownToken => _shutdownNow.Token;

    /// <inheritdoc />
    public void Execute(Action command)
    {
        Throw.IfNull(command);
        Enqueue(new ActionWorkItem(command));
    }

    /// <inheritdoc />
    public Task Submit(Action task)
    {
        Throw.IfNull(task);
        ActionWorkItem item = new(task);
        Enqueue(item);
        return item.Task;
    }

    /// <inheritdoc />
    public Task<TResult> Submit<TResult>(Func<TResult> task)
    {
        Throw.IfNull(task);
        FuncWorkItem<TResult> item = new(task);
        Enqueue(item);
        return item.TypedTask;
    }

    /// <inheritdoc />
    public Task Submit(Func<Task> task)
    {
        Throw.IfNull(task);

        // Blocking the worker is deliberate: it is what makes ThreadCount an upper bound on how many of
        // these run concurrently. GetResult also unwraps the exception instead of aggregating it.
        ActionWorkItem item = new(() => task().GetAwaiter().GetResult());
        Enqueue(item);
        return item.Task;
    }

    /// <inheritdoc />
    public Task<TResult> Submit<TResult>(Func<Task<TResult>> task)
    {
        Throw.IfNull(task);
        FuncWorkItem<TResult> item = new(() => task().GetAwaiter().GetResult());
        Enqueue(item);
        return item.TypedTask;
    }

    /// <inheritdoc />
    public void Shutdown()
    {
        if (Interlocked.CompareExchange(ref _state, ShuttingDown, Running) == Running)
        {
            _queue.CompleteAdding();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<Task> ShutdownNow()
    {
        int previous = Interlocked.Exchange(ref _state, Stopped);
        if (previous == Running)
        {
            _queue.CompleteAdding();
        }

        // Signalled before draining so tasks already running observe it as early as possible.
        // Idempotent: cancelling an already-cancelled source is a no-op.
        _shutdownNow.Cancel();

        List<Task> pending = new();
        while (_queue.TryTake(out WorkItem? item))
        {
            item.Cancel();
            _metrics.TaskCompleted(item.Task.Status);
            pending.Add(item.Task);
        }

        return pending;
    }

    /// <inheritdoc />
    public bool AwaitTermination(TimeSpan timeout)
    {
        return _terminated.Task.Wait(timeout);
    }

    /// <inheritdoc />
    public async Task<bool> AwaitTerminationAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        try
        {
            await _terminated.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    ///     Shuts down the executor and blocks until all queued tasks finish.
    ///     Does not block when called from one of the executor's own worker threads.
    /// </summary>
    public void Dispose()
    {
        Shutdown();
        if (!IsWorkerThread())
        {
            // Deliberately uncancellable: Dispose must not return until the queue has drained. The only
            // token in reach is ShutdownToken, which fires on ShutdownNow — waiting on it would hand
            // back a half-torn-down executor.
            // ReSharper disable once MethodSupportsCancellation
            _terminated.Task.Wait();
        }
    }

    /// <summary>
    ///     Shuts down the executor and asynchronously waits until all queued tasks finish.
    ///     Does not wait when called from one of the executor's own worker threads.
    /// </summary>
    /// <returns>A task that completes when the executor has terminated.</returns>
    public async ValueTask DisposeAsync()
    {
        Shutdown();

        // Same guard as Dispose, and for a sharper reason: Submit(Func<Task>) blocks its worker on
        // GetAwaiter().GetResult(), so awaiting termination from that worker would keep it alive
        // forever and _terminated would never complete. Evaluated before the first await, so it
        // still observes the caller's thread.
        if (!IsWorkerThread())
        {
            await _terminated.Task.ConfigureAwait(false);
        }
    }

    private void Enqueue(WorkItem item)
    {
        // Captured per submission, not once per worker thread: ExecutionContext.Capture returns null
        // when the caller suppressed flow, which is the standard opt-out.
        item.Context = ExecutionContext.Capture();

        if (_metrics.QueueDurationEnabled)
        {
            item.EnqueuedTimestamp = Stopwatch.GetTimestamp();
        }

        // The queue's completed state is the single source of truth for rejection: both Shutdown and
        // ShutdownNow complete it, and BlockingCollection.Add is atomic with respect to CompleteAdding.
        // No token on the Add either: the queue is unbounded, so it never blocks, and a submission
        // racing ShutdownNow has to surface as RejectedExecutionException, not as a cancellation.
        try
        {
            // ReSharper disable once MethodSupportsCancellation
            _queue.Add(item);
        }
        catch (InvalidOperationException ex)
        {
            _metrics.TaskRejected();
            throw new RejectedExecutionException("Task rejected: the executor has been shut down.", ex);
        }

        _metrics.TaskSubmitted();
    }

    private bool IsWorkerThread()
    {
        Thread current = Thread.CurrentThread;
        return _workers.Any(worker => ReferenceEquals(worker, current));
    }

    /// <summary>
    ///     Runs <paramref name="item" /> on the calling thread, or cancels it without running when
    ///     <see cref="ShutdownNow" /> has already been called (the worker dequeued it before the drain).
    /// </summary>
    internal void Dispatch(WorkItem item)
    {
        if (item.EnqueuedTimestamp is { } enqueued)
        {
            _metrics.RecordQueueDuration(Clock.ElapsedSince(enqueued));
        }

        if (Volatile.Read(ref _state) == Stopped)
        {
            item.Cancel();
            _metrics.TaskCompleted(item.Task.Status);
            return;
        }

        long? started = _metrics.ExecutionDurationEnabled ? Stopwatch.GetTimestamp() : null;
        item.Run();
        if (started is { } start)
        {
            _metrics.RecordExecutionDuration(Clock.ElapsedSince(start));
        }

        _metrics.TaskCompleted(item.Task.Status);
    }

    private void WorkerLoop()
    {
        try
        {
            foreach (WorkItem item in _queue.GetConsumingEnumerable())
            {
                Dispatch(item);
            }
        }
        finally
        {
            // The queue is intentionally never disposed of: ShutdownNow and QueuedCount remain valid
            // after termination, and BlockingCollection holds no OS handles unless a WaitHandle is requested.
            if (Interlocked.Decrement(ref _liveWorkers) == 0)
            {
                // Disposing the meter unregisters the observable gauges, so this terminated executor
                // is not kept alive by their callbacks. Shutdown alone never reaches Dispose().
                _metrics.Dispose();
                _terminated.TrySetResult();
            }
        }
    }
}
