using BenchmarkDotNet.Running;
using HeroParser.Benchmarks;

namespace HeroParser.Benchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--quick")
        {
            // Quick test mode (no BenchmarkDotNet)
            QuickTest.Run();
            return;
        }

        if (args.Length > 0 && args[0] == "--throughput")
        {
            // Run only throughput benchmarks
            BenchmarkRunner.Run<ThroughputBenchmarks>();
            return;
        }

        if (args.Length > 0 && args[0] == "--vs-sep")
        {
            // Run only comparison benchmarks
            BenchmarkRunner.Run<VsSepBenchmarks>();
            return;
        }

        if (args.Length > 0 && args[0] == "--quotes")
        {
            // Run quoted vs unquoted benchmarks
            BenchmarkRunner.Run<QuotedVsUnquotedBenchmarks>();
            return;
        }

        // Default: show menu
        Console.WriteLine("=== HeroParser Benchmarks ===");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --quick       Quick throughput test (no BenchmarkDotNet)");
        Console.WriteLine("  --throughput  Run throughput benchmarks");
        Console.WriteLine("  --vs-sep      Run HeroParser vs Sep comparison");
        Console.WriteLine("  --quotes      Run quoted vs unquoted comparison (VERIFY SIMD)");
        Console.WriteLine("  --all         Run all benchmarks");
        Console.WriteLine();
        Console.WriteLine("Hardware:");
        Console.WriteLine($"  {HeroParser.Simd.SimdParserFactory.GetHardwareInfo()}");
        Console.WriteLine();

        if (args.Length == 0)
        {
            Console.WriteLine("No arguments provided. Running quick test...");
            Console.WriteLine();
            QuickTest.Run();
        }
        else if (args[0] == "--all")
        {
            BenchmarkRunner.Run<ThroughputBenchmarks>();
            BenchmarkRunner.Run<VsSepBenchmarks>();
            BenchmarkRunner.Run<QuotedVsUnquotedBenchmarks>();
        }
        else
        {
            Console.WriteLine($"Unknown option: {args[0]}");
            Console.WriteLine("Use --help to see available options");
        }
    }
}
