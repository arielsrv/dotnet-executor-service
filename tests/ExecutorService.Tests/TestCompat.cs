// The suite is written against the newest BCL. These are the pieces .NET Framework lacks, so the same
// test bodies can run on net472 and prove the netstandard2.0 build of the library behaves identically.

#if NET472
namespace ExecutorService.Tests;

internal static class TestCompat
{
    /// <summary>Stands in for <c>Task.WaitAsync(TimeSpan, CancellationToken)</c>, added in .NET 6.</summary>
    public static async Task WaitAsync(this Task task, TimeSpan timeout, CancellationToken cancellationToken)
    {
        await Race(task, timeout, cancellationToken).ConfigureAwait(false);
        await task.ConfigureAwait(false);
    }

    /// <inheritdoc cref="WaitAsync(Task, TimeSpan, CancellationToken)" />
    public static async Task<TResult> WaitAsync<TResult>(
        this Task<TResult> task,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        await Race(task, timeout, cancellationToken).ConfigureAwait(false);
        return await task.ConfigureAwait(false);
    }

    /// <summary>Stands in for <c>CollectionExtensions.GetValueOrDefault</c>, absent from netstandard2.0.</summary>
    public static TValue? GetValueOrDefault<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key)
    {
        return dictionary.TryGetValue(key, out TValue? value) ? value : default;
    }

    private static async Task Race(Task task, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using CancellationTokenSource stopTimer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task timer = Task.Delay(timeout, stopTimer.Token);

        if (await Task.WhenAny(task, timer).ConfigureAwait(false) == timer)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException();
        }

        stopTimer.Cancel();
    }
}
#endif
