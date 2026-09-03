using System.Collections.Concurrent;

using ExecutorService.Internal;

namespace ExecutorService;

/// <summary>
/// An <see cref="IExecutorService"/> that executes each submitted task using one of a fixed number
/// of dedicated worker threads, fed from an unbounded FIFO queue.
/// Mirrors the fixed-size configuration of <c>java.util.concurrent.ThreadPoolExecutor</c>.
/// </summary>
/// <remarks>
/// <para>
/// Worker threads are created eagerly in the constructor and live until the executor terminates.
/// Because they are dedicated threads (not the shared .NET <see cref="ThreadPool"/>), blocking
/// work does not starve the rest of the process.
/// </para>
/// <para>
/// Exceptions thrown by tasks are captured in the returned <see cref="Task"/>. For
/// <see cref="Execute"/>, which returns nothing, exceptions are silently observed and dropped.
/// </para>
/// </remarks>
public sealed class ThreadPoolExecutor : IExecutorService
{
    private const int Running = 0;
    private const int ShuttingDown = 1;
    private const int Stopped = 2;

    private readonly BlockingCollection<WorkItem> _queue = new(new ConcurrentQueue<WorkItem>());
    private readonly Thread[] _workers;
    private readonly TaskCompletionSource _terminated = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _state = Running;
    private int _liveWorkers;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThreadPoolExecutor"/> class with a fixed number of threads.
    /// </summary>
    /// <param name="threadCount">Number of worker threads. Must be at least 1.</param>
    /// <param name="options">Thread creation options, or <see langword="null"/> for defaults.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="threadCount"/> is less than 1.</exception>
    public ThreadPoolExecutor(int threadCount, ThreadPoolExecutorOptions? options = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(threadCount, 1);
        options ??= new ThreadPoolExecutorOptions();

        _workers = new Thread[threadCount];
        _liveWorkers = threadCount;

        for (var i = 0; i < threadCount; i++)
        {
            var worker = new Thread(WorkerLoop)
            {
                Name = $"{options.ThreadNamePrefix}-{i}",
                IsBackground = options.IsBackground,
                Priority = options.Priority,
            };
            _workers[i] = worker;
            worker.Start();
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
    public void Execute(Action command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Enqueue(new ActionWorkItem(command));
    }

    /// <inheritdoc />
    public Task Submit(Action task)
    {
        ArgumentNullException.ThrowIfNull(task);
        var item = new ActionWorkItem(task);
        Enqueue(item);
        return item.Task;
    }

    /// <inheritdoc />
    public Task<TResult> Submit<TResult>(Func<TResult> task)
    {
        ArgumentNullException.ThrowIfNull(task);
        var item = new FuncWorkItem<TResult>(task);
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
        var previous = Interlocked.Exchange(ref _state, Stopped);
        if (previous == Running)
        {
            _queue.CompleteAdding();
        }

        var pending = new List<Task>();
        while (_queue.TryTake(out var item))
        {
            item.Cancel();
            pending.Add(item.Task);
        }

        return pending;
    }

    /// <inheritdoc />
    public bool AwaitTermination(TimeSpan timeout) => _terminated.Task.Wait(timeout);

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
    /// Shuts down the executor and blocks until all queued tasks finish.
    /// Does not block when called from one of the executor's own worker threads.
    /// </summary>
    public void Dispose()
    {
        Shutdown();
        if (!IsWorkerThread())
        {
            _terminated.Task.Wait();
        }
    }

    /// <summary>
    /// Shuts down the executor and asynchronously waits until all queued tasks finish.
    /// </summary>
    /// <returns>A task that completes when the executor has terminated.</returns>
    public async ValueTask DisposeAsync()
    {
        Shutdown();
        await _terminated.Task.ConfigureAwait(false);
    }

    private void Enqueue(WorkItem item)
    {
        // The queue's completed state is the single source of truth for rejection: both Shutdown and
        // ShutdownNow complete it, and BlockingCollection.Add is atomic with respect to CompleteAdding.
        try
        {
            _queue.Add(item);
        }
        catch (InvalidOperationException ex)
        {
            throw new RejectedExecutionException("Task rejected: the executor has been shut down.", ex);
        }
    }

    private bool IsWorkerThread()
    {
        var current = Thread.CurrentThread;
        foreach (var worker in _workers)
        {
            if (ReferenceEquals(worker, current))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Runs <paramref name="item"/> on the calling thread, or cancels it without running when
    /// <see cref="ShutdownNow"/> has already been called (the worker dequeued it before the drain).
    /// </summary>
    internal void Dispatch(WorkItem item)
    {
        if (Volatile.Read(ref _state) == Stopped)
        {
            item.Cancel();
            return;
        }

        item.Run();
    }

    private void WorkerLoop()
    {
        try
        {
            foreach (var item in _queue.GetConsumingEnumerable())
            {
                Dispatch(item);
            }
        }
        finally
        {
            // The queue is intentionally never disposed: ShutdownNow and QueuedCount remain valid
            // after termination, and BlockingCollection holds no OS handles unless a WaitHandle is requested.
            if (Interlocked.Decrement(ref _liveWorkers) == 0)
            {
                _terminated.TrySetResult();
            }
        }
    }
}
