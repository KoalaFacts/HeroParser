using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text;
using HeroParser.AotTests.Models;
using HeroParser.Conversion;
using HeroParser.SeparatedValues.Reading.Records;
using HeroParser.Validation;

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
        runner.Run("Extended Types: JSONL custom parsable fallback + write validation", JsonlCustomParsableAndWriteValidation);
        runner.Run("Extended Types: JSONL typeInfo precedence & non-binder write validation", JsonlTypeInfoPrecedenceAndValidation);
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

        ExtendedJsonlBigIntegerAotRecord[] bigIntRecords =
        [
            new()
            {
                RequiredBigInt = -BigInteger.Pow(2, 200),
                OptionalBigInt = null
            },
            new()
            {
                RequiredBigInt = BigInteger.Parse("1234567890123456789012345678901234567890", CultureInfo.InvariantCulture),
                OptionalBigInt = BigInteger.Parse("9999999999999999999999999999999999999999", CultureInfo.InvariantCulture)
            }
        ];

        string serialized = Jsonl.SerializeRecords(bigIntRecords, options: null, ExtendedJsonlBigIntegerAotContext.Default.ExtendedJsonlBigIntegerAotRecord);
        if (serialized.Contains("IsPowerOfTwo", StringComparison.Ordinal))
            throw new Exception($"BigIntegerJsonConverter was not applied during AOT serialization: {serialized}");

        var viaTypeInfo = Jsonl.DeserializeRecords(serialized, ExtendedJsonlBigIntegerAotContext.Default.ExtendedJsonlBigIntegerAotRecord).ToList();
        var viaBinder = Jsonl.Read<ExtendedJsonlBigIntegerAotRecord>().FromText(serialized).ToList();
        if (viaTypeInfo.Count != 2 || viaBinder.Count != 2)
            throw new Exception($"Expected 2 AOT BigInteger JSONL records, got {viaTypeInfo.Count}/{viaBinder.Count}");
        for (int i = 0; i < 2; i++)
        {
            if (viaTypeInfo[i].RequiredBigInt != bigIntRecords[i].RequiredBigInt ||
                viaTypeInfo[i].OptionalBigInt != bigIntRecords[i].OptionalBigInt ||
                viaBinder[i].RequiredBigInt != bigIntRecords[i].RequiredBigInt ||
                viaBinder[i].OptionalBigInt != bigIntRecords[i].OptionalBigInt)
            {
                throw new Exception($"AOT BigInteger JSONL round-trip mismatch at index {i}: {serialized}");
            }
        }
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

    private static void JsonlCustomParsableAndWriteValidation()
    {
        var typeInfo = ExtendedJsonlBigIntegerAotContext.Default.AotCustomParsableJsonlRecord;
        var valid = new AotCustomParsableJsonlRecord
        {
            Name = "alpha",
            Score = 50,
            Code = new AotCustomSpanCode { Raw = "obj-1" }
        };

        string objectShapeJsonl = Jsonl.Write<AotCustomParsableJsonlRecord>().ToText([valid], typeInfo);
        if (!objectShapeJsonl.Contains("\"Raw\":\"obj-1\"", StringComparison.Ordinal))
            throw new Exception($"Expected object shape JSON for custom parsable, got: {objectShapeJsonl}");

        var objectRead = Jsonl.Read<AotCustomParsableJsonlRecord>().FromText(objectShapeJsonl, typeInfo).ToList();
        if (objectRead.Count != 1 || objectRead[0].Name != "alpha" || objectRead[0].Code.Raw != "obj-1")
            throw new Exception("AOT object-shape fallback round-trip failed");

        string stringShapeJsonl = "{\"Name\":\"beta\",\"Score\":75,\"Code\":\"str-2\"}";
        var stringRead = Jsonl.Read<AotCustomParsableJsonlRecord>().FromText(stringShapeJsonl).ToList();
        if (stringRead.Count != 1 || stringRead[0].Code.Raw != "str-2")
            throw new Exception("AOT string fast-path read failed");

        bool threwFallbackValidation = false;
        try
        {
            _ = Jsonl.Read<AotCustomParsableJsonlRecord>()
                .FromText("{\"Name\":\"\",\"Score\":10,\"Code\":{\"Raw\":\"x\"}}", typeInfo)
                .ToList();
        }
        catch (ValidationException)
        {
            threwFallbackValidation = true;
        }
        if (!threwFallbackValidation)
            throw new Exception("Expected ValidationException on invalid AOT fallback record");

        var invalid = new AotCustomParsableJsonlRecord
        {
            Name = "",
            Score = 200,
            Code = new AotCustomSpanCode { Raw = "bad" }
        };

        using var ms = new MemoryStream();
        bool threwWriteValidation = false;
        try
        {
            Jsonl.Write<AotCustomParsableJsonlRecord>().ToStream(ms, [valid, invalid], typeInfo, leaveOpen: true);
        }
        catch (ValidationException ex)
        {
            threwWriteValidation = ex.Errors.Count == 2 && ex.Errors.All(e => e.RowNumber == 2);
        }
        if (!threwWriteValidation)
            throw new Exception("Expected Strict write ValidationException with RowNumber == 2");

        string partial = Encoding.UTF8.GetString(ms.ToArray());
        if (partial.Contains('\n'))
            throw new Exception("Strict write failure should not leave a dangling trailing newline");

        string lenientText = Jsonl.Write<AotCustomParsableJsonlRecord>()
            .WithValidationMode(ValidationMode.Lenient)
            .ToText([invalid], typeInfo);
        if (!lenientText.Contains("\"Score\":200", StringComparison.Ordinal))
            throw new Exception("Lenient write should emit invalid record");
    }

    private static void JsonlTypeInfoPrecedenceAndValidation()
    {
        var kebabTypeInfo = AotKebabJsonlContext.Default.AotKebabJsonlRecord;
        const string kebabJsonl = "{\"value-id\":7,\"display-name\":\"Ada\"}\n";

        var syncList = Jsonl.Read<AotKebabJsonlRecord>().FromText(kebabJsonl, kebabTypeInfo).ToList();
        if (syncList.Count != 1 || syncList[0].ValueId != 7 || syncList[0].DisplayName != "Ada")
            throw new Exception($"Expected kebab JsonTypeInfo sync read ValueId=7/Ada, got {syncList.Count}");

        using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(kebabJsonl)))
        {
            var asyncList = new List<AotKebabJsonlRecord>();
            var enumerator = Jsonl.DeserializeRecordsAsync(ms, kebabTypeInfo).GetAsyncEnumerator();
            try
            {
                while (enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult())
                    asyncList.Add(enumerator.Current);
            }
            finally
            {
                enumerator.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            if (asyncList.Count != 1 || asyncList[0].ValueId != 7 || asyncList[0].DisplayName != "Ada")
                throw new Exception("Expected kebab JsonTypeInfo async read ValueId=7/Ada");
        }

        bool threwReadValidation = false;
        try
        {
            _ = Jsonl.Read<AotKebabJsonlRecord>()
                .FromText("{\"value-id\":7,\"display-name\":\"  \"}", kebabTypeInfo)
                .ToList();
        }
        catch (ValidationException)
        {
            threwReadValidation = true;
        }
        if (!threwReadValidation)
            throw new Exception("Expected ValidationException for kebab JsonTypeInfo read with blank DisplayName");

        var metaOnlyTypeInfo = AotTypeInfoValidatedContext.Default.AotTypeInfoValidatedRecord;
        var invalidMeta = new AotTypeInfoValidatedRecord { Name = "  ", Score = 500 };

        bool threwMetaWriteValidation = false;
        try
        {
            _ = Jsonl.Write<AotTypeInfoValidatedRecord>().ToText([invalidMeta], metaOnlyTypeInfo);
        }
        catch (ValidationException ex)
        {
            threwMetaWriteValidation = ex.Errors.Count == 2;
        }
        if (!threwMetaWriteValidation)
            throw new Exception("Expected Strict JsonTypeInfo write validation to enforce [Validate] without [GenerateBinder] in NativeAOT");

        string lenient = Jsonl.Write<AotTypeInfoValidatedRecord>()
            .WithValidationMode(ValidationMode.Lenient)
            .ToText([invalidMeta], metaOnlyTypeInfo);
        if (!lenient.Contains("\"Score\":500", StringComparison.Ordinal))
            throw new Exception("Expected Lenient JsonTypeInfo write to emit invalid record");

        string emptyStructJson = Jsonl.Write<AotEmptyMetadataStruct>()
            .ToText([default], AotEmptyMetadataContext.Default.AotEmptyMetadataStruct);
        if (emptyStructJson != "{}")
            throw new Exception($"Expected empty metadata struct to write '{{}}', got '{emptyStructJson}'");
    }
}
