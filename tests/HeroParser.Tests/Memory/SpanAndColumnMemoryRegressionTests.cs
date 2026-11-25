using System.Globalization;
using System.Text;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Streaming;
using HeroParser.FixedWidths.Writing;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Shared;
using HeroParser.SeparatedValues.Writing;
using Xunit;

namespace HeroParser.Tests.Memory;

[Collection(MemoryRegressionCollection.NAME)]
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed class SpanAndColumnMemoryRegressionTests
{
    private const int ROW_COUNT = 1_000;
    private const int SMALL_ROWS = 200;
    private const int LARGE_ROWS = 2_000;
    private const int DELTA_ROWS = LARGE_ROWS - SMALL_ROWS;

    private static string BuildCsvPayload(bool quoted, int rows)
    {
        var sb = new StringBuilder(rows * 260);
        for (int i = 0; i < rows; i++)
        {
            if (quoted)
            {
                sb.Append(i).Append(',')
                  .Append(10_000_000_000L + i).Append(',')
                  .Append((i + 1) * 1.25).Append(',')
                  .Append((i + 1) * 12.5m).Append(',')
                  .Append((i & 1) == 0 ? "true" : "false").Append(',')
                  .Append("2026-11-23T12:00:00Z,")
                  .Append("2026-11-23,")
                  .Append("12:34:56.789,")
                  .Append("2026-11-23T12:00:00+02:00,")
                  .Append("01:02:03.456,")
                  .Append("11111111-2222-3333-4444-555555555555,")
                  .Append("1.5,")
                  .Append("170141183460469231731687303715884105727,")
                  .Append("340282366920938463463374607431768211455,")
                  .Append("\"hello,quoted\"\n");
            }
            else
            {
                sb.Append(i).Append(',')
                  .Append(10_000_000_000L + i).Append(',')
                  .Append((i + 1) * 1.25).Append(',')
                  .Append((i + 1) * 12.5m).Append(',')
                  .Append((i & 1) == 0 ? "true" : "false").Append(',')
                  .Append("2026-11-23T12:00:00Z,")
                  .Append("2026-11-23,")
                  .Append("12:34:56.789,")
                  .Append("2026-11-23T12:00:00+02:00,")
                  .Append("01:02:03.456,")
                  .Append("11111111-2222-3333-4444-555555555555,")
                  .Append("1.5,")
                  .Append("170141183460469231731687303715884105727,")
                  .Append("340282366920938463463374607431768211455\n");
            }
        }
        return sb.ToString();
    }

    private static string BuildFixedWidthPayload()
    {
        const string line =
            "0000000042" +
            "00009000000042" +
            "    123.45" +
            "      3.50" +
            "true  " +
            "2026-11-23T12:00:00Z" +
            "2026-11-23" +
            "12:34:56.789" +
            "2026-11-23T12:00:00+02:00 " +
            "01:02:03.456" +
            "11111111-2222-3333-4444-555555555555" +
            "1.5   " +
            "0017014118346046923173168730371588410572" +
            "0034028236692093846346337460743176821145\n";

        var sb = new StringBuilder(ROW_COUNT * line.Length);
        for (int i = 0; i < ROW_COUNT; i++)
            sb.Append(line);
        return sb.ToString();
    }

    private static void RequireParsed(bool ok, string fieldName)
    {
        if (!ok)
            throw new InvalidOperationException($"Failed to parse {fieldName}.");
    }

    private static long ParseAllCsvByteRows(ReadOnlySpan<byte> utf8, CsvReadOptions options)
    {
        long checksum = 0;
        using var reader = Csv.ReadFromByteSpan(utf8, options);
        while (reader.MoveNext())
        {
            var row = reader.Current;
            if (options.EnableQuotedFields)
            {
                checksum += row[14].Unquote((byte)'"').Length;
            }

            RequireParsed(row[0].TryParseInt32(out int i32), "int32");
            RequireParsed(row[1].TryParseInt64(out long i64), "int64");
            RequireParsed(row[2].TryParseDouble(out double d), "double");
            RequireParsed(row[3].TryParseDecimal(out decimal dec), "decimal");
            RequireParsed(row[4].TryParseBoolean(out bool b), "bool");
            RequireParsed(row[5].TryParseDateTime(out DateTime dt), "datetime");
            RequireParsed(row[6].TryParseDateOnly(out DateOnly dateOnly), "dateonly");
            RequireParsed(row[7].TryParseTimeOnly(out TimeOnly timeOnly), "timeonly");
            RequireParsed(row[8].TryParseDateTimeOffset(out DateTimeOffset dto), "datetimeoffset");
            RequireParsed(row[9].TryParseTimeSpan(out TimeSpan ts), "timespan");
            RequireParsed(row[10].TryParseGuid(out Guid g), "guid");
            RequireParsed(row[11].TryParseHalf(out Half h), "half");
            RequireParsed(row[12].TryParseInt128(out Int128 i128), "int128");
            RequireParsed(row[13].TryParseUInt128(out UInt128 u128), "uint128");

            checksum += i32 + (i64 & 0xFF) + (long)d + (long)dec + (b ? 1 : 0) +
                        dt.Day + dateOnly.Day + timeOnly.Millisecond + dto.Offset.Hours +
                        ts.Milliseconds + (g == Guid.Empty ? 0 : 1) + (int)(double)h +
                        (long)(i128 & 0xFF) + (long)(u128 & 0xFF);
        }
        return checksum;
    }

    private static long ParseAllCsvCharRows(ReadOnlySpan<char> chars, CsvReadOptions options)
    {
        long checksum = 0;
        using var reader = Csv.ReadFromCharSpan(chars, options);
        while (reader.MoveNext())
        {
            var row = reader.Current;
            if (options.EnableQuotedFields)
            {
                checksum += row[14].Unquote('"').Length;
            }

            RequireParsed(row[0].TryParseInt32(out int i32), "int32");
            RequireParsed(row[1].TryParseInt64(out long i64), "int64");
            RequireParsed(row[2].TryParseDouble(out double d), "double");
            RequireParsed(row[3].TryParseDecimal(out decimal dec), "decimal");
            RequireParsed(row[4].TryParseBoolean(out bool b), "bool");
            RequireParsed(row[5].TryParseDateTime(out DateTime dt), "datetime");
            RequireParsed(row[6].TryParseDateOnly(out DateOnly dateOnly), "dateonly");
            RequireParsed(row[7].TryParseTimeOnly(out TimeOnly timeOnly), "timeonly");
            RequireParsed(row[8].TryParseDateTimeOffset(out DateTimeOffset dto), "datetimeoffset");
            RequireParsed(row[9].TryParseTimeSpan(out TimeSpan ts), "timespan");
            RequireParsed(row[10].TryParseGuid(out Guid g), "guid");
            RequireParsed(row[11].TryParseHalf(out Half h), "half");
            RequireParsed(row[12].TryParseInt128(out Int128 i128), "int128");
            RequireParsed(row[13].TryParseUInt128(out UInt128 u128), "uint128");

            checksum += i32 + (i64 & 0xFF) + (long)d + (long)dec + (b ? 1 : 0) +
                        dt.Day + dateOnly.Day + timeOnly.Millisecond + dto.Offset.Hours +
                        ts.Milliseconds + (g == Guid.Empty ? 0 : 1) + (int)(double)h +
                        (long)(i128 & 0xFF) + (long)(u128 & 0xFF);
        }
        return checksum;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Csv_ReadFromByteSpan_UnquotedAndQuoted_ZeroPerRowGrowth(bool quoted, bool trackLineNumbers)
    {
        byte[] smallUtf8 = Encoding.UTF8.GetBytes(BuildCsvPayload(quoted, SMALL_ROWS));
        byte[] largeUtf8 = Encoding.UTF8.GetBytes(BuildCsvPayload(quoted, LARGE_ROWS));
        var options = new CsvReadOptions
        {
            EnableQuotedFields = quoted,
            TrackSourceLineNumbers = trackLineNumbers
        };

        long lastChecksum = 0;
        AllocationMeter.AssertZeroPerRowGrowth(
            () => lastChecksum = ParseAllCsvByteRows(smallUtf8, options),
            () => lastChecksum = ParseAllCsvByteRows(largeUtf8, options),
            DELTA_ROWS,
            $"Csv.ReadFromByteSpan(quoted={quoted}, trackLine={trackLineNumbers})");

        long constantBytes = AllocationMeter.MeasureMin(() => lastChecksum = ParseAllCsvByteRows(largeUtf8, options));
        Assert.True(constantBytes <= 128, $"Expected single-cursor constant <= 128 B, measured {constantBytes} B.");
        Assert.NotEqual(0L, lastChecksum);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Csv_ReadFromCharSpan_UnquotedAndQuoted_ZeroPerRowGrowth(bool quoted, bool trackLineNumbers)
    {
        string smallText = BuildCsvPayload(quoted, SMALL_ROWS);
        string largeText = BuildCsvPayload(quoted, LARGE_ROWS);
        var options = new CsvReadOptions
        {
            EnableQuotedFields = quoted,
            TrackSourceLineNumbers = trackLineNumbers
        };

        long lastChecksum = 0;
        AllocationMeter.AssertZeroPerRowGrowth(
            () => lastChecksum = ParseAllCsvCharRows(smallText.AsSpan(), options),
            () => lastChecksum = ParseAllCsvCharRows(largeText.AsSpan(), options),
            DELTA_ROWS,
            $"Csv.ReadFromCharSpan(quoted={quoted}, trackLine={trackLineNumbers})");

        long constantBytes = AllocationMeter.MeasureMin(() => lastChecksum = ParseAllCsvCharRows(largeText.AsSpan(), options));
        Assert.True(constantBytes <= 128, $"Expected single-cursor constant <= 128 B, measured {constantBytes} B.");
        Assert.NotEqual(0L, lastChecksum);
    }

    [Fact]
    public void Csv_ReadFromByteSpan_AllSimdFallbackTiers_ZeroPerRowGrowth()
    {
        byte[] smallUnquoted = Encoding.UTF8.GetBytes(BuildCsvPayload(quoted: false, SMALL_ROWS));
        byte[] largeUnquoted = Encoding.UTF8.GetBytes(BuildCsvPayload(quoted: false, LARGE_ROWS));
        byte[] smallQuoted = Encoding.UTF8.GetBytes(BuildCsvPayload(quoted: true, SMALL_ROWS));
        byte[] largeQuoted = Encoding.UTF8.GetBytes(BuildCsvPayload(quoted: true, LARGE_ROWS));

        var simdOptions = new CsvReadOptions { EnableQuotedFields = true, TrackSourceLineNumbers = true, UseSimdIfAvailable = true };
        var unquotedOptions = new CsvReadOptions { EnableQuotedFields = false, TrackSourceLineNumbers = true, UseSimdIfAvailable = true };
        var scalarOptions = new CsvReadOptions { EnableQuotedFields = true, TrackSourceLineNumbers = true, UseSimdIfAvailable = false };

        long lastChecksum = 0;

        using (HardwareCapabilities.Override(avx512BW: false))
        {
            AllocationMeter.AssertZeroPerRowGrowth(
                () => lastChecksum = ParseAllCsvByteRows(smallUnquoted, unquotedOptions),
                () => lastChecksum = ParseAllCsvByteRows(largeUnquoted, unquotedOptions),
                DELTA_ROWS,
                "Csv.ReadFromByteSpan unquoted with AVX512 disabled");
            AllocationMeter.AssertZeroPerRowGrowth(
                () => lastChecksum = ParseAllCsvByteRows(smallQuoted, simdOptions),
                () => lastChecksum = ParseAllCsvByteRows(largeQuoted, simdOptions),
                DELTA_ROWS,
                "Csv.ReadFromByteSpan quoted with AVX512 disabled");
        }

        using (HardwareCapabilities.Override(avx512BW: false, avx2: false, pclmulqdq: false))
        {
            AllocationMeter.AssertZeroPerRowGrowth(
                () => lastChecksum = ParseAllCsvByteRows(smallUnquoted, unquotedOptions),
                () => lastChecksum = ParseAllCsvByteRows(largeUnquoted, unquotedOptions),
                DELTA_ROWS,
                "Csv.ReadFromByteSpan unquoted with Vector128 / software prefix-XOR");
            AllocationMeter.AssertZeroPerRowGrowth(
                () => lastChecksum = ParseAllCsvByteRows(smallQuoted, simdOptions),
                () => lastChecksum = ParseAllCsvByteRows(largeQuoted, simdOptions),
                DELTA_ROWS,
                "Csv.ReadFromByteSpan quoted with Vector128 / software prefix-XOR");
        }

        using (HardwareCapabilities.Override(avx2: false, avx512BW: false, pclmulqdq: false, advSimd: false, vector128: false))
        {
            AllocationMeter.AssertZeroPerRowGrowth(
                () => lastChecksum = ParseAllCsvByteRows(smallQuoted, scalarOptions),
                () => lastChecksum = ParseAllCsvByteRows(largeQuoted, scalarOptions),
                DELTA_ROWS,
                "Csv.ReadFromByteSpan scalar fallback");
        }

        Assert.NotEqual(0L, lastChecksum);
    }

    [Fact]
    public void FixedWidth_ReadFromUtf8ByteSpan_ZeroAlloc()
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(BuildFixedWidthPayload());
        var options = new FixedWidthReadOptions { TrackSourceLineNumbers = true };
        long lastChecksum = 0;

        AllocationMeter.AssertZeroAlloc(() =>
        {
            long checksum = 0;
            foreach (var row in FixedWidth.ReadFromUtf8ByteSpan(utf8, options))
            {
                RequireParsed(row.GetField(0, 10, (byte)' ', FieldAlignment.Right).TryParseInt32(out int i32), "int32");
                RequireParsed(row.GetField(10, 14, (byte)' ', FieldAlignment.Right).TryParseInt64(out long i64), "int64");
                RequireParsed(row.GetField(24, 10, (byte)' ', FieldAlignment.Right).TryParseDecimal(out decimal dec), "decimal");
                RequireParsed(row.GetField(34, 10, (byte)' ', FieldAlignment.Right).TryParseDouble(out double d), "double");
                RequireParsed(row.GetField(44, 6, (byte)' ', FieldAlignment.Left).TryParseBoolean(out bool b), "bool");
                RequireParsed(row.GetField(50, 20, (byte)' ', FieldAlignment.Left).TryParseDateTime(out DateTime dt), "datetime");
                RequireParsed(row.GetField(70, 10, (byte)' ', FieldAlignment.Left).TryParseDateOnly(out DateOnly dateOnly), "dateonly");
                RequireParsed(row.GetField(80, 12, (byte)' ', FieldAlignment.Left).TryParseTimeOnly(out TimeOnly timeOnly), "timeonly");
                RequireParsed(row.GetField(92, 26, (byte)' ', FieldAlignment.Left).TryParseDateTimeOffset(out DateTimeOffset dto), "datetimeoffset");
                RequireParsed(row.GetField(118, 12, (byte)' ', FieldAlignment.Left).TryParseTimeSpan(out TimeSpan ts), "timespan");
                RequireParsed(row.GetField(130, 36, (byte)' ', FieldAlignment.Left).TryParseGuid(out Guid g), "guid");
                RequireParsed(row.GetField(166, 6, (byte)' ', FieldAlignment.Left).TryParseHalf(out Half h), "half");
                RequireParsed(row.GetField(172, 40, (byte)' ', FieldAlignment.Right).TryParseInt128(out Int128 i128), "int128");
                RequireParsed(row.GetField(212, 40, (byte)' ', FieldAlignment.Right).TryParseUInt128(out UInt128 u128), "uint128");

                checksum += i32 + (i64 & 0xFF) + (long)dec + (long)d + (b ? 1 : 0) +
                            dt.Day + dateOnly.Day + timeOnly.Millisecond + dto.Offset.Hours +
                            ts.Milliseconds + (g == Guid.Empty ? 0 : 1) + (int)(double)h +
                            (long)(i128 & 0xFF) + (long)(u128 & 0xFF);
            }
            lastChecksum = checksum;
        }, "FixedWidth.ReadFromUtf8ByteSpan");

        Assert.NotEqual(0L, lastChecksum);
    }

    [Fact]
    public void FixedWidth_ReadFromCharSpan_ZeroAlloc()
    {
        string text = BuildFixedWidthPayload();
        var options = new FixedWidthReadOptions { TrackSourceLineNumbers = true };
        long lastChecksum = 0;

        AllocationMeter.AssertZeroAlloc(() =>
        {
            long checksum = 0;
            foreach (var row in FixedWidth.ReadFromCharSpan(text.AsSpan(), options))
            {
                RequireParsed(row.GetField(0, 10, ' ', FieldAlignment.Right).TryParseInt32(out int i32), "int32");
                RequireParsed(row.GetField(10, 14, ' ', FieldAlignment.Right).TryParseInt64(out long i64), "int64");
                RequireParsed(row.GetField(24, 10, ' ', FieldAlignment.Right).TryParseDecimal(out decimal dec), "decimal");
                RequireParsed(row.GetField(34, 10, ' ', FieldAlignment.Right).TryParseDouble(out double d), "double");
                RequireParsed(row.GetField(44, 6, ' ', FieldAlignment.Left).TryParseBoolean(out bool b), "bool");
                RequireParsed(row.GetField(50, 20, ' ', FieldAlignment.Left).TryParseDateTime(out DateTime dt), "datetime");
                RequireParsed(row.GetField(70, 10, ' ', FieldAlignment.Left).TryParseDateOnly(out DateOnly dateOnly), "dateonly");
                RequireParsed(row.GetField(80, 12, ' ', FieldAlignment.Left).TryParseTimeOnly(out TimeOnly timeOnly), "timeonly");
                RequireParsed(row.GetField(92, 26, ' ', FieldAlignment.Left).TryParseDateTimeOffset(out DateTimeOffset dto), "datetimeoffset");
                RequireParsed(row.GetField(118, 12, ' ', FieldAlignment.Left).TryParseTimeSpan(out TimeSpan ts), "timespan");
                RequireParsed(row.GetField(130, 36, ' ', FieldAlignment.Left).TryParseGuid(out Guid g), "guid");
                RequireParsed(row.GetField(166, 6, ' ', FieldAlignment.Left).TryParseHalf(out Half h), "half");
                RequireParsed(row.GetField(172, 40, ' ', FieldAlignment.Right).TryParseInt128(out Int128 i128), "int128");
                RequireParsed(row.GetField(212, 40, ' ', FieldAlignment.Right).TryParseUInt128(out UInt128 u128), "uint128");

                checksum += i32 + (i64 & 0xFF) + (long)dec + (long)d + (b ? 1 : 0) +
                            dt.Day + dateOnly.Day + timeOnly.Millisecond + dto.Offset.Hours +
                            ts.Milliseconds + (g == Guid.Empty ? 0 : 1) + (int)(double)h +
                            (long)(i128 & 0xFF) + (long)(u128 & 0xFF);
            }
            lastChecksum = checksum;
        }, "FixedWidth.ReadFromCharSpan");

        Assert.NotEqual(0L, lastChecksum);
    }

    [Fact]
    public void CsvStreamWriter_TypedAndQuotedSpanFields_ZeroAlloc()
    {
        using var writer = new CsvStreamWriter(TextWriter.Null, new CsvWriteOptions { NewLine = "\n" }, leaveOpen: true);
        DateTime dt = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        DateTimeOffset dto = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
        DateOnly dateOnly = new(2026, 11, 23);
        TimeOnly timeOnly = new(12, 34, 56, 789);
        TimeSpan ts = new(0, 1, 2, 3, 456);
        Guid guid = new("11111111-2222-3333-4444-555555555555");
        Half half = (Half)1.5f;
        Int128 i128 = 1234567890123456789;
        UInt128 u128 = 9876543210987654321;

        AllocationMeter.AssertZeroAlloc(() =>
        {
            for (int i = 0; i < ROW_COUNT; i++)
            {
                writer.WriteField("plain_span".AsSpan());
                writer.WriteField("needs,\"quoting".AsSpan());
                writer.WriteField(i);
                writer.WriteField(10_000_000_000L + i);
                writer.WriteField(1.5 + i);
                writer.WriteField(99.25m + i);
                writer.WriteField((i & 1) == 0);
                writer.WriteField(dt);
                writer.WriteField(dto);
                writer.WriteField(dateOnly);
                writer.WriteField(timeOnly);
                writer.WriteField(ts);
                writer.WriteField('X');
                writer.WriteField(half);
                writer.WriteField(i128);
                writer.WriteField(u128);
                writer.WriteField(guid);
                writer.EndRow();
            }
            writer.Flush();
        }, "CsvStreamWriter typed and quoted ReadOnlySpan<char> fields");
    }

    [Fact]
    public async Task CsvAsyncStreamWriter_TypedFields_SyncCompletedValueTasks_ZeroAlloc()
    {
        await using var writer = new CsvAsyncStreamWriter(Stream.Null, new CsvWriteOptions { NewLine = "\n" }, leaveOpen: true);
        DateTime dt = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        DateTimeOffset dto = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
        DateOnly dateOnly = new(2026, 11, 23);
        TimeOnly timeOnly = new(12, 34, 56, 789);
        TimeSpan ts = new(0, 1, 2, 3, 456);
        Guid guid = new("11111111-2222-3333-4444-555555555555");
        Half half = (Half)1.5f;
        Int128 i128 = 1234567890123456789;
        UInt128 u128 = 9876543210987654321;
        var ct = TestContext.Current.CancellationToken;

        AllocationMeter.AssertZeroAlloc(() =>
        {
            for (int i = 0; i < ROW_COUNT; i++)
            {
                CompleteSync(writer.WriteFieldAsync("plain", ct));
                CompleteSync(writer.WriteFieldAsync(i, ct));
                CompleteSync(writer.WriteFieldAsync(10_000_000_000L + i, ct));
                CompleteSync(writer.WriteFieldAsync(1.5 + i, ct));
                CompleteSync(writer.WriteFieldAsync(99.25m + i, ct));
                CompleteSync(writer.WriteFieldAsync((i & 1) == 0, ct));
                CompleteSync(writer.WriteFieldAsync(dt, ct));
                CompleteSync(writer.WriteFieldAsync(dto, ct));
                CompleteSync(writer.WriteFieldAsync(dateOnly, ct));
                CompleteSync(writer.WriteFieldAsync(timeOnly, ct));
                CompleteSync(writer.WriteFieldAsync(ts, ct));
                CompleteSync(writer.WriteFieldAsync('X', ct));
                CompleteSync(writer.WriteFieldAsync(half, ct));
                CompleteSync(writer.WriteFieldAsync(i128, ct));
                CompleteSync(writer.WriteFieldAsync(u128, ct));
                CompleteSync(writer.WriteFieldAsync(guid, ct));
                CompleteSync(writer.EndRowAsync(ct));
            }
            CompleteSync(writer.FlushAsync(ct));
        }, "CsvAsyncStreamWriter typed ValueTask hot path");
    }

    [Fact]
    public async Task FixedWidthStreamWriter_And_AsyncStreamWriter_TypedFields_ZeroAlloc()
    {
        DateTime dt = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        Guid guid = new("11111111-2222-3333-4444-555555555555");

        using (var syncWriter = new FixedWidthStreamWriter(TextWriter.Null, new FixedWidthWriteOptions { NewLine = "\n" }, leaveOpen: true))
        {
            AllocationMeter.AssertZeroAlloc(() =>
            {
                for (int i = 0; i < ROW_COUNT; i++)
                {
                    syncWriter.WriteField("fixed_span".AsSpan(), 12);
                    syncWriter.WriteField(i, 8, FieldAlignment.Right, '0');
                    syncWriter.WriteField(10_000_000_000L + i, 14, FieldAlignment.Right, '0');
                    syncWriter.WriteField(1.5 + i, 10);
                    syncWriter.WriteField(99.25m + i, 10);
                    syncWriter.WriteField((i & 1) == 0, 6);
                    syncWriter.WriteField(dt, 20, null, null, "yyyy-MM-ddTHH:mm:ssZ");
                    syncWriter.WriteField(guid, 36);
                    syncWriter.EndRow();
                }
                syncWriter.Flush();
            }, "FixedWidthStreamWriter typed fields");
        }

        ReadOnlyMemory<char> preAllocatedLabel = "fixed_memory".AsMemory();
        await using var asyncWriter = new FixedWidthAsyncStreamWriter(Stream.Null, new FixedWidthWriteOptions { NewLine = "\n" }, leaveOpen: true);
        var ct = TestContext.Current.CancellationToken;
        AllocationMeter.AssertZeroAlloc(() =>
        {
            for (int i = 0; i < ROW_COUNT; i++)
            {
                CompleteSync(asyncWriter.WriteFieldAsync(preAllocatedLabel, 12, FieldAlignment.Left, ' ', ct));
                CompleteSync(asyncWriter.WriteFieldAsync(preAllocatedLabel, 16, FieldAlignment.Right, '0', ct));
                CompleteSync(asyncWriter.EndRowAsync(ct));
            }
            CompleteSync(asyncWriter.FlushAsync(ct));
        }, "FixedWidthAsyncStreamWriter ReadOnlyMemory<char> hot path");
    }

    private static void CompleteSync(ValueTask valueTask)
    {
        if (!valueTask.IsCompletedSuccessfully)
            throw new InvalidOperationException("Expected synchronous ValueTask completion.");
        valueTask.GetAwaiter().GetResult();
    }
}
