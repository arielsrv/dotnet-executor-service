# Benchmarks

What a submission costs, in time and in allocations. The repository claims the hot path allocates no more
than Java's implementation would; this is where that claim is checked rather than asserted.

## Run them

From the repository root:

```shell
task bench                          # the full run, several minutes
task bench -- --job short           # a rough answer in about a minute
task bench -- --filter *Submit*     # one benchmark
```

Always in Release: BenchmarkDotNet refuses to take a Debug build seriously, and it is right to.

## What is measured

Batches of a thousand submissions, so the per-call number is not swamped by a single thread wake-up, at two
pool widths (`Threads=1`, the strictly sequential case, and `Threads=4`).

| Benchmark                         | What it isolates                                                        |
|-----------------------------------|-------------------------------------------------------------------------|
| `Task.Run x1000`                  | The baseline: the same work without this library                        |
| `Submit x1000`                    | A tracked submission — the queue entry, the future, the wake-up         |
| `Execute x1000 (fire and forget)` | The cheapest path: no future handed back, so nothing to allocate for it |

`Task.Run` is the baseline, not a rival. It runs on the process-wide `ThreadPool`, which grows under load and
offers neither FIFO order nor an upper bound on concurrency. Reading the ratio as "which is faster" misses the
point; it says what the executor's guarantees cost.

`Execute` drains through one sentinel task per worker before the measurement stops. FIFO means a sentinel is
dequeued only after the whole batch is, so the number covers the work actually finishing rather than merely
being queued.
