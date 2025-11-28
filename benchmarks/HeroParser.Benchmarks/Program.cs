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

        if (args.Length > 0 && args[0] == "--vs-sep-reading")
        {
            // Run reading comparison benchmarks
            BenchmarkRunner.Run<VsSepReadingBenchmarks>();
            return;
        }

        if (args.Length > 0 && args[0] == "--vs-sep-writing")
        {
            // Run writing comparison benchmarks
            BenchmarkRunner.Run<VsSepWritingBenchmarks>();
            return;
        }

        if (args.Length > 0 && args[0] == "--writer")
        {
            // Run all writer benchmarks (both sync and async)
            BenchmarkRunner.Run<WriterBenchmarks>();
            BenchmarkRunner.Run<AsyncWriterBenchmarks>();
            return;
        }

        if (args.Length > 0 && args[0] == "--sync-writer")
        {
            // Run sync writer benchmarks only
            BenchmarkRunner.Run<WriterBenchmarks>();
            return;
        }

        if (args.Length > 0 && args[0] == "--async-writer")
        {
            // Run async writer benchmarks only (sync vs async comparison)
            BenchmarkRunner.Run<AsyncWriterBenchmarks>();
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

        if (args.Length > 0 && args[0] == "--features")
        {
            // Run new features overhead benchmarks
            BenchmarkRunner.Run<NewFeaturesBenchmark>();
            return;
        }

        if (args.Length > 0 && args[0] == "--security")
        {
            // Run all security and validation benchmarks
            BenchmarkRunner.Run<WriteInjectionProtectionBenchmarks>();
            BenchmarkRunner.Run<ReadInjectionProtectionBenchmarks>();
            BenchmarkRunner.Run<OutputLimitsBenchmarks>();
            BenchmarkRunner.Run<ValidationBenchmarks>();
            BenchmarkRunner.Run<SmartDetectionMicroBenchmarks>();
            return;
        }

        if (args.Length > 0 && args[0] == "--validation")
        {
            // Run only validation benchmarks
            BenchmarkRunner.Run<ValidationBenchmarks>();
            return;
        }

        if (args.Length > 0 && args[0] == "--injection")
        {
            // Run all injection protection benchmarks (write and read)
            BenchmarkRunner.Run<WriteInjectionProtectionBenchmarks>();
            BenchmarkRunner.Run<ReadInjectionProtectionBenchmarks>();
            BenchmarkRunner.Run<SmartDetectionMicroBenchmarks>();
            return;
        }

        if (args.Length > 0 && args[0] == "--injection-write")
        {
            // Run write-side injection protection benchmarks
            BenchmarkRunner.Run<WriteInjectionProtectionBenchmarks>();
            return;
        }

        if (args.Length > 0 && args[0] == "--injection-read")
        {
            // Run read-side injection protection benchmarks
            BenchmarkRunner.Run<ReadInjectionProtectionBenchmarks>();
            return;
        }

        // Default: show menu
        Console.WriteLine("=== HeroParser Benchmarks ===");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --throughput       Run throughput benchmarks");
        Console.WriteLine("  --streaming        Run streaming throughput benchmarks (text vs stream vs async)");
        Console.WriteLine("  --vs-sep-reading   Run HeroParser vs Sep reading comparison");
        Console.WriteLine("  --vs-sep-writing   Run HeroParser vs Sep writing comparison");
        Console.WriteLine("  --writer           Run all writer benchmarks (sync + async)");
        Console.WriteLine("  --sync-writer      Run sync writer benchmarks only");
        Console.WriteLine("  --async-writer     Run async writer benchmarks only");
        Console.WriteLine("  --quotes           Run quoted vs unquoted comparison (VERIFY SIMD)");
        Console.WriteLine("  --simd             Run SIMD vs Scalar comparison");
        Console.WriteLine("  --features         Run new features overhead benchmarks (Comment/Trim/MaxFieldLength)");
        Console.WriteLine("  --security         Run security and validation benchmarks (injection/limits/validation)");
        Console.WriteLine("  --validation       Run only validation benchmarks");
        Console.WriteLine("  --injection        Run all injection protection benchmarks");
        Console.WriteLine("  --injection-write  Run write-side injection benchmarks");
        Console.WriteLine("  --injection-read   Run read-side injection benchmarks");
        Console.WriteLine("  --all              Run all benchmarks");
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
            BenchmarkRunner.Run<VsSepReadingBenchmarks>();
            BenchmarkRunner.Run<VsSepWritingBenchmarks>();
            BenchmarkRunner.Run<QuotedVsUnquotedBenchmarks>();
        }
        else
        {
            Console.WriteLine($"Unknown option: {args[0]}");
            Console.WriteLine("Use --help to see available options");
        }
    }
}
