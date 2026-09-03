namespace ExecutorService;

/// <summary>
/// Options that control how a <see cref="ThreadPoolExecutor"/> creates its worker threads.
/// </summary>
public sealed record ThreadPoolExecutorOptions
{
    /// <summary>
    /// Gets the prefix used to name worker threads. Threads are named <c>{prefix}-{index}</c>.
    /// Defaults to <c>executor</c>.
    /// </summary>
    public string ThreadNamePrefix { get; init; } = "executor";

    /// <summary>
    /// Gets a value indicating whether worker threads are background threads.
    /// Background threads do not keep the process alive. Defaults to <see langword="true"/>.
    /// </summary>
    public bool IsBackground { get; init; } = true;

    /// <summary>
    /// Gets the scheduling priority of worker threads. Defaults to <see cref="ThreadPriority.Normal"/>.
    /// </summary>
    public ThreadPriority Priority { get; init; } = ThreadPriority.Normal;
}
