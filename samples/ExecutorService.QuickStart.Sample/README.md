# Quick start sample

A console app that exercises the library **as published on nuget.org**, prints what it observed and exits
non-zero if anything is off. It runs in about a second.

## Why this one is different

Every other project in this repository builds the library from source, so none of them would notice a package
that restores but does not work — a target framework missing from the nupkg, a type left behind by packing, a
dependency that resolves only locally. This sample references it the way a stranger does:

```xml
<PackageReference Include="ExecutorService"/>   <!-- version in Directory.Packages.props -->
```

That makes it a post-release check as much as a sample: bump the pinned version in
[`Directory.Packages.props`](../../Directory.Packages.props) to whatever was just released, run it, and the
exit code tells you whether that release is usable.

## Run it

From the repository root:

```shell
task quickstart
```

Or directly:

```shell
dotnet run --project samples/ExecutorService.QuickStart.Sample
```

```text
ExecutorService quick start
  package   ExecutorService 0.6.2+a06bfb76f2ffeb2f106890a4ebc3a312f49c0afe
  assembly  .../samples/ExecutorService.QuickStart.Sample/bin/Debug/net10.0/ExecutorService.dll

  [ ok ] Submit hands back the delegate's value          6 * 7 = 42
  [ ok ] Submit follows async work to completion         resolved "after the await"
  [ ok ] 4 dedicated threads, never more at once         peak 4 on 4 threads named quickstart-*
  [ ok ] One thread means strict FIFO                    20 tasks ran in submission order
  [ ok ] Shutdown drains the queue and refuses new work  queued work finished, new work rejected

5/5 checks passed
```

The `+` suffix on the version is the commit the package was built from, so the line also says exactly which
source produced the assembly being tested.

## What it checks

1. **`Submit` returns a value** — the future resolves to what the delegate computed.
2. **`Submit` follows async work** — an `async` delegate's task completes after its awaits, not at the first one.
3. **Fixed, dedicated threads** — eight tasks on a four-thread pool. Four of them park until all four are
   running, which a narrower pool could not satisfy; the peak never passes four, which an elastic one would.
   None of the work lands on a `ThreadPool` thread.
4. **Strict FIFO** — twenty tasks on a single-thread executor come out in submission order.
5. **Shutdown semantics** — queued work still finishes, new submissions raise `RejectedExecutionException`,
   and the executor reaches `IsTerminated`.

Each check has its own timeout, so a pool that never reaches the expected concurrency fails the run instead of
hanging it.
