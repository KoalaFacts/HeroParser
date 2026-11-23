using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text;
using HeroParser.AotTests.Models;
using HeroParser.Conversion;
using HeroParser.SeparatedValues.Reading.Records;

namespace HeroParser.AotTests.Tests;

[UnconditionalSuppressMessage(
    "Trimming",
    "IL2026:Members attributed with RequiresUnreferencedCode may break when trimming",
    Justification = "All record types in this test class carry [GenerateBinder]; reflection fallbacks are never used.")]
[UnconditionalSuppressMessage(
    "AOT",
    "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling.",
    Justification = "All record types in this test class carry [GenerateBinder]; reflection fallbacks are never used.")]
public static class ExtendedTypesAotTests
{
    public static void Run(TestRunner runner)
    {
        runner.PrintSection("Extended CLR Types AOT Tests");

        runner.Run("Extended Types: CSV generated round-trip (char + UTF-8)", CsvRoundTrip);
        runner.Run("Extended Types: FixedWidth generated round-trip (char + UTF-8)", FixedWidthRoundTrip);
        runner.Run("Extended Types: Excel generated round-trip", ExcelRoundTrip);
        runner.Run("Extended Types: JSONL generated read + DataReader export", JsonlAndDataReaderRoundTrip);
        runner.Run("Extended Types: HTB generated + DataReader + converter round-trip", HtbRoundTrip);
    }

    private static ExtendedTabularAotRecord CreateTabularSample() => new()
    {
        OffsetVal = new DateTimeOffset(2026, 11, 23, 14, 30, 45, TimeSpan.FromHours(11)),
        DateVal = new DateOnly(2026, 11, 23),
        TimeVal = new TimeOnly(14, 30, 45, 321),
        Duration = new TimeSpan(1, 2, 3, 4),
        UIntVal = 3_500_000_000u,
        ULongVal = 17_000_000_000_000_000_001ul,
        UShortVal = 61_000,
        SByteVal = -42,
        CharVal = 'A',
        HalfVal = (Half)6.5,
        Int128Val = Int128.Parse("-123456789012345678901234567890", CultureInfo.InvariantCulture),
        UInt128Val = UInt128.Parse("123456789012345678901234567890", CultureInfo.InvariantCulture),
        BigIntVal = BigInteger.Parse("99999999999999999999999999999999999999", CultureInfo.InvariantCulture),
        UriVal = new Uri("https://aot.example/test"),
        BytesVal = [5, 10, 15, 20],
        Token = new CustomAotToken(88)
    };

    private static void VerifyTabularSample(ExtendedTabularAotRecord expected, ExtendedTabularAotRecord actual, string context)
    {
        if (actual.OffsetVal != expected.OffsetVal ||
            actual.DateVal != expected.DateVal ||
            actual.TimeVal != expected.TimeVal ||
            actual.Duration != expected.Duration ||
            actual.UIntVal != expected.UIntVal ||
            actual.ULongVal != expected.ULongVal ||
            actual.UShortVal != expected.UShortVal ||
            actual.SByteVal != expected.SByteVal ||
            actual.CharVal != expected.CharVal ||
            actual.HalfVal != expected.HalfVal ||
            actual.Int128Val != expected.Int128Val ||
            actual.UInt128Val != expected.UInt128Val ||
            actual.BigIntVal != expected.BigIntVal ||
            actual.UriVal != expected.UriVal ||
            !actual.BytesVal.SequenceEqual(expected.BytesVal) ||
            actual.Token != expected.Token)
        {
            throw new Exception($"{context} extended record mismatch");
        }
    }

    private static void CsvRoundTrip()
    {
        var sample = CreateTabularSample();
        string csv = Csv.WriteToText([sample]);

        var charParsed = Csv.DeserializeRecords<ExtendedTabularAotRecord>(csv).ToList();
        if (charParsed.Count != 1)
            throw new Exception($"Expected 1 CSV record, got {charParsed.Count}");
        VerifyTabularSample(sample, charParsed[0], "CSV char");

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var byteParsed = new List<ExtendedTabularAotRecord>();
        using (var byteReader = Csv.Read<ExtendedTabularAotRecord>().FromStream(ms, out _))
        {
            while (byteReader.MoveNext())
            {
                byteParsed.Add(byteReader.Current);
            }
        }
        if (byteParsed.Count != 1)
            throw new Exception($"Expected 1 CSV byte record, got {byteParsed.Count}");
        VerifyTabularSample(sample, byteParsed[0], "CSV byte");
    }

    private static void FixedWidthRoundTrip()
    {
        var sample = CreateTabularSample();
        string text = FixedWidth.WriteToText([sample]);

        var charParsed = FixedWidth.DeserializeRecords<ExtendedTabularAotRecord>(text).ToList();
        if (charParsed.Count != 1)
            throw new Exception($"Expected 1 FixedWidth record, got {charParsed.Count}");
        VerifyTabularSample(sample, charParsed[0], "FixedWidth char");

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(text));
        var byteParsed = FixedWidth.Read<ExtendedTabularAotRecord>().FromStream(ms).ToList();
        if (byteParsed.Count != 1)
            throw new Exception($"Expected 1 FixedWidth byte record, got {byteParsed.Count}");
        VerifyTabularSample(sample, byteParsed[0], "FixedWidth byte");
    }

    private static void ExcelRoundTrip()
    {
        var sample = CreateTabularSample();
        byte[] bytes = Excel.Write<ExtendedTabularAotRecord>().ToBytes([sample]);
        using var ms = new MemoryStream(bytes);
        var parsed = Excel.Read<ExtendedTabularAotRecord>().FromStream(ms);
        if (parsed.Count != 1)
            throw new Exception($"Expected 1 Excel record, got {parsed.Count}");
        VerifyTabularSample(sample, parsed[0], "Excel");
    }

    private static void JsonlAndDataReaderRoundTrip()
    {
        const string JSONL =
            "{\"OffsetVal\":\"2026-11-23T14:30:45.0000000+11:00\",\"DateVal\":\"2026-11-23\",\"TimeVal\":\"14:30:45.321\"," +
            "\"Duration\":\"1.02:03:04\",\"UIntVal\":3500000000,\"ULongVal\":17000000000000000001," +
            "\"UShortVal\":61000,\"SByteVal\":-42,\"CharVal\":\"A\",\"HalfVal\":6.5," +
            "\"Int128Val\":-123456789012345678901234567890,\"UInt128Val\":123456789012345678901234567890," +
            "\"BigIntVal\":99999999999999999999999999999999999999," +
            "\"UriVal\":\"https://aot.example/test\",\"BytesVal\":\"BQoUFA==\",\"Token\":\"aot:88\"}\n";

        var expected = CreateTabularSample();
        expected.BytesVal = [5, 10, 20, 20]; // BQoUFA==
        var parsed = Jsonl.Read<ExtendedTabularAotRecord>().FromText(JSONL).ToList();
        if (parsed.Count != 1)
            throw new Exception($"Expected 1 JSONL record, got {parsed.Count}");
        VerifyTabularSample(expected, parsed[0], "JSONL");
    }

    private static void HtbRoundTrip()
    {
        ExtendedHtbAotRecord sample = new()
        {
            OffsetVal = new DateTimeOffset(2026, 11, 23, 14, 30, 45, TimeSpan.FromHours(-5)),
            DateVal = new DateOnly(2026, 11, 23),
            TimeVal = new TimeOnly(14, 30, 45, 789),
            Duration = TimeSpan.FromHours(27),
            ShortVal = -30000,
            UShortVal = 60000,
            ByteVal = 240,
            SByteVal = -100,
            UIntVal = 3_999_999_999u,
            ULongVal = 17_999_999_999_999_999_999ul,
            CharVal = 'Ω',
            HalfVal = (Half)(-42.5),
            Int128Val = Int128.MinValue + 42,
            UInt128Val = UInt128.MaxValue - 42,
            BytesVal = [0, 1, 127, 255],
            BigIntVal = -BigInteger.Pow(2, 200),
            UriVal = new Uri("https://example.org/htb")
        };

        using var ms = new MemoryStream();
        Htb.Write<ExtendedHtbAotRecord>().ToStream(ms, [sample], leaveOpen: true);

        ms.Position = 0;
        var records = Htb.Read<ExtendedHtbAotRecord>().FromStream(ms, leaveOpen: true).ToList();
        if (records.Count != 1 || records[0].BigIntVal != sample.BigIntVal || !records[0].BytesVal.SequenceEqual(sample.BytesVal))
            throw new Exception("HTB generated extended round-trip failed");

        ms.Position = 0;
        using var exportMs = new MemoryStream();
        using (var dr = Htb.CreateDataReader(ms, leaveOpen: true))
        {
            Htb.WriteFromDataReader(dr, exportMs, leaveOpen: true);
        }

        exportMs.Position = 0;
        var reRead = Htb.Read<ExtendedHtbAotRecord>().FromStream(exportMs).ToList();
        if (reRead.Count != 1 || reRead[0].BigIntVal != sample.BigIntVal || reRead[0].UriVal != sample.UriVal)
            throw new Exception("HTB DataReader re-export failed");

        ms.Position = 0;
        using var csvOut = new StringWriter();
        HtbToCsvConverter.Convert(ms, csvOut);
        string csvText = csvOut.ToString();
        if (!csvText.Contains("https://example.org/htb", StringComparison.Ordinal))
            throw new Exception("HtbToCsvConverter missing Uri");
    }
}
