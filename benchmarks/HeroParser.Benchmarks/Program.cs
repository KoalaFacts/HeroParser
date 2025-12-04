using BenchmarkDotNet.Running;

namespace HeroParser.Benchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--throughput")
        {
            // Run only throughput benchmarks
            RunBenchmarks(args, typeof(ThroughputBenchmarks));
            return;
        }

        if (args.Length > 0 && args[0] == "--streaming")
        {
            // Run streaming vs text benchmarks
            RunBenchmarks(args, typeof(StreamingThroughputBenchmarks));
            return;
        }

        if (args.Length > 0 && args[0] == "--vs-sep-reading")
        {
            // Run reading comparison benchmarks
            RunBenchmarks(args, typeof(VsSepReadingBenchmarks));
            return;
        }

        if (args.Length > 0 && args[0] == "--vs-sep-writing")
        {
            // Run writing comparison benchmarks
            RunBenchmarks(args, typeof(VsSepWritingBenchmarks));
            return;
        }

        if (args.Length > 0 && args[0] == "--writer")
        {
            // Run all writer benchmarks (both sync and async)
            RunBenchmarks(args, typeof(WriterBenchmarks), typeof(AsyncWriterBenchmarks));
            return;
        }

        if (args.Length > 0 && args[0] == "--sync-writer")
        {
            // Run sync writer benchmarks only
            RunBenchmarks(args, typeof(WriterBenchmarks));
            return;
        }

        if (args.Length > 0 && args[0] == "--async-writer")
        {
            // Run async writer benchmarks only (sync vs async comparison)
            RunBenchmarks(args, typeof(AsyncWriterBenchmarks));
            return;
        }

        if (args.Length > 0 && args[0] == "--quotes")
        {
            // Run quoted vs unquoted benchmarks
            RunBenchmarks(args, typeof(QuotedVsUnquotedBenchmarks));
            return;
        }

        if (args.Length > 0 && args[0] == "--simd")
        {
            // Run SIMD comparison benchmarks
            RunBenchmarks(args, typeof(SimdComparisonBenchmarks));
            return;
        }

        if (args.Length > 0 && args[0] == "--features")
        {
            // Run new features overhead benchmarks
            RunBenchmarks(args, typeof(NewFeaturesBenchmark));
            return;
        }

        if (args.Length > 0 && args[0] == "--security")
        {
            // Run all security and validation benchmarks
            RunBenchmarks(args,
                typeof(WriteInjectionProtectionBenchmarks),
                typeof(ReadInjectionProtectionBenchmarks),
                typeof(OutputLimitsBenchmarks),
                typeof(ValidationBenchmarks),
                typeof(SmartDetectionMicroBenchmarks));
            return;
        }

        if (args.Length > 0 && args[0] == "--validation")
        {
            // Run only validation benchmarks
            RunBenchmarks(args, typeof(ValidationBenchmarks));
            return;
        }

        if (args.Length > 0 && args[0] == "--injection")
        {
            // Run all injection protection benchmarks (write and read)
            RunBenchmarks(args,
                typeof(WriteInjectionProtectionBenchmarks),
                typeof(ReadInjectionProtectionBenchmarks),
                typeof(SmartDetectionMicroBenchmarks));
            return;
        }

        if (args.Length > 0 && args[0] == "--injection-write")
        {
            // Run write-side injection protection benchmarks
            RunBenchmarks(args, typeof(WriteInjectionProtectionBenchmarks));
            return;
        }

        if (args.Length > 0 && args[0] == "--injection-read")
        {
            // Run read-side injection protection benchmarks
            RunBenchmarks(args, typeof(ReadInjectionProtectionBenchmarks));
            return;
        }

        if (args.Length > 0 && args[0] == "--fixed-width")
        {
            // Run all fixed-width parsing benchmarks
            RunBenchmarks(args,
                typeof(FixedWidthBenchmarks),
                typeof(FixedWidthMicroBenchmarks),
                typeof(FixedWidthStreamingBenchmarks),
                typeof(FixedWidthFieldParseBenchmarks),
                typeof(FixedWidthWriterBenchmarks),
                typeof(FixedWidthCustomConverterBenchmarks),
                typeof(FixedWidthByteSpanBenchmarks),
                typeof(FixedWidthAlignmentBenchmarks));
            return;
        }

        if (args.Length > 0 && args[0] == "--fixed-width-writer")
        {
            // Run fixed-width writer benchmarks
            RunBenchmarks(args, typeof(FixedWidthWriterBenchmarks));
            return;
        }

        if (args.Length > 0 && args[0] == "--fixed-width-converters")
        {
            // Run custom converter benchmarks
            RunBenchmarks(args, typeof(FixedWidthCustomConverterBenchmarks));
            return;
        }

        if (args.Length > 0 && args[0] == "--fixed-width-bytespan")
        {
            // Run byte span benchmarks
            RunBenchmarks(args, typeof(FixedWidthByteSpanBenchmarks));
            return;
        }

        if (args.Length > 0 && args[0] == "--fixed-width-alignment")
        {
            // Run alignment benchmarks
            RunBenchmarks(args, typeof(FixedWidthAlignmentBenchmarks));
            return;
        }

        if (args.Length > 0 && args[0] == "--fixed-width-streaming")
        {
            // Run fixed-width streaming throughput benchmarks
            RunBenchmarks(args, typeof(FixedWidthStreamingBenchmarks));
            return;
        }

        if (args.Length > 0 && args[0] == "--multi-schema")
        {
            // Run multi-schema benchmarks
            RunBenchmarks(args, typeof(MultiSchemaBenchmarks));
            return;
        }

        if (args.Length > 0 && args[0] == "--binder-overhead")
        {
            // Run binder overhead profiling benchmarks
            RunBenchmarks(args, typeof(BinderOverheadBenchmarks));
            return;
        }

        if (args.Length > 0 && args[0] == "--fixed-width-parsing")
        {
            // Run fixed-width field parsing benchmarks
            RunBenchmarks(args, typeof(FixedWidthFieldParseBenchmarks));
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
        Console.WriteLine("  --fixed-width      Run all fixed-width benchmarks (throughput + streaming + parsing + writer)");
        Console.WriteLine("  --fixed-width-streaming  Run fixed-width streaming throughput benchmarks");
        Console.WriteLine("  --fixed-width-parsing    Run fixed-width field parsing benchmarks");
        Console.WriteLine("  --fixed-width-writer     Run fixed-width writer benchmarks");
        Console.WriteLine("  --fixed-width-converters Run fixed-width custom converter benchmarks");
        Console.WriteLine("  --fixed-width-bytespan   Run fixed-width byte span vs char span benchmarks");
        Console.WriteLine("  --fixed-width-alignment  Run fixed-width alignment operation benchmarks");
        Console.WriteLine("  --multi-schema     Run multi-schema CSV parsing benchmarks");
        Console.WriteLine("  --memory           Run memory vs string binding benchmarks");
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
            BenchmarkRunner.Run<FixedWidthBenchmarks>();
            BenchmarkRunner.Run<FixedWidthMicroBenchmarks>();
            BenchmarkRunner.Run<FixedWidthStreamingBenchmarks>();
            BenchmarkRunner.Run<FixedWidthFieldParseBenchmarks>();
        }
        else
        {
            Console.WriteLine($"Unknown option: {args[0]}");
            Console.WriteLine("Use --help to see available options");
        }
    }

    private static void RunBenchmarks(string[] args, params Type[] benchmarks)
    {
        var extraArgs = args.Length > 1 ? args[1..] : [];

        if (extraArgs.Length > 0)
        {
            BenchmarkSwitcher.FromTypes(benchmarks).Run(extraArgs);
        }
        else
        {
            foreach (var benchmark in benchmarks)
            {
                BenchmarkRunner.Run(benchmark);
            }
        }
    }
}
