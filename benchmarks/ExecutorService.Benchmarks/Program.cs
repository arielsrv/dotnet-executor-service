using BenchmarkDotNet.Running;

// `dotnet run -c Release` runs everything; pass BenchmarkDotNet's own arguments to narrow it down,
// for example `-- --filter *Submit*` or `-- --job short` for a rough answer in a fraction of the time.
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

/// <summary>Entry point holder, so the switcher has a type from this assembly to start from.</summary>
public partial class Program;
