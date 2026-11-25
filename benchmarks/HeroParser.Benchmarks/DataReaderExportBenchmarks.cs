using System.Data;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using HeroParser.Conversion;
using HeroParser.FixedWidths;

namespace HeroParser.Benchmarks;

/// <summary>
/// Measures allocation footprint and throughput of NativeAOT-safe DbDataReader exports across all 5 formats
/// and streaming HTB DataReader consumption.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class DataReaderExportBenchmarks
{
    private DataTable table = null!;
    private IReadOnlyList<FixedWidthFieldDefinition> fixedColumns = null!;
    private byte[] htbBytes = null!;

    [Params(1_000, 10_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Amount", typeof(decimal));
        table.Columns.Add("Active", typeof(bool));
        table.Columns.Add("CreatedAt", typeof(DateTime));
        table.Columns.Add("Token", typeof(Guid));

        DateTime baseDate = new(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < Rows; i++)
        {
            table.Rows.Add(
                i,
                $"item-{i}",
                i * 1.25m,
                (i & 1) == 0,
                baseDate.AddSeconds(i),
                Guid.Empty);
        }

        fixedColumns =
        [
            new("Id", 8, FieldAlignment.Right, '0'),
            new("Name", 16, FieldAlignment.Left),
            new("Amount", 12, FieldAlignment.Right),
            new("Active", 6, FieldAlignment.Left)
        ];

        using var htbMs = new MemoryStream();
        using (var source = table.CreateDataReader())
            Htb.WriteFromDataReader(source, htbMs, leaveOpen: true);
        htbBytes = htbMs.ToArray();

        ValidateOutputs();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        table?.Dispose();
    }

    private void ValidateOutputs()
    {
        if (Csv_WriteFromDataReader() <= 0 ||
            FixedWidth_WriteFromDataReader() <= 0 ||
            Jsonl_WriteFromDataReader() <= 0 ||
            Htb_WriteFromDataReader() <= 0 ||
            Excel_WriteFromDataReader() <= 0)
        {
            throw new InvalidOperationException("DataReader export benchmark produced an empty payload.");
        }

        long expectedSum = (long)Rows * (Rows - 1) / 2;
        if (Htb_CreateDataReader_Read() != expectedSum)
            throw new InvalidOperationException("HTB DataReader read checksum mismatch.");
    }

    [Benchmark(Baseline = true)]
    public long Csv_WriteFromDataReader()
    {
        using var reader = table.CreateDataReader();
        using var ms = new MemoryStream(capacity: Rows * 64);
        Csv.WriteFromDataReader(reader, ms, leaveOpen: true);
        return ms.Length;
    }

    [Benchmark]
    public long FixedWidth_WriteFromDataReader()
    {
        using var reader = table.CreateDataReader();
        using var ms = new MemoryStream(capacity: Rows * 48);
        FixedWidth.WriteFromDataReader(reader, ms, fixedColumns, leaveOpen: true);
        return ms.Length;
    }

    [Benchmark]
    public long Jsonl_WriteFromDataReader()
    {
        using var reader = table.CreateDataReader();
        using var ms = new MemoryStream(capacity: Rows * 128);
        Jsonl.WriteFromDataReader(reader, ms, leaveOpen: true);
        return ms.Length;
    }

    [Benchmark]
    public long Htb_WriteFromDataReader()
    {
        using var reader = table.CreateDataReader();
        using var ms = new MemoryStream(capacity: Rows * 64);
        Htb.WriteFromDataReader(reader, ms, leaveOpen: true);
        return ms.Length;
    }

    [Benchmark]
    public long Excel_WriteFromDataReader()
    {
        using var reader = table.CreateDataReader();
        using var ms = new MemoryStream(capacity: Rows * 48);
        Excel.WriteFromDataReader(reader, ms, leaveOpen: true);
        return ms.Length;
    }

    [Benchmark]
    public long Htb_CreateDataReader_Read()
    {
        using var ms = new MemoryStream(htbBytes, writable: false);
        using var reader = Htb.CreateDataReader(ms, leaveOpen: true);
        long total = 0;
        while (reader.Read())
        {
            total += reader.GetInt32(0);
        }
        return total;
    }
}
