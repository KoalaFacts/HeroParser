using System.Numerics;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

namespace HeroParser.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class ColumnParseBenchmarks
{
    private string csv = null!;
    private byte[] utf8 = null!;
    private string extendedCsv = null!;
    private byte[] extendedUtf8 = null!;

    [GlobalSetup]
    public void Setup()
    {
        csv = "123,4294967295,2025-11-20T12:34:56Z,Europe/Berlin,TRUE,3.14";
        utf8 = Encoding.UTF8.GetBytes(csv);
        extendedCsv = "2026-11-23,14:30:00,2026-11-23T14:30:00+02:00,01:02:03,1.5,170141183460469231731687303715884105727,340282366920938463463374607431768211455,11111111-2222-3333-4444-555555555555";
        extendedUtf8 = Encoding.UTF8.GetBytes(extendedCsv);

        // This checks the exact logical input for both parsers before any timing.
        // A checksum by itself cannot prove that every conversion succeeded.
        ValidateCharFields();
        ValidateUtf8Fields();
        ValidateExtendedCharFields();
        ValidateExtendedUtf8Fields();
        const long EXPECTED_TOTAL = 4_294_967_424L;
        if (Char_Parse() != EXPECTED_TOTAL || Utf8_Parse() != EXPECTED_TOTAL
            || Utf8_Parse_Culture() != 35)
            throw new InvalidOperationException("Column parsing benchmark returned an invalid checksum.");
        if (Char_Parse_ExtendedTypes() != 68 || Utf8_Parse_ExtendedTypes() != 68)
            throw new InvalidOperationException("Extended-type column parsing benchmark returned an invalid checksum.");
    }

    private void ValidateExtendedCharFields()
    {
        using var reader = Csv.ReadFromText(extendedCsv);
        if (!reader.MoveNext())
            throw new InvalidOperationException("Missing extended character CSV row.");

        var row = reader.Current;
        if (row.ColumnCount != 8
            || !row[0].TryParseDateOnly(out var d) || d != new DateOnly(2026, 11, 23)
            || !row[1].TryParseTimeOnly(out var t) || t != new TimeOnly(14, 30, 0)
            || !row[2].TryParseDateTimeOffset(out var dto) || dto.Offset != TimeSpan.FromHours(2)
            || !row[3].TryParseTimeSpan(out var ts) || ts != new TimeSpan(1, 2, 3)
            || !row[4].TryParseHalf(out var h) || h != (Half)1.5f
            || !row[5].TryParseInt128(out var i128) || i128 <= long.MaxValue
            || !row[6].TryParseUInt128(out var u128) || u128 <= ulong.MaxValue
            || !row[7].TryParseGuid(out var g) || g != new Guid("11111111-2222-3333-4444-555555555555")
            || reader.MoveNext())
            throw new InvalidOperationException("Extended character CSV column parsing changed the expected data.");
    }

    private void ValidateExtendedUtf8Fields()
    {
        using var reader = Csv.ReadFromByteSpan(extendedUtf8);
        if (!reader.MoveNext())
            throw new InvalidOperationException("Missing extended UTF-8 CSV row.");

        var row = reader.Current;
        if (row.ColumnCount != 8
            || !row[0].TryParseDateOnly(out var d) || d != new DateOnly(2026, 11, 23)
            || !row[1].TryParseTimeOnly(out var t) || t != new TimeOnly(14, 30, 0)
            || !row[2].TryParseDateTimeOffset(out var dto) || dto.Offset != TimeSpan.FromHours(2)
            || !row[3].TryParseTimeSpan(out var ts) || ts != new TimeSpan(1, 2, 3)
            || !row[4].TryParseHalf(out var h) || h != (Half)1.5f
            || !row[5].TryParseInt128(out var i128) || i128 <= long.MaxValue
            || !row[6].TryParseUInt128(out var u128) || u128 <= ulong.MaxValue
            || !row[7].TryParseGuid(out var g) || g != new Guid("11111111-2222-3333-4444-555555555555")
            || reader.MoveNext())
            throw new InvalidOperationException("Extended UTF-8 CSV column parsing changed the expected data.");
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

    [Benchmark(Description = "UTF8 - custom format & timezone parsing")]
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

    [Benchmark(Description = "Char - extended CLR types (DateOnly/TimeOnly/DateTimeOffset/TimeSpan/Half/Int128/UInt128/Guid)")]
    public int Char_Parse_ExtendedTypes()
    {
        using var reader = Csv.ReadFromText(extendedCsv);
        reader.MoveNext();
        var row = reader.Current;

        int total = 0;
        total += row[0].TryParseDateOnly(out var d) ? d.Day : 0;
        total += row[1].TryParseTimeOnly(out var t) ? t.Minute : 0;
        total += row[2].TryParseDateTimeOffset(out var dto) ? dto.Offset.Hours : 0;
        total += row[3].TryParseTimeSpan(out var ts) ? ts.Hours : 0;
        total += row[4].TryParseHalf(out var h) ? (int)(float)h : 0;
        total += row[5].TryParseInt128(out var i128) && i128 > 0 ? 1 : 0;
        total += row[6].TryParseUInt128(out var u128) && u128 > 0 ? 1 : 0;
        total += row[7].TryParseGuid(out var _) ? 9 : 0;
        return total;
    }

    [Benchmark(Description = "UTF8 - extended CLR types (DateOnly/TimeOnly/DateTimeOffset/TimeSpan/Half/Int128/UInt128/Guid)")]
    public int Utf8_Parse_ExtendedTypes()
    {
        using var reader = Csv.ReadFromByteSpan(extendedUtf8);
        reader.MoveNext();
        var row = reader.Current;

        int total = 0;
        total += row[0].TryParseDateOnly(out var d) ? d.Day : 0;
        total += row[1].TryParseTimeOnly(out var t) ? t.Minute : 0;
        total += row[2].TryParseDateTimeOffset(out var dto) ? dto.Offset.Hours : 0;
        total += row[3].TryParseTimeSpan(out var ts) ? ts.Hours : 0;
        total += row[4].TryParseHalf(out var h) ? (int)(float)h : 0;
        total += row[5].TryParseInt128(out var i128) && i128 > 0 ? 1 : 0;
        total += row[6].TryParseUInt128(out var u128) && u128 > 0 ? 1 : 0;
        total += row[7].TryParseGuid(out var _) ? 9 : 0;
        return total;
    }
}
