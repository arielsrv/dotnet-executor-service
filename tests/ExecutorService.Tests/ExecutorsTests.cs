namespace ExecutorService.Tests;

public sealed class ExecutorsTests
{
    [Fact]
    public void NewFixedThreadPool_CreatesPoolWithRequestedThreads()
    {
        using var executor = Executors.NewFixedThreadPool(4);

        var pool = Assert.IsType<ThreadPoolExecutor>(executor);
        Assert.Equal(4, pool.ThreadCount);
    }

    [Fact]
    public void NewSingleThreadExecutor_CreatesPoolWithOneThread()
    {
        using var executor = Executors.NewSingleThreadExecutor();

        var pool = Assert.IsType<ThreadPoolExecutor>(executor);
        Assert.Equal(1, pool.ThreadCount);
    }

    [Fact]
    public async Task NewSingleThreadExecutor_RunsTasksSequentially()
    {
        using var executor = Executors.NewSingleThreadExecutor();
        var order = new List<int>();

        var tasks = Enumerable.Range(0, 50).Select(i => executor.Submit(() =>
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
