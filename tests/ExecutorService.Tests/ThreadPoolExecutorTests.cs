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
        using var executor = new ThreadPoolExecutor(2);

        var result = await executor.Submit(() => 21 * 2).WaitAsync(Timeout, Ct);

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task Submit_Action_RunsOnNamedWorkerThread()
    {
        using var executor = new ThreadPoolExecutor(1, new ThreadPoolExecutorOptions { ThreadNamePrefix = "unit" });
        string? threadName = null;

        await executor.Submit(() => threadName = Thread.CurrentThread.Name).WaitAsync(Timeout, Ct);

        Assert.Equal("unit-0", threadName);
    }

    [Fact]
    public async Task Submit_ExceptionIsSurfacedThroughTask()
    {
        using var executor = new ThreadPoolExecutor(1);

        var task = executor.Submit<int>(() => throw new InvalidOperationException("boom"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => task.WaitAsync(Timeout, Ct));
        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    public async Task Submit_OperationCanceledExceptionCancelsTask()
    {
        using var executor = new ThreadPoolExecutor(1);

        var task = executor.Submit(() => throw new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(Timeout, Ct));
        Assert.True(task.IsCanceled);
    }

    [Fact]
    public void Submit_NullThrowsArgumentNull()
    {
        using var executor = new ThreadPoolExecutor(1);

#pragma warning disable xUnit2014 // Submit validates arguments synchronously before returning a Task.
        Assert.Throws<ArgumentNullException>(() => { executor.Submit((Action)null!); });
        Assert.Throws<ArgumentNullException>(() => { executor.Submit((Func<int>)null!); });
#pragma warning restore xUnit2014
        Assert.Throws<ArgumentNullException>(() => executor.Execute(null!));
    }

    [Fact]
    public void Execute_RunsCommand()
    {
        using var executor = new ThreadPoolExecutor(1);
        using var ran = new ManualResetEventSlim();

        executor.Execute(ran.Set);

        Assert.True(ran.Wait(Timeout, Ct));
    }

    [Fact]
    public async Task Execute_ExceptionDoesNotKillWorker()
    {
        using var executor = new ThreadPoolExecutor(1);

        executor.Execute(() => throw new InvalidOperationException());
        var result = await executor.Submit(() => "alive").WaitAsync(Timeout, Ct);

        Assert.Equal("alive", result);
    }

    [Fact]
    public async Task FixedPool_NeverExceedsThreadCount()
    {
        const int threads = 2;
        const int tasks = 20;
        using var executor = new ThreadPoolExecutor(threads);
        var concurrent = 0;
        var maxConcurrent = 0;

        var all = Enumerable.Range(0, tasks).Select(_ => executor.Submit(() =>
        {
            var now = Interlocked.Increment(ref concurrent);
            InterlockedMax(ref maxConcurrent, now);
            Thread.Sleep(10);
            Interlocked.Decrement(ref concurrent);
        }));

        await Task.WhenAll(all).WaitAsync(Timeout, Ct);

        Assert.InRange(maxConcurrent, 1, threads);
    }

    [Fact]
    public async Task Shutdown_RejectsNewTasks_ButRunsQueuedOnes()
    {
        var executor = new ThreadPoolExecutor(1);
        using var gate = new ManualResetEventSlim();
        var blocker = executor.Submit(() => gate.Wait(Timeout, Ct));
        var queued = executor.Submit(() => "queued");

        executor.Shutdown();

        Assert.True(executor.IsShutdown);
        Assert.False(executor.IsTerminated);
#pragma warning disable xUnit2014 // Rejection is thrown synchronously at submission time, not inside the Task.
        Assert.Throws<RejectedExecutionException>(() => { executor.Submit(() => 1); });
#pragma warning restore xUnit2014
        Assert.Throws<RejectedExecutionException>(() => executor.Execute(() => { }));

        gate.Set();
        Assert.Equal("queued", await queued.WaitAsync(Timeout, Ct));
        Assert.True(executor.AwaitTermination(Timeout));
        Assert.True(executor.IsTerminated);
        await blocker;
    }

    [Fact]
    public async Task ShutdownNow_CancelsPendingTasksAndReturnsThem()
    {
        var executor = new ThreadPoolExecutor(1);
        using var started = new ManualResetEventSlim();
        using var gate = new ManualResetEventSlim();
        var running = executor.Submit(() =>
        {
            started.Set();
            gate.Wait(Timeout, Ct);
        });
        Assert.True(started.Wait(Timeout, Ct));
        var pendingA = executor.Submit(() => 1);
        var pendingB = executor.Submit(() => 2);

        var dropped = executor.ShutdownNow();

        Assert.Equal(2, dropped.Count);
        Assert.All(dropped, t => Assert.True(t.IsCanceled));
        Assert.True(pendingA.IsCanceled);
        Assert.True(pendingB.IsCanceled);
        Assert.True(executor.IsShutdown);

        gate.Set();
        await running.WaitAsync(Timeout, Ct);
        Assert.True(await executor.AwaitTerminationAsync(Timeout, Ct));
    }

    [Fact]
    public void ShutdownNow_AfterShutdown_StillDrainsQueue()
    {
        var executor = new ThreadPoolExecutor(1);
        using var started = new ManualResetEventSlim();
        using var gate = new ManualResetEventSlim();
        _ = executor.Submit(() =>
        {
            started.Set();
            gate.Wait(Timeout, Ct);
        });
        Assert.True(started.Wait(Timeout, Ct));
        var pending = executor.Submit(() => 1);

        executor.Shutdown();
        var dropped = executor.ShutdownNow();

        Assert.Single(dropped);
        Assert.True(pending.IsCanceled);
        gate.Set();
        Assert.True(executor.AwaitTermination(Timeout));
    }

    [Fact]
    public void ShutdownNow_AfterTermination_ReturnsEmptyList()
    {
        var executor = new ThreadPoolExecutor(1);
        executor.Shutdown();
        Assert.True(executor.AwaitTermination(Timeout));

        var dropped = executor.ShutdownNow();

        Assert.Empty(dropped);
        Assert.True(executor.IsTerminated);
    }

    [Fact]
    public void AwaitTermination_TimesOutWhileRunning()
    {
        using var executor = new ThreadPoolExecutor(1);

        Assert.False(executor.AwaitTermination(TimeSpan.FromMilliseconds(20)));
        Assert.False(executor.IsTerminated);
    }

    [Fact]
    public async Task AwaitTerminationAsync_ReturnsFalseOnTimeout_TrueAfterShutdown()
    {
        var executor = new ThreadPoolExecutor(1);

        Assert.False(await executor.AwaitTerminationAsync(TimeSpan.FromMilliseconds(20), Ct));

        executor.Shutdown();

        Assert.True(await executor.AwaitTerminationAsync(Timeout, Ct));
    }

    [Fact]
    public void Dispose_ShutsDownAndWaitsForQueuedWork()
    {
        var executor = new ThreadPoolExecutor(2);
        var completed = 0;
        for (var i = 0; i < 10; i++)
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
        var executor = new ThreadPoolExecutor(1);
        var task = executor.Submit(() => 7);

        await executor.DisposeAsync();

        Assert.True(executor.IsTerminated);
        Assert.Equal(7, await task);
    }

    [Fact]
    public async Task Dispose_FromWorkerThread_DoesNotDeadlock()
    {
        var executor = new ThreadPoolExecutor(1);

        var task = executor.Submit(executor.Dispose);

        await task.WaitAsync(Timeout, Ct);
        Assert.True(await executor.AwaitTerminationAsync(Timeout, Ct));
    }

    [Fact]
    public void Shutdown_IsIdempotent()
    {
        var executor = new ThreadPoolExecutor(1);

        executor.Shutdown();
        executor.Shutdown();

        Assert.True(executor.AwaitTermination(Timeout));
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
        }
        while (Interlocked.CompareExchange(ref location, value, current) != current);
    }
}
