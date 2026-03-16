using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

namespace HeroParser.Benchmarks;

/// <summary>
/// Benchmarks for Excel (.xlsx) writing performance.
/// Compares the reflection-based path against the source-generated path for typed record serialization.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class ExcelWriteBenchmarks
{
    private BenchmarkExcelRecord[] records = null!;
    private MemoryStream stream = null!;

    /// <summary>
    /// Populates test records and allocates a reusable <see cref="MemoryStream"/> before any benchmark iteration runs.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        records = new BenchmarkExcelRecord[10_000];
        for (int i = 0; i < records.Length; i++)
        {
            records[i] = new BenchmarkExcelRecord
            {
                Name = $"Name{i}",
                Quantity = i,
                Price = i * 9.99m,
                Score = i * 0.01,
                IsActive = i % 2 == 0,
                CreatedDate = DateTime.Today.AddDays(-i % 365),
                Category = $"Category{i % 10}",
                Id = i,
                Rating = i * 0.1f,
                Description = $"Description for record {i}"
            };
        }

        stream = new MemoryStream(capacity: 4 * 1024 * 1024);

        Console.WriteLine($"Hardware: {Hardware.GetHardwareInfo()}");
        Console.WriteLine($"Record count: {records.Length:N0}");
    }

    /// <summary>
    /// Releases the reusable stream after all iterations have completed.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        stream.Dispose();
    }

    /// <summary>
    /// Writes 10,000 records using the fluent <c>Excel.Write&lt;T&gt;().ToStream()</c> builder API (source-generated path).
    /// </summary>
    [Benchmark(Baseline = true)]
    public long FluentBuilder_ToStream()
    {
        stream.SetLength(0);
        Excel.Write<BenchmarkExcelRecord>().ToStream(stream, records, leaveOpen: true);
        return stream.Length;
    }

    /// <summary>
    /// Writes 10,000 records using the <c>Excel.WriteToStream&lt;T&gt;()</c> facade API (source-generated path).
    /// </summary>
    [Benchmark]
    public long Facade_WriteToStream()
    {
        stream.SetLength(0);
        Excel.WriteToStream(stream, records, leaveOpen: true);
        return stream.Length;
    }

    /// <summary>
    /// Writes 10,000 records using the <c>Excel.Write&lt;T&gt;().ToBytes()</c> builder API, which
    /// allocates a fresh <see cref="MemoryStream"/> internally and returns the resulting byte array.
    /// </summary>
    [Benchmark]
    public int FluentBuilder_ToBytes()
    {
        return Excel.Write<BenchmarkExcelRecord>().ToBytes(records).Length;
    }

    /// <summary>
    /// The record type used for Excel write benchmarks.
    /// </summary>
    [GenerateBinder]
    public class BenchmarkExcelRecord
    {
        /// <summary>Gets or sets the name.</summary>
        public string Name { get; set; } = "";
        /// <summary>Gets or sets the quantity.</summary>
        public int Quantity { get; set; }
        /// <summary>Gets or sets the price.</summary>
        public decimal Price { get; set; }
        /// <summary>Gets or sets the score.</summary>
        public double Score { get; set; }
        /// <summary>Gets or sets a value indicating whether this record is active.</summary>
        public bool IsActive { get; set; }
        /// <summary>Gets or sets the creation date.</summary>
        public DateTime CreatedDate { get; set; }
        /// <summary>Gets or sets the category.</summary>
        public string Category { get; set; } = "";
        /// <summary>Gets or sets the identifier.</summary>
        public long Id { get; set; }
        /// <summary>Gets or sets the rating.</summary>
        public float Rating { get; set; }
        /// <summary>Gets or sets the description.</summary>
        public string Description { get; set; } = "";
    }
}
