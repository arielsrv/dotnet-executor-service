namespace ExecutorService.Internal;

/// <summary>
///     A unit of work queued on an executor. Bridges a synchronous delegate to a <see cref="Task" />.
/// </summary>
internal abstract class WorkItem
{
    /// <summary>Gets the task that observers await.</summary>
    public abstract Task Task { get; }

    /// <summary>Runs the work on the calling thread, routing the outcome to <see cref="Task" />. Never throws.</summary>
    public abstract void Run();

    /// <summary>Transitions <see cref="Task" /> to canceled without running the work.</summary>
    public abstract void Cancel();
}

internal sealed class ActionWorkItem(Action action) : WorkItem
{
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override Task Task => _completion.Task;

    public override void Run()
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

    public override void Run()
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
