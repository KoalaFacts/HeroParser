using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using HeroParser.SeparatedValues.Reading.Shared;
using HeroParser.SeparatedValues.Writing;
using System.Text;

namespace HeroParser.Benchmarks;

/// <summary>
/// Benchmarks comparing sync vs async CSV writing performance.
/// Measures throughput and allocations for various writing scenarios.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class AsyncWriterBenchmarks
{
    private TestRecord[] records = null!;
    private string[] stringRow = null!;
    private string filePath = null!;

    [Params(1_000, 10_000, 100_000)]
    public int Rows { get; set; }

    [Params(10)]
    public int Columns { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        records = GenerateRecords(Rows);
        stringRow = GenerateStringRow(Columns);
        filePath = Path.Combine(Path.GetTempPath(), $"heroparser-async-bench-{Guid.NewGuid():N}.csv");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private static TestRecord[] GenerateRecords(int count)
    {
        var result = new TestRecord[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = new TestRecord
            {
                Id = i,
                Name = $"Name{i}",
                Value = i * 1.5,
                IsActive = i % 2 == 0,
                Created = DateTime.Now.AddDays(-i)
            };
        }
        return result;
    }

    private static string[] GenerateStringRow(int columns)
    {
        var result = new string[columns];
        for (int i = 0; i < columns; i++)
        {
            result[i] = $"value{i}";
        }
        return result;
    }

    #region Sync vs Async - Raw Row Writing

    [Benchmark(Baseline = true)]
    public void SyncWriter_RawRows_MemoryStream()
    {
        using var ms = new MemoryStream();
        using var sw = new StreamWriter(ms, Encoding.UTF8, bufferSize: 16 * 1024, leaveOpen: true);
        using var writer = new CsvStreamWriter(sw, CsvWriterOptions.Default, leaveOpen: true);

        for (int r = 0; r < Rows; r++)
        {
            writer.WriteRow(stringRow);
        }
        writer.Flush();
    }

    [Benchmark]
    public async Task AsyncWriter_RawRows_MemoryStream()
    {
        using var ms = new MemoryStream();
        await using var writer = new CsvAsyncStreamWriter(ms, CsvWriterOptions.Default, Encoding.UTF8, leaveOpen: true);

        for (int r = 0; r < Rows; r++)
        {
            await writer.WriteRowAsync(stringRow).ConfigureAwait(false);
        }
        await writer.FlushAsync().ConfigureAwait(false);
    }

    #endregion

    #region Sync vs Async - Factory Method Comparison

    [Benchmark]
    public void SyncWriter_CreateStreamWriter_Stream()
    {
        using var ms = new MemoryStream();
        using var writer = Csv.CreateStreamWriter(ms);

        for (int r = 0; r < Rows; r++)
        {
            writer.WriteRow(stringRow);
        }
        writer.Flush();
    }

    [Benchmark]
    public async Task AsyncWriter_CreateAsyncStreamWriter()
    {
        using var ms = new MemoryStream();
        await using var writer = Csv.CreateAsyncStreamWriter(ms);

        for (int r = 0; r < Rows; r++)
        {
            await writer.WriteRowAsync(stringRow).ConfigureAwait(false);
        }
        await writer.FlushAsync().ConfigureAwait(false);
    }

    #endregion

    #region Sync vs Async - Record Writing to Stream

    [Benchmark]
    public void SyncWriter_Records_MemoryStream()
    {
        using var ms = new MemoryStream();
        Csv.WriteToStream(ms, records);
    }

    [Benchmark]
    public async Task AsyncWriter_Records_MemoryStream()
    {
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(ms, ToAsyncEnumerable(records)).ConfigureAwait(false);
    }

    /// <summary>
    /// Async writer using IEnumerable directly (avoids IAsyncEnumerable wrapper overhead)
    /// </summary>
    [Benchmark]
    public async Task AsyncWriter_Records_Direct_MemoryStream()
    {
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(ms, records).ConfigureAwait(false);
    }

    #endregion

    #region Sync vs Async - File Writing

    [Benchmark]
    public void SyncWriter_Records_File()
    {
        Csv.WriteToFile(filePath, records);
    }

    [Benchmark]
    public async Task AsyncWriter_Records_File()
    {
        await Csv.WriteToFileAsync(filePath, ToAsyncEnumerable(records)).ConfigureAwait(false);
    }

    /// <summary>
    /// Async file writer using IEnumerable directly (avoids IAsyncEnumerable wrapper overhead)
    /// </summary>
    [Benchmark]
    public async Task AsyncWriter_Records_Direct_File()
    {
        await Csv.WriteToFileAsync(filePath, records).ConfigureAwait(false);
    }

    #endregion

    #region Builder API - Sync vs Async Streaming

    [Benchmark]
    public void SyncBuilder_ToStream()
    {
        using var ms = new MemoryStream();
        Csv.Write<TestRecord>()
            .WithHeader()
            .ToStream(ms, records);
    }

    [Benchmark]
    public async Task AsyncBuilder_ToStreamAsyncStreaming()
    {
        using var ms = new MemoryStream();
        await Csv.Write<TestRecord>()
            .WithHeader()
            .ToStreamAsyncStreaming(ms, ToAsyncEnumerable(records)).ConfigureAwait(false);
    }

    /// <summary>
    /// Builder async streaming with IEnumerable directly (avoids IAsyncEnumerable wrapper overhead)
    /// </summary>
    [Benchmark]
    public async Task AsyncBuilder_ToStreamAsyncStreaming_Direct()
    {
        using var ms = new MemoryStream();
        await Csv.Write<TestRecord>()
            .WithHeader()
            .ToStreamAsyncStreaming(ms, records).ConfigureAwait(false);
    }

    #endregion

    #region Async Writer with Options

    [Benchmark]
    public async Task AsyncWriter_AlwaysQuoted()
    {
        using var ms = new MemoryStream();
        var options = new CsvWriterOptions { QuoteStyle = QuoteStyle.Always };
        await using var writer = new CsvAsyncStreamWriter(ms, options, Encoding.UTF8, leaveOpen: true);

        for (int r = 0; r < Rows; r++)
        {
            await writer.WriteRowAsync(stringRow).ConfigureAwait(false);
        }
        await writer.FlushAsync().ConfigureAwait(false);
    }

    [Benchmark]
    public async Task AsyncWriter_InjectionProtection()
    {
        using var ms = new MemoryStream();
        var options = new CsvWriterOptions { InjectionProtection = CsvInjectionProtection.EscapeWithQuote };
        await using var writer = new CsvAsyncStreamWriter(ms, options, Encoding.UTF8, leaveOpen: true);

        for (int r = 0; r < Rows; r++)
        {
            await writer.WriteRowAsync(stringRow).ConfigureAwait(false);
        }
        await writer.FlushAsync().ConfigureAwait(false);
    }

    #endregion

    #region Helpers

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> source)
    {
        foreach (var item in source)
        {
            yield return item;
        }
        await Task.CompletedTask; // Ensure async state machine is generated
    }

    #endregion

    public class TestRecord
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public double Value { get; set; }
        public bool IsActive { get; set; }
        public DateTime Created { get; set; }
    }
}
