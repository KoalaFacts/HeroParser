using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using System.Text;

namespace HeroParser.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class ColumnParseBenchmarks
{
    private string csv = null!;
    private byte[] utf8 = null!;

    [GlobalSetup]
    public void Setup()
    {
        csv = "123,4294967295,2025-11-20T12:34:56Z,Europe/Berlin,TRUE,3.14";
        utf8 = Encoding.UTF8.GetBytes(csv);
    }

    [Benchmark(Baseline = true, Description = "Char - invariant ints/dates")]
    public int Char_Parse()
    {
        using var reader = Csv.ReadFromText(csv);
        reader.MoveNext();
        var row = reader.Current;

        int total = 0;
        total += row[0].TryParseInt32(out var i32) ? i32 : 0;
        total += row[1].TryParseUInt32(out var u32) ? (int)u32 : 0;
        total += row[2].TryParseDateTime(out var _) ? 1 : 0;
        total += row[3].TryParseTimeZoneInfo(out var _) ? 1 : 0;
        total += row[4].TryParseBoolean(out var b) && b ? 1 : 0;
        total += row[5].TryParseDouble(out var d) ? (int)d : 0;
        return total;
    }

    [Benchmark(Description = "UTF8 - invariant ints/dates")]
    public int Utf8_Parse()
    {
        using var reader = Csv.ReadFromByteSpan(utf8);
        reader.MoveNext();
        var row = reader.Current;

        int total = 0;
        total += row[0].TryParseInt32(out var i32) ? i32 : 0;
        total += row[1].TryParseUInt32(out var u32) ? (int)u32 : 0;
        total += row[2].TryParseDateTime(out var _) ? 1 : 0;
        total += row[3].TryParseTimeZoneInfo(out var _) ? 1 : 0;
        total += row[4].TryParseBoolean(out var b) && b ? 1 : 0;
        total += row[5].TryParseDouble(out var d) ? (int)d : 0;
        return total;
    }

    [Benchmark(Description = "UTF8 - culture/format parsing (allocates)")]
    public int Utf8_Parse_Culture()
    {
        using var reader = Csv.ReadFromByteSpan(utf8);
        reader.MoveNext();
        var row = reader.Current;

        int total = 0;
        total += row[2].TryParseDateTime(out var dt, "yyyy-MM-ddTHH:mm:ssZ", provider: null) ? dt.Minute : 0;
        total += row[3].TryParseTimeZoneInfo(out var tz) ? tz.BaseUtcOffset.Hours : 0;
        return total;
    }
}
