namespace ExecutorService;

/// <summary>
///     Factory methods for common <see cref="IExecutorService" /> configurations.
///     Mirrors <c>java.util.concurrent.Executors</c>.
/// </summary>
public static class Executors
{
    /// <summary>
    ///     Creates an executor that reuses a fixed number of threads operating off a shared unbounded queue.
    ///     At any point, at most <paramref name="threadCount" /> tasks are actively processing.
    /// </summary>
    /// <param name="threadCount">The number of threads in the pool.</param>
    /// <param name="options">Thread creation options, or <see langword="null" /> for defaults.</param>
    /// <returns>The newly created executor.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="threadCount" /> is less than 1.</exception>
    public static IExecutorService NewFixedThreadPool(int threadCount, ThreadPoolExecutorOptions? options = null)
    {
        return new ThreadPoolExecutor(threadCount, options);
    }

    /// <summary>
    ///     Creates an executor that uses a single worker thread operating off an unbounded queue.
    ///     Tasks are guaranteed to execute sequentially, and no more than one task will be active at any given time.
    /// </summary>
    /// <param name="options">Thread creation options, or <see langword="null" /> for defaults.</param>
    /// <returns>The newly created executor.</returns>
    public static IExecutorService NewSingleThreadExecutor(ThreadPoolExecutorOptions? options = null)
    {
        return new ThreadPoolExecutor(1, options);
    }
}
