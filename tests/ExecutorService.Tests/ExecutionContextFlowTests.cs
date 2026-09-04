using System.Diagnostics;

namespace ExecutorService.Tests;

public sealed class ExecutionContextFlowTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly AsyncLocal<string?> Ambient = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Submit_FlowsAmbientContextFromTheSubmitter()
    {
        await using ThreadPoolExecutor executor = new(1);

        // Set *after* construction: worker threads must not serve the context they were started with.
        Ambient.Value = "set-after-construction";

        string? seen = await executor.Submit<string?>(() => Ambient.Value).WaitAsync(Timeout, Ct);

        Assert.Equal("set-after-construction", seen);
    }

    [Fact]
    public async Task Submit_FlowsTheContextOfEachSubmissionIndependently()
    {
        await using ThreadPoolExecutor executor = new(1);

        Ambient.Value = "first";
        Task<string?> first = executor.Submit<string?>(() => Ambient.Value);
        Ambient.Value = "second";
        Task<string?> second = executor.Submit<string?>(() => Ambient.Value);

        Assert.Equal("first", await first.WaitAsync(Timeout, Ct));
        Assert.Equal("second", await second.WaitAsync(Timeout, Ct));
    }

    [Fact]
    public async Task Submit_HonoursSuppressedFlowAndNeverServesTheConstructorContext()
    {
        Ambient.Value = "context-at-construction";
        await using ThreadPoolExecutor executor = new(1);

        Task<string?> task;
        using (ExecutionContext.SuppressFlow())
        {
            task = executor.Submit<string?>(() => Ambient.Value);
        }

        // Null, not "context-at-construction": the workers were started without capturing it.
        Assert.Null(await task.WaitAsync(Timeout, Ct));
    }

    [Fact]
    public async Task Submit_ParentsActivitiesStartedInsideTheTask()
    {
        using ActivitySource source = new(nameof(Submit_ParentsActivitiesStartedInsideTheTask));
        using ActivityListener listener = new();
        listener.ShouldListenTo = s => s.Name == source.Name;
        listener.Sample = (ref _) => ActivitySamplingResult.AllData;
        ActivitySource.AddActivityListener(listener);
        await using ThreadPoolExecutor executor = new(1);

        using Activity? parent = source.StartActivity("parent");
        string? childParentId = await executor
            .Submit(() =>
            {
                using Activity? child = source.StartActivity("child");
                return child?.ParentId;
            })
            .WaitAsync(Timeout, Ct);

        Assert.NotNull(parent);
        Assert.Equal(parent.Id, childParentId);
    }

    [Fact]
    public async Task Execute_FlowsAmbientContextFromTheSubmitter()
    {
        await using ThreadPoolExecutor executor = new(1);
        using ManualResetEventSlim done = new();
        string? seen = null;

        Ambient.Value = "via-execute";
        executor.Execute(() =>
        {
            seen = Ambient.Value;
            done.Set();
        });

        Assert.True(done.Wait(Timeout, Ct));
        await Task.CompletedTask;
        Assert.Equal("via-execute", seen);
    }
}
