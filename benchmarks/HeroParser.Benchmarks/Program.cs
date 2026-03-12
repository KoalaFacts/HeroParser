using BenchmarkDotNet.Running;

namespace HeroParser.Benchmarks;

public class Program
{
    private static readonly Type[] allBenchmarks =
    [
        typeof(ThroughputBenchmarks),
        typeof(CsvStreamingBenchmarks),
        typeof(CsvPipeReaderBenchmarks),
        typeof(CsvTypedPipeReaderBenchmarks),
        typeof(VsSepReadingBenchmarks),
        typeof(VsSepWritingBenchmarks),
        typeof(QuotedVsUnquotedBenchmarks),
        typeof(SimdComparisonBenchmarks),
        typeof(NewFeaturesBenchmark),
        typeof(ColumnParseBenchmarks),
        typeof(WriteInjectionProtectionBenchmarks),
        typeof(ReadInjectionProtectionBenchmarks),
        typeof(OutputLimitsBenchmarks),
        typeof(WriterBenchmarks),
        typeof(AsyncWriterBenchmarks),
        typeof(WriterDestinationBenchmarks),
        typeof(WriterQuotingBenchmarks),
        typeof(BinderOverheadBenchmarks),
        typeof(MultiSchemaBenchmarks),
        typeof(FixedWidthBenchmarks),
        typeof(FixedWidthMicroBenchmarks),
        typeof(FixedWidthStreamingBenchmarks),
        typeof(FixedWidthFieldParseBenchmarks),
        typeof(FixedWidthWriterBenchmarks),
        typeof(FixedWidthAsyncWriterBenchmarks),
        typeof(FixedWidthCustomConverterBenchmarks),
        typeof(FixedWidthByteSpanBenchmarks),
        typeof(FixedWidthAlignmentBenchmarks),
        typeof(FixedWidthPipeReaderBenchmarks),
        typeof(FixedWidthTypedPipeReaderBenchmarks)
    ];

    private static readonly Dictionary<string, Type[]> benchmarkGroups = new(StringComparer.Ordinal)
    {
        ["--throughput"] = [typeof(ThroughputBenchmarks)],
        ["--csv-streaming"] = [typeof(CsvStreamingBenchmarks)],
        ["--csv-pipe"] =
        [
            typeof(CsvPipeReaderBenchmarks),
            typeof(CsvTypedPipeReaderBenchmarks)
        ],
        ["--vs-sep-reading"] = [typeof(VsSepReadingBenchmarks)],
        ["--vs-sep-writing"] = [typeof(VsSepWritingBenchmarks)],
        ["--writer"] =
        [
            typeof(WriterBenchmarks),
            typeof(AsyncWriterBenchmarks),
            typeof(WriterDestinationBenchmarks),
            typeof(WriterQuotingBenchmarks)
        ],
        ["--sync-writer"] =
        [
            typeof(WriterBenchmarks),
            typeof(WriterDestinationBenchmarks),
            typeof(WriterQuotingBenchmarks)
        ],
        ["--async-writer"] = [typeof(AsyncWriterBenchmarks)],
        ["--quotes"] = [typeof(QuotedVsUnquotedBenchmarks)],
        ["--simd"] = [typeof(SimdComparisonBenchmarks)],
        ["--features"] = [typeof(NewFeaturesBenchmark)],
        ["--column-parse"] = [typeof(ColumnParseBenchmarks)],
        ["--security"] =
        [
            typeof(WriteInjectionProtectionBenchmarks),
            typeof(ReadInjectionProtectionBenchmarks),
            typeof(OutputLimitsBenchmarks)
        ],
        ["--injection"] =
        [
            typeof(WriteInjectionProtectionBenchmarks),
            typeof(ReadInjectionProtectionBenchmarks)
        ],
        ["--injection-write"] = [typeof(WriteInjectionProtectionBenchmarks)],
        ["--injection-read"] = [typeof(ReadInjectionProtectionBenchmarks)],
        ["--binder-overhead"] = [typeof(BinderOverheadBenchmarks)],
        ["--multi-schema"] = [typeof(MultiSchemaBenchmarks)],
        ["--fixed-width"] =
        [
            typeof(FixedWidthBenchmarks),
            typeof(FixedWidthMicroBenchmarks),
            typeof(FixedWidthStreamingBenchmarks),
            typeof(FixedWidthFieldParseBenchmarks),
            typeof(FixedWidthWriterBenchmarks),
            typeof(FixedWidthAsyncWriterBenchmarks),
            typeof(FixedWidthCustomConverterBenchmarks),
            typeof(FixedWidthByteSpanBenchmarks),
            typeof(FixedWidthAlignmentBenchmarks),
            typeof(FixedWidthPipeReaderBenchmarks),
            typeof(FixedWidthTypedPipeReaderBenchmarks)
        ],
        ["--fixed-width-streaming"] = [typeof(FixedWidthStreamingBenchmarks)],
        ["--fixed-width-parsing"] = [typeof(FixedWidthFieldParseBenchmarks)],
        ["--fixed-width-writer"] =
        [
            typeof(FixedWidthWriterBenchmarks),
            typeof(FixedWidthAsyncWriterBenchmarks)
        ],
        ["--fixed-width-converters"] = [typeof(FixedWidthCustomConverterBenchmarks)],
        ["--fixed-width-bytespan"] = [typeof(FixedWidthByteSpanBenchmarks)],
        ["--fixed-width-alignment"] = [typeof(FixedWidthAlignmentBenchmarks)],
        ["--fixed-width-pipe"] =
        [
            typeof(FixedWidthPipeReaderBenchmarks),
            typeof(FixedWidthTypedPipeReaderBenchmarks)
        ]
    };

    public static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return;
        }

        if (args[0] == "--all")
        {
            RunBenchmarks(args, allBenchmarks);
            return;
        }

        if (benchmarkGroups.TryGetValue(args[0], out var benchmarks))
        {
            RunBenchmarks(args, benchmarks);
            return;
        }

        PrintHelp($"Unknown option: {args[0]}");
    }

    private static void PrintHelp(string? error = null)
    {
        if (!string.IsNullOrEmpty(error))
        {
            Console.WriteLine(error);
            Console.WriteLine();
        }

        Console.WriteLine("=== HeroParser Benchmarks ===");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --throughput          Run raw CSV throughput benchmarks");
        Console.WriteLine("  --csv-streaming       Run CSV async stream reader benchmarks");
        Console.WriteLine("  --csv-pipe            Run CSV PipeReader benchmarks");
        Console.WriteLine("  --vs-sep-reading      Run HeroParser vs Sep reading comparison");
        Console.WriteLine("  --vs-sep-writing      Run HeroParser vs Sep writing comparison");
        Console.WriteLine("  --writer              Run all CSV writer benchmarks");
        Console.WriteLine("  --sync-writer         Run sync CSV writer benchmarks");
        Console.WriteLine("  --async-writer        Run async CSV writer benchmarks");
        Console.WriteLine("  --quotes              Run quoted vs unquoted parsing benchmarks");
        Console.WriteLine("  --simd                Run SIMD vs scalar CSV parsing benchmarks");
        Console.WriteLine("  --features            Run CSV feature overhead benchmarks");
        Console.WriteLine("  --column-parse        Run CSV column parsing benchmarks");
        Console.WriteLine("  --security            Run injection and output-limit benchmarks");
        Console.WriteLine("  --injection           Run all injection protection benchmarks");
        Console.WriteLine("  --injection-write     Run write-side injection benchmarks");
        Console.WriteLine("  --injection-read      Run read-side injection benchmarks");
        Console.WriteLine("  --binder-overhead     Run typed binder overhead benchmarks");
        Console.WriteLine("  --multi-schema        Run multi-schema parsing benchmarks");
        Console.WriteLine("  --fixed-width         Run all fixed-width benchmarks");
        Console.WriteLine("  --fixed-width-streaming  Run fixed-width streaming benchmarks");
        Console.WriteLine("  --fixed-width-parsing    Run fixed-width field parsing benchmarks");
        Console.WriteLine("  --fixed-width-writer     Run fixed-width writer benchmarks");
        Console.WriteLine("  --fixed-width-converters Run fixed-width custom converter benchmarks");
        Console.WriteLine("  --fixed-width-bytespan   Run fixed-width byte span benchmarks");
        Console.WriteLine("  --fixed-width-alignment  Run fixed-width alignment benchmarks");
        Console.WriteLine("  --fixed-width-pipe       Run fixed-width PipeReader benchmarks");
        Console.WriteLine("  --all                 Run all benchmarks");
        Console.WriteLine();
        Console.WriteLine("Hardware:");
        Console.WriteLine($"  {Hardware.GetHardwareInfo()}");
        Console.WriteLine();
    }

    private static void RunBenchmarks(string[] args, params Type[] benchmarks)
    {
        var extraArgs = args.Length > 1 ? args[1..] : [];

        if (extraArgs.Length > 0)
        {
            BenchmarkSwitcher.FromTypes(benchmarks).Run(extraArgs);
            return;
        }

        foreach (var benchmark in benchmarks)
        {
            BenchmarkRunner.Run(benchmark);
        }
    }
}
