using BenchmarkDotNet.Running;

namespace HeroParser.Benchmarks;

/// <summary>
/// Benchmark suite for HeroParser CSV parsing library.
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return;
        }

        switch (args[0].ToLower())
        {
            case "compete":
            case "competitive":
                Console.WriteLine("====================================");
                Console.WriteLine("   COMPETITIVE BENCHMARK");
                Console.WriteLine("====================================");
                Console.WriteLine("Comparing HeroParser against Sep, Sylvan, and CsvHelper");
                Console.WriteLine();
                BenchmarkRunner.Run<CompetitiveBenchmarks>();
                break;

            case "perf":
            case "performance":
                Console.WriteLine("====================================");
                Console.WriteLine("   HEROPARSER PERFORMANCE ANALYSIS");
                Console.WriteLine("====================================");
                Console.WriteLine("Testing internal optimizations and edge cases");
                Console.WriteLine();
                BenchmarkRunner.Run<HeroParserPerformance>();
                break;

            case "cycle3":
            case "simd":
                Console.WriteLine("====================================");
                Console.WriteLine("   F1 CYCLE 3 SIMD VALIDATION");
                Console.WriteLine("====================================");
                Console.WriteLine("Testing SIMD optimizations and constitutional targets");
                Console.WriteLine();
                BenchmarkRunner.Run<CsvReadingCycle3Benchmarks>();
                break;

            case "all":
                Console.WriteLine("Running all benchmarks...\n");
                BenchmarkRunner.Run<CompetitiveBenchmarks>();
                Console.WriteLine("\n" + new string('=', 50) + "\n");
                BenchmarkRunner.Run<HeroParserPerformance>();
                Console.WriteLine("\n" + new string('=', 50) + "\n");
                BenchmarkRunner.Run<CsvReadingCycle3Benchmarks>();
                break;

            default:
                Console.WriteLine($"Unknown benchmark: {args[0]}");
                PrintUsage();
                break;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("HeroParser Benchmark Suite");
        Console.WriteLine("==========================");
        Console.WriteLine();
        Console.WriteLine("Usage: dotnet run -c Release -- [benchmark]");
        Console.WriteLine();
        Console.WriteLine("Available benchmarks:");
        Console.WriteLine("  compete     - Compare HeroParser vs competitors (Sep, Sylvan, CsvHelper)");
        Console.WriteLine("  performance - Analyze HeroParser's internal performance characteristics");
        Console.WriteLine("  cycle3      - F1 Cycle 3 SIMD validation and constitutional targets");
        Console.WriteLine("  all         - Run all benchmarks");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run -c Release -- compete");
        Console.WriteLine("  dotnet run -c Release -- performance");
    }
}