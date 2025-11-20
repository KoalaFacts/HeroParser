using BenchmarkDotNet.Running;

namespace HeroParser.Benchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--throughput")
        {
            // Run only throughput benchmarks
            BenchmarkRunner.Run<ThroughputBenchmarks>();
            return;
        }

        if (args.Length > 0 && args[0] == "--streaming")
        {
            // Run streaming vs text benchmarks
            BenchmarkRunner.Run<StreamingThroughputBenchmarks>();
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

        if (args.Length > 0 && args[0] == "--simd")
        {
            // Run SIMD comparison benchmarks
            BenchmarkRunner.Run<SimdComparisonBenchmarks>();
            return;
        }

        // Default: show menu
        Console.WriteLine("=== HeroParser Benchmarks ===");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --throughput  Run throughput benchmarks");
        Console.WriteLine("  --streaming   Run streaming throughput benchmarks (text vs stream vs async)");
        Console.WriteLine("  --vs-sep      Run HeroParser vs Sep comparison");
        Console.WriteLine("  --quotes      Run quoted vs unquoted comparison (VERIFY SIMD)");
        Console.WriteLine("  --simd        Run SIMD vs Scalar comparison");
        Console.WriteLine("  --all         Run all benchmarks");
        Console.WriteLine();
        Console.WriteLine("Hardware:");

        Console.WriteLine($"  {Hardware.GetHardwareInfo()}");
        Console.WriteLine();

        if (args.Length == 0)
        {
            Console.WriteLine("No arguments provided. Nothing to run...");
            Console.WriteLine();
        }
        else if (args[0] == "--all")
        {
            BenchmarkRunner.Run<ThroughputBenchmarks>();
            BenchmarkRunner.Run<StreamingThroughputBenchmarks>();
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
