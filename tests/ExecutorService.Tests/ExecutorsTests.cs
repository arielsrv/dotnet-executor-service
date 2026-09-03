namespace ExecutorService.Tests;

public sealed class ExecutorsTests
{
    [Fact]
    public void NewFixedThreadPool_CreatesPoolWithRequestedThreads()
    {
        using IExecutorService executor = Executors.NewFixedThreadPool(4);

        ThreadPoolExecutor pool = Assert.IsType<ThreadPoolExecutor>(executor);
        Assert.Equal(4, pool.ThreadCount);
    }

    [Fact]
    public void NewSingleThreadExecutor_CreatesPoolWithOneThread()
    {
        using IExecutorService executor = Executors.NewSingleThreadExecutor();

        ThreadPoolExecutor pool = Assert.IsType<ThreadPoolExecutor>(executor);
        Assert.Equal(1, pool.ThreadCount);
    }

    [Fact]
    public async Task NewSingleThreadExecutor_RunsTasksSequentially()
    {
        using IExecutorService executor = Executors.NewSingleThreadExecutor();
        List<int> order = new();

        IEnumerable<Task> tasks = Enumerable.Range(0, 50).Select(i => executor.Submit(() =>
        {
            lock (order)
            {
                order.Add(i);
            }
        }));
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(Enumerable.Range(0, 50), order);
    }
}
