using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

using ExecutorService;

// A five-second smoke test of the ExecutorService package as published on nuget.org.
//
// The point of this sample is the reference in the .csproj: it is a PackageReference, not a
// ProjectReference, so everything below runs against the assembly that was packed, restored and
// unpacked — the same one a consumer gets. Nothing here reaches into the repository's sources.
//
// Every check prints what it observed and the process exits non-zero if any of them fails, so this
// doubles as a post-release sanity check: `dotnet run --project samples/ExecutorService.QuickStart.Sample`.
const int Threads = 4;
const string NamePrefix = "quickstart";

Assembly library = typeof(Executors).Assembly;
string version = library.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                 ?? library.GetName().Version?.ToString()
                 ?? "unknown";

Console.WriteLine("ExecutorService quick start");
Console.WriteLine($"  package   ExecutorService {version}");
Console.WriteLine($"  assembly  {library.Location}");
Console.WriteLine();

int passed = 0;
int total = 0;

using (IExecutorService pool = Executors.NewFixedThreadPool(
           Threads,
           new ThreadPoolExecutorOptions { ThreadNamePrefix = NamePrefix }))
{
    Run("Submit hands back the delegate's value", () => ValueRoundTrip(pool));
    Run("Submit follows async work to completion", () => AsyncRoundTrip(pool));
    Run($"{Threads} dedicated threads, never more at once", () => DedicatedThreads(pool, Threads, NamePrefix));
}

Run("One thread means strict FIFO", SequentialOrder);
Run("Shutdown drains the queue and refuses new work", ShutdownDrains);

Console.WriteLine();
Console.WriteLine($"{passed}/{total} checks passed");
return passed == total ? 0 : 1;

void Run(string name, Func<(bool Ok, string Detail)> check)
{
    total++;
    (bool ok, string detail) = check();
    if (ok)
    {
        passed++;
    }

    Console.WriteLine($"  [{(ok ? " ok " : "fail")}] {name,-46}  {detail}");
}

static (bool Ok, string Detail) ValueRoundTrip(IExecutorService pool)
{
    Task<int> answer = pool.Submit(() => 6 * 7);

    return answer.Wait(TimeSpan.FromSeconds(5))
        ? (answer.Result == 42, $"6 * 7 = {answer.Result}")
        : (false, "timed out");
}

static (bool Ok, string Detail) AsyncRoundTrip(IExecutorService pool)
{
    // Passing the async delegate directly means the returned task tracks the awaited work, not just
    // the part before the first await.
    Task<string> work = pool.Submit(async () =>
    {
        await Task.Delay(25).ConfigureAwait(false);
        return "after the await";
    });

    return work.Wait(TimeSpan.FromSeconds(5))
        ? (work.Result == "after the await", $"resolved \"{work.Result}\"")
        : (false, "timed out");
}

static (bool Ok, string Detail) DedicatedThreads(IExecutorService pool, int threads, string namePrefix)
{
    // The first `threads` tasks each park until all of them are running, which a pool narrower than
    // `threads` could never satisfy; the timeout turns that into a failed check instead of a hung
    // sample. Twice as many tasks are submitted as there are threads, so the peak below also shows
    // the pool refusing to grow past its fixed size.
    using CountdownEvent allRunning = new(threads);
    StrongBox<int> running = new();
    StrongBox<int> peak = new();
    ConcurrentDictionary<string, byte> names = new(StringComparer.Ordinal);
    bool sawSharedPoolThread = false;
    bool timedOut = false;

    Task[] tasks = new Task[threads * 2];
    for (int i = 0; i < tasks.Length; i++)
    {
        bool parks = i < threads;
        tasks[i] = pool.Submit(() =>
        {
            Max(ref peak.Value, Interlocked.Increment(ref running.Value));
            names[Thread.CurrentThread.Name ?? "(unnamed)"] = 0;

            // Dedicated threads, not the process-wide ThreadPool: that is the whole reason the
            // library exists, and it is the one property a broken package would silently lose.
            if (Thread.CurrentThread.IsThreadPoolThread)
            {
                sawSharedPoolThread = true;
            }

            if (parks)
            {
                allRunning.Signal();
                if (!allRunning.Wait(TimeSpan.FromSeconds(5)))
                {
                    timedOut = true;
                }
            }

            Interlocked.Decrement(ref running.Value);
        });
    }

    Task.WaitAll(tasks);

    bool ok = !timedOut
              && !sawSharedPoolThread
              && peak.Value == threads
              && names.Count == threads
              && names.Keys.All(name => name.StartsWith(namePrefix, StringComparison.Ordinal));

    return (ok, $"peak {peak.Value} on {names.Count} threads named {namePrefix}-*");
}

static (bool Ok, string Detail) SequentialOrder()
{
    const int Count = 20;

    using IExecutorService single = Executors.NewSingleThreadExecutor(
        new ThreadPoolExecutorOptions { ThreadNamePrefix = "sequential" });

    ConcurrentQueue<int> order = new();
    Task[] tasks = new Task[Count];
    for (int i = 0; i < Count; i++)
    {
        int position = i;
        tasks[i] = single.Submit(() => order.Enqueue(position));
    }

    Task.WaitAll(tasks);

    return (order.SequenceEqual(Enumerable.Range(0, Count)), $"{Count} tasks ran in submission order");
}

static (bool Ok, string Detail) ShutdownDrains()
{
    using IExecutorService closing = Executors.NewFixedThreadPool(
        2,
        new ThreadPoolExecutorOptions { ThreadNamePrefix = "closing" });

    Task queued = closing.Submit(() => Thread.Sleep(100));
    closing.Shutdown();

    bool refused = false;
    try
    {
        _ = closing.Submit(() => { });
    }
    catch (RejectedExecutionException)
    {
        refused = true;
    }

    // Work accepted before the shutdown still runs: that is what separates Shutdown from ShutdownNow.
    bool terminated = closing.AwaitTermination(TimeSpan.FromSeconds(5));
    bool ok = refused && terminated && closing.IsTerminated && queued.IsCompletedSuccessfully;

    return (ok, refused ? "queued work finished, new work rejected" : "new work was not rejected");
}

static void Max(ref int target, int candidate)
{
    int seen = Volatile.Read(ref target);
    while (candidate > seen)
    {
        int actual = Interlocked.CompareExchange(ref target, candidate, seen);
        if (actual == seen)
        {
            return;
        }

        seen = actual;
    }
}
