using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

namespace HeroParser.Benchmarks;

/// <summary>
/// Isolates the performance and allocation delta between the source-generated
/// template writer and the reflection-based writer for CSV and Fixed-Width.
/// Two record types of identical shape are used: one decorated with
/// <c>[GenerateBinder]</c> (source-generated path via the
/// <c>CsvRecordWriterFactory.GetWriter&lt;T&gt;</c> registry fast-path) and one
/// without (reflection path). Same input data, same output format.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class WriteGeneratorBenchmarks
{
    private GenOrder[] genRecords = null!;
    private RefOrder[] refRecords = null!;
    private GenFwOrder[] genFwRecords = null!;
    private RefFwOrder[] refFwRecords = null!;

    [Params(1_000, 10_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        genRecords = new GenOrder[Rows];
        refRecords = new RefOrder[Rows];
        genFwRecords = new GenFwOrder[Rows];
        refFwRecords = new RefFwOrder[Rows];

        for (int i = 0; i < Rows; i++)
        {
            genRecords[i] = new GenOrder { Id = i, Customer = $"Customer {i}", Amount = i * 1.25m };
            refRecords[i] = new RefOrder { Id = i, Customer = $"Customer {i}", Amount = i * 1.25m };
            genFwRecords[i] = new GenFwOrder { Id = i, Customer = $"Customer {i}" };
            refFwRecords[i] = new RefFwOrder { Id = i, Customer = $"Customer {i}" };
        }
    }

    // ── CSV ───────────────────────────────────────────────────────────────

    [Benchmark(Baseline = true, Description = "CSV — reflection writer")]
    public string CsvReflection()
    {
        return Csv.WriteToText(refRecords);
    }

    [Benchmark(Description = "CSV — generated writer")]
    public string CsvGenerated()
    {
        return Csv.WriteToText(genRecords);
    }

    // ── Fixed-Width ───────────────────────────────────────────────────────

    [Benchmark(Description = "FW — reflection writer")]
    public string FixedWidthReflection()
    {
        return FixedWidth.WriteToText(refFwRecords);
    }

    [Benchmark(Description = "FW — generated writer")]
    public string FixedWidthGenerated()
    {
        return FixedWidth.WriteToText(genFwRecords);
    }

    // ── Record types ──────────────────────────────────────────────────────
    //
    // Identical shape for each pair; the only difference is [GenerateBinder]
    // on the "Gen" variants. At runtime:
    //   - GenOrder hits CsvRecordWriterFactory.GetWriter<T>()'s registry-fast-path
    //     (WriterTemplate[] built at module load, no reflection).
    //   - RefOrder misses the registry and falls through to
    //     new CsvRecordWriter<T>(options), which compiles a PropertyAccessor[]
    //     from typeof(T).GetProperties() + expression trees.
    // Same dichotomy holds for FixedWidthRecordWriterFactory.GetWriter<T>().

    [GenerateBinder]
    public class GenOrder
    {
        public int Id { get; set; }
        public string Customer { get; set; } = "";
        public decimal Amount { get; set; }
    }

    public class RefOrder
    {
        public int Id { get; set; }
        public string Customer { get; set; } = "";
        public decimal Amount { get; set; }
    }

    [GenerateBinder]
    public class GenFwOrder
    {
        [PositionalMap(Start = 0, Length = 10, Alignment = FieldAlignment.Right)]
        public int Id { get; set; }

        [PositionalMap(Start = 10, Length = 30)]
        public string Customer { get; set; } = "";
    }

    public class RefFwOrder
    {
        [PositionalMap(Start = 0, Length = 10, Alignment = FieldAlignment.Right)]
        public int Id { get; set; }

        [PositionalMap(Start = 10, Length = 30)]
        public string Customer { get; set; } = "";
    }
}
