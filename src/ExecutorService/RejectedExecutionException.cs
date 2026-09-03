namespace ExecutorService;

/// <summary>
/// Thrown by an <see cref="IExecutor"/> when a task cannot be accepted for execution,
/// typically because the executor has been shut down.
/// Mirrors <c>java.util.concurrent.RejectedExecutionException</c>.
/// </summary>
public sealed class RejectedExecutionException : InvalidOperationException
{
    /// <summary>Initializes a new instance of the <see cref="RejectedExecutionException"/> class.</summary>
    public RejectedExecutionException()
        : base("Task rejected: the executor has been shut down.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RejectedExecutionException"/> class with a message.</summary>
    /// <param name="message">The error message.</param>
    public RejectedExecutionException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RejectedExecutionException"/> class with a message and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The cause of this exception.</param>
    public RejectedExecutionException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
