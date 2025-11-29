using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Records.Binding;
using HeroParser.FixedWidths.Writing;
using System.Globalization;
using System.Text;

namespace HeroParser.Benchmarks;

/// <summary>
/// Benchmarks for fixed-width file writing operations.
/// Tests writer performance, formatting, and typed record serialization.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class FixedWidthWriterBenchmarks
{
    private WriteBenchmarkRecord[] records = null!;

    [Params(1_000, 10_000, 100_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        records = [.. Enumerable.Range(1, Rows)
            .Select(i => new WriteBenchmarkRecord
            {
                Id = i,
                Name = $"Person{i}",
                Amount = i * 123.45m,
                Date = DateTime.Today.AddDays(-i % 365)
            })];

        Console.WriteLine($"Hardware: {Hardware.GetHardwareInfo()}");
        Console.WriteLine($"Record count: {records.Length:N0}");
    }

    [FixedWidthGenerateBinder]
    public class WriteBenchmarkRecord
    {
        [FixedWidthColumn(Start = 0, Length = 10, Alignment = FieldAlignment.Right, PadChar = '0')]
        public int Id { get; set; }

        [FixedWidthColumn(Start = 10, Length = 20)]
        public string Name { get; set; } = "";

        [FixedWidthColumn(Start = 30, Length = 15, Alignment = FieldAlignment.Right, Format = "F2")]
        public decimal Amount { get; set; }

        [FixedWidthColumn(Start = 45, Length = 10, Format = "yyyy-MM-dd")]
        public DateTime Date { get; set; }
    }

    [Benchmark(Baseline = true)]
    public string WriteToText_TypedRecords()
    {
        return FixedWidth.Write<WriteBenchmarkRecord>().ToText(records);
    }

    [Benchmark]
    public int WriteToStream_TypedRecords()
    {
        using var stream = new MemoryStream();
        FixedWidth.Write<WriteBenchmarkRecord>().ToStream(stream, records);
        return (int)stream.Length;
    }

    [Benchmark]
    public string WriteManual_StreamWriter()
    {
        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        using var writer = new FixedWidthStreamWriter(sw, FixedWidthWriterOptions.Default, leaveOpen: true);

        foreach (var record in records)
        {
            writer.WriteField(record.Id, 10, FieldAlignment.Right, '0');
            writer.WriteField(record.Name, 20);
            writer.WriteField(record.Amount, 15, FieldAlignment.Right, format: "F2");
            writer.WriteField(record.Date, 10, format: "yyyy-MM-dd");
            writer.EndRow();
        }

        writer.Flush();
        return sb.ToString();
    }
}

/// <summary>
/// Benchmarks for custom type converters in fixed-width parsing.
/// Compares built-in converters vs custom converters.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class FixedWidthCustomConverterBenchmarks
{
    private string dataWithMoneyField = null!;

    [Params(1_000, 10_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var sb = new StringBuilder();
        for (int i = 1; i <= Rows; i++)
        {
            sb.Append($"{i:D10}");              // Id: 10 chars
            sb.Append($"{"Item" + i,-20}");     // Name: 20 chars
            sb.Append($"{i * 99.99m,15:F2}");   // Amount: 15 chars (Money field)
            sb.AppendLine();
        }
        dataWithMoneyField = sb.ToString();

        Console.WriteLine($"Hardware: {Hardware.GetHardwareInfo()}");
        Console.WriteLine($"Data size: {dataWithMoneyField.Length:N0} chars");
    }

    // Using built-in decimal converter
    [FixedWidthGenerateBinder]
    public class RecordWithDecimal
    {
        [FixedWidthColumn(Start = 0, Length = 10, Alignment = FieldAlignment.Right, PadChar = '0')]
        public int Id { get; set; }

        [FixedWidthColumn(Start = 10, Length = 20)]
        public string Name { get; set; } = "";

        [FixedWidthColumn(Start = 30, Length = 15, Alignment = FieldAlignment.Right)]
        public decimal Amount { get; set; }
    }

    // Using custom Money type that requires custom converter
    public class RecordWithMoney
    {
        [FixedWidthColumn(Start = 0, Length = 10, Alignment = FieldAlignment.Right, PadChar = '0')]
        public int Id { get; set; }

        [FixedWidthColumn(Start = 10, Length = 20)]
        public string Name { get; set; } = "";

        [FixedWidthColumn(Start = 30, Length = 15, Alignment = FieldAlignment.Right)]
        public Money Amount { get; set; }
    }

    // Simple Money value type for testing custom converters
    public readonly record struct Money(decimal Value);

    [Benchmark(Baseline = true)]
    public decimal ParseWithBuiltInDecimal()
    {
        decimal total = 0;
        foreach (var record in FixedWidth.Read<RecordWithDecimal>().FromText(dataWithMoneyField))
        {
            total += record.Amount;
        }
        return total;
    }

    [Benchmark]
    public decimal ParseWithCustomMoneyConverter()
    {
        decimal total = 0;
        foreach (var record in FixedWidth.Read<RecordWithMoney>()
            .RegisterConverter<Money>((value, culture, format, out result) =>
            {
                if (decimal.TryParse(value, NumberStyles.Number, culture, out var amount))
                {
                    result = new Money(amount);
                    return true;
                }
                result = default;
                return false;
            })
            .FromText(dataWithMoneyField))
        {
            total += record.Amount.Value;
        }
        return total;
    }
}

/// <summary>
/// Benchmarks for UTF-8 byte span reader.
/// Compares char-based vs byte-based parsing.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class FixedWidthByteSpanBenchmarks
{
    private string charData = null!;
    private byte[] utf8Data = null!;

    [Params(1_000, 10_000, 100_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var sb = new StringBuilder();
        for (int i = 1; i <= Rows; i++)
        {
            sb.Append($"{i:D10}");              // Id: 10 bytes
            sb.Append($"{"Name" + i,-20}");     // Name: 20 bytes
            sb.Append($"{i * 100,10}");         // Value: 10 bytes
            sb.AppendLine();
        }
        charData = sb.ToString();
        utf8Data = Encoding.UTF8.GetBytes(charData);

        Console.WriteLine($"Hardware: {Hardware.GetHardwareInfo()}");
        Console.WriteLine($"Char data: {charData.Length:N0} chars");
        Console.WriteLine($"UTF-8 data: {utf8Data.Length:N0} bytes");
    }

    [Benchmark(Baseline = true)]
    public int ParseFromCharSpan()
    {
        int total = 0;
        foreach (var row in FixedWidth.ReadFromText(charData))
        {
            var idField = row.GetField(0, 10, '0', FieldAlignment.Right);
            if (int.TryParse(idField.CharSpan, out var id))
            {
                total += id;
            }
        }
        return total;
    }

    [Benchmark]
    public int ParseFromUtf8ByteSpan()
    {
        int total = 0;
        foreach (var row in FixedWidth.ReadFromUtf8ByteSpan(utf8Data))
        {
            var idField = row.GetField(0, 10, (byte)'0', FieldAlignment.Right);
            if (idField.TryParseInt32(out var id))
            {
                total += id;
            }
        }
        return total;
    }

    [Benchmark]
    public int ParseFromUtf8ByteSpan_AllFields()
    {
        int total = 0;
        foreach (var row in FixedWidth.ReadFromUtf8ByteSpan(utf8Data))
        {
            var idField = row.GetField(0, 10, (byte)'0', FieldAlignment.Right);
            var nameField = row.GetField(10, 20);
            var valueField = row.GetField(30, 10, (byte)' ', FieldAlignment.Right);

            if (idField.TryParseInt32(out var id))
            {
                total += id;
            }
            total += nameField.Length;
            if (valueField.TryParseInt32(out var value))
            {
                total += value;
            }
        }
        return total;
    }
}

/// <summary>
/// Benchmarks for field alignment and padding operations.
/// Tests different alignment modes and padding characters.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class FixedWidthAlignmentBenchmarks
{
    private string testData = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Create data with different alignment patterns
        var sb = new StringBuilder();
        for (int i = 0; i < 10_000; i++)
        {
            sb.Append($"{i:D10}");                      // Right-aligned zeros
            sb.Append($"{"Text" + i,-20}");             // Left-aligned spaces
            sb.Append($"{i,10}");                       // Right-aligned spaces
            sb.Append($"{"Center" + i}".PadLeft(15).PadRight(20)); // Center-ish
            sb.AppendLine();
        }
        testData = sb.ToString();

        Console.WriteLine($"Hardware: {Hardware.GetHardwareInfo()}");
        Console.WriteLine($"Data size: {testData.Length:N0} chars");
    }

    [Benchmark(Baseline = true)]
    public int Alignment_None()
    {
        int total = 0;
        foreach (var row in FixedWidth.ReadFromText(testData))
        {
            total += row.GetField(0, 10, ' ', FieldAlignment.None).CharSpan.Length;
        }
        return total;
    }

    [Benchmark]
    public int Alignment_Left()
    {
        int total = 0;
        foreach (var row in FixedWidth.ReadFromText(testData))
        {
            total += row.GetField(10, 20, ' ', FieldAlignment.Left).CharSpan.Length;
        }
        return total;
    }

    [Benchmark]
    public int Alignment_Right()
    {
        int total = 0;
        foreach (var row in FixedWidth.ReadFromText(testData))
        {
            total += row.GetField(30, 10, ' ', FieldAlignment.Right).CharSpan.Length;
        }
        return total;
    }

    [Benchmark]
    public int Alignment_Center()
    {
        int total = 0;
        foreach (var row in FixedWidth.ReadFromText(testData))
        {
            total += row.GetField(40, 20, ' ', FieldAlignment.Center).CharSpan.Length;
        }
        return total;
    }

    [Benchmark]
    public int Alignment_RightWithZeroPad()
    {
        int total = 0;
        foreach (var row in FixedWidth.ReadFromText(testData))
        {
            total += row.GetField(0, 10, '0', FieldAlignment.Right).CharSpan.Length;
        }
        return total;
    }
}
