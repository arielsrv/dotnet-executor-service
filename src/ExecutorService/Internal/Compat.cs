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
        ArgumentNullException.ThrowIfNull(argument, paramName);
    }

    public static void IfLessThan(
        int value,
        int minimum,
        [CallerArgumentExpression(nameof(value))]
        string? paramName = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, minimum, paramName);
    }
}

/// <summary>
///     Elapsed time from a <see cref="Stopwatch" /> timestamp. Uses the modern
///     <see cref="Stopwatch.GetElapsedTime" /> API available in .NET 7+.
/// </summary>
internal static class Clock
{
    public static TimeSpan ElapsedSince(long startingTimestamp)
    {
        return Stopwatch.GetElapsedTime(startingTimestamp);
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
        thread.UnsafeStart();
    }
}

