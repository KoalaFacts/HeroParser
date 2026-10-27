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

        // This checks the exact logical input for both parsers before any timing.
        // A checksum by itself cannot prove that every conversion succeeded.
        ValidateCharFields();
        ValidateUtf8Fields();
        const long EXPECTED_TOTAL = 4_294_967_424L;
        if (Char_Parse() != EXPECTED_TOTAL || Utf8_Parse() != EXPECTED_TOTAL
            || Utf8_Parse_Culture() != 35)
            throw new InvalidOperationException("Column parsing benchmark returned an invalid checksum.");
    }

    private void ValidateCharFields()
    {
        using var reader = Csv.ReadFromText(csv);
        if (!reader.MoveNext())
            throw new InvalidOperationException("Missing character CSV row.");

        var row = reader.Current;
        if (row.ColumnCount != 6
            || !row[0].TryParseInt32(out int number) || number != 123
            || !row[1].TryParseUInt32(out uint unsigned) || unsigned != uint.MaxValue
            || !row[2].TryParseDateTime(out DateTime date) || date.Second != 56
            || !row[3].TryParseTimeZoneInfo(out var zone) || zone is null || zone.BaseUtcOffset != TimeSpan.FromHours(1)
            || !row[4].TryParseBoolean(out bool active) || !active
            || !row[5].TryParseDouble(out double amount) || amount != 3.14
            || reader.MoveNext())
            throw new InvalidOperationException("Character CSV column parsing changed the expected data.");
    }

    private void ValidateUtf8Fields()
    {
        using var reader = Csv.ReadFromByteSpan(utf8);
        if (!reader.MoveNext())
            throw new InvalidOperationException("Missing UTF-8 CSV row.");

        var row = reader.Current;
        if (row.ColumnCount != 6
            || !row[0].TryParseInt32(out int number) || number != 123
            || !row[1].TryParseUInt32(out uint unsigned) || unsigned != uint.MaxValue
            || !row[2].TryParseDateTime(out DateTime date) || date.Second != 56
            || !row[3].TryParseTimeZoneInfo(out var zone) || zone is null || zone.BaseUtcOffset != TimeSpan.FromHours(1)
            || !row[4].TryParseBoolean(out bool active) || !active
            || !row[5].TryParseDouble(out double amount) || amount != 3.14
            || reader.MoveNext())
            throw new InvalidOperationException("UTF-8 CSV column parsing changed the expected data.");
    }

    [Benchmark(Baseline = true, Description = "Char - invariant ints/dates")]
    public long Char_Parse()
    {
        using var reader = Csv.ReadFromText(csv);
        reader.MoveNext();
        var row = reader.Current;

        long total = 0;
        total += row[0].TryParseInt32(out var i32) ? i32 : 0;
        total += row[1].TryParseUInt32(out var u32) ? u32 : 0;
        total += row[2].TryParseDateTime(out var _) ? 1 : 0;
        total += row[3].TryParseTimeZoneInfo(out var _) ? 1 : 0;
        total += row[4].TryParseBoolean(out var b) && b ? 1 : 0;
        total += row[5].TryParseDouble(out var d) ? (int)d : 0;
        return total;
    }

    [Benchmark(Description = "UTF8 - invariant ints/dates")]
    public long Utf8_Parse()
    {
        using var reader = Csv.ReadFromByteSpan(utf8);
        reader.MoveNext();
        var row = reader.Current;

        long total = 0;
        total += row[0].TryParseInt32(out var i32) ? i32 : 0;
        total += row[1].TryParseUInt32(out var u32) ? u32 : 0;
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
