using ExecutorService.Internal;

namespace ExecutorService.Tests;

public sealed class ThreadPoolExecutorTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Constructor_RejectsNonPositiveThreadCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThreadPoolExecutor(0));
    }

    [Fact]
    public async Task Submit_Func_ReturnsResult()
    {
        using ThreadPoolExecutor executor = new(2);

        int result = await executor.Submit(() => 21 * 2).WaitAsync(Timeout, Ct);

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task Submit_Action_RunsOnNamedWorkerThread()
    {
        using ThreadPoolExecutor executor = new(1, new ThreadPoolExecutorOptions { ThreadNamePrefix = "unit" });
        string? threadName = null;

        await executor.Submit(() => threadName = Thread.CurrentThread.Name).WaitAsync(Timeout, Ct);

        Assert.Equal("unit-0", threadName);
    }

    [Fact]
    public async Task Submit_ExceptionIsSurfacedThroughTask()
    {
        using ThreadPoolExecutor executor = new(1);

        static int Boom() => throw new InvalidOperationException("boom");

        Task<int> task = executor.Submit(Boom);

        InvalidOperationException ex =
            await Assert.ThrowsAsync<InvalidOperationException>(() => task.WaitAsync(Timeout, Ct));
        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    public async Task Submit_OperationCanceledExceptionCancelsTask()
    {
        using ThreadPoolExecutor executor = new(1);

        Task task = executor.Submit(() => throw new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(Timeout, Ct));
        Assert.True(task.IsCanceled);
    }

    [Fact]
    public async Task Submit_Func_OperationCanceledExceptionCancelsTask()
    {
        using ThreadPoolExecutor executor = new(1);

        static int Cancel() => throw new OperationCanceledException();

        Task<int> task = executor.Submit(Cancel);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(Timeout, Ct));
        Assert.True(task.IsCanceled);
    }

    [Fact]
    public void Submit_NullThrowsArgumentNull()
    {
        using ThreadPoolExecutor executor = new(1);

#pragma warning disable xUnit2014 // Submit validates arguments synchronously before returning a Task.
        Assert.Throws<ArgumentNullException>(() => { executor.Submit(null!); });
        Assert.Throws<ArgumentNullException>(() => { executor.Submit((Func<int>)null!); });
#pragma warning restore xUnit2014
        Assert.Throws<ArgumentNullException>(() => executor.Execute(null!));
    }

    [Fact]
    public void Execute_RunsCommand()
    {
        using ThreadPoolExecutor executor = new(1);
        using ManualResetEventSlim ran = new();

        executor.Execute(ran.Set);

        Assert.True(ran.Wait(Timeout, Ct));
    }

    [Fact]
    public async Task Execute_ExceptionDoesNotKillWorker()
    {
        using ThreadPoolExecutor executor = new(1);

        executor.Execute(() => throw new InvalidOperationException());
        string result = await executor.Submit(() => "alive").WaitAsync(Timeout, Ct);

        Assert.Equal("alive", result);
    }

    [Fact]
    public async Task FixedPool_NeverExceedsThreadCount()
    {
        const int threads = 2;
        const int tasks = 20;
        using ThreadPoolExecutor executor = new(threads);
        int concurrent = 0;
        int maxConcurrent = 0;

        IEnumerable<Task> all = Enumerable.Range(0, tasks).Select(_ => executor.Submit(() =>
        {
            int now = Interlocked.Increment(ref concurrent);
            InterlockedMax(ref maxConcurrent, now);
            Thread.Sleep(10);
            Interlocked.Decrement(ref concurrent);
        }));

        await Task.WhenAll(all).WaitAsync(Timeout, Ct);

        Assert.InRange(maxConcurrent, 1, threads);
    }

    [Fact]
    public void QueuedCount_ReflectsWaitingTasks()
    {
        ThreadPoolExecutor executor = new(1);
        using ManualResetEventSlim started = new();
        using ManualResetEventSlim gate = new();
        executor.Execute(() =>
        {
            started.Set();
            gate.Wait(Timeout, Ct);
        });
        Assert.True(started.Wait(Timeout, Ct));
        Assert.Equal(0, executor.QueuedCount);

        executor.Execute(() => { });
        executor.Execute(() => { });

        Assert.Equal(2, executor.QueuedCount);
        gate.Set();
        executor.Dispose();
        Assert.Equal(0, executor.QueuedCount);
    }

    [Fact]
    public async Task Termination_CompletesWhenExecutorTerminates()
    {
        ThreadPoolExecutor executor = new(1);
        Task termination = executor.Termination;

        Assert.False(termination.IsCompleted);
        executor.Shutdown();

        await termination.WaitAsync(Timeout, Ct);
        Assert.Same(termination, executor.Termination);
        Assert.True(executor.IsTerminated);
    }

    [Fact]
    public async Task Shutdown_RejectsNewTasks_ButRunsQueuedOnes()
    {
        ThreadPoolExecutor executor = new(1);
        using ManualResetEventSlim gate = new();
        Task<bool> blocker = executor.Submit(() => gate.Wait(Timeout, Ct));
        Task<string> queued = executor.Submit(() => "queued");

        executor.Shutdown();

        Assert.True(executor.IsShutdown);
        Assert.False(executor.IsTerminated);
#pragma warning disable xUnit2014 // Rejection is thrown synchronously at submission time, not inside the Task.
        Assert.Throws<RejectedExecutionException>(() => { executor.Submit(() => 1); });
#pragma warning restore xUnit2014
        RejectedExecutionException rejected =
            Assert.Throws<RejectedExecutionException>(() => executor.Execute(() => { }));
        Assert.IsType<InvalidOperationException>(rejected.InnerException);

        gate.Set();
        Assert.Equal("queued", await queued.WaitAsync(Timeout, Ct));
        Assert.True(await executor.AwaitTerminationAsync(Timeout, Ct));
        Assert.True(executor.IsTerminated);
        await blocker;
    }

    [Fact]
    public async Task ShutdownNow_CancelsPendingTasksAndReturnsThem()
    {
        ThreadPoolExecutor executor = new(1);
        using ManualResetEventSlim started = new();
        using ManualResetEventSlim gate = new();
        Task running = executor.Submit(() =>
        {
            started.Set();
            gate.Wait(Timeout, Ct);
        });
        Assert.True(started.Wait(Timeout, Ct));
        Task<int> pendingA = executor.Submit(() => 1);
        Task pendingB = executor.Submit(() => { });
        bool executed = false;
        executor.Execute(() => executed = true);

        IReadOnlyList<Task> dropped = executor.ShutdownNow();

        Assert.Equal(3, dropped.Count);
        Assert.All(dropped, t => Assert.True(t.IsCanceled));
        Assert.True(pendingA.IsCanceled);
        Assert.True(pendingB.IsCanceled);
        Assert.False(executed);
        Assert.True(executor.IsShutdown);

        gate.Set();
        await running.WaitAsync(Timeout, Ct);
        Assert.True(await executor.AwaitTerminationAsync(Timeout, Ct));
    }

    [Fact]
    public void ShutdownNow_AfterShutdown_StillDrainsQueue()
    {
        ThreadPoolExecutor executor = new(1);
        using ManualResetEventSlim started = new();
        using ManualResetEventSlim gate = new();
        _ = executor.Submit(() =>
        {
            started.Set();
            gate.Wait(Timeout, Ct);
        });
        Assert.True(started.Wait(Timeout, Ct));
        Task<int> pending = executor.Submit(() => 1);

        executor.Shutdown();
        IReadOnlyList<Task> dropped = executor.ShutdownNow();

        Assert.Single(dropped);
        Assert.True(pending.IsCanceled);
        gate.Set();
        Assert.True(executor.AwaitTermination(Timeout));
    }

    [Fact]
    public void Dispatch_AfterShutdownNow_CancelsInsteadOfRunning()
    {
        ThreadPoolExecutor executor = new(1);
        executor.ShutdownNow();
        Assert.True(executor.AwaitTermination(Timeout));
        bool ran = false;
        ActionWorkItem item = new(() => ran = true);

        executor.Dispatch(item);

        Assert.True(item.Task.IsCanceled);
        Assert.False(ran);
    }

    [Fact]
    public async Task Dispatch_WhileRunning_RunsItem()
    {
        using ThreadPoolExecutor executor = new(1);
        FuncWorkItem<int> item = new(() => 42);

        executor.Dispatch(item);

        Assert.Equal(42, await item.TypedTask.WaitAsync(Timeout, Ct));
    }

    [Fact]
    public void ShutdownNow_AfterTermination_ReturnsEmptyList()
    {
        ThreadPoolExecutor executor = new(1);
        executor.Shutdown();
        Assert.True(executor.AwaitTermination(Timeout));

        IReadOnlyList<Task> dropped = executor.ShutdownNow();

        Assert.Empty(dropped);
        Assert.True(executor.IsTerminated);
    }

    [Fact]
    public void AwaitTermination_TimesOutWhileRunning()
    {
        using ThreadPoolExecutor executor = new(1);

        Assert.False(executor.AwaitTermination(TimeSpan.FromMilliseconds(20)));
        Assert.False(executor.IsTerminated);
    }

    [Fact]
    public async Task AwaitTerminationAsync_ReturnsFalseOnTimeout_TrueAfterShutdown()
    {
        ThreadPoolExecutor executor = new(1);

        Assert.False(await executor.AwaitTerminationAsync(TimeSpan.FromMilliseconds(20), Ct));

        executor.Shutdown();

        Assert.True(await executor.AwaitTerminationAsync(Timeout, Ct));
    }

    [Fact]
    public void Dispose_ShutsDownAndWaitsForQueuedWork()
    {
        ThreadPoolExecutor executor = new(2);
        int completed = 0;
        for (int i = 0; i < 10; i++)
        {
            executor.Execute(() =>
            {
                Thread.Sleep(5);
                Interlocked.Increment(ref completed);
            });
        }

        executor.Dispose();

        Assert.True(executor.IsTerminated);
        Assert.Equal(10, completed);
    }

    [Fact]
    public async Task DisposeAsync_ShutsDownAndWaits()
    {
        ThreadPoolExecutor executor = new(1);
        Task<int> task = executor.Submit(() => 7);

        await executor.DisposeAsync();

        Assert.True(executor.IsTerminated);
        Assert.Equal(7, await task);
    }

    [Fact]
    public async Task Dispose_FromWorkerThread_DoesNotDeadlock()
    {
        ThreadPoolExecutor executor = new(1);

        Task task = executor.Submit(executor.Dispose);

        await task.WaitAsync(Timeout, Ct);
        Assert.True(await executor.AwaitTerminationAsync(Timeout, Ct));
    }

    [Fact]
    public async Task DisposeAsync_FromWorkerThread_DoesNotDeadlock()
    {
        ThreadPoolExecutor executor = new(1);

        // Submit(Func<Task>) blocks the worker on the returned task, so a DisposeAsync that awaited
        // termination here would wait on the very thread that has to finish first.
        Task task = executor.Submit(async () => await executor.DisposeAsync());

        await task.WaitAsync(Timeout, Ct);
        Assert.True(await executor.AwaitTerminationAsync(Timeout, Ct));
    }

    [Fact]
    public void Shutdown_IsIdempotent()
    {
        ThreadPoolExecutor executor = new(1);

        executor.Shutdown();
        executor.Shutdown();

        Assert.True(executor.AwaitTermination(Timeout));
    }

    [Fact]
    public void ShutdownToken_IsNotCanceledWhileRunning()
    {
        using ThreadPoolExecutor executor = new(1);

        Assert.True(executor.ShutdownToken.CanBeCanceled);
        Assert.False(executor.ShutdownToken.IsCancellationRequested);
    }

    [Fact]
    public void Shutdown_DoesNotCancelShutdownToken()
    {
        ThreadPoolExecutor executor = new(1);

        executor.Shutdown();

        Assert.True(executor.AwaitTermination(Timeout));
        Assert.False(executor.ShutdownToken.IsCancellationRequested);
    }

    [Fact]
    public async Task ShutdownNow_CancelsShutdownTokenSoRunningTaskStopsCooperatively()
    {
        ThreadPoolExecutor executor = new(1);
        using ManualResetEventSlim started = new();
        Task running = executor.Submit(() =>
        {
            started.Set();
            while (!executor.ShutdownToken.IsCancellationRequested)
            {
                Thread.Sleep(1);
            }

            executor.ShutdownToken.ThrowIfCancellationRequested();
        });
        Assert.True(started.Wait(Timeout, Ct));

        executor.ShutdownNow();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(Timeout, Ct));
        Assert.True(running.IsCanceled);
        Assert.True(await executor.AwaitTerminationAsync(Timeout, Ct));
    }

    [Fact]
    public void ShutdownToken_RemainsObservableAfterTermination()
    {
        ThreadPoolExecutor executor = new(1);

        executor.ShutdownNow();

        Assert.True(executor.AwaitTermination(Timeout));
        Assert.True(executor.ShutdownToken.IsCancellationRequested);
    }

    private static void InterlockedMax(ref int location, int value)
    {
        int current;
        do
        {
            current = Volatile.Read(ref location);
            if (value <= current)
            {
                return;
            }
        } while (Interlocked.CompareExchange(ref location, value, current) != current);
    }
}
