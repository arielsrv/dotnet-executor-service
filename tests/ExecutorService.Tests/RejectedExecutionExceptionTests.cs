namespace ExecutorService.Tests;

public sealed class RejectedExecutionExceptionTests
{
    [Fact]
    public void DefaultConstructor_UsesShutdownMessage()
    {
        var ex = new RejectedExecutionException();

        Assert.Equal("Task rejected: the executor has been shut down.", ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void MessageConstructor_PreservesMessage()
    {
        var ex = new RejectedExecutionException("custom");

        Assert.Equal("custom", ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void MessageAndInnerConstructor_PreservesBoth()
    {
        var inner = new InvalidOperationException("inner");

        var ex = new RejectedExecutionException("custom", inner);

        Assert.Equal("custom", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void IsAnInvalidOperationException()
    {
        Assert.IsAssignableFrom<InvalidOperationException>(new RejectedExecutionException());
    }
}
