using System.Diagnostics.Metrics;
using System.Globalization;

namespace ExecutorService.Tests;

public sealed class ExecutorMetricsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Metrics_RecordSubmittedCompletedAndDurations()
    {
        using Meter meter = new("test.metrics.success");
        using Collector collector = new(meter);
        await using ThreadPoolExecutor executor = new(1, new ThreadPoolExecutorOptions { Meter = meter });

        await executor.Submit(() => 42).WaitAsync(Timeout, Ct);

        Assert.Equal(1, collector.Sum("executor.tasks.submitted"));
        Assert.Equal(1, collector.Sum("executor.tasks.completed"));
        Assert.Equal("success", collector.Tag("executor.tasks.completed", "executor.task.status"));
        Assert.Equal("executor", collector.Tag("executor.tasks.submitted", "executor.name"));
        Assert.True(collector.Count("executor.task.queue.duration") >= 1);
        Assert.True(collector.Count("executor.task.execution.duration") >= 1);
        Assert.All(collector.Values("executor.task.queue.duration"), v => Assert.True(v >= 0));
    }

    [Fact]
    public async Task Metrics_TagCompletedWithFaultedAndCanceledStatus()
    {
        using Meter meter = new("test.metrics.status");
        using Collector collector = new(meter);
        await using ThreadPoolExecutor executor = new(1, new ThreadPoolExecutorOptions { Meter = meter });

        Task faulted = executor.Submit(() => throw new InvalidOperationException("boom"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => faulted.WaitAsync(Timeout, Ct));
        Task canceled = executor.Submit(() => throw new OperationCanceledException());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled.WaitAsync(Timeout, Ct));

        Assert.Contains("faulted", collector.Tags("executor.tasks.completed", "executor.task.status"));
        Assert.Contains("canceled", collector.Tags("executor.tasks.completed", "executor.task.status"));
    }

    [Fact]
    public void Metrics_CountRejectedSubmissionsAfterShutdown()
    {
        using Meter meter = new("test.metrics.rejected");
        using Collector collector = new(meter);
        ThreadPoolExecutor executor = new(1, new ThreadPoolExecutorOptions { Meter = meter });
        executor.Shutdown();
        Assert.True(executor.AwaitTermination(Timeout));

        Assert.Throws<RejectedExecutionException>(() => { _ = executor.Submit(() => { }); });

        Assert.Equal(1, collector.Sum("executor.tasks.rejected"));
        Assert.Equal(0, collector.Sum("executor.tasks.submitted"));
    }

    [Fact]
    public void Metrics_ObservableGaugesReportQueueDepthAndThreadCount()
    {
        using Meter meter = new("test.metrics.gauges");
        using Collector collector = new(meter);
        ThreadPoolExecutor executor = new(2, new ThreadPoolExecutorOptions { Meter = meter });
        using ManualResetEventSlim started = new();
        using ManualResetEventSlim gate = new();
        try
        {
            executor.Execute(() =>
            {
                started.Set();
                gate.Wait(Timeout, Ct);
            });
            Assert.True(started.Wait(Timeout, Ct));
            executor.Execute(() => gate.Wait(Timeout, Ct));
            executor.Execute(() => { });

            collector.PullGauges();

            Assert.Equal(2, collector.Last("executor.threads"));
            Assert.True(collector.Last("executor.tasks.queued") >= 1);
        }
        finally
        {
            // Unpark the workers and let them exit before the gates leave scope: a failing assert above
            // would otherwise dispose the gates from under threads still waiting on them.
            gate.Set();
            executor.Shutdown();
            _ = executor.AwaitTermination(Timeout);
        }
    }

    [Fact]
    public async Task Metrics_UseOwnMeterWhenNoneSuppliedAndDisposeItOnTermination()
    {
        using Collector collector = new(ThreadPoolExecutor.MeterName, "owned-meter");
        ThreadPoolExecutor executor = new(1, new ThreadPoolExecutorOptions { ThreadNamePrefix = "owned-meter" });

        await executor.Submit(() => { }).WaitAsync(Timeout, Ct);

        Assert.Equal(1, collector.Sum("executor.tasks.submitted"));
        collector.PullGauges();
        Assert.Equal(1, collector.Last("executor.threads"));

        executor.Shutdown();
        Assert.True(await executor.AwaitTerminationAsync(Timeout, Ct));
        collector.Clear();
        collector.PullGauges();

        // The meter was disposed with the last worker, so the gauge callbacks are gone.
        Assert.Equal(0, collector.Count("executor.threads"));
    }

    [Fact]
    public void Metrics_LeaveSuppliedMeterUsableAfterTermination()
    {
        using Meter meter = new("test.metrics.supplied");
        using Collector collector = new(meter);
        ThreadPoolExecutor executor = new(1, new ThreadPoolExecutorOptions { Meter = meter });
        executor.Shutdown();
        Assert.True(executor.AwaitTermination(Timeout));

        meter.CreateCounter<long>("probe").Add(7);

        Assert.Equal(7, collector.Sum("probe"));
    }

    private sealed record Measured(string Name, double Value, Dictionary<string, object?> Tags);

    /// <summary>Captures every measurement published by one meter, mirroring what an exporter would see.</summary>
    private sealed class Collector : IDisposable
    {
        private readonly Func<Measured, bool> _accept;
        private readonly Lock _gate = new();
        private readonly MeterListener _listener = new();
        private readonly List<Measured> _measurements = [];

        /// <summary>Listens to one meter instance, which no other test shares.</summary>
        public Collector(Meter meter) : this(instrument => ReferenceEquals(instrument.Meter, meter), _ => true)
        {
        }

        /// <summary>
        ///     Listens to a meter by name — the default meter every executor in the process publishes to — so
        ///     measurements must also be narrowed to one executor by its <c>executor.name</c> tag, otherwise
        ///     tests running in parallel contaminate each other.
        /// </summary>
        public Collector(string meterName, string executorName) : this(
            instrument => string.Equals(instrument.Meter.Name, meterName, StringComparison.Ordinal),
            measured => Equals(measured.Tags.GetValueOrDefault("executor.name"), executorName))
        {
        }

        private Collector(Func<Instrument, bool> published, Func<Measured, bool> accept)
        {
            _accept = accept;
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (published(instrument))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>(Record);
            _listener.SetMeasurementEventCallback<double>(Record);
            _listener.SetMeasurementEventCallback<int>(Record);
            _listener.Start();
        }

        public void Dispose()
        {
            _listener.Dispose();
        }

        public void PullGauges()
        {
            _listener.RecordObservableInstruments();
        }

        public void Clear()
        {
            lock (_gate)
            {
                _measurements.Clear();
            }
        }

        public int Count(string name)
        {
            return Snapshot(name).Count;
        }

        public double Sum(string name)
        {
            return Snapshot(name).Sum(m => m.Value);
        }

        public double Last(string name)
        {
            return Snapshot(name)[^1].Value;
        }

        public List<double> Values(string name)
        {
            return Snapshot(name).Select(m => m.Value).ToList();
        }

        public object? Tag(string name, string tagKey)
        {
            return Snapshot(name)[^1].Tags[tagKey];
        }

        public List<object?> Tags(string name, string tagKey)
        {
            return Snapshot(name).Select(m => m.Tags[tagKey]).ToList();
        }

        private List<Measured> Snapshot(string name)
        {
            lock (_gate)
            {
                return _measurements
                    .Where(m => string.Equals(m.Name, name, StringComparison.Ordinal))
                    .Where(_accept)
                    .ToList();
            }
        }

        private void Record<T>(
            Instrument instrument,
            T measurement,
            ReadOnlySpan<KeyValuePair<string, object?>> tags,
            object? state)
            where T : struct
        {
            Dictionary<string, object?> copy = new(StringComparer.Ordinal);
            foreach (KeyValuePair<string, object?> tag in tags)
            {
                copy[tag.Key] = tag.Value;
            }

            lock (_gate)
            {
                _measurements.Add(new Measured(
                    instrument.Name,
                    Convert.ToDouble(measurement, CultureInfo.InvariantCulture),
                    copy));
            }
        }
    }
}
