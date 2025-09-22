using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Environments;

namespace HeroParser.BenchmarkTests;

/// <summary>
/// Entry point for HeroParser benchmarking suite.
/// Compares performance against Sep, Sylvan.Data.Csv, and CsvHelper.
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        var config = DefaultConfig.Instance
            .AddJob(Job.Default.WithRuntime(CoreRuntime.Core80))
            .AddJob(Job.Default.WithRuntime(CoreRuntime.Core90))
            .WithOption(ConfigOptions.DisableOptimizationsValidator, true);

        // Run all benchmark classes
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
    }
}