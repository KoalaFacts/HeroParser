using BenchmarkDotNet.Running;

namespace HeroParser.Benchmarks;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("==============================================");
        Console.WriteLine("  HeroParser Benchmarks - Beat Sep Challenge");
        Console.WriteLine("  Target: 30+ GB/s on AVX-512 Hardware");
        Console.WriteLine("==============================================");
        Console.WriteLine();

        // Display hardware capabilities
        Console.WriteLine("Hardware Detection:");
        Console.WriteLine(HeroParser.Simd.SimdParserFactory.GetHardwareInfo());
        Console.WriteLine();

        if (args.Length > 0 && args[0] == "--quick")
        {
            // Quick test run
            Console.WriteLine("Running quick performance test...");
            QuickTest.Run();
        }
        else
        {
            // Full benchmark suite
            var summary = BenchmarkRunner.Run<VsSepBenchmark>();
            Console.WriteLine();
            Console.WriteLine("Benchmark completed!");
        }
    }
}
