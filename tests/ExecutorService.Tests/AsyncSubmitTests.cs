namespace ExecutorService.Tests;

public sealed class AsyncSubmitTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Submit_Async_CompletesOnlyWhenTheWorkFinishes()
    {
        using ThreadPoolExecutor executor = new(1);
        using ManualResetEventSlim started = new();
        using ManualResetEventSlim release = new();
        bool finished = false;

        // Binds to Submit(Func<Task>), not Submit<Task>(Func<TResult>): the returned task must track the
        // asynchronous work, not merely the call that started it.
        Task submitted = executor.Submit(async () =>
        {
            await Task.Yield();
            started.Set();
            release.Wait(Timeout, Ct);
            finished = true;
        });

        try
        {
            // Once the body has begun and parked on the gate, the work provably has not finished — so a
            // completed task here would mean the executor only tracked the call that started it.
            Assert.True(started.Wait(Timeout, Ct));
            Assert.False(submitted.IsCompleted);
        }
        finally
        {
            release.Set();
        }

        await submitted.WaitAsync(Timeout, Ct);
        Assert.True(finished);
    }

    [Fact]
    public async Task Submit_Async_Func_ReturnsTheAwaitedResult()
    {
        using ThreadPoolExecutor executor = new(1);

        Task<int> submitted = executor.Submit(async () =>
        {
            await Task.Yield();
            return 21 * 2;
        });

        Assert.Equal(42, await submitted.WaitAsync(Timeout, Ct));
    }

    [Fact]
    public async Task Submit_Async_ThreadCountBoundsConcurrency()
    {
        const int Threads = 2;
        const int Submissions = 6;
        using ThreadPoolExecutor executor = new(Threads);
        int running = 0;
        int peak = 0;

        Task[] all = Enumerable.Range(0, Submissions)
            .Select(_ => executor.Submit(async () =>
            {
                int current = Interlocked.Increment(ref running);
                InterlockedMax(ref peak, current);
                await Task.Delay(20, Ct);
                Interlocked.Decrement(ref running);
            }))
            .ToArray();

        await Task.WhenAll(all).WaitAsync(Timeout, Ct);

        // Without blocking the worker, all six would overlap and the pool would bound nothing.
        Assert.True(Volatile.Read(ref peak) <= Threads, $"peak concurrency was {Volatile.Read(ref peak)}");
    }

    [Fact]
    public async Task Submit_Async_SurfacesExceptionsUnwrapped()
    {
        using ThreadPoolExecutor executor = new(1);

        Task submitted = executor.Submit(async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("boom");
        });

        InvalidOperationException ex =
            await Assert.ThrowsAsync<InvalidOperationException>(() => submitted.WaitAsync(Timeout, Ct));
        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    public async Task Submit_Async_CancellationCancelsTheTask()
    {
        using ThreadPoolExecutor executor = new(1);

        Task submitted = executor.Submit(async () =>
        {
            await Task.Yield();
            throw new OperationCanceledException();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => submitted.WaitAsync(Timeout, Ct));
        Assert.True(submitted.IsCanceled);
    }

    [Fact]
    public async Task Submit_Async_Func_SurfacesExceptionsUnwrapped()
    {
        using ThreadPoolExecutor executor = new(1);

        Task<int> submitted = executor.Submit(async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("boom");
#pragma warning disable CS0162 // Unreachable, but the lambda must be Func<Task<int>>.
            return 0;
#pragma warning restore CS0162
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => submitted.WaitAsync(Timeout, Ct));
    }

    [Fact]
    public void Submit_Async_NullThrowsArgumentNull()
    {
        using ThreadPoolExecutor executor = new(1);

        Assert.Throws<ArgumentNullException>(() => { _ = executor.Submit((Func<Task>)null!); });
        Assert.Throws<ArgumentNullException>(() => { _ = executor.Submit((Func<Task<int>>)null!); });
    }

    [Fact]
    public async Task Submit_Async_RejectedAfterShutdown()
    {
        ThreadPoolExecutor executor = new(1);
        executor.Shutdown();
        Assert.True(await executor.AwaitTerminationAsync(Timeout, Ct));

        Assert.Throws<RejectedExecutionException>(() => { _ = executor.Submit(() => Task.CompletedTask); });
        Assert.Throws<RejectedExecutionException>(() => { _ = executor.Submit(() => Task.FromResult(1)); });
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
