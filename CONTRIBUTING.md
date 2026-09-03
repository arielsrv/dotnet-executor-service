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

## Pull requests

1. Fork and create a branch from `main` (`feat/...`, `fix/...`, `docs/...`).
2. Keep changes focused. One logical change per PR.
3. Add or update tests. Concurrency code without tests will not be merged.
4. Public API changes need XML documentation and an entry under `[Unreleased]` in `CHANGELOG.md`.
5. Use [Conventional Commits](https://www.conventionalcommits.org/) for commit messages (`feat:`, `fix:`, `docs:`, `test:`, `chore:`).
6. Make sure `dotnet build` and `dotnet test` pass locally before opening the PR.

## Design guidelines

- Mirror Java's `java.util.concurrent` semantics unless .NET idioms clearly call for something else. Document every deviation in XML docs.
- Prefer `Task` / `Task<T>` over custom future types.
- Never let a task exception escape onto a worker thread; it must surface through the returned `Task`.
- No allocations on the hot path that Java's implementation would not also incur.

## Releasing (maintainers)

Versions come from git tags via [MinVer](https://github.com/adamralph/minver).

1. Move `[Unreleased]` entries in `CHANGELOG.md` under a new version heading.
2. Tag: `git tag v1.2.3 && git push origin v1.2.3`.
3. The `release` workflow packs, pushes to NuGet.org and creates a GitHub Release.
