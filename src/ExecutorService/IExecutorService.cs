namespace ExecutorService;

/// <summary>
///     An <see cref="IExecutor" /> that provides methods to manage termination and methods that can
///     produce a <see cref="Task" /> for tracking progress of one or more asynchronous tasks.
///     Mirrors <c>java.util.concurrent.ExecutorService</c>.
/// </summary>
/// <remarks>
///     <para>
///         An <see cref="IExecutorService" /> can be shut down, which will cause it to reject new tasks.
///         <see cref="Shutdown" /> allows previously submitted tasks to execute before terminating, while
///         <see cref="ShutdownNow" /> prevents waiting tasks from starting. Upon termination, an executor
///         has no tasks actively executing, no tasks awaiting execution, and no new tasks can be submitted.
///     </para>
///     <para>
///         Disposing the service is equivalent to Java's <c>close()</c>: it calls <see cref="Shutdown" />
///         and waits for termination.
///     </para>
/// </remarks>
public interface IExecutorService : IExecutor, IDisposable, IAsyncDisposable
{
    /// <summary>
    ///     Gets a value indicating whether this executor has been shut down.
    /// </summary>
    bool IsShutdown { get; }

    /// <summary>
    ///     Gets a value indicating whether all tasks have completed following shut down.
    ///     This is never <see langword="true" /> unless <see cref="Shutdown" /> or <see cref="ShutdownNow" />
    ///     was called first.
    /// </summary>
    bool IsTerminated { get; }

    /// <summary>
    ///     Gets a token that is canceled by <see cref="ShutdownNow" />. Observing it is the only way a task
    ///     already running can be stopped, because .NET has no thread interruption.
    /// </summary>
    /// <remarks>
    ///     The graceful <see cref="Shutdown" /> never cancels this token: it lets queued tasks run to
    ///     completion. Throwing <see cref="OperationCanceledException" /> from a task — for example via
    ///     <see cref="CancellationToken.ThrowIfCancellationRequested" /> — transitions that task's
    ///     <see cref="Task" /> to <see cref="TaskStatus.Canceled" />.
    /// </remarks>
    CancellationToken ShutdownToken { get; }

    /// <summary>
    ///     Initiates an orderly shutdown in which previously submitted tasks are executed,
    ///     but no new tasks will be accepted. Invocation has no additional effect if already shut down.
    ///     This method does not wait for previously submitted tasks to complete execution;
    ///     use <see cref="AwaitTermination" /> to do that.
    /// </summary>
    void Shutdown();

    /// <summary>
    ///     Attempts to stop all actively executing tasks, halts the processing of waiting tasks,
    ///     and returns the tasks that were awaiting execution. Those tasks are transitioned to the
    ///     <see cref="TaskStatus.Canceled" /> state.
    /// </summary>
    /// <remarks>
    ///     Unlike Java, .NET has no thread interruption; tasks that are already running are not forcibly
    ///     stopped. They can stop cooperatively by observing <see cref="ShutdownToken" />, which this
    ///     method cancels before draining the queue.
    /// </remarks>
    /// <returns>The tasks that never commenced execution.</returns>
    IReadOnlyList<Task> ShutdownNow();

    /// <summary>
    ///     Blocks until all tasks have completed execution after a shutdown request,
    ///     or the timeout occurs, whichever happens first.
    /// </summary>
    /// <param name="timeout">The maximum time to wait. Use <see cref="Timeout.InfiniteTimeSpan" /> to wait indefinitely.</param>
    /// <returns><see langword="true" /> if this executor terminated; <see langword="false" /> if the timeout elapsed first.</returns>
    bool AwaitTermination(TimeSpan timeout);

    /// <summary>
    ///     Asynchronously waits until all tasks have completed execution after a shutdown request,
    ///     or the timeout occurs, whichever happens first.
    /// </summary>
    /// <param name="timeout">The maximum time to wait. Use <see cref="Timeout.InfiniteTimeSpan" /> to wait indefinitely.</param>
    /// <param name="cancellationToken">A token to cancel the wait (not the executor).</param>
    /// <returns><see langword="true" /> if this executor terminated; <see langword="false" /> if the timeout elapsed first.</returns>
    Task<bool> AwaitTerminationAsync(TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Submits a value-returning task for execution and returns a <see cref="Task{TResult}" />
    ///     representing its pending result.
    /// </summary>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="task">The task to submit.</param>
    /// <returns>
    ///     A task that completes with the result, faults with the thrown exception, or is canceled by
    ///     <see cref="ShutdownNow" />.
    /// </returns>
    /// <exception cref="RejectedExecutionException">The task cannot be scheduled for execution.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="task" /> is <see langword="null" />.</exception>
    Task<TResult> Submit<TResult>(Func<TResult> task);

    /// <summary>
    ///     Submits asynchronous work for execution and returns a <see cref="Task" /> that completes when the
    ///     work has finished — not when it has merely started.
    /// </summary>
    /// <remarks>
    ///     The worker thread stays occupied until the returned <see cref="Task" /> completes, so the executor's
    ///     thread count bounds how many of these run at once. That is the point of submitting async work here
    ///     rather than starting it directly: the pool becomes a concurrency limit.
    /// </remarks>
    /// <param name="task">The work to submit.</param>
    /// <returns>
    ///     A task that completes when the work finishes, faults with the thrown exception, or is canceled by
    ///     <see cref="ShutdownNow" />.
    /// </returns>
    /// <exception cref="RejectedExecutionException">The task cannot be scheduled for execution.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="task" /> is <see langword="null" />.</exception>
    Task Submit(Func<Task> task);

    /// <summary>
    ///     Submits asynchronous, value-returning work and returns a <see cref="Task{TResult}" /> that completes
    ///     with its result when the work has finished — not when it has merely started.
    /// </summary>
    /// <remarks>
    ///     The worker thread stays occupied until the work completes, so the executor's thread count bounds how
    ///     many of these run at once.
    /// </remarks>
    /// <typeparam name="TResult">The type of the result.</typeparam>
    /// <param name="task">The work to submit.</param>
    /// <returns>
    ///     A task that completes with the result, faults with the thrown exception, or is canceled by
    ///     <see cref="ShutdownNow" />.
    /// </returns>
    /// <exception cref="RejectedExecutionException">The task cannot be scheduled for execution.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="task" /> is <see langword="null" />.</exception>
    Task<TResult> Submit<TResult>(Func<Task<TResult>> task);

    /// <summary>
    ///     Submits a task for execution and returns a <see cref="Task" /> representing it.
    /// </summary>
    /// <param name="task">The task to submit.</param>
    /// <returns>
    ///     A task that completes when the work finishes, faults with the thrown exception, or is canceled by
    ///     <see cref="ShutdownNow" />.
    /// </returns>
    /// <exception cref="RejectedExecutionException">The task cannot be scheduled for execution.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="task" /> is <see langword="null" />.</exception>
    Task Submit(Action task);
}
