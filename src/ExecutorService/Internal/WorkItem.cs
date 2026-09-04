namespace ExecutorService.Internal;

/// <summary>
///     A unit of work queued on an executor. Bridges a synchronous delegate to a <see cref="Task" />.
/// </summary>
internal abstract class WorkItem
{
    private static readonly ContextCallback InvokeCallback = static state => ((WorkItem)state!).Invoke();

    /// <summary>Gets the task that observers await.</summary>
    public abstract Task Task { get; }

    /// <summary>
    ///     Gets or sets the timestamp taken when the item was enqueued, or <see langword="null" /> when
    ///     queue latency is not being measured.
    /// </summary>
    public long? EnqueuedTimestamp { get; set; }

    /// <summary>
    ///     Gets or sets the caller's ambient execution context, captured at submission so that
    ///     <see cref="AsyncLocal{T}" /> values — <see cref="System.Diagnostics.Activity.Current" /> among
    ///     them — reach the work the same way they would through <see cref="Task.Run(Action)" />.
    ///     <see langword="null" /> when the caller suppressed flow.
    /// </summary>
    public ExecutionContext? Context { get; set; }

    /// <summary>Runs the work on the calling thread, routing the outcome to <see cref="Task" />. Never throws.</summary>
    public void Run()
    {
        if (Context is null)
        {
            Invoke();
            return;
        }

        ExecutionContext.Run(Context, InvokeCallback, this);
    }

    /// <summary>Transitions <see cref="Task" /> to canceled without running the work.</summary>
    public abstract void Cancel();

    /// <summary>Invokes the delegate and routes its outcome to <see cref="Task" />. Never throws.</summary>
    protected abstract void Invoke();
}

internal sealed class ActionWorkItem(Action action) : WorkItem
{
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override Task Task => _completion.Task;

    protected override void Invoke()
    {
        try
        {
            action();
            _completion.TrySetResult();
        }
        catch (OperationCanceledException ex)
        {
            _completion.TrySetCanceled(ex.CancellationToken);
        }
#pragma warning disable CA1031 // Exceptions are surfaced through the Task, never lost.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _completion.TrySetException(ex);
        }
    }

    public override void Cancel()
    {
        _completion.TrySetCanceled();
    }
}

internal sealed class FuncWorkItem<TResult>(Func<TResult> func) : WorkItem
{
    private readonly TaskCompletionSource<TResult>
        _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<TResult> TypedTask => _completion.Task;

    public override Task Task => _completion.Task;

    protected override void Invoke()
    {
        try
        {
            _completion.TrySetResult(func());
        }
        catch (OperationCanceledException ex)
        {
            _completion.TrySetCanceled(ex.CancellationToken);
        }
#pragma warning disable CA1031 // Exceptions are surfaced through the Task, never lost.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _completion.TrySetException(ex);
        }
    }

    public override void Cancel()
    {
        _completion.TrySetCanceled();
    }
}
