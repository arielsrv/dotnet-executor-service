// Types that exist on the modern targets but not on netstandard2.0, which is what .NET Framework
// consumers restore. Declaring them here lets the rest of the library be written once, against the
// newest API, instead of carrying #if through its logic. The compiler recognizes IsExternalInit and
// CallerArgumentExpressionAttribute by full name, so these are honoured exactly like the real ones.

#if NETSTANDARD2_0
namespace System.Runtime.CompilerServices
{
    /// <summary>Enables <c>init</c> accessors and records.</summary>
    internal static class IsExternalInit;

    /// <summary>Lets an argument-validation helper report the caller's expression as the parameter name.</summary>
    [AttributeUsage(AttributeTargets.Parameter)]
    internal sealed class CallerArgumentExpressionAttribute(string parameterName) : Attribute
    {
        public string ParameterName { get; } = parameterName;
    }
}

namespace System.Threading.Tasks
{
    /// <summary>
    ///     The non-generic <see cref="TaskCompletionSource" />, added in .NET 5. Backed by the generic one,
    ///     which behaves identically: a <see cref="Task{TResult}" /> is a <see cref="Task" />.
    /// </summary>
    internal sealed class TaskCompletionSource(TaskCreationOptions creationOptions)
    {
        private readonly TaskCompletionSource<bool> _source = new(creationOptions);

        public Task Task => _source.Task;

        public bool TrySetResult()
        {
            return _source.TrySetResult(true);
        }

        public bool TrySetCanceled()
        {
            return _source.TrySetCanceled();
        }

        public bool TrySetCanceled(Threading.CancellationToken cancellationToken)
        {
            return _source.TrySetCanceled(cancellationToken);
        }

        public bool TrySetException(Exception exception)
        {
            return _source.TrySetException(exception);
        }
    }
}
#endif
