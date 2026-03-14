using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Mapping;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Records;
using System.IO.Pipelines;
using System.Text;

namespace HeroParser.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class CsvPipeReaderBenchmarks
{
    private byte[] csvUtf8 = null!;
    private CsvReadOptions options = null!;

    [Params(10_000, 100_000)]
    public int Rows { get; set; }

    [Params(4, 8)]
    public int Columns { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        csvUtf8 = Encoding.UTF8.GetBytes(GenerateCsv(Rows, Columns));
        options = new CsvReadOptions
        {
            MaxColumnCount = Columns + 4,
            MaxRowCount = Rows + 100
        };
    }

    [Benchmark(Baseline = true)]
    public int ParseFromByteSpan()
    {
        using var reader = Csv.ReadFromByteSpan(csvUtf8, options);
        int total = 0;
        foreach (var row in reader)
        {
            total += row.ColumnCount;
        }
        return total;
    }

    [Benchmark]
    public async Task<int> ParseFromPipeReader()
    {
        using var stream = new MemoryStream(csvUtf8, writable: false);
        var reader = PipeReader.Create(stream);

        int total = 0;
        await foreach (var row in Csv.ReadFromPipeReaderAsync(reader, options))
        {
            total += row.ColumnCount;
        }

        return total;
    }

    [Benchmark]
    public async Task<int> ParseFromPipeSequenceReader()
    {
        using var stream = new MemoryStream(csvUtf8, writable: false);
        var pipeReader = PipeReader.Create(stream);
        await using var reader = Csv.CreatePipeSequenceReader(pipeReader, options);

        int total = 0;
        while (await reader.MoveNextAsync())
        {
            total += reader.Current.ColumnCount;
        }

        return total;
    }

    private static string GenerateCsv(int rows, int columns)
    {
        var sb = new StringBuilder();
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                if (c > 0)
                {
                    sb.Append(',');
                }

                sb.Append($"val{r}_{c}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }
}

[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class CsvTypedPipeReaderBenchmarks
{
    private string csvData = null!;
    private byte[] csvUtf8 = null!;
    private CsvReadOptions parserOptions = null!;
    private CsvRecordOptions recordOptions = null!;

    [Params(10_000, 100_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        csvData = GenerateTypedCsv(Rows);
        csvUtf8 = Encoding.UTF8.GetBytes(csvData);
        parserOptions = new CsvReadOptions
        {
            MaxColumnCount = 4,
            MaxRowCount = Rows + 100
        };
        recordOptions = new CsvRecordOptions
        {
            HasHeaderRow = true
        };
    }

    [Benchmark(Baseline = true)]
    public int BindFromText()
    {
        using var reader = Csv.DeserializeRecords<CsvTypedPipeRecord>(csvData, recordOptions, parserOptions);
        int total = 0;
        foreach (var record in reader)
        {
            total += record.Age + record.Name.Length;
        }

        return total;
    }

    [Benchmark]
    public async Task<int> BindFromPipeReader_Static()
    {
        using var stream = new MemoryStream(csvUtf8, writable: false);
        var reader = PipeReader.Create(stream);

        int total = 0;
        await foreach (var record in Csv.DeserializeRecordsAsync<CsvTypedPipeRecord>(reader, recordOptions, parserOptions))
        {
            total += record.Age + record.Name.Length;
        }

        return total;
    }

    [Benchmark]
    public async Task<int> BindFromPipeReader_Builder()
    {
        using var stream = new MemoryStream(csvUtf8, writable: false);
        var reader = PipeReader.Create(stream);

        int total = 0;
        await foreach (var record in Csv.Read<CsvTypedPipeRecord>()
            .WithMaxRows(Rows + 100)
            .FromPipeReaderAsync(reader))
        {
            total += record.Age + record.Name.Length;
        }

        return total;
    }

    private static string GenerateTypedCsv(int rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Name,Age");
        for (int i = 1; i <= rows; i++)
        {
            sb.Append("Name");
            sb.Append(i);
            sb.Append(',');
            sb.Append(i % 100);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    [GenerateBinder]
    public sealed class CsvTypedPipeRecord
    {
        public string Name { get; set; } = string.Empty;

        public int Age { get; set; }
    }
}

[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class FixedWidthPipeReaderBenchmarks
{
    private byte[] fixedWidthUtf8 = null!;
    private FixedWidthReadOptions options = null!;

    [Params(10_000, 100_000)]
    public int Rows { get; set; }

    [Params(4, 8)]
    public int Fields { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        fixedWidthUtf8 = Encoding.UTF8.GetBytes(GenerateFixedWidthData(Rows, Fields));
        options = new FixedWidthReadOptions
        {
            MaxRecordCount = Rows + 100
        };
    }

    [Benchmark(Baseline = true)]
    public int ParseFromUtf8ByteSpan()
    {
        int total = 0;
        foreach (var row in FixedWidth.ReadFromUtf8ByteSpan(fixedWidthUtf8, options))
        {
            total += row.GetField(0, 10).Length;
        }
        return total;
    }

    [Benchmark]
    public async Task<int> ParseFromPipeReader()
    {
        using var stream = new MemoryStream(fixedWidthUtf8, writable: false);
        var reader = PipeReader.Create(stream);

        int total = 0;
        await foreach (var row in FixedWidth.ReadFromPipeReaderAsync(reader, options))
        {
            total += row.GetField(0, 10).Length;
        }

        return total;
    }

    private static string GenerateFixedWidthData(int rows, int fields)
    {
        var sb = new StringBuilder();
        for (int r = 0; r < rows; r++)
        {
            for (int f = 0; f < fields; f++)
            {
                AppendFixedWidth(sb, $"F{f}_{r}", 10);
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void AppendFixedWidth(StringBuilder sb, string value, int width)
    {
        if (value.Length >= width)
        {
            sb.Append(value, 0, width);
            return;
        }

        sb.Append(value);
        sb.Append(' ', width - value.Length);
    }
}

[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class FixedWidthTypedPipeReaderBenchmarks
{
    private string fixedWidthData = null!;
    private byte[] fixedWidthUtf8 = null!;
    private FixedWidthReadOptions options = null!;
    private TypedPipeMappedRecordMap map = null!;

    [Params(10_000, 100_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        fixedWidthData = GenerateTypedData(Rows);
        fixedWidthUtf8 = Encoding.UTF8.GetBytes(fixedWidthData);
        options = new FixedWidthReadOptions
        {
            MaxRecordCount = Rows + 100
        };
        map = new TypedPipeMappedRecordMap();
    }

    [Benchmark(Baseline = true)]
    public int BindFromText_SourceGenerated()
    {
        int total = 0;
        foreach (var record in FixedWidth.Read<TypedPipeRecord>().FromText(fixedWidthData))
        {
            total += record.Id + record.Name.Length;
        }
        return total;
    }

    [Benchmark]
    public async Task<int> BindFromPipeReader_Static()
    {
        using var stream = new MemoryStream(fixedWidthUtf8, writable: false);
        var reader = PipeReader.Create(stream);

        int total = 0;
        await foreach (var record in FixedWidth.DeserializeRecordsAsync<TypedPipeRecord>(reader, options))
        {
            total += record.Id + record.Name.Length;
        }

        return total;
    }

    [Benchmark]
    public async Task<int> BindFromPipeReader_Builder()
    {
        using var stream = new MemoryStream(fixedWidthUtf8, writable: false);
        var reader = PipeReader.Create(stream);

        int total = 0;
        await foreach (var record in FixedWidth.Read<TypedPipeRecord>()
            .WithMaxRecords(Rows + 100)
            .FromPipeReaderAsync(reader))
        {
            total += record.Id + record.Name.Length;
        }

        return total;
    }

    [Benchmark]
    public async Task<int> BindFromPipeReader_BuilderWithMap()
    {
        using var stream = new MemoryStream(fixedWidthUtf8, writable: false);
        var reader = PipeReader.Create(stream);

        int total = 0;
        await foreach (var record in FixedWidth.Read<TypedPipeMappedRecord>()
            .WithMaxRecords(Rows + 100)
            .WithMap(map)
            .FromPipeReaderAsync(reader))
        {
            total += record.Id + record.Name.Length;
        }

        return total;
    }

    private static string GenerateTypedData(int rows)
    {
        var sb = new StringBuilder();
        for (int i = 1; i <= rows; i++)
        {
            sb.Append($"{i:D4}");
            AppendFixedWidth(sb, $"N{i}", 6);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void AppendFixedWidth(StringBuilder sb, string value, int width)
    {
        if (value.Length >= width)
        {
            sb.Append(value, 0, width);
            return;
        }

        sb.Append(value);
        sb.Append(' ', width - value.Length);
    }

    [GenerateBinder]
    public class TypedPipeRecord
    {
        [PositionalMap(Start = 0, Length = 4, Alignment = FieldAlignment.Right, PadChar = '0')]
        public int Id { get; set; }

        [PositionalMap(Start = 4, Length = 6)]
        public string Name { get; set; } = string.Empty;
    }

    public class TypedPipeMappedRecord
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    public class TypedPipeMappedRecordMap : FixedWidthMap<TypedPipeMappedRecord>
    {
        public TypedPipeMappedRecordMap()
        {
            Map(x => x.Id, c => c.Start(0).Length(4).PadChar('0').Alignment(FieldAlignment.Right));
            Map(x => x.Name, c => c.Start(4).Length(6));
        }
    }
}
