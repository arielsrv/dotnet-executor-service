using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace ExecutorService.Internal;

/// <summary>
///     Argument validation that delegates to the framework's own throw helpers where they exist, so the
///     exception messages a consumer sees are the ones their runtime produces everywhere else.
/// </summary>
internal static class Throw
{
    public static void IfNull(object? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(argument, paramName);
#else
        if (argument is null)
        {
            throw new ArgumentNullException(paramName);
        }
#endif
    }

    public static void IfLessThan(
        int value,
        int minimum,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
#if NET8_0_OR_GREATER
        ArgumentOutOfRangeException.ThrowIfLessThan(value, minimum, paramName);
#else
        if (value < minimum)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"('{paramName}') must be greater than or equal to '{minimum}'.");
        }
#endif
    }
}

/// <summary>
///     Elapsed time from a <see cref="Stopwatch" /> timestamp. netstandard2.0 has
///     <see cref="Stopwatch.GetTimestamp" /> but not <c>Stopwatch.GetElapsedTime</c>, which arrived in .NET 7.
/// </summary>
internal static class Clock
{
#if !NET7_0_OR_GREATER
    private static readonly double TicksPerTimestamp = TimeSpan.TicksPerSecond / (double)Stopwatch.Frequency;
#endif

    public static TimeSpan ElapsedSince(long startingTimestamp)
    {
#if NET7_0_OR_GREATER
        return Stopwatch.GetElapsedTime(startingTimestamp);
#else
        return new TimeSpan((long)((Stopwatch.GetTimestamp() - startingTimestamp) * TicksPerTimestamp));
#endif
    }
}

/// <summary>Thread start that does not carry the creating thread's ambient state onto the worker.</summary>
internal static class ThreadCompat
{
    /// <summary>
    ///     Starts <paramref name="thread" /> without flowing the caller's <see cref="ExecutionContext" />.
    ///     A worker outlives the submission that created it, so capturing that context would pin whatever
    ///     it holds — an <see cref="AsyncLocal{T}" /> value, an <c>Activity</c> — for the pool's lifetime.
    /// </summary>
    public static void StartWithoutContextFlow(this Thread thread)
    {
#if NET5_0_OR_GREATER
        thread.UnsafeStart();
#else
        // SuppressFlow throws if flow is already suppressed, which a caller inside its own
        // SuppressFlow() scope would have done — and in that case there is nothing left to suppress.
        if (ExecutionContext.IsFlowSuppressed())
        {
            thread.Start();
            return;
        }

        using (ExecutionContext.SuppressFlow())
        {
            thread.Start();
        }
#endif
    }
}

#if NETSTANDARD2_0
/// <summary>
///     The netstandard2.0 stand-in for <c>Task.WaitAsync(TimeSpan, CancellationToken)</c>, added in .NET 6.
///     On the modern targets the framework's instance method wins overload resolution and this is not compiled.
/// </summary>
internal static class TaskCompat
{
    public static async Task WaitAsync(this Task task, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using CancellationTokenSource stopTimer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task timer = Task.Delay(timeout, stopTimer.Token);

        if (await Task.WhenAny(task, timer).ConfigureAwait(false) == timer)
        {
            // A canceled token completes the timer too, and cancellation outranks the timeout.
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException();
        }

        // Cancel the timer rather than leaving it to fire into nothing.
        stopTimer.Cancel();

        // Surfaces the task's own exception, matching what the framework method does.
        await task.ConfigureAwait(false);
    }
}
#endif
