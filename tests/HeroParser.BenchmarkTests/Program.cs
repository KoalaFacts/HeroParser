using BenchmarkDotNet.Running;

namespace HeroParser.BenchmarkTests;

/// <summary>
/// Entry point for HeroParser competitor benchmarking.
/// Constitution: Benchmark-Driven Development - comparison against Sep, Sylvan, CsvHelper.
/// Usage: dotnet run -- --filter *CompetitorBenchmarks*
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        // Constitution: Performance-First Architecture
        // Use BenchmarkDotNet's built-in CLI instead of custom interactive menu
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}