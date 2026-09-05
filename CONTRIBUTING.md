# Contributing

Thanks for taking the time to contribute!

## Reporting issues

- Search [existing issues](https://github.com/arielsrv/dotnet-executor-service/issues) first.
- Use the issue templates. Include a minimal reproduction for bugs.
- For security vulnerabilities, follow [SECURITY.md](SECURITY.md) instead of opening a public issue.

## Development setup

Requirements: the .NET SDK version pinned in `global.json` (`dotnet --version` should match or roll forward).

```shell
git clone https://github.com/arielsrv/dotnet-executor-service.git
cd dotnet-executor-service
dotnet build
dotnet test --solution ExecutorService.slnx
```

Formatting is enforced by `.editorconfig` and analyzers run with warnings as errors:

```shell
dotnet format --verify-no-changes
```

If you have [Task](https://taskfile.dev) installed, `Taskfile.yml` wraps the common commands.
Run `task` to list them; the most useful are:

```shell
task test                 # run the suite (extra args after --, e.g. task test -- --filter-method "*Shutdown*")
task test:stress N=20     # run the suite repeatedly to catch flaky concurrency tests
task coverage:check       # tests + HTML report in TestResults/coverage, fails below 100% line/branch coverage
task lint:md              # lint the Markdown docs (task lint:md:fix applies what it can fix itself)
task bench                # run the benchmarks (task bench -- --job short for a rough answer)
task aot                  # publish the quick start sample as a native binary and run it
task ci                   # everything CI runs: format check, Markdown lint, Release build, tests, coverage gate, pack
```

The project keeps 100% line and branch coverage. New code needs tests that keep it there.

## Pull requests

1. Fork and create a branch from `main` (`feat/...`, `fix/...`, `docs/...`).
2. Keep changes focused. One logical change per PR.
3. Add or update tests. Concurrency code without tests will not be merged.
4. Public API changes need XML documentation and an entry under `[Unreleased]` in `CHANGELOG.md`.
5. Use [Conventional Commits](https://www.conventionalcommits.org/) for commit messages
   (`feat:`, `fix:`, `docs:`, `test:`, `chore:`).
6. Make sure `task ci` (or `dotnet build`, `dotnet test` and the coverage gate) passes locally before opening the PR.

## Releasing

1. Move the `[Unreleased]` entries in `CHANGELOG.md` under a `## [x.y.z] - yyyy-mm-dd` heading and update the
   link definitions at the bottom. `dotnet pack` lifts that section into the package's release notes, so it is
   what nuget.org will show.
2. Tag `vx.y.z` and push the tag. The release workflow builds, tests, attests provenance and publishes.
3. Once the version is live on nuget.org, bump `PackageValidationBaselineVersion` in
   `src/ExecutorService/ExecutorService.csproj` to it, so the next pack is compared against the newest
   release. Bumping it before the package is indexed breaks the build: the baseline has to be restorable.

## Design guidelines

- Mirror Java's `java.util.concurrent` semantics unless .NET idioms clearly call for something else.
  Document every deviation in XML docs.
- Prefer `Task` / `Task<T>` over custom future types.
- Never let a task exception escape onto a worker thread; it must surface through the returned `Task`.
- No allocations on the hot path that Java's implementation would not also incur.

## Releasing (maintainers)

Versions come from git tags via [MinVer](https://github.com/adamralph/minver).

1. Move `[Unreleased]` entries in `CHANGELOG.md` under a new version heading.
2. Tag: `git tag v1.2.3 && git push origin v1.2.3`.
3. The `release` workflow packs, pushes to NuGet.org and creates a GitHub Release.
