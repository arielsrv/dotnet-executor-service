namespace ExecutorService;

/// <summary>
/// An object that executes submitted commands. Mirrors <c>java.util.concurrent.Executor</c>.
/// </summary>
/// <remarks>
/// This interface decouples task submission from the mechanics of how each task will be run,
/// including details of thread use and scheduling.
/// </remarks>
public interface IExecutor
{
    /// <summary>
    /// Executes the given command at some time in the future. The command may execute in a new
    /// thread, in a pooled thread, or in the calling thread, at the discretion of the implementation.
    /// </summary>
    /// <param name="command">The command to run.</param>
    /// <exception cref="RejectedExecutionException">The command cannot be accepted for execution.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is <see langword="null"/>.</exception>
    void Execute(Action command);
}
