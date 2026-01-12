using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Records.Binding;
using System.Text;

namespace HeroParser.Benchmarks;

/// <summary>
/// Benchmarks for fixed-width file parsing.
/// Compares line-delimited vs fixed-record-length parsing modes,
/// and source-generated vs reflection-based record binding.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class FixedWidthBenchmarks
{
    private string lineDelimitedData = null!;
    private string fixedLengthData = null!;
    private string typedRecordData = null!;
    private FixedWidthReadOptions defaultOptions = null!;
    private FixedWidthReadOptions fixedLengthOptions = null!;

    [Params(1_000, 10_000, 100_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        lineDelimitedData = GenerateLineDelimitedData(Rows);
        fixedLengthData = GenerateFixedLengthData(Rows);
        typedRecordData = GenerateTypedRecordData(Rows);

        defaultOptions = new FixedWidthReadOptions();
        fixedLengthOptions = new FixedWidthReadOptions { RecordLength = 50 }; // 50 chars per record

        Console.WriteLine($"Hardware: {Hardware.GetHardwareInfo()}");
        Console.WriteLine($"Line-delimited size: {lineDelimitedData.Length:N0} chars");
        Console.WriteLine($"Fixed-length size: {fixedLengthData.Length:N0} chars");
        Console.WriteLine($"Typed record size: {typedRecordData.Length:N0} chars");
    }

    /// <summary>
    /// Generates line-delimited fixed-width data (records separated by newlines).
    /// Format: ID(10) + Name(20) + Amount(10) + Date(10) = 50 chars per line + newline
    /// </summary>
    private static string GenerateLineDelimitedData(int rows)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < rows; i++)
        {
            sb.Append($"{i:D10}");                      // ID: 10 chars
            sb.Append($"{"Name" + i,-20}");             // Name: 20 chars left-aligned
            sb.Append($"{(i * 100.5m):0000000.00}");    // Amount: 10 chars
            sb.Append($"{DateTime.Today:yyyy-MM-dd}");  // Date: 10 chars
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>
    /// Generates fixed-length data (no newlines, records identified by position).
    /// Format: ID(10) + Name(20) + Amount(10) + Date(10) = 50 chars per record
    /// </summary>
    private static string GenerateFixedLengthData(int rows)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < rows; i++)
        {
            sb.Append($"{i:D10}");                      // ID: 10 chars
            sb.Append($"{"Name" + i,-20}");             // Name: 20 chars left-aligned
            sb.Append($"{(i * 100.5m):0000000.00}");    // Amount: 10 chars
            sb.Append($"{DateTime.Today:yyyy-MM-dd}");  // Date: 10 chars
            // No newline - records are identified by fixed length
        }
        return sb.ToString();
    }

    /// <summary>
    /// Generates data for typed record binding tests.
    /// Format matches BenchmarkRecord: Id(10) + Name(20) + Value(10)
    /// </summary>
    private static string GenerateTypedRecordData(int rows)
    {
        var sb = new StringBuilder();
        // Start from 1 to avoid i=0 which becomes "0000000000" and trims to "" with Right alignment
        for (int i = 1; i <= rows; i++)
        {
            sb.Append($"{i:D10}");              // Id: 10 chars, zero-padded (e.g., "0000000001")
            sb.Append($"{"Person" + i,-20}");   // Name: 20 chars, left-aligned
            sb.Append($"{i * 100,10}");         // Value: 10 chars, right-aligned
            sb.AppendLine();
        }
        return sb.ToString();
    }

    #region Raw Parsing Benchmarks

    [Benchmark(Baseline = true)]
    public int ParseLineDelimited()
    {
        int rowCount = 0;
        foreach (var row in FixedWidth.ReadFromText(lineDelimitedData, defaultOptions))
        {
            rowCount++;
            // Access first field to ensure parsing happens
            _ = row.GetField(0, 10);
        }
        return rowCount;
    }

    [Benchmark]
    public int ParseFixedLength()
    {
        int rowCount = 0;
        foreach (var row in FixedWidth.ReadFromText(fixedLengthData, fixedLengthOptions))
        {
            rowCount++;
            // Access first field to ensure parsing happens
            _ = row.GetField(0, 10);
        }
        return rowCount;
    }

    [Benchmark]
    public int ParseWithFieldAccess()
    {
        int total = 0;
        foreach (var row in FixedWidth.ReadFromText(lineDelimitedData, defaultOptions))
        {
            // Access all 4 fields
            var id = row.GetField(0, 10);
            var name = row.GetField(10, 20);
            var amount = row.GetField(30, 10);
            var date = row.GetField(40, 10);

            total += id.CharSpan.Length + name.CharSpan.Length;
        }
        return total;
    }

    #endregion

    #region Record Binding Benchmarks

    [FixedWidthGenerateBinder]
    public class BenchmarkRecord
    {
        [FixedWidthColumn(Start = 0, Length = 10, Alignment = FieldAlignment.Right, PadChar = '0')]
        public int Id { get; set; }

        [FixedWidthColumn(Start = 10, Length = 20)]
        public string Name { get; set; } = "";

        [FixedWidthColumn(Start = 30, Length = 10, Alignment = FieldAlignment.Right)]
        public int Value { get; set; }
    }

    [Benchmark]
    public int BindToRecords_SourceGenerated()
    {
        int total = 0;
        foreach (var record in FixedWidth.Read<BenchmarkRecord>().FromText(typedRecordData))
        {
            total += record.Id + record.Value;
        }
        return total;
    }

    /// <summary>
    /// Uses the callback-based ForEach API with object reuse for near-zero allocation.
    /// Only string allocations should occur (unavoidable for string properties).
    /// </summary>
    [Benchmark]
    public int BindToRecords_ForEach()
    {
        int total = 0;
        FixedWidth.Read<BenchmarkRecord>().ForEachFromText(typedRecordData, record =>
        {
            total += record.Id + record.Value;
        });
        return total;
    }

    #endregion
}

/// <summary>
/// Micro-benchmarks for individual fixed-width operations.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class FixedWidthMicroBenchmarks
{
    private string singleRecord = null!;
    private FixedWidthReadOptions options = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Single 50-char record
        singleRecord = "0000000001John Doe            0000012345";
        options = new FixedWidthReadOptions();
    }

    [Benchmark]
    public string GetField_LeftAligned()
    {
        foreach (var row in FixedWidth.ReadFromText(singleRecord, options))
        {
            // Left-aligned field - trims trailing spaces
            return row.GetField(10, 20, ' ', FieldAlignment.Left).ToString();
        }
        return "";
    }

    [Benchmark]
    public string GetField_RightAligned()
    {
        foreach (var row in FixedWidth.ReadFromText(singleRecord, options))
        {
            // Right-aligned field - trims leading zeros
            return row.GetField(0, 10, '0', FieldAlignment.Right).ToString();
        }
        return "";
    }

    [Benchmark]
    public string GetField_NoTrim()
    {
        foreach (var row in FixedWidth.ReadFromText(singleRecord, options))
        {
            // No trimming - returns raw field
            return row.GetField(10, 20, ' ', FieldAlignment.None).ToString();
        }
        return "";
    }

    [Benchmark]
    public int ParseInt_RightAligned()
    {
        foreach (var row in FixedWidth.ReadFromText(singleRecord, options))
        {
            var field = row.GetField(30, 10, '0', FieldAlignment.Right);
            return int.Parse(field.CharSpan);
        }
        return 0;
    }
}

/// <summary>
/// Compares throughput across API shapes: string, memory stream, file streaming (sync), and file streaming (async).
/// Similar to CSV's StreamingThroughputBenchmarks.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class FixedWidthStreamingBenchmarks
{
    private string fixedWidthData = null!;
    private byte[] fixedWidthUtf8 = null!;
    private string filePath = null!;

    [Params(10_000, 100_000)]
    public int Rows { get; set; }

    [Params(4, 8)]
    public int Fields { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        fixedWidthData = GenerateFixedWidthData(Rows, Fields);
        fixedWidthUtf8 = Encoding.UTF8.GetBytes(fixedWidthData);
        filePath = Path.Combine(Path.GetTempPath(), $"heroparser-fw-stream-{Rows}-{Fields}-{Guid.NewGuid():N}.dat");
        File.WriteAllBytes(filePath, fixedWidthUtf8);

        Console.WriteLine($"Hardware: {Hardware.GetHardwareInfo()}");
        Console.WriteLine($"Data size: {fixedWidthData.Length:N0} chars, {fixedWidthUtf8.Length:N0} bytes");
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

    /// <summary>
    /// Generates line-delimited fixed-width data with variable number of fields.
    /// Each field is 10 chars wide.
    /// </summary>
    private static string GenerateFixedWidthData(int rows, int fields)
    {
        var sb = new StringBuilder();
        for (int r = 0; r < rows; r++)
        {
            for (int f = 0; f < fields; f++)
            {
                sb.Append($"{"Field" + f + "_" + r,-10}");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    [Benchmark(Baseline = true)]
    public int ParseFromText()
    {
        int total = 0;
        foreach (var row in FixedWidth.ReadFromText(fixedWidthData))
        {
            total += row.GetField(0, 10).CharSpan.Length;
        }
        return total;
    }

    [Benchmark]
    public int ParseFromStreamMemory()
    {
        using var stream = new MemoryStream(fixedWidthUtf8, writable: false);
        int total = 0;
        foreach (var record in FixedWidth.Read<StreamingBenchmarkRecord>().FromStream(stream))
        {
            total += record.Field0?.Length ?? 0;
        }
        return total;
    }

    [Benchmark]
    public int ParseFromFileStreaming()
    {
        using var fs = File.OpenRead(filePath);
        int total = 0;
        foreach (var record in FixedWidth.Read<StreamingBenchmarkRecord>().FromStream(fs))
        {
            total += record.Field0?.Length ?? 0;
        }
        return total;
    }

    [Benchmark]
    public async Task<int> ParseFromFileAsyncStreaming()
    {
        int total = 0;
        await foreach (var record in FixedWidth.Read<StreamingBenchmarkRecord>().FromFileAsync(filePath))
        {
            total += record.Field0?.Length ?? 0;
        }
        return total;
    }

    [FixedWidthGenerateBinder]
    public class StreamingBenchmarkRecord
    {
        [FixedWidthColumn(Start = 0, Length = 10)]
        public string Field0 { get; set; } = "";
    }
}

/// <summary>
/// Field parsing benchmarks for fixed-width files.
/// Similar to CSV's ColumnParseBenchmarks.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class FixedWidthFieldParseBenchmarks
{
    private string singleRecord = null!;
    private FixedWidthReadOptions options = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Format: ID(10) + UInt(10) + DateTime(20) + Bool(5) + Double(10) = 55 chars
        // Values: "0000000123" + "4294967295" + "2025-11-20T12:34:56Z" + "TRUE " + "     3.14"
        singleRecord = "00000001234294967295  2025-11-20T12:34:56TRUE      3.14";
        options = new FixedWidthReadOptions();
    }

    [Benchmark(Baseline = true)]
    public int ParseAllFields()
    {
        foreach (var row in FixedWidth.ReadFromText(singleRecord, options))
        {
            int total = 0;

            // Int32 - right-aligned with zeros
            var intField = row.GetField(0, 10, '0', FieldAlignment.Right);
            if (int.TryParse(intField.CharSpan, out var i32)) total += i32;

            // UInt32 - right-aligned with zeros
            var uintField = row.GetField(10, 10, '0', FieldAlignment.Right);
            if (uint.TryParse(uintField.CharSpan, out var u32)) total += (int)u32;

            // DateTime - left-aligned with spaces
            var dateField = row.GetField(20, 20, ' ', FieldAlignment.Left);
            if (DateTime.TryParse(dateField.CharSpan, out _)) total += 1;

            // Boolean - left-aligned with spaces
            var boolField = row.GetField(40, 5, ' ', FieldAlignment.Left);
            if (bool.TryParse(boolField.CharSpan, out var b) && b) total += 1;

            // Double - right-aligned with spaces
            var doubleField = row.GetField(45, 10, ' ', FieldAlignment.Right);
            if (double.TryParse(doubleField.CharSpan, out var d)) total += (int)d;

            return total;
        }
        return 0;
    }

    [Benchmark]
    public int ParseIntsOnly()
    {
        foreach (var row in FixedWidth.ReadFromText(singleRecord, options))
        {
            int total = 0;

            var intField = row.GetField(0, 10, '0', FieldAlignment.Right);
            if (int.TryParse(intField.CharSpan, out var i32)) total += i32;

            var uintField = row.GetField(10, 10, '0', FieldAlignment.Right);
            if (uint.TryParse(uintField.CharSpan, out var u32)) total += (int)u32;

            return total;
        }
        return 0;
    }

    [Benchmark]
    public int ParseDateTimeOnly()
    {
        foreach (var row in FixedWidth.ReadFromText(singleRecord, options))
        {
            var dateField = row.GetField(20, 20, ' ', FieldAlignment.Left);
            return DateTime.TryParse(dateField.CharSpan, out var dt) ? dt.Minute : 0;
        }
        return 0;
    }

    [Benchmark]
    public int ParseDoubleOnly()
    {
        foreach (var row in FixedWidth.ReadFromText(singleRecord, options))
        {
            var doubleField = row.GetField(45, 10, ' ', FieldAlignment.Right);
            return double.TryParse(doubleField.CharSpan, out var d) ? (int)d : 0;
        }
        return 0;
    }
}

