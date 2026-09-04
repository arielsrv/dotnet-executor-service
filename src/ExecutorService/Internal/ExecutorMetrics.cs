using System.Diagnostics.Metrics;

namespace ExecutorService.Internal;

/// <summary>
///     Publishes an executor's metrics through <see cref="System.Diagnostics.Metrics" />, the in-box
///     OpenTelemetry metrics API. One instance per executor.
/// </summary>
/// <remarks>
///     The <see cref="Meter" /> is owned (and disposed) by this instance unless the caller supplied one.
///     Disposal is what unregisters the observable gauges, so a terminated executor is not kept alive by
///     their callbacks.
/// </remarks>
internal sealed class ExecutorMetrics : IDisposable
{
    private readonly Counter<long> _completed;
    private readonly Histogram<double> _executionDuration;
    private readonly Meter _meter;
    private readonly KeyValuePair<string, object?> _name;
    private readonly bool _ownsMeter;
    private readonly Histogram<double> _queueDuration;
    private readonly Counter<long> _rejected;
    private readonly Counter<long> _submitted;

    public ExecutorMetrics(string executorName, Meter? meter, Func<int> queuedCount, Func<int> threadCount)
    {
        _ownsMeter = meter is null;
        _meter = meter ?? new Meter(ThreadPoolExecutor.MeterName);
        _name = new KeyValuePair<string, object?>("executor.name", executorName);

        _submitted = _meter.CreateCounter<long>(
            "executor.tasks.submitted", "{task}", "Tasks accepted for execution.");
        _completed = _meter.CreateCounter<long>(
            "executor.tasks.completed", "{task}", "Tasks that reached a terminal state.");
        _rejected = _meter.CreateCounter<long>(
            "executor.tasks.rejected", "{task}", "Tasks rejected because the executor was shut down.");
        _queueDuration = _meter.CreateHistogram<double>(
            "executor.task.queue.duration", "s", "Time a task waited in the queue before starting.");
        _executionDuration = _meter.CreateHistogram<double>(
            "executor.task.execution.duration", "s", "Time a task spent executing.");

        _meter.CreateObservableGauge(
            "executor.tasks.queued", () => Observe(queuedCount()), "{task}", "Tasks waiting to be executed.");
        _meter.CreateObservableGauge(
            "executor.threads", () => Observe(threadCount()), "{thread}", "Worker threads owned by the executor.");
    }

    /// <summary>Gets a value indicating whether queue latency is worth timestamping.</summary>
    public bool QueueDurationEnabled => _queueDuration.Enabled;

    /// <summary>Gets a value indicating whether execution time is worth timestamping.</summary>
    public bool ExecutionDurationEnabled => _executionDuration.Enabled;

    public void Dispose()
    {
        if (_ownsMeter)
        {
            _meter.Dispose();
        }
    }

    public void TaskSubmitted()
    {
        _submitted.Add(1, _name);
    }

    public void TaskRejected()
    {
        _rejected.Add(1, _name);
    }

    public void TaskCompleted(TaskStatus status)
    {
        _completed.Add(1, _name, new KeyValuePair<string, object?>("executor.task.status", StatusName(status)));
    }

    public void RecordQueueDuration(TimeSpan elapsed)
    {
        _queueDuration.Record(elapsed.TotalSeconds, _name);
    }

    public void RecordExecutionDuration(TimeSpan elapsed)
    {
        _executionDuration.Record(elapsed.TotalSeconds, _name);
    }

    private static string StatusName(TaskStatus status)
    {
        return status switch
        {
            TaskStatus.RanToCompletion => "success",
            TaskStatus.Canceled => "canceled",
            _ => "faulted"
        };
    }

    private Measurement<int> Observe(int value)
    {
        return new Measurement<int>(value, [_name]);
    }
}
