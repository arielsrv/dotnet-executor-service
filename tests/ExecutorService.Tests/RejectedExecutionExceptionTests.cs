namespace ExecutorService.Tests;

public sealed class RejectedExecutionExceptionTests
{
    [Fact]
    public void DefaultConstructor_UsesShutdownMessage()
    {
        RejectedExecutionException ex = new();

        Assert.Equal("Task rejected: the executor has been shut down.", ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void MessageConstructor_PreservesMessage()
    {
        RejectedExecutionException ex = new("custom");

        Assert.Equal("custom", ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void MessageAndInnerConstructor_PreservesBoth()
    {
        InvalidOperationException inner = new("inner");

        RejectedExecutionException ex = new("custom", inner);

        Assert.Equal("custom", ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void IsAnInvalidOperationException()
    {
        Assert.IsAssignableFrom<InvalidOperationException>(new RejectedExecutionException());
    }
}
