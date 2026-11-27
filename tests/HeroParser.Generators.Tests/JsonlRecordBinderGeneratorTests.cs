using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using HeroParser.Generators;
using HeroParser.JsonLines;
using HeroParser.JsonLines.Reading;
using HeroParser.JsonLines.Writing;
using HeroParser.SeparatedValues.Reading.Records;
using HeroParser.Validation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace HeroParser.Generators.Tests;

[Trait("Category", "Unit")]
public class JsonlRecordBinderGeneratorTests
{
    [GenerateBinder]
    public sealed class JsonlRuntimeValidatedRecord
    {
        [TabularMap(Name = "id")]
        [Validate(NotNull = true, RangeMin = 1, RangeMax = 1000)]
        public int Id { get; set; }

        [TabularMap(Name = "code")]
        [Validate(NotNull = true, NotEmpty = true, MinLength = 2, MaxLength = 8, Pattern = "^[A-Z]+-[0-9]+$")]
        public string? Code { get; set; }

        [TabularMap(Name = "event_date")]
        [Parse(Format = "yyyyMMdd")]
        public DateOnly EventDate { get; set; }

        [TabularMap(Name = "created_at")]
        [Parse(Format = "yyyy-MM-dd HH:mm")]
        public DateTime CreatedAt { get; set; }
    }

    [GenerateBinder]
    public sealed class JsonlNotNullStringRecord
    {
        [TabularMap(Name = "name")]
        [Validate(NotNull = true)]
        public string Name { get; set; } = "";
    }

    [GenerateBinder]
    public sealed class JsonlRequiredTypedFieldsRecord
    {
        [Validate(NotNull = true)]
        public int Id { get; set; }

        [Validate(NotNull = true)]
        public Uri Website { get; set; } = new("https://example.invalid");

        [Validate(NotNull = true)]
        public byte[] Payload { get; set; } = [];
    }

    [GenerateBinder]
    public sealed class DecimalSubMinRangeRecord
    {
        [Validate(RangeMin = 1e-30)]
        public decimal PosAmount { get; set; }

        [Validate(RangeMax = -1e-30)]
        public decimal NegAmount { get; set; }
    }

    [GenerateBinder]
    public sealed class JsonlEscapedPropertyRecord
    {
        [JsonPropertyName("café")]
        [Validate(NotNull = true)]
        public string? Beverage { get; set; }

        public int Count { get; set; }
    }

    public readonly record struct CustomTagCode : ISpanParsable<CustomTagCode>
    {
        public string Raw { get; init; }

        public static CustomTagCode Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
            => TryParse(s, provider, out var result)
                ? result
                : throw new FormatException("Invalid tag code.");

        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out CustomTagCode result)
        {
            if (s.IsEmpty || s.IsWhiteSpace())
            {
                result = default;
                return false;
            }
            result = new CustomTagCode { Raw = s.ToString() };
            return true;
        }

        public static CustomTagCode Parse(string s, IFormatProvider? provider) => Parse(s.AsSpan(), provider);

        public static bool TryParse(string? s, IFormatProvider? provider, out CustomTagCode result)
            => TryParse((s ?? string.Empty).AsSpan(), provider, out result);
    }

    [GenerateBinder]
    public sealed class JsonlCustomParsableRecord
    {
        [Validate(NotNull = true)]
        public string? Title { get; set; }

        public CustomTagCode Tag { get; set; }
    }

    [GenerateBinder]
    public sealed class JsonlWriteValidatedRecord
    {
        [Validate(NotNull = true)]
        public string Name { get; set; } = "";

        [Validate(RangeMin = 1, RangeMax = 100)]
        public int Score { get; set; }
    }

    public sealed class JsonlReflectionValidatedRecord
    {
        [Validate(NotNull = true, MinLength = 2)]
        public string Name { get; set; } = "";

        [Validate(RangeMin = 1, RangeMax = 10)]
        public int Rank { get; set; }
    }

    [Fact]
    public void GeneratedBinder_ParsesFormattedDatesAndPassesValidRecords()
    {
        string jsonl = /*lang=json,strict*/ """
            {"id":42,"code":"AB-12","event_date":"20261116","created_at":"2026-11-16 14:30"}
            """;

        var records = Jsonl.DeserializeRecords<JsonlRuntimeValidatedRecord>(jsonl);

        var record = Assert.Single(records);
        Assert.Equal(42, record.Id);
        Assert.Equal("AB-12", record.Code);
        Assert.Equal(new DateOnly(2026, 11, 16), record.EventDate);
        Assert.Equal(new DateTime(2026, 11, 16, 14, 30, 0), record.CreatedAt);
    }

    [Fact]
    public void GeneratedBinder_EnforcesValidationRules_AndThrowsValidationException()
    {
        string invalidJsonl = /*lang=json,strict*/ """
            {"id":0,"code":"   ","event_date":"20261116","created_at":"2026-11-16 14:30"}
            """;

        var ex = Assert.Throws<ValidationException>(() => Jsonl.DeserializeRecords<JsonlRuntimeValidatedRecord>(invalidJsonl).ToList());
        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(JsonlRuntimeValidatedRecord.Id) && e.Rule == "Range");
        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(JsonlRuntimeValidatedRecord.Code) && e.Rule == "NotEmpty");
        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(JsonlRuntimeValidatedRecord.Code) && e.Rule == "Pattern");
    }

    [Fact]
    public void GeneratedBinder_OmittedRequiredFields_ThrowsValidationException()
    {
        string missingJsonl = /*lang=json,strict*/ """
            {"event_date":"20261116","created_at":"2026-11-16 14:30"}
            """;

        var ex = Assert.Throws<ValidationException>(() => Jsonl.DeserializeRecords<JsonlRuntimeValidatedRecord>(missingJsonl).ToList());
        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(JsonlRuntimeValidatedRecord.Id) && e.Rule == "NotNull");
        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(JsonlRuntimeValidatedRecord.Code) && e.Rule == "NotNull");
    }

    [Fact]
    public void GeneratedBinder_SupportsFloatArrayParseFormatAndValidation_InCompiledAssembly()
    {
        const string source = """
            using System;
            using System.Linq;
            using HeroParser;
            namespace DynamicJsonlTest;

            [GenerateBinder]
            public sealed class EmbeddingRecord
            {
                [Validate(NotNull = true, RangeMin = 1)]
                public int Id { get; set; }

                public float[] Vector { get; set; } = [];

                public float[]? OptionalVector { get; set; }

                [Parse(Format = "yyyy/MM/dd HH:mm zzz")]
                public DateTimeOffset RecordedAt { get; set; }

                [Parse(Format = "HH-mm")]
                public TimeOnly CutOff { get; set; }
            }

            public static class TestInvoker
            {
                public static global::System.Collections.Generic.List<EmbeddingRecord> Deserialize(string jsonl)
                    => global::HeroParser.Jsonl.DeserializeRecords<EmbeddingRecord>(jsonl).ToList();
            }
            """;

        var cancellationToken = TestContext.Current.CancellationToken;
        var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
        var platformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
        var references = platformAssemblies.Split(Path.PathSeparator)
            .Where(path => Path.GetFileName(path) is var name &&
                (name.StartsWith("System.", StringComparison.Ordinal) ||
                 name is "netstandard.dll" or "System.Private.CoreLib.dll"))
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(Csv).Assembly.Location))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "DynamicJsonlAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        CSharpGeneratorDriver.Create(new JsonlRecordBinderGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var genDiagnostics, cancellationToken);

        Assert.DoesNotContain(genDiagnostics, d => d.Id == "HERO101");

        using var peStream = new MemoryStream();
        var emitResult = outputCompilation.Emit(peStream, cancellationToken: cancellationToken);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        peStream.Position = 0;
        var alc = new AssemblyLoadContext("JsonlGenTest", isCollectible: true);
        var assembly = alc.LoadFromStream(peStream);
        var invokerType = assembly.GetType("DynamicJsonlTest.TestInvoker", throwOnError: true)!;
        var deserializeMethod = invokerType.GetMethod("Deserialize", BindingFlags.Static | BindingFlags.Public)!;

        string validJsonl = /*lang=json,strict*/ """{"id":5,"vector":[0.25,0.5,-1.25],"optionalVector":null,"recordedAt":"2026/11/16 09:15 +00:00","cutOff":"14-45"}""";
        var records = (System.Collections.IList)deserializeMethod.Invoke(null, [validJsonl])!;
        Assert.Single(records);
        var result = records[0]!;
        var recordType = result.GetType();

        Assert.Equal(5, (int)recordType.GetProperty("Id")!.GetValue(result)!);
        Assert.Equal([0.25f, 0.5f, -1.25f], (float[])recordType.GetProperty("Vector")!.GetValue(result)!);
        Assert.Null(recordType.GetProperty("OptionalVector")!.GetValue(result));
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 9, 15, 0, TimeSpan.Zero), (DateTimeOffset)recordType.GetProperty("RecordedAt")!.GetValue(result)!);
        Assert.Equal(new TimeOnly(14, 45), (TimeOnly)recordType.GetProperty("CutOff")!.GetValue(result)!);

        string invalidJsonl = /*lang=json,strict*/ """{"vector":[1.0]}""";
        var tie = Assert.Throws<TargetInvocationException>(() => deserializeMethod.Invoke(null, [invalidJsonl]));
        var valEx = Assert.IsType<ValidationException>(tie.InnerException);
        Assert.Contains(valEx.Errors, e => e.PropertyName == "Id" && e.Rule == "NotNull");
    }

    [Fact]
    public async Task GeneratedBinder_LenientMode_PhysicalLineNumber_And_BlankStringNotNull_SyncAndAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        string multiLine = string.Join(
            '\n',
            /*lang=json,strict*/ """{"id":1,"code":"AB-12","event_date":"20261116","created_at":"2026-11-16 14:30"}""",
            /*lang=json,strict*/ """{"id":0,"code":"AB-12","event_date":"20261116","created_at":"2026-11-16 14:30"}""",
            /*lang=json,strict*/ """{"id":2,"code":"CD-34","event_date":"20261116","created_at":"2026-11-16 14:30"}""");

        var syncLenient = Jsonl.Read<JsonlRuntimeValidatedRecord>()
            .WithValidationMode(ValidationMode.Lenient)
            .FromText(multiLine)
            .ToList();
        Assert.Equal([1, 2], [.. syncLenient.Select(r => r.Id)]);

        using (var asyncStream = new MemoryStream(Encoding.UTF8.GetBytes(multiLine)))
        {
            var asyncLenient = new List<JsonlRuntimeValidatedRecord>();
            await foreach (var item in Jsonl.Read<JsonlRuntimeValidatedRecord>()
                .WithValidationMode(ValidationMode.Lenient)
                .FromStreamAsync(asyncStream, leaveOpen: true, cancellationToken: ct))
            {
                asyncLenient.Add(item);
            }
            Assert.Equal([1, 2], [.. asyncLenient.Select(r => r.Id)]);
        }

        var strictSyncEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Read<JsonlRuntimeValidatedRecord>().FromText(multiLine).ToList());
        Assert.All(strictSyncEx.Errors, err => Assert.Equal(2, err.RowNumber));
        Assert.Contains("Row 2", strictSyncEx.Message, StringComparison.Ordinal);

        using (var asyncStrictStream = new MemoryStream(Encoding.UTF8.GetBytes(multiLine)))
        {
            var strictAsyncEx = await Assert.ThrowsAsync<ValidationException>(async () =>
            {
                await foreach (var _ in Jsonl.Read<JsonlRuntimeValidatedRecord>()
                    .FromStreamAsync(asyncStrictStream, leaveOpen: true, cancellationToken: ct))
                {
                }
            });
            Assert.All(strictAsyncEx.Errors, err => Assert.Equal(2, err.RowNumber));
            Assert.Contains("Row 2", strictAsyncEx.Message, StringComparison.Ordinal);
        }

        foreach (string blankJsonl in (string[])[/*lang=json,strict*/ "{\"name\":\"\"}", /*lang=json,strict*/ "{\"name\":\"   \"}"])
        {
            var blankEx = Assert.Throws<ValidationException>(() =>
                Jsonl.Read<JsonlNotNullStringRecord>().FromText(blankJsonl).ToList());
            Assert.Contains(blankEx.Errors, e => e.PropertyName == nameof(JsonlNotNullStringRecord.Name) && e.Rule == "NotNull");
        }
    }

    [Fact]
    public async Task GeneratedBinder_ExplicitNullOnNotNullProperties_ReportsNotNullInStrictAndLenientSyncAndAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        string multiLine = string.Join(
            '\n',
            /*lang=json,strict*/ """{"Id":10,"Website":"https://a.example","Payload":"AQ=="}""",
            /*lang=json,strict*/ """{"Id":null,"Website":"https://b.example","Payload":"Ag=="}""",
            /*lang=json,strict*/ """{"Id":20,"Id":null,"Website":"https://c.example","Payload":"Aw=="}""",
            /*lang=json,strict*/ """{"Id":30,"Website":null,"Payload":null}""",
            /*lang=json,strict*/ """{"Id":40,"Website":"https://d.example","Payload":"BA=="}""");

        // Strict sync must throw ValidationException (not JsonlException) on explicit null for non-nullable NotNull value types.
        var strictNullIdEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Read<JsonlRequiredTypedFieldsRecord>()
                .FromText(/*lang=json,strict*/ """{"Id":null,"Website":"https://ok.example","Payload":"AQ=="}""")
                .ToList());
        var err = Assert.Single(strictNullIdEx.Errors);
        Assert.Equal(nameof(JsonlRequiredTypedFieldsRecord.Id), err.PropertyName);
        Assert.Equal("NotNull", err.Rule);

        // Strict sync on duplicate property where second occurrence resets value to null.
        var strictOverwrittenNullEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Read<JsonlRequiredTypedFieldsRecord>()
                .FromText(/*lang=json,strict*/ """{"Id":1,"Id":null,"Website":"https://ok.example","Payload":"AQ=="}""")
                .ToList());
        Assert.Contains(strictOverwrittenNullEx.Errors, e => e.PropertyName == nameof(JsonlRequiredTypedFieldsRecord.Id) && e.Rule == "NotNull");

        // Lenient sync without OnError skips explicit null rows and continues yielding valid rows.
        var syncDefaultLenient = Jsonl.Read<JsonlRequiredTypedFieldsRecord>()
            .WithValidationMode(ValidationMode.Lenient)
            .FromText(multiLine)
            .ToList();
        Assert.Equal([10, 40], [.. syncDefaultLenient.Select(r => r.Id)]);

        // Lenient sync with OnError reports ValidationException with physical RowNumber and skips invalid rows.
        var syncErrors = new List<ValidationError>();
        var syncReader = Jsonl.Read<JsonlRequiredTypedFieldsRecord>()
            .WithValidationMode(ValidationMode.Lenient)
            .OnError((ctx, ex) =>
            {
                if (ex is ValidationException valEx)
                {
                    syncErrors.AddRange(valEx.Errors);
                }
                return HeroParser.JsonLines.Reading.JsonlDeserializeErrorAction.SkipRecord;
            });
        var syncValid = syncReader.FromText(multiLine).ToList();
        Assert.Equal([10, 40], [.. syncValid.Select(r => r.Id)]);
        Assert.Equal(4, syncErrors.Count);
        Assert.Contains(syncErrors, e => e.RowNumber == 2 && e.PropertyName == nameof(JsonlRequiredTypedFieldsRecord.Id) && e.Rule == "NotNull");
        Assert.Contains(syncErrors, e => e.RowNumber == 3 && e.PropertyName == nameof(JsonlRequiredTypedFieldsRecord.Id) && e.Rule == "NotNull");
        Assert.Contains(syncErrors, e => e.RowNumber == 4 && e.PropertyName == nameof(JsonlRequiredTypedFieldsRecord.Website) && e.Rule == "NotNull");
        Assert.Contains(syncErrors, e => e.RowNumber == 4 && e.PropertyName == nameof(JsonlRequiredTypedFieldsRecord.Payload) && e.Rule == "NotNull");

        // Lenient async parity.
        var asyncErrors = new List<ValidationError>();
        var asyncValid = new List<JsonlRequiredTypedFieldsRecord>();
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(multiLine));
        await foreach (var item in Jsonl.Read<JsonlRequiredTypedFieldsRecord>()
            .WithValidationMode(ValidationMode.Lenient)
            .OnError((ctx, ex) =>
            {
                if (ex is ValidationException valEx)
                {
                    asyncErrors.AddRange(valEx.Errors);
                }
                return HeroParser.JsonLines.Reading.JsonlDeserializeErrorAction.SkipRecord;
            })
            .FromStreamAsync(ms, leaveOpen: true, cancellationToken: ct))
        {
            asyncValid.Add(item);
        }
        Assert.Equal([10, 40], [.. asyncValid.Select(r => r.Id)]);
        Assert.Equal(4, asyncErrors.Count);
        Assert.Contains(asyncErrors, e => e.RowNumber == 2 && e.PropertyName == nameof(JsonlRequiredTypedFieldsRecord.Id) && e.Rule == "NotNull");
        Assert.Contains(asyncErrors, e => e.RowNumber == 4 && e.PropertyName == nameof(JsonlRequiredTypedFieldsRecord.Payload) && e.Rule == "NotNull");
    }

    [Fact]
    public void GeneratedBinders_DecimalBoundsRoundingToZero_PreserveSignInJsonlCsvAndWrite()
    {
        var validRecord = new DecimalSubMinRangeRecord
        {
            PosAmount = 0.0000000000000000000000000001m,
            NegAmount = -0.0000000000000000000000000001m
        };
        string csvOk = Csv.WriteToText([validRecord]);
        var csvBack = Csv.DeserializeRecords<DecimalSubMinRangeRecord>(csvOk).ToList();
        Assert.Single(csvBack);
        Assert.Equal(validRecord.PosAmount, csvBack[0].PosAmount);
        Assert.Equal(validRecord.NegAmount, csvBack[0].NegAmount);

        string jsonlOk = /*lang=json,strict*/ """{"PosAmount":0.0000000000000000000000000001,"NegAmount":-0.0000000000000000000000000001}""";
        var jsonlBack = Jsonl.DeserializeRecords<DecimalSubMinRangeRecord>(jsonlOk).ToList();
        Assert.Single(jsonlBack);
        Assert.Equal(validRecord.PosAmount, jsonlBack[0].PosAmount);
        Assert.Equal(validRecord.NegAmount, jsonlBack[0].NegAmount);

        var zeroRecord = new DecimalSubMinRangeRecord { PosAmount = 0m, NegAmount = 0m };
        Assert.Throws<ValidationException>(() => Csv.WriteToText([zeroRecord]));

        var csvZeroEx = Assert.Throws<ValidationException>(() =>
            Csv.DeserializeRecords<DecimalSubMinRangeRecord>("PosAmount,NegAmount\n0,0\n").ToList());
        Assert.Contains(csvZeroEx.Errors, e => e.PropertyName == nameof(DecimalSubMinRangeRecord.PosAmount) && e.Rule == "Range");
        Assert.Contains(csvZeroEx.Errors, e => e.PropertyName == nameof(DecimalSubMinRangeRecord.NegAmount) && e.Rule == "Range");

        var jsonlZeroEx = Assert.Throws<ValidationException>(() =>
            Jsonl.DeserializeRecords<DecimalSubMinRangeRecord>(/*lang=json,strict*/ """{"PosAmount":0,"NegAmount":0}""").ToList());
        Assert.Contains(jsonlZeroEx.Errors, e => e.PropertyName == nameof(DecimalSubMinRangeRecord.PosAmount) && e.Rule == "Range");
        Assert.Contains(jsonlZeroEx.Errors, e => e.PropertyName == nameof(DecimalSubMinRangeRecord.NegAmount) && e.Rule == "Range");
    }

    [Fact]
    public async Task GeneratedBinder_EscapedPropertyNames_MatchedViaValueTextEquals_SyncAndAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        string escapedJsonl = /*lang=json,strict*/ """{"caf\u00e9":"espresso","Count":2}""";

        var sync = Jsonl.Read<JsonlEscapedPropertyRecord>().FromText(escapedJsonl).ToList();
        var item = Assert.Single(sync);
        Assert.Equal("espresso", item.Beverage);
        Assert.Equal(2, item.Count);

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(escapedJsonl));
        var asyncItems = new List<JsonlEscapedPropertyRecord>();
        await foreach (var record in Jsonl.Read<JsonlEscapedPropertyRecord>().FromStreamAsync(ms, leaveOpen: true, cancellationToken: ct))
        {
            asyncItems.Add(record);
        }
        var asyncItem = Assert.Single(asyncItems);
        Assert.Equal("espresso", asyncItem.Beverage);
        Assert.Equal(2, asyncItem.Count);

        foreach (string invalid in (string[])[
            /*lang=json,strict*/ """{"Count":1}""",
            /*lang=json,strict*/ """{"caf\u00e9":null,"Count":1}"""
        ])
        {
            var ex = Assert.Throws<ValidationException>(() =>
                Jsonl.Read<JsonlEscapedPropertyRecord>().FromText(invalid).ToList());
            Assert.Contains(ex.Errors, e => e.PropertyName == nameof(JsonlEscapedPropertyRecord.Beverage) && e.Rule == "NotNull");
        }
    }

    [Fact]
    public async Task GeneratedBinder_CustomSpanParsable_ObjectShapeFallsBackToStjAndValidates()
    {
        var ct = TestContext.Current.CancellationToken;
        var original = new JsonlCustomParsableRecord
        {
            Title = "item1",
            Tag = new CustomTagCode { Raw = "alpha" }
        };

        string objectShapeJsonl = Jsonl.WriteToText([original]);
        Assert.Contains("\"Raw\":\"alpha\"", objectShapeJsonl, StringComparison.Ordinal);

        // 1. Sync reader fallback
        var syncRecord = Assert.Single(Jsonl.Read<JsonlCustomParsableRecord>().FromText(objectShapeJsonl).ToList());
        Assert.Equal("item1", syncRecord.Title);
        Assert.Equal("alpha", syncRecord.Tag.Raw);

        // 2. Async builder fallback
        using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(objectShapeJsonl)))
        {
            var asyncList = new List<JsonlCustomParsableRecord>();
            await foreach (var item in Jsonl.Read<JsonlCustomParsableRecord>().FromStreamAsync(ms, leaveOpen: true, cancellationToken: ct))
                asyncList.Add(item);
            var asyncItem = Assert.Single(asyncList);
            Assert.Equal("alpha", asyncItem.Tag.Raw);
        }

        // 3. Jsonl.DeserializeRecordsAsync fallback
        using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(objectShapeJsonl)))
        {
            var directList = new List<JsonlCustomParsableRecord>();
            await foreach (var item in Jsonl.DeserializeRecordsAsync<JsonlCustomParsableRecord>(ms, cancellationToken: ct))
                directList.Add(item);
            Assert.Equal("alpha", Assert.Single(directList).Tag.Raw);
        }

        // 4. String fast-path still works
        string stringShapeJsonl = /*lang=json,strict*/ """{"Title":"item2","Tag":"beta"}""";
        var fastRecord = Assert.Single(Jsonl.Read<JsonlCustomParsableRecord>().FromText(stringShapeJsonl).ToList());
        Assert.Equal("beta", fastRecord.Tag.Raw);

        // 5. Validation still runs when STJ fallback triggers
        string invalidFallback = string.Join(
            '\n',
            /*lang=json,strict*/ """{"Title":"ok","Tag":{"Raw":"good"}}""",
            /*lang=json,strict*/ """{"Title":"","Tag":{"Raw":"bad"}}""");
        var fallbackEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Read<JsonlCustomParsableRecord>().FromText(invalidFallback).ToList());
        var fallbackErr = Assert.Single(fallbackEx.Errors);
        Assert.Equal(2, fallbackErr.RowNumber);
        Assert.Equal("NotNull", fallbackErr.Rule);

        using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(invalidFallback)))
        {
            var asyncFallbackEx = await Assert.ThrowsAsync<ValidationException>(async () =>
            {
                await foreach (var _ in Jsonl.DeserializeRecordsAsync<JsonlCustomParsableRecord>(ms, cancellationToken: ct)) { }
            });
            Assert.Equal(2, Assert.Single(asyncFallbackEx.Errors).RowNumber);
        }
    }

    [Fact]
    public async Task WriteValidation_StrictAndLenient_EnforcedForGeneratedAndReflectionRecordsSyncAndAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var validGen = new JsonlWriteValidatedRecord { Name = "Alice", Score = 50 };
        var invalidGen = new JsonlWriteValidatedRecord { Name = "", Score = 0 };

        // Generated record strict sync: second record fails without leaving a dangling newline
        using (var ms = new MemoryStream())
        {
            var ex = Assert.Throws<ValidationException>(() =>
                Jsonl.Write<JsonlWriteValidatedRecord>().ToStream(ms, [validGen, invalidGen], leaveOpen: true));
            Assert.All(ex.Errors, e => Assert.Equal(2, e.RowNumber));
            Assert.Contains(ex.Errors, e => e.PropertyName == nameof(JsonlWriteValidatedRecord.Name) && e.Rule == "NotNull");
            Assert.Contains(ex.Errors, e => e.PropertyName == nameof(JsonlWriteValidatedRecord.Score) && e.Rule == "Range");

            string partial = Encoding.UTF8.GetString(ms.ToArray());
            Assert.DoesNotContain("\n", partial, StringComparison.Ordinal);
            Assert.Contains("\"Name\":\"Alice\"", partial, StringComparison.Ordinal);
        }

        // Generated record strict async
        using (var ms = new MemoryStream())
        {
            var ex = await Assert.ThrowsAsync<ValidationException>(async () =>
                await Jsonl.Write<JsonlWriteValidatedRecord>().ToStreamFromEnumerableAsync(ms, [validGen, invalidGen], leaveOpen: true, cancellationToken: ct));
            Assert.All(ex.Errors, e => Assert.Equal(2, e.RowNumber));
        }

        // Generated record lenient mode allows invalid record
        string lenientText = Jsonl.Write<JsonlWriteValidatedRecord>()
            .WithValidationMode(ValidationMode.Lenient)
            .ToText([invalidGen]);
        Assert.Contains("\"Score\":0", lenientText, StringComparison.Ordinal);

        // Reflection record (no [GenerateBinder])
        var validRefl = new JsonlReflectionValidatedRecord { Name = "Bob", Rank = 5 };
        var invalidRefl = new JsonlReflectionValidatedRecord { Name = "X", Rank = 99 };
        var reflEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlReflectionValidatedRecord>().ToText([validRefl, invalidRefl]));
        Assert.All(reflEx.Errors, e => Assert.Equal(2, e.RowNumber));
        Assert.Contains(reflEx.Errors, e => e.PropertyName == nameof(JsonlReflectionValidatedRecord.Name) && e.Rule == "MinLength");
        Assert.Contains(reflEx.Errors, e => e.PropertyName == nameof(JsonlReflectionValidatedRecord.Rank) && e.Rule == "Range");

        string lenientRefl = Jsonl.Write<JsonlReflectionValidatedRecord>()
            .WithValidationMode(ValidationMode.Lenient)
            .ToText([invalidRefl]);
        Assert.Contains("\"Rank\":99", lenientRefl, StringComparison.Ordinal);

        // Dynamic generator-only test for get-only / init-only / primary-constructor [GenerateBinder] records
        string source = """
            using System;
            using HeroParser;
            using HeroParser.JsonLines;
            using HeroParser.Validation;

            namespace DynamicJsonlWriteTest;

            [GenerateBinder]
            public sealed class GetOnlyValidatedRecord(string raw)
            {
                public string Raw => raw;

                [Validate(NotNull = true, MinLength = 2, Pattern = "^[a-z]+$")]
                public string Code => raw;
            }

            [GenerateBinder]
            public sealed class ComputedValidatedRecord
            {
                public string Name { get; set; } = "";
                public int Score { get; set; }

                [Validate(NotNull = true, Pattern = "^[A-Z]+:[0-9]+$")]
                public string ComputedTag => $"{Name}:{Score}";
            }

            public static class TestInvoker
            {
                public static string WriteGetOnly(string raw)
                    => global::HeroParser.Jsonl.WriteToText([new GetOnlyValidatedRecord(raw)]);

                public static string WriteComputed(string name, int score)
                    => global::HeroParser.Jsonl.WriteToText([new ComputedValidatedRecord { Name = name, Score = score }]);
            }
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: ct);
        var platformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
        var references = platformAssemblies.Split(Path.PathSeparator)
            .Where(path => Path.GetFileName(path) is var name &&
                (name.StartsWith("System.", StringComparison.Ordinal) ||
                 name is "netstandard.dll" or "System.Private.CoreLib.dll"))
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(Csv).Assembly.Location))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "DynamicJsonlWriteAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        CSharpGeneratorDriver.Create(new JsonlRecordBinderGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _, ct);

        using var peStream = new MemoryStream();
        var emitResult = outputCompilation.Emit(peStream, cancellationToken: ct);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        peStream.Position = 0;
        var alc = new AssemblyLoadContext("JsonlWriteGenTest", isCollectible: true);
        try
        {
            var assembly = alc.LoadFromStream(peStream);
            var invoker = assembly.GetType("DynamicJsonlWriteTest.TestInvoker", throwOnError: true)!;
            var writeGetOnly = invoker.GetMethod("WriteGetOnly", BindingFlags.Static | BindingFlags.Public)!;
            var writeComputed = invoker.GetMethod("WriteComputed", BindingFlags.Static | BindingFlags.Public)!;

            var tie1 = Assert.Throws<TargetInvocationException>(() => writeGetOnly.Invoke(null, ["1"]));
            var valEx1 = Assert.IsType<ValidationException>(tie1.InnerException);
            Assert.Contains(valEx1.Errors, e => e.PropertyName == "Code" && e.Rule == "MinLength");
            Assert.Contains(valEx1.Errors, e => e.PropertyName == "Code" && e.Rule == "Pattern");

            string validGetOnly = (string)writeGetOnly.Invoke(null, ["abc"])!;
            Assert.Contains("\"Code\":\"abc\"", validGetOnly, StringComparison.Ordinal);

            var tie2 = Assert.Throws<TargetInvocationException>(() => writeComputed.Invoke(null, ["lower", 5]));
            var valEx2 = Assert.IsType<ValidationException>(tie2.InnerException);
            Assert.Contains(valEx2.Errors, e => e.PropertyName == "ComputedTag" && e.Rule == "Pattern");

            string validComputed = (string)writeComputed.Invoke(null, ["ALICE", 5])!;
            Assert.Contains("\"ComputedTag\":\"ALICE:5\"", validComputed, StringComparison.Ordinal);
        }
        finally
        {
            alc.Unload();
        }
    }

    [Fact]
    public async Task GeneratedBinder_HonorsSuppliedTypeInfoAndSerializerOptions_AcrossSyncAndAsyncAndValidates()
    {
        var ct = TestContext.Current.CancellationToken;
        var typeInfo = JsonlKebabCaseTestContext.Default.JsonlKebabCaseTestRecord;
        var serializerOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower };
        string jsonl = /*lang=json,strict*/ """{"value-id":7,"display-name":"Alice"}""";

        // 1. Explicit JsonTypeInfo<T> must take precedence over [GenerateBinder]
        var tiSyncStatic = Assert.Single(Jsonl.DeserializeRecords(jsonl, typeInfo).ToList());
        var tiSyncBuilder = Assert.Single(Jsonl.Read<JsonlKebabCaseTestRecord>().FromText(jsonl, typeInfo).ToList());

        List<JsonlKebabCaseTestRecord> tiAsyncStatic = [];
        await using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(jsonl)))
        {
            await foreach (var item in Jsonl.DeserializeRecordsAsync(ms, typeInfo, cancellationToken: ct))
                tiAsyncStatic.Add(item);
        }

        List<JsonlKebabCaseTestRecord> tiAsyncBuilder = [];
        await using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(jsonl)))
        {
            await foreach (var item in Jsonl.Read<JsonlKebabCaseTestRecord>().FromStreamAsync(ms, typeInfo, cancellationToken: ct))
                tiAsyncBuilder.Add(item);
        }

        foreach (var item in (JsonlKebabCaseTestRecord[])[tiSyncStatic, tiSyncBuilder, Assert.Single(tiAsyncStatic), Assert.Single(tiAsyncBuilder)])
        {
            Assert.Equal(7, item.ValueId);
            Assert.Equal("Alice", item.DisplayName);
        }

        // 2. Explicit SerializerOptions must also take precedence over [GenerateBinder]
        var optSyncStatic = Assert.Single(Jsonl.DeserializeRecords<JsonlKebabCaseTestRecord>(jsonl, new JsonlReadOptions { SerializerOptions = serializerOptions }).ToList());
        var optSyncBuilder = Assert.Single(Jsonl.Read<JsonlKebabCaseTestRecord>().WithJsonOptions(serializerOptions).FromText(jsonl).ToList());

        List<JsonlKebabCaseTestRecord> optAsyncStatic = [];
        await using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(jsonl)))
        {
            await foreach (var item in Jsonl.DeserializeRecordsAsync<JsonlKebabCaseTestRecord>(ms, new JsonlReadOptions { SerializerOptions = serializerOptions }, cancellationToken: ct))
                optAsyncStatic.Add(item);
        }

        List<JsonlKebabCaseTestRecord> optAsyncBuilder = [];
        await using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(jsonl)))
        {
            await foreach (var item in Jsonl.Read<JsonlKebabCaseTestRecord>().WithJsonOptions(serializerOptions).FromStreamAsync(ms, cancellationToken: ct))
                optAsyncBuilder.Add(item);
        }

        foreach (var item in (JsonlKebabCaseTestRecord[])[optSyncStatic, optSyncBuilder, Assert.Single(optAsyncStatic), Assert.Single(optAsyncBuilder)])
        {
            Assert.Equal(7, item.ValueId);
            Assert.Equal("Alice", item.DisplayName);
        }

        // 3. [Validate] rules still run after STJ deserialization
        string invalidJsonl = /*lang=json,strict*/ """{"value-id":7,"display-name":"   "}""";
        var tiEx = Assert.Throws<ValidationException>(() => Jsonl.Read<JsonlKebabCaseTestRecord>().FromText(invalidJsonl, typeInfo).ToList());
        Assert.Contains(tiEx.Errors, e => e.PropertyName == nameof(JsonlKebabCaseTestRecord.DisplayName) && e.Rule == "NotEmpty");

        await using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(invalidJsonl)))
        {
            var asyncEx = await Assert.ThrowsAsync<ValidationException>(async () =>
            {
                await foreach (var _ in Jsonl.DeserializeRecordsAsync(ms, typeInfo, cancellationToken: ct)) { }
            });
            Assert.Contains(asyncEx.Errors, e => e.PropertyName == nameof(JsonlKebabCaseTestRecord.DisplayName) && e.Rule == "NotEmpty");
        }

        var optEx = Assert.Throws<ValidationException>(() => Jsonl.Read<JsonlKebabCaseTestRecord>().WithJsonOptions(serializerOptions).FromText(invalidJsonl).ToList());
        Assert.Contains(optEx.Errors, e => e.PropertyName == nameof(JsonlKebabCaseTestRecord.DisplayName) && e.Rule == "NotEmpty");
    }

    [Fact]
    public async Task GeneratedBinder_TrailingContentAfterObject_IsRejectedWhileTrailingWhitespaceSucceeds()
    {
        var ct = TestContext.Current.CancellationToken;

        // Trailing whitespace must succeed on both Bind and TryBind
        var okBind = Assert.Single(Jsonl.Read<JsonlNotNullStringRecord>().FromText(/*lang=json,strict*/ """{"Name":"Alice"}""" + "   \t").ToList());
        Assert.Equal("Alice", okBind.Name);

        var okTryBind = Assert.Single(Jsonl.Read<JsonlCustomParsableRecord>().FromText(/*lang=json,strict*/ """{"Title":"ok","Tag":"t"}""" + "   \t").ToList());
        Assert.Equal("t", okTryBind.Tag.Raw);

        await using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(/*lang=json,strict*/ """{"Name":"Alice"}""" + "   \t\n")))
        {
            List<JsonlNotNullStringRecord> asyncOk = [];
            await foreach (var item in Jsonl.DeserializeRecordsAsync<JsonlNotNullStringRecord>(ms, cancellationToken: ct))
                asyncOk.Add(item);
            Assert.Equal("Alice", Assert.Single(asyncOk).Name);
        }

        foreach (string malformed in (string[])[
            "{\"Name\":\"Alice\"} {\"Name\":\"Bob\"}",
            "{\"Name\":\"Alice\"} 123",
            "{\"Name\":\"Alice\"} garbage"
        ])
        {
            var syncEx = Assert.Throws<JsonlException>(() =>
                Jsonl.Read<JsonlNotNullStringRecord>().FromText(malformed).ToList());
            Assert.Equal(JsonlErrorCode.DeserializeError, syncEx.ErrorCode);

            await using var ms = new MemoryStream(Encoding.UTF8.GetBytes(malformed));
            var asyncEx = await Assert.ThrowsAsync<JsonlException>(async () =>
            {
                await foreach (var _ in Jsonl.DeserializeRecordsAsync<JsonlNotNullStringRecord>(ms, cancellationToken: ct)) { }
            });
            Assert.Equal(JsonlErrorCode.DeserializeError, asyncEx.ErrorCode);
        }

        var tryBindEx = Assert.Throws<JsonlException>(() =>
            Jsonl.Read<JsonlCustomParsableRecord>().FromText("{\"Title\":\"ok\",\"Tag\":\"t\"} {\"Title\":\"extra\"}").ToList());
        Assert.Equal(JsonlErrorCode.DeserializeError, tryBindEx.ErrorCode);
    }

    [Fact]
    public async Task WriteValidation_TypeInfoWithoutGenerateBinder_EnforcesStrictAndAllowsLenient()
    {
        var ct = TestContext.Current.CancellationToken;
        var typeInfo = JsonlTypeInfoOnlyContext.Default.JsonlTypeInfoOnlyValidatedRecord;
        var valid = new JsonlTypeInfoOnlyValidatedRecord { Name = "Alice", Score = 50 };
        var invalid = new JsonlTypeInfoOnlyValidatedRecord { Name = "   ", Score = 200 };

        using (var ms = new MemoryStream())
        {
            var ex = Assert.Throws<ValidationException>(() =>
                Jsonl.Write<JsonlTypeInfoOnlyValidatedRecord>().ToStream(ms, [valid, invalid], typeInfo, leaveOpen: true));
            Assert.All(ex.Errors, e => Assert.Equal(2, e.RowNumber));
            Assert.Contains(ex.Errors, e => e.PropertyName == nameof(JsonlTypeInfoOnlyValidatedRecord.Name) && e.Rule == "NotEmpty");
            Assert.Contains(ex.Errors, e => e.PropertyName == nameof(JsonlTypeInfoOnlyValidatedRecord.Score) && e.Rule == "Range");

            string partial = Encoding.UTF8.GetString(ms.ToArray());
            Assert.DoesNotContain("\n", partial, StringComparison.Ordinal);
            Assert.Contains("\"Name\":\"Alice\"", partial, StringComparison.Ordinal);
        }

        using (var ms = new MemoryStream())
        {
            var ex = await Assert.ThrowsAsync<ValidationException>(async () =>
                await Jsonl.Write<JsonlTypeInfoOnlyValidatedRecord>().ToStreamFromEnumerableAsync(ms, [valid, invalid], typeInfo, leaveOpen: true, cancellationToken: ct));
            Assert.All(ex.Errors, e => Assert.Equal(2, e.RowNumber));
        }

        string lenient = Jsonl.Write<JsonlTypeInfoOnlyValidatedRecord>()
            .WithValidationMode(ValidationMode.Lenient)
            .ToText([invalid], typeInfo);
        Assert.Contains("\"Score\":200", lenient, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeneratedBinder_PreservesInheritedPropertiesAndHonorsJsonIgnoreAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        string jsonl = /*lang=json,strict*/ """{"BaseId":42,"Ignored":999,"Optional":"note","Name":"Alice"}""";

        var sync = Assert.Single(Jsonl.Read<JsonlContractInheritedAndIgnoredRecord>().FromText(jsonl).ToList());
        Assert.Equal(42, sync.BaseId);
        Assert.Equal(0, sync.Ignored);
        Assert.Equal("note", sync.Optional);
        Assert.Equal("Alice", sync.Name);

        await using var ms = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));
        List<JsonlContractInheritedAndIgnoredRecord> asyncItems = [];
        await foreach (var item in Jsonl.DeserializeRecordsAsync<JsonlContractInheritedAndIgnoredRecord>(ms, cancellationToken: ct))
            asyncItems.Add(item);

        var asyncItem = Assert.Single(asyncItems);
        Assert.Equal(42, asyncItem.BaseId);
        Assert.Equal(0, asyncItem.Ignored);
        Assert.Equal("note", asyncItem.Optional);
        Assert.Equal("Alice", asyncItem.Name);

        var ex = Assert.Throws<ValidationException>(() =>
            Jsonl.Read<JsonlContractInheritedAndIgnoredRecord>().FromText(/*lang=json,strict*/ """{"BaseId":1,"Name":""}""").ToList());
        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(JsonlContractInheritedAndIgnoredRecord.Name) && e.Rule == "NotEmpty");
    }

    [Fact]
    public async Task GeneratedBinder_StjContractFeatures_FallBackToSerializerAndStillValidateAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        string source = """
            using System;
            using System.Collections.Generic;
            using System.IO;
            using System.Text;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Threading;
            using System.Threading.Tasks;
            using HeroParser;
            using HeroParser.JsonLines;
            using HeroParser.Validation;

            namespace DynamicJsonlContractTest;

            [GenerateBinder]
            public sealed class StjContractRecord
            {
                [Validate(NotEmpty = true)]
                public string Name { get; set; } = "";

                [JsonRequired]
                public int RequiredCode { get; init; }

                [JsonExtensionData]
                public Dictionary<string, JsonElement>? Extra { get; set; }
            }

            public static class ContractInvoker
            {
                public static StjContractRecord[] ReadSync(string jsonl)
                    => [.. global::HeroParser.Jsonl.Read<StjContractRecord>().FromText(jsonl)];

                public static async Task<StjContractRecord[]> ReadAsync(string jsonl, CancellationToken ct)
                {
                    await using var ms = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));
                    var list = new List<StjContractRecord>();
                    await foreach (var item in global::HeroParser.Jsonl.DeserializeRecordsAsync<StjContractRecord>(ms, cancellationToken: ct))
                        list.Add(item);
                    return [.. list];
                }
            }
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: ct);
        var platformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
        var references = platformAssemblies.Split(Path.PathSeparator)
            .Where(path => Path.GetFileName(path) is var name &&
                (name.StartsWith("System.", StringComparison.Ordinal) ||
                 name is "netstandard.dll" or "System.Private.CoreLib.dll"))
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(Csv).Assembly.Location))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "DynamicJsonlContractAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        CSharpGeneratorDriver.Create(new JsonlRecordBinderGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _, ct);

        using var peStream = new MemoryStream();
        var emitResult = outputCompilation.Emit(peStream, cancellationToken: ct);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        peStream.Position = 0;
        var alc = new AssemblyLoadContext("JsonlContractTest", isCollectible: true);
        try
        {
            var assembly = alc.LoadFromStream(peStream);
            var invoker = assembly.GetType("DynamicJsonlContractTest.ContractInvoker", throwOnError: true)!;
            var readSync = invoker.GetMethod("ReadSync", BindingFlags.Static | BindingFlags.Public)!;
            var readAsync = invoker.GetMethod("ReadAsync", BindingFlags.Static | BindingFlags.Public)!;

            string validJsonl = /*lang=json,strict*/ """{"Name":"Ada","RequiredCode":99,"CustomTag":"x"}""";
            var syncItems = (object[])readSync.Invoke(null, [validJsonl])!;
            object record = Assert.Single(syncItems);
            Assert.Equal("Ada", (string?)record.GetType().GetProperty("Name")!.GetValue(record));
            Assert.Equal(99, (int)record.GetType().GetProperty("RequiredCode")!.GetValue(record)!);
            var extra = (Dictionary<string, JsonElement>?)record.GetType().GetProperty("Extra")!.GetValue(record);
            Assert.NotNull(extra);
            Assert.Equal("x", extra!["CustomTag"].GetString());

            var asyncTask = (Task)readAsync.Invoke(null, [validJsonl, ct])!;
            await asyncTask;
            var asyncItems = (object[])asyncTask.GetType().GetProperty("Result")!.GetValue(asyncTask)!;
            object asyncRecord = Assert.Single(asyncItems);
            Assert.Equal(99, (int)asyncRecord.GetType().GetProperty("RequiredCode")!.GetValue(asyncRecord)!);

            // Missing [JsonRequired] fails STJ deserialization (wrapped in JsonlException)
            var missingRequired = Assert.Throws<TargetInvocationException>(() =>
                readSync.Invoke(null, [/*lang=json,strict*/ """{"Name":"Ada"}"""]));
            var jsonlEx = Assert.IsType<JsonlException>(missingRequired.InnerException);
            Assert.Equal(JsonlErrorCode.DeserializeError, jsonlEx.ErrorCode);

            // Blank Name still fails generated validator
            var validationTie = Assert.Throws<TargetInvocationException>(() =>
                readSync.Invoke(null, [/*lang=json,strict*/ """{"Name":"  ","RequiredCode":99}"""]));
            Assert.IsType<ValidationException>(validationTie.InnerException);
        }
        finally
        {
            alc.Unload();
        }
    }

    [Fact]
    public async Task WriteValidation_FastPathSerializationOnlyTypeInfo_RejectsStrictUnlessGenerateBinderOrLenientAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var noBinderInfo = JsonlFastPathSerializationContext.Default.JsonlTypeInfoOnlyValidatedRecord;
        var invalidNoBinder = new JsonlTypeInfoOnlyValidatedRecord { Name = "", Score = 50 };

        var syncEx = Assert.Throws<JsonlException>(() =>
            Jsonl.Write<JsonlTypeInfoOnlyValidatedRecord>().ToText([invalidNoBinder], noBinderInfo));
        Assert.Equal(JsonlErrorCode.InvalidOptions, syncEx.ErrorCode);

        await using (var ms = new MemoryStream())
        {
            var asyncEx = await Assert.ThrowsAsync<JsonlException>(async () =>
                await Jsonl.Write<JsonlTypeInfoOnlyValidatedRecord>().ToStreamFromEnumerableAsync(ms, [invalidNoBinder], noBinderInfo, leaveOpen: true, cancellationToken: ct));
            Assert.Equal(JsonlErrorCode.InvalidOptions, asyncEx.ErrorCode);
            Assert.Equal(0, ms.Length);
        }

        string lenient = Jsonl.Write<JsonlTypeInfoOnlyValidatedRecord>()
            .WithValidationMode(ValidationMode.Lenient)
            .ToText([invalidNoBinder], noBinderInfo);
        Assert.Contains("\"Name\":\"\"", lenient, StringComparison.Ordinal);

        // With [GenerateBinder], strict mode validates and works even with Serialization-only JsonTypeInfo
        var binderInfo = JsonlFastPathSerializationContext.Default.JsonlFastPathBinderValidatedRecord;
        var invalidBinder = new JsonlFastPathBinderValidatedRecord { Name = "", Score = 0 };
        var valEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlFastPathBinderValidatedRecord>().ToText([invalidBinder], binderInfo));
        Assert.Contains(valEx.Errors, e => e.PropertyName == nameof(JsonlFastPathBinderValidatedRecord.Name));

        string validBinderJson = Jsonl.Write<JsonlFastPathBinderValidatedRecord>()
            .ToText([new JsonlFastPathBinderValidatedRecord { Name = "Alice", Score = 10 }], binderInfo);
        Assert.Contains("\"Name\":\"Alice\"", validBinderJson, StringComparison.Ordinal);

        // With [GenerateBinder] and 0 [Validate] rules, strict mode registers a no-op validator and succeeds
        string unvalidatedBinderJson = Jsonl.Write<JsonlFastPathBinderUnvalidatedRecord>()
            .ToText(
                [new JsonlFastPathBinderUnvalidatedRecord { Value = 42 }],
                JsonlFastPathSerializationContext.Default.JsonlFastPathBinderUnvalidatedRecord);
        Assert.Contains("\"Value\":42", unvalidatedBinderJson, StringComparison.Ordinal);

        // Built-in JsonElement scalar works in Strict mode
        using var doc = JsonDocument.Parse(/*lang=json,strict*/ """{"k":1}""");
        string elemText = Jsonl.Write<JsonElement>().ToText([doc.RootElement], JsonlFastPathSerializationContext.Default.JsonElement);
        Assert.Contains("\"k\":1", elemText, StringComparison.Ordinal);

        // Parameterized Serialization-only record with [Validate] rejects Strict and allows Lenient with 0 bytes written
        var paramSerInfo = JsonlFastPathSerializationContext.Default.JsonlParameterizedSerializationRecord;
        var paramInvalid = new JsonlParameterizedSerializationRecord("");
        var paramEx = Assert.Throws<JsonlException>(() =>
            Jsonl.Write<JsonlParameterizedSerializationRecord>().ToText([paramInvalid], paramSerInfo));
        Assert.Equal(JsonlErrorCode.InvalidOptions, paramEx.ErrorCode);

        await using (var ms = new MemoryStream())
        {
            var asyncParamEx = await Assert.ThrowsAsync<JsonlException>(async () =>
                await Jsonl.Write<JsonlParameterizedSerializationRecord>().ToStreamFromEnumerableAsync(ms, [paramInvalid], paramSerInfo, leaveOpen: true, cancellationToken: ct));
            Assert.Equal(JsonlErrorCode.InvalidOptions, asyncParamEx.ErrorCode);
            Assert.Equal(0, ms.Length);
        }

        string paramLenient = Jsonl.Write<JsonlParameterizedSerializationRecord>()
            .WithValidationMode(ValidationMode.Lenient)
            .ToText([paramInvalid], paramSerInfo);
        Assert.Contains("\"Name\":\"\"", paramLenient, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteValidation_MetadataEmptyStruct_AllowsStrictModeAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var emptyInfo = JsonlMetadataEmptyContext.Default.JsonlEmptyMetadataStruct;
        string emptyOut = Jsonl.Write<JsonlEmptyMetadataStruct>().ToText([default], emptyInfo);
        Assert.Equal("{}", emptyOut);

        var strictEmptyInfo = JsonlMetadataEmptyContext.Default.JsonlStrictEmptyMetadataStruct;
        await using var ms = new MemoryStream();
        await Jsonl.Write<JsonlStrictEmptyMetadataStruct>().ToStreamFromEnumerableAsync(ms, [default], strictEmptyInfo, leaveOpen: true, cancellationToken: ct);
        Assert.Equal("{}", Encoding.UTF8.GetString(ms.ToArray()));
    }

    [Fact]
    public async Task GeneratedBinder_ReferenceRecord_ReadsJsonNullLiteralAcrossSyncAndAsyncAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        string recordLine = /*lang=json,strict*/ """{"Name":"Ada"}""";
        string jsonl = string.Join('\n', "null", recordLine, "  null  ");

        var syncFromText = Jsonl.Read<JsonlContractInheritedAndIgnoredRecord>().FromText(jsonl).ToList();
        Assert.Equal(3, syncFromText.Count);
        Assert.Null(syncFromText[0]);
        Assert.Equal("Ada", syncFromText[1]?.Name);
        Assert.Null(syncFromText[2]);

        using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(jsonl)))
        {
            var syncFromStream = Jsonl.Read<JsonlContractInheritedAndIgnoredRecord>().FromStream(ms).ToList();
            Assert.Equal(3, syncFromStream.Count);
            Assert.Null(syncFromStream[0]);
            Assert.Equal("Ada", syncFromStream[1]?.Name);
            Assert.Null(syncFromStream[2]);
        }

        await using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(jsonl)))
        {
            List<JsonlContractInheritedAndIgnoredRecord> asyncItems = [];
            await foreach (var item in Jsonl.DeserializeRecordsAsync<JsonlContractInheritedAndIgnoredRecord>(ms, cancellationToken: ct))
                asyncItems.Add(item);
            Assert.Equal(3, asyncItems.Count);
            Assert.Null(asyncItems[0]);
            Assert.Equal("Ada", asyncItems[1]?.Name);
            Assert.Null(asyncItems[2]);
        }

        await using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(jsonl)))
        {
            List<JsonlContractInheritedAndIgnoredRecord> builderItems = [];
            await foreach (var item in Jsonl.Read<JsonlContractInheritedAndIgnoredRecord>().FromStreamAsync(ms, cancellationToken: ct))
                builderItems.Add(item);
            Assert.Equal(3, builderItems.Count);
            Assert.Null(builderItems[0]);
            Assert.Equal("Ada", builderItems[1]?.Name);
            Assert.Null(builderItems[2]);
        }

        // Non-null garbage after null literal still throws
        Assert.Throws<JsonlException>(() =>
            Jsonl.Read<JsonlContractInheritedAndIgnoredRecord>().FromText("null extra").ToList());
    }

    [Fact]
    public async Task GeneratedBinder_EmptyAndWhitespaceJsonPropertyName_BindsVerbatimAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        string jsonl = /*lang=json,strict*/ """{"":5,"  ":"ok"}""";

        var sync = Assert.Single(Jsonl.Read<JsonlEmptyJsonNameRecord>().FromText(jsonl).ToList());
        Assert.Equal(5, sync.EmptyKeyValue);
        Assert.Equal("ok", sync.BlankKeyValue);

        await using var ms = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));
        List<JsonlEmptyJsonNameRecord> asyncItems = [];
        await foreach (var item in Jsonl.DeserializeRecordsAsync<JsonlEmptyJsonNameRecord>(ms, cancellationToken: ct))
            asyncItems.Add(item);
        var asyncItem = Assert.Single(asyncItems);
        Assert.Equal(5, asyncItem.EmptyKeyValue);
        Assert.Equal("ok", asyncItem.BlankKeyValue);

        // CLR property name must not match when [JsonPropertyName("")] is specified
        var unmatched = Assert.Single(
            Jsonl.Read<JsonlEmptyJsonNameRecord>().FromText(/*lang=json,strict*/ """{"EmptyKeyValue":5,"BlankKeyValue":"bad"}""").ToList());
        Assert.Equal(0, unmatched.EmptyKeyValue);
        Assert.Null(unmatched.BlankKeyValue);
    }

    [Fact]
    public async Task GeneratedBinder_DeserializationCallbacksAndJsonIncludeValidatedFields_WorkAcrossSyncAndAsyncAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        string validCallbackJsonl = /*lang=json,strict*/ """{"Name":"Ada"}""";

        var syncRes = Assert.Single(Jsonl.Read<JsonlCallbackValidatedRecord>().FromText(validCallbackJsonl).ToList());
        Assert.True(syncRes.SawDeserializing);
        Assert.True(syncRes.SawDeserialized);

        await using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(validCallbackJsonl)))
        {
            List<JsonlCallbackValidatedRecord> asyncItems = [];
            await foreach (var item in Jsonl.DeserializeRecordsAsync<JsonlCallbackValidatedRecord>(ms, cancellationToken: ct))
                asyncItems.Add(item);
            var asyncRes = Assert.Single(asyncItems);
            Assert.True(asyncRes.SawDeserializing);
            Assert.True(asyncRes.SawDeserialized);
        }

        Assert.Throws<ValidationException>(() =>
            Jsonl.Read<JsonlCallbackValidatedRecord>().FromText(/*lang=json,strict*/ """{"Name":" "}""").ToList());

        // [GenerateBinder] + [JsonInclude, Validate] public & internal fields validate in Strict mode (even with Serialization-only TypeInfo)
        var pubSerInfo = JsonlIncludeFieldsSerializationContext.Default.JsonlPublicIncludeFieldRecord;
        var invalidPub = new JsonlPublicIncludeFieldRecord { Id = 1, PublicCode = "  ", InternalScore = 50 };
        var pubEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlPublicIncludeFieldRecord>().ToText([invalidPub], pubSerInfo));
        Assert.Contains(pubEx.Errors, e => e.PropertyName == nameof(JsonlPublicIncludeFieldRecord.PublicCode) && e.Rule == "NotEmpty");

        var invalidInt = new JsonlPublicIncludeFieldRecord { Id = 1, PublicCode = "OK", InternalScore = 0 };
        var intEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlPublicIncludeFieldRecord>().ToText([invalidInt], pubSerInfo));
        Assert.Contains(intEx.Errors, e => e.PropertyName == nameof(JsonlPublicIncludeFieldRecord.InternalScore) && e.Rule == "Range");

        var validPub = new JsonlPublicIncludeFieldRecord { Id = 1, PublicCode = "OK", InternalScore = 42 };
        string writtenPub = Jsonl.Write<JsonlPublicIncludeFieldRecord>().ToText([validPub], pubSerInfo);
        Assert.Contains("\"PublicCode\":\"OK\"", writtenPub, StringComparison.Ordinal);
        Assert.Contains("\"InternalScore\":42", writtenPub, StringComparison.Ordinal);

        // When an unsupported-type property has [Validate], generator skips validator registration so metadata fallback runs cleanly
        const string UNSUPPORTED_VALIDATED_SOURCE = """
            #nullable enable
            using System.Collections.Generic;
            using HeroParser;

            namespace UnsupportedValidatedFieldNs;

            [GenerateBinder]
            public sealed class UnsupportedValidatedRecord
            {
                public int Id { get; set; }

                [Validate(NotNull = true)]
                public List<string>? Tags { get; set; }
            }
            """;
        var syntaxTree = CSharpSyntaxTree.ParseText(UNSUPPORTED_VALIDATED_SOURCE, cancellationToken: ct);
        var platformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
        var references = platformAssemblies.Split(Path.PathSeparator)
            .Where(path => Path.GetFileName(path) is var name &&
                (name.StartsWith("System.", StringComparison.Ordinal) ||
                 name is "netstandard.dll" or "System.Private.CoreLib.dll"))
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(Csv).Assembly.Location))
            .ToList();
        var compilation = CSharpCompilation.Create(
            "UnsupportedValidatedJsonlAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        var genResult = CSharpGeneratorDriver.Create(new JsonlRecordBinderGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out _, out _, ct)
            .GetRunResult();
        string regSource = string.Join(
            Environment.NewLine,
            genResult.GeneratedTrees.Select(t => t.GetText(ct).ToString()));
        Assert.DoesNotContain("RegisterNoOpValidator", regSource, StringComparison.Ordinal);
        Assert.DoesNotContain("RegisterValidator", regSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JsonTypeInfo_CustomHandleNullConverter_HonorsConverterAcrossAllReadPathsAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var opts = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            Converters = { new HandleNullSentinelConverter() }
        };
        var typeInfo = (JsonTypeInfo<HandleNullSentinelRecord>)opts.GetTypeInfo(typeof(HandleNullSentinelRecord));
        const string JSONL = "null\n";

        var syncTypeInfo = Assert.Single(Jsonl.Read<HandleNullSentinelRecord>().FromText(JSONL, typeInfo).ToList());
        Assert.Equal("from-null-converter", syncTypeInfo.Label);

        var reflectionOpts = Assert.Single(Jsonl.Read<HandleNullSentinelRecord>().WithJsonOptions(opts).FromText(JSONL).ToList());
        Assert.Equal("from-null-converter", reflectionOpts.Label);

        await using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(JSONL)))
        {
            List<HandleNullSentinelRecord> builderAsync = [];
            await foreach (var item in Jsonl.Read<HandleNullSentinelRecord>().FromStreamAsync(ms, typeInfo, cancellationToken: ct))
                builderAsync.Add(item);
            var row = Assert.Single(builderAsync);
            Assert.Equal("from-null-converter", row.Label);
        }

        await using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(JSONL)))
        {
            List<HandleNullSentinelRecord> staticAsync = [];
            await foreach (var item in Jsonl.DeserializeRecordsAsync(ms, typeInfo, cancellationToken: ct))
                staticAsync.Add(item);
            var row = Assert.Single(staticAsync);
            Assert.Equal("from-null-converter", row.Label);
        }
    }

    [Fact]
    public void WriteValidation_BuiltInBinaryMemoryTypes_AllowStrictMode()
    {
        ReadOnlyMemory<byte> roMem = new byte[] { 1, 2, 3 };
        string roOut = Jsonl.Write<ReadOnlyMemory<byte>>()
            .ToText([roMem], JsonlFastPathSerializationContext.Default.ReadOnlyMemoryByte);
        Assert.Equal("\"AQID\"", roOut);

        Memory<byte> mem = new byte[] { 1, 2, 3 };
        string memOut = Jsonl.Write<Memory<byte>>()
            .ToText([mem], JsonlFastPathSerializationContext.Default.MemoryByte);
        Assert.Equal("\"AQID\"", memOut);
    }

    [Fact]
    public void WriteValidation_EmptyContractWithCreateObject_DoesNotInvokeFactoryOnWrite()
    {
        var opts = new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        var meta = JsonTypeInfo.CreateJsonTypeInfo<DisposableEmptyJsonlRecord>(opts);
        int factoryCalls = 0;
        int disposals = 0;
        meta.CreateObject = () =>
        {
            factoryCalls++;
            return new DisposableEmptyJsonlRecord(() => disposals++);
        };

        using var row = new DisposableEmptyJsonlRecord(() => disposals++);
        string json = Jsonl.Write<DisposableEmptyJsonlRecord>().ToText([row], meta);
        Assert.Equal("{}", json);
        Assert.Equal(0, factoryCalls);
        Assert.Equal(0, disposals);
    }

    [Fact]
    public async Task ReadWithTypeInfo_RequiredValueTypeField_RejectsMissingKeyButAcceptsExplicitZeroAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var typeInfo = JsonlReview13Context.Default.JsonlRequiredIntRecord;

        var ok = Assert.Single(Jsonl.Read<JsonlRequiredIntRecord>().FromText(/*lang=json,strict*/ """{"Id":0}""", typeInfo).ToList());
        Assert.Equal(0, ok.Id);

        var ex = Assert.Throws<ValidationException>(() =>
            Jsonl.Read<JsonlRequiredIntRecord>().FromText("{}", typeInfo).ToList());
        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(JsonlRequiredIntRecord.Id) && e.Rule == "NotNull");

        await using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(/*lang=json,strict*/ """{"Id":0}""")))
        {
            List<JsonlRequiredIntRecord> asyncOk = [];
            await foreach (var item in Jsonl.DeserializeRecordsAsync(ms, typeInfo, cancellationToken: ct))
                asyncOk.Add(item);
            Assert.Equal(0, Assert.Single(asyncOk).Id);
        }

        await using (var ms = new MemoryStream("{}"u8.ToArray()))
        {
            var asyncEx = await Assert.ThrowsAsync<ValidationException>(async () =>
            {
                await foreach (var _ in Jsonl.DeserializeRecordsAsync(ms, typeInfo, cancellationToken: ct)) { }
            });
            Assert.Contains(asyncEx.Errors, e => e.PropertyName == nameof(JsonlRequiredIntRecord.Id) && e.Rule == "NotNull");
        }

        var relaxedOptions = new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };
        var relaxedOk = Assert.Single(
            Jsonl.Read<JsonlRequiredIntRecord>().WithJsonOptions(relaxedOptions).FromText(/*lang=json*/ """{"Id":0, /* ok */ }""").ToList());
        Assert.Equal(0, relaxedOk.Id);

        var relaxedEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Read<JsonlRequiredIntRecord>().WithJsonOptions(relaxedOptions).FromText("{ /* missing */ }").ToList());
        Assert.Contains(relaxedEx.Errors, e => e.PropertyName == nameof(JsonlRequiredIntRecord.Id) && e.Rule == "NotNull");
    }

    [Fact]
    public void WriteValidation_JsonIgnoredValidatedMember_IsNotValidated()
    {
        var typeInfo = JsonlReview13Context.Default.JsonlIgnoredValidatedRecord;
        string json = Jsonl.Write<JsonlIgnoredValidatedRecord>().ToText([new JsonlIgnoredValidatedRecord()], typeInfo);
        Assert.Equal(/*lang=json,strict*/ """{"Id":0}""", json);
    }

    [Fact]
    public void GeneratedBinder_EmptyRelativeUri_RoundTrips()
    {
        var row = Assert.Single(Jsonl.Read<JsonlUriRecord>().FromText(/*lang=json,strict*/ """{"Site":""}""").ToList());
        Assert.NotNull(row.Site);
        Assert.False(row.Site.IsAbsoluteUri);
        Assert.Equal(string.Empty, row.Site.OriginalString);

        var whitespaceRow = Assert.Single(Jsonl.Read<JsonlUriRecord>().FromText(/*lang=json,strict*/ """{"Site":" "}""").ToList());
        Assert.NotNull(whitespaceRow.Site);
        Assert.False(whitespaceRow.Site.IsAbsoluteUri);
        Assert.Equal(" ", whitespaceRow.Site.OriginalString);

        var nullRow = Assert.Single(Jsonl.Read<JsonlUriRecord>().FromText(/*lang=json,strict*/ """{"Site":null}""").ToList());
        Assert.Null(nullRow.Site);
    }

    [Fact]
    public async Task Generator_HiddenValidatedMember_CompilesWithoutDuplicateNamesAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        const string SOURCE = """
            #nullable enable
            using HeroParser;

            namespace HiddenMemberNs;

            public class BaseRecord
            {
                [Validate(Pattern = "^[A-Z]+$")]
                public string? Code = "";
            }

            [GenerateBinder]
            public sealed class DerivedRecord : BaseRecord
            {
                [Validate(Pattern = "^[a-z]+$")]
                public new string? Code { get; set; }
            }
            """;
        var syntaxTree = CSharpSyntaxTree.ParseText(SOURCE, cancellationToken: ct);
        var platformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
        var references = platformAssemblies.Split(Path.PathSeparator)
            .Where(path => Path.GetFileName(path) is var name &&
                (name.StartsWith("System.", StringComparison.Ordinal) ||
                 name is "netstandard.dll" or "System.Private.CoreLib.dll"))
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(Csv).Assembly.Location))
            .ToList();
        var compilation = CSharpCompilation.Create(
            "HiddenMemberAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        CSharpGeneratorDriver.Create(new JsonlRecordBinderGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _, ct);
        var errors = updated.GetDiagnostics(ct).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ReadWithTypeInfo_RequiredValueTypeField_IgnoresNonDeserializableAndKeepsConstructorBoundAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var computedTypeInfo = JsonlReview14Context.Default.JsonlComputedAndPrivateSetRequiredRecord;

        var okComputed = Assert.Single(
            Jsonl.Read<JsonlComputedAndPrivateSetRequiredRecord>().FromText(/*lang=json,strict*/ """{"Id":1}""", computedTypeInfo).ToList());
        Assert.Equal(1, okComputed.Id);
        Assert.Equal(2, okComputed.Computed);

        await using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(/*lang=json,strict*/ """{"Id":1}""")))
        {
            List<JsonlComputedAndPrivateSetRequiredRecord> asyncComputed = [];
            await foreach (var item in Jsonl.DeserializeRecordsAsync(ms, computedTypeInfo, cancellationToken: ct))
                asyncComputed.Add(item);
            Assert.Equal(2, Assert.Single(asyncComputed).Computed);
        }

        var ctorTypeInfo = JsonlReview14Context.Default.JsonlCtorRequiredIntRecord;
        var okCtor = Assert.Single(
            Jsonl.Read<JsonlCtorRequiredIntRecord>().FromText(/*lang=json,strict*/ """{"Id":0}""", ctorTypeInfo).ToList());
        Assert.Equal(0, okCtor.Id);

        var ctorEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Read<JsonlCtorRequiredIntRecord>().FromText("{}", ctorTypeInfo).ToList());
        Assert.Contains(ctorEx.Errors, e => e.PropertyName == nameof(JsonlCtorRequiredIntRecord.Id) && e.Rule == "NotNull");

        await using (var ms = new MemoryStream("{}"u8.ToArray()))
        {
            var asyncCtorEx = await Assert.ThrowsAsync<ValidationException>(async () =>
            {
                await foreach (var _ in Jsonl.DeserializeRecordsAsync(ms, ctorTypeInfo, cancellationToken: ct)) { }
            });
            Assert.Contains(asyncCtorEx.Errors, e => e.PropertyName == nameof(JsonlCtorRequiredIntRecord.Id) && e.Rule == "NotNull");
        }
    }

#if NET10_0_OR_GREATER
    [Fact]
    public async Task GeneratedBinder_JsonIgnoreWhenReading_LeavesPropertyUntouchedSyncAndAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        string jsonl = /*lang=json,strict*/ """{"Id":7,"Secret":"injected"}""";

        var sync = Assert.Single(Jsonl.Read<JsonlWhenReadingIgnoredRecord>().FromText(jsonl).ToList());
        Assert.Equal(7, sync.Id);
        Assert.Equal("initial", sync.Secret);

        await using var ms = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));
        List<JsonlWhenReadingIgnoredRecord> asyncItems = [];
        await foreach (var item in Jsonl.DeserializeRecordsAsync<JsonlWhenReadingIgnoredRecord>(ms, cancellationToken: ct))
            asyncItems.Add(item);

        var asyncItem = Assert.Single(asyncItems);
        Assert.Equal(7, asyncItem.Id);
        Assert.Equal("initial", asyncItem.Secret);
    }
#endif

    [Fact]
    public async Task GeneratedBinder_LargeBackingEnums_ReadNumericValuesSyncAndAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        string jsonl = /*lang=json,strict*/ """{"BigSigned":4294967296,"BigUnsigned":18446744073709551615,"UIntBacked":3000000000,"ByteBacked":200}""";

        var sync = Assert.Single(Jsonl.Read<JsonlLargeEnumRecord>().FromText(jsonl).ToList());
        Assert.Equal(JsonlLongEnum.Large, sync.BigSigned);
        Assert.Equal(JsonlUlongEnum.Max, sync.BigUnsigned);
        Assert.Equal(JsonlUIntEnum.High, sync.UIntBacked);
        Assert.Equal(JsonlByteEnum.High, sync.ByteBacked);

        await using var ms = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));
        List<JsonlLargeEnumRecord> asyncItems = [];
        await foreach (var item in Jsonl.DeserializeRecordsAsync<JsonlLargeEnumRecord>(ms, cancellationToken: ct))
            asyncItems.Add(item);

        var asyncItem = Assert.Single(asyncItems);
        Assert.Equal(JsonlLongEnum.Large, asyncItem.BigSigned);
        Assert.Equal(JsonlUlongEnum.Max, asyncItem.BigUnsigned);
        Assert.Equal(JsonlUIntEnum.High, asyncItem.UIntBacked);
        Assert.Equal(JsonlByteEnum.High, asyncItem.ByteBacked);
    }

    [Fact]
    public async Task GeneratedBinder_AlwaysIgnoredValidatedMember_DoesNotFailStrictWriteAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var row = new JsonlGeneratedAlwaysIgnoredValidatedRecord { Id = 1, Ignored = null };
        var typeInfo = JsonlReview15Context.Default.JsonlGeneratedAlwaysIgnoredValidatedRecord;

        string syncJson = Jsonl.Write<JsonlGeneratedAlwaysIgnoredValidatedRecord>().ToText([row], typeInfo);
        Assert.Equal(/*lang=json,strict*/ """{"Id":1}""", syncJson);

        await using var ms = new MemoryStream();
        await Jsonl.Write<JsonlGeneratedAlwaysIgnoredValidatedRecord>().ToStreamFromEnumerableAsync(ms, [row], typeInfo, leaveOpen: true, cancellationToken: ct);
        Assert.Equal(/*lang=json,strict*/ """{"Id":1}""", Encoding.UTF8.GetString(ms.ToArray()));
    }

    [Fact]
    public async Task ReadWithoutGenerateBinder_EnforcesValidateAttributesAcrossSyncAndAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var typeInfo = JsonlReview15Context.Default.JsonlPlainValidatedDto;
        var serializerOptions = new JsonSerializerOptions();
        string invalidJsonl = /*lang=json,strict*/ """{"Amount":1,"Name":"ok"}""";
        string validJsonl = /*lang=json,strict*/ """{"Amount":10,"Name":"ok"}""";

        var tiSyncEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Read<JsonlPlainValidatedDto>().FromText(invalidJsonl, typeInfo).ToList());
        Assert.Contains(tiSyncEx.Errors, e => e.PropertyName == nameof(JsonlPlainValidatedDto.Amount) && e.Rule == "Range");

        var optSyncEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Read<JsonlPlainValidatedDto>().WithJsonOptions(serializerOptions).FromText(invalidJsonl).ToList());
        Assert.Contains(optSyncEx.Errors, e => e.PropertyName == nameof(JsonlPlainValidatedDto.Amount) && e.Rule == "Range");

        await using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(invalidJsonl)))
        {
            var asyncEx = await Assert.ThrowsAsync<ValidationException>(async () =>
            {
                await foreach (var _ in Jsonl.DeserializeRecordsAsync(ms, typeInfo, cancellationToken: ct)) { }
            });
            Assert.Contains(asyncEx.Errors, e => e.PropertyName == nameof(JsonlPlainValidatedDto.Amount) && e.Rule == "Range");
        }

        Assert.Empty(
            Jsonl.Read<JsonlPlainValidatedDto>().WithValidationMode(ValidationMode.Lenient).FromText(invalidJsonl, typeInfo).ToList());

        var valid = Assert.Single(Jsonl.Read<JsonlPlainValidatedDto>().FromText(validJsonl, typeInfo).ToList());
        Assert.Equal(10, valid.Amount);
        Assert.Equal("ok", valid.Name);
    }

    [Fact]
    public async Task ReflectionWriteValidation_RespectsJsonIgnoreAndJsonIncludeFieldsAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var valid = new JsonlReflectionContractDto { Ignored = null, Code = "OK" };
        string syncOut = Jsonl.Write<JsonlReflectionContractDto>().ToText([valid]);
        Assert.Equal(/*lang=json,strict*/ """{"Code":"OK"}""", syncOut);

        await using (var ms = new MemoryStream())
        {
            await Jsonl.Write<JsonlReflectionContractDto>().ToStreamFromEnumerableAsync(ms, [valid], leaveOpen: true, cancellationToken: ct);
            Assert.Equal(/*lang=json,strict*/ """{"Code":"OK"}""", Encoding.UTF8.GetString(ms.ToArray()));
        }

        var invalid = new JsonlReflectionContractDto { Ignored = "x", Code = "   " };
        var syncEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlReflectionContractDto>().ToText([invalid]));
        Assert.Contains(syncEx.Errors, e => e.PropertyName == nameof(JsonlReflectionContractDto.Code) && e.Rule == "NotEmpty");

        await using (var ms = new MemoryStream())
        {
            var asyncEx = await Assert.ThrowsAsync<ValidationException>(async () =>
                await Jsonl.Write<JsonlReflectionContractDto>().ToStreamFromEnumerableAsync(ms, [invalid], leaveOpen: true, cancellationToken: ct));
            Assert.Contains(asyncEx.Errors, e => e.PropertyName == nameof(JsonlReflectionContractDto.Code) && e.Rule == "NotEmpty");
        }
    }

    [Fact]
    public async Task FallbackValidation_FloatArrayLength_MeasuresElementCountOnReadAndWriteAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var typeInfo = JsonlReview16Context.Default.JsonlFloatArrayLengthDto;

        var valid = new JsonlFloatArrayLengthDto { Embedding = [1f, 2f] };
        string written = Jsonl.Write<JsonlFloatArrayLengthDto>().ToText([valid], typeInfo);
        Assert.Equal(/*lang=json,strict*/ """{"Embedding":[1,2]}""", written);
        Assert.Equal(/*lang=json,strict*/ """{"Embedding":[1,2]}""", Jsonl.Write<JsonlFloatArrayLengthDto>().ToText([valid]));

        var readTi = Assert.Single(Jsonl.Read<JsonlFloatArrayLengthDto>().FromText(written, typeInfo).ToList());
        Assert.Equal(2, readTi.Embedding!.Length);

        var readRefl = Assert.Single(Jsonl.Read<JsonlFloatArrayLengthDto>().WithJsonOptions(new JsonSerializerOptions()).FromText(written).ToList());
        Assert.Equal(2, readRefl.Embedding!.Length);

        var tooShort = new JsonlFloatArrayLengthDto { Embedding = [1f] };
        var shortEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlFloatArrayLengthDto>().ToText([tooShort], typeInfo));
        Assert.Contains(shortEx.Errors, e => e.PropertyName == nameof(JsonlFloatArrayLengthDto.Embedding) && e.Rule == "MinLength");

        var tooLong = new JsonlFloatArrayLengthDto { Embedding = new float[16] };
        var longWriteEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlFloatArrayLengthDto>().ToText([tooLong]));
        Assert.Contains(longWriteEx.Errors, e => e.PropertyName == nameof(JsonlFloatArrayLengthDto.Embedding) && e.Rule == "MaxLength");

        await using var ms = new MemoryStream(Encoding.UTF8.GetBytes(/*lang=json,strict*/ """{"Embedding":[1,2,3,4]}"""));
        var longReadEx = await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await foreach (var _ in Jsonl.DeserializeRecordsAsync(ms, typeInfo, cancellationToken: ct)) { }
        });
        Assert.Contains(longReadEx.Errors, e => e.PropertyName == nameof(JsonlFloatArrayLengthDto.Embedding) && e.Rule == "MaxLength");
    }

    [Fact]
    public async Task WriteValidation_CustomJsonConverter_AllowedInDefaultStrictModeAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var item = new JsonlCustomConverterDto(42);
        var typeInfo = JsonlReview17Context.Default.JsonlCustomConverterDto;

        Assert.Equal("\"42\"", Jsonl.Write<JsonlCustomConverterDto>().ToText([item], typeInfo));
        Assert.Equal("\"42\"", Jsonl.Write<JsonlCustomConverterDto>().ToText([item]));

        await using var ms = new MemoryStream();
        await Jsonl.Write<JsonlCustomConverterDto>().ToStreamFromEnumerableAsync(ms, [item], typeInfo, leaveOpen: true, cancellationToken: ct);
        Assert.Equal("\"42\"", Encoding.UTF8.GetString(ms.ToArray()));
    }

    [Fact]
    public async Task GeneratedBinder_NonJsonIncludeFieldAndComputedGetter_RespectActiveContractAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var typeInfo = JsonlReview17Context.Default.JsonlPartialContractRecord;

        // Public field without [JsonInclude] is omitted in default writes and must not fail validation
        var row = new JsonlPartialContractRecord { Id = 10, UnserializedField = null };
        Assert.Equal(/*lang=json,strict*/ """{"Id":10,"Computed":10}""", Jsonl.Write<JsonlPartialContractRecord>().ToText([row], typeInfo));
        Assert.Equal(/*lang=json,strict*/ """{"Id":10,"Computed":10}""", Jsonl.Write<JsonlPartialContractRecord>().ToText([row]));

        // Explicit IncludeFields = true includes the field in the contract and enforces [Validate(NotNull = true)]
        var includeOpts = new JsonSerializerOptions { IncludeFields = true };
        var includeEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlPartialContractRecord>().WithJsonOptions(includeOpts).ToText([row]));
        Assert.Contains(includeEx.Errors, e => e.PropertyName == nameof(JsonlPartialContractRecord.UnserializedField) && e.Rule == "NotNull");

        // Getter-only computed property cannot be populated on read, so both fast-path and JsonTypeInfo read accept {"Id":1}
        string readJson = /*lang=json,strict*/ """{"Id":1}""";
        var fastRead = Assert.Single(Jsonl.Read<JsonlPartialContractRecord>().FromText(readJson).ToList());
        Assert.Equal(1, fastRead.Id);

        var tiRead = Assert.Single(Jsonl.Read<JsonlPartialContractRecord>().FromText(readJson, typeInfo).ToList());
        Assert.Equal(1, tiRead.Id);

        await using var ms = new MemoryStream(Encoding.UTF8.GetBytes(readJson));
        List<JsonlPartialContractRecord> asyncTi = [];
        await foreach (var item in Jsonl.DeserializeRecordsAsync(ms, typeInfo, cancellationToken: ct))
            asyncTi.Add(item);
        Assert.Equal(1, Assert.Single(asyncTi).Id);
    }

    [Fact]
    public async Task WriteValidation_CustomContractAndIgnoreReadOnlyProperties_HonorEffectiveContractAsync()
    {
        var ct = TestContext.Current.CancellationToken;

        // 1. Custom resolver modifier removing Optional on a [GenerateBinder] record
        var prunedOptions = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers =
                {
                    ti =>
                    {
                        if (ti.Type == typeof(JsonlCustomPrunedRecord))
                        {
                            for (int i = ti.Properties.Count - 1; i >= 0; i--)
                            {
                                if (ti.Properties[i].Name == nameof(JsonlCustomPrunedRecord.Optional))
                                    ti.Properties.RemoveAt(i);
                            }
                        }
                    }
                }
            }
        };

        var prunedRecord = new JsonlCustomPrunedRecord { Id = 1, Optional = null };
        Assert.Throws<ValidationException>(() => Jsonl.Write<JsonlCustomPrunedRecord>().ToText([prunedRecord]));

        Assert.Equal(/*lang=json,strict*/ """{"Id":1}""", Jsonl.Write<JsonlCustomPrunedRecord>().WithJsonOptions(prunedOptions).ToText([prunedRecord]));

        prunedOptions.MakeReadOnly(populateMissingResolver: true);
        var prunedTypeInfo = (JsonTypeInfo<JsonlCustomPrunedRecord>)prunedOptions.GetTypeInfo(typeof(JsonlCustomPrunedRecord));
        Assert.Equal(/*lang=json,strict*/ """{"Id":1}""", Jsonl.Write<JsonlCustomPrunedRecord>().ToText([prunedRecord], prunedTypeInfo));

        // 2. IgnoreReadOnlyProperties = true omits getter-only scalars on both [GenerateBinder] and non-binder types
        var ignoreReadOnlyOpts = new JsonSerializerOptions { IgnoreReadOnlyProperties = true };
        var binderReadOnlyRow = new JsonlReadOnlyValidatedRecord { Id = 7 };
        var plainReadOnlyRow = new JsonlPlainReadOnlyValidatedRecord { Id = 7 };

        Assert.Throws<ValidationException>(() => Jsonl.Write<JsonlReadOnlyValidatedRecord>().ToText([binderReadOnlyRow]));
        Assert.Throws<ValidationException>(() => Jsonl.Write<JsonlPlainReadOnlyValidatedRecord>().ToText([plainReadOnlyRow]));

        Assert.Equal(/*lang=json,strict*/ """{"Id":7}""", Jsonl.Write<JsonlReadOnlyValidatedRecord>().WithJsonOptions(ignoreReadOnlyOpts).ToText([binderReadOnlyRow]));
        Assert.Equal(/*lang=json,strict*/ """{"Id":7}""", Jsonl.Write<JsonlPlainReadOnlyValidatedRecord>().WithJsonOptions(ignoreReadOnlyOpts).ToText([plainReadOnlyRow]));

        var ignoreReadOnlyContext = new JsonlIgnoreReadOnlyContext(new JsonSerializerOptions { IgnoreReadOnlyProperties = true });
        await using var outMs = new MemoryStream();
        await Jsonl.Write<JsonlPlainReadOnlyValidatedRecord>().ToStreamFromEnumerableAsync(
            outMs,
            [plainReadOnlyRow],
            ignoreReadOnlyContext.JsonlPlainReadOnlyValidatedRecord,
            leaveOpen: true,
            cancellationToken: ct);
        Assert.Equal(/*lang=json,strict*/ """{"Id":7}""", Encoding.UTF8.GetString(outMs.ToArray()));
    }

    [Fact]
    public void BindValidation_PrivateGetterValidatedProperties_CompileAndEnforceValidation()
    {
        string validJson = /*lang=json,strict*/ """{"Secret":"ok","Score":15}""";
        var ok = Assert.Single(Jsonl.Read<JsonlPrivateGetterValidatedRecord>().FromText(validJson).ToList());
        Assert.True(ok.Matches("ok", 15));

        string missingSecretJson = /*lang=json,strict*/ """{"Secret":null,"Score":15}""";
        var secretEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Read<JsonlPrivateGetterValidatedRecord>().FromText(missingSecretJson).ToList());
        Assert.Contains(secretEx.Errors, e => e.PropertyName == nameof(JsonlPrivateGetterValidatedRecord.Secret) && e.Rule == "NotNull");

        string badScoreJson = /*lang=json,strict*/ """{"Secret":"ok","Score":5}""";
        var scoreEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Read<JsonlPrivateGetterValidatedRecord>().FromText(badScoreJson).ToList());
        Assert.Contains(scoreEx.Errors, e => e.PropertyName == nameof(JsonlPrivateGetterValidatedRecord.Score) && e.Rule == "Range");
    }

    [Fact]
    public void WriteValidation_WhenWritingDefaultOrNull_SkipsOmittedPropertiesAndValidatesSerializedValues()
    {
        var typeInfo = JsonlReview19Context.Default.JsonlConditionalIgnoreValidatedRecord;

        // Default Score = 0 and null Note/Amount are omitted by [JsonIgnore(Condition = ...)], so strict write succeeds
        var defaultRow = new JsonlConditionalIgnoreValidatedRecord { Id = 1, Score = 0, Note = null, Amount = null };
        Assert.Equal(/*lang=json,strict*/ """{"Id":1}""", Jsonl.Write<JsonlConditionalIgnoreValidatedRecord>().ToText([defaultRow]));
        Assert.Equal(/*lang=json,strict*/ """{"Id":1}""", Jsonl.Write<JsonlConditionalIgnoreValidatedRecord>().ToText([defaultRow], typeInfo));

        // Explicit non-default out-of-range Score = 5 is serialized, so strict write still rejects it
        var invalidNonDefault = new JsonlConditionalIgnoreValidatedRecord { Id = 1, Score = 5, Note = null, Amount = null };
        var rangeEx1 = Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlConditionalIgnoreValidatedRecord>().ToText([invalidNonDefault]));
        Assert.Contains(rangeEx1.Errors, e => e.PropertyName == nameof(JsonlConditionalIgnoreValidatedRecord.Score) && e.Rule == "Range");

        var rangeEx2 = Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlConditionalIgnoreValidatedRecord>().ToText([invalidNonDefault], typeInfo));
        Assert.Contains(rangeEx2.Errors, e => e.PropertyName == nameof(JsonlConditionalIgnoreValidatedRecord.Score) && e.Rule == "Range");

        // Populated Nullable<int> holding 0 IS serialized as {"Id":1,"Amount":0} under WhenWritingDefault,
        // so Strict mode must validate it rather than treating boxed 0 as default.
        var nullableZeroRow = new JsonlConditionalIgnoreValidatedRecord { Id = 1, Score = 0, Note = null, Amount = 0 };
        var nullableEx1 = Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlConditionalIgnoreValidatedRecord>().ToText([nullableZeroRow]));
        Assert.Contains(nullableEx1.Errors, e => e.PropertyName == nameof(JsonlConditionalIgnoreValidatedRecord.Amount) && e.Rule == "Range");

        var nullableEx2 = Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlConditionalIgnoreValidatedRecord>().ToText([nullableZeroRow], typeInfo));
        Assert.Contains(nullableEx2.Errors, e => e.PropertyName == nameof(JsonlConditionalIgnoreValidatedRecord.Amount) && e.Rule == "Range");
    }

    [Fact]
    public async Task Bind_TabularMapAliasCollidingWithJsonPropertyName_PrefersActualJsonPropertyNamesAsync()
    {
        var ct = TestContext.Current.CancellationToken;

        string exactPayload = /*lang=json,strict*/ """{"X":1,"Y":2}""";
        var syncExact = Assert.Single(Jsonl.Read<JsonlTabularCollisionRecord>().FromText(exactPayload).ToList());
        Assert.Equal(1, syncExact.X);
        Assert.Equal(2, syncExact.Y);

        await using (var exactMs = new MemoryStream(Encoding.UTF8.GetBytes(exactPayload)))
        {
            var asyncExactList = new List<JsonlTabularCollisionRecord>();
            await foreach (var item in Jsonl.Read<JsonlTabularCollisionRecord>().FromStreamAsync(exactMs, leaveOpen: true, cancellationToken: ct))
                asyncExactList.Add(item);
            var asyncExact = Assert.Single(asyncExactList);
            Assert.Equal(1, asyncExact.X);
            Assert.Equal(2, asyncExact.Y);
        }

        string lowerPayload = /*lang=json,strict*/ """{"x":1,"y":2}""";
        var syncLower = Assert.Single(Jsonl.Read<JsonlTabularCollisionRecord>().FromText(lowerPayload).ToList());
        Assert.Equal(1, syncLower.X);
        Assert.Equal(2, syncLower.Y);

        await using (var lowerMs = new MemoryStream(Encoding.UTF8.GetBytes(lowerPayload)))
        {
            var asyncLowerList = new List<JsonlTabularCollisionRecord>();
            await foreach (var item in Jsonl.Read<JsonlTabularCollisionRecord>().FromStreamAsync(lowerMs, leaveOpen: true, cancellationToken: ct))
                asyncLowerList.Add(item);
            var asyncLower = Assert.Single(asyncLowerList);
            Assert.Equal(1, asyncLower.X);
            Assert.Equal(2, asyncLower.Y);
        }

        string readOnlyPayload = /*lang=json,strict*/ """{"X":1,"Y":99}""";
        var readOnlyResult = Assert.Single(Jsonl.Read<JsonlTabularReadOnlyCollisionRecord>().FromText(readOnlyPayload).ToList());
        Assert.Equal(1, readOnlyResult.X);
    }

    [Fact]
    public void WriteValidation_MetadataPath_RespectsShouldSerializeOverrideAndInheritedOverrideValidation()
    {
        var shouldSerializeOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers =
                {
                    ti =>
                    {
                        if (ti.Type == typeof(JsonlMetadataShouldSerializeRecord))
                        {
                            foreach (var prop in ti.Properties)
                            {
                                if (prop.Name == nameof(JsonlMetadataShouldSerializeRecord.Score))
                                    prop.ShouldSerialize = (_, _) => true;
                            }
                        }
                    }
                }
            }
        };
        shouldSerializeOptions.MakeReadOnly(populateMissingResolver: true);
        var shouldSerializeTypeInfo = (JsonTypeInfo<JsonlMetadataShouldSerializeRecord>)shouldSerializeOptions.GetTypeInfo(typeof(JsonlMetadataShouldSerializeRecord));

        var zeroScoreRow = new JsonlMetadataShouldSerializeRecord { Score = 0 };
        var shouldSerializeEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlMetadataShouldSerializeRecord>().ToText([zeroScoreRow], shouldSerializeTypeInfo));
        Assert.Contains(shouldSerializeEx.Errors, e => e.PropertyName == nameof(JsonlMetadataShouldSerializeRecord.Score) && e.Rule == "Range");

        var derivedOptions = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
        derivedOptions.MakeReadOnly(populateMissingResolver: true);
        var derivedTypeInfo = (JsonTypeInfo<JsonlDerivedOverrideValidatedRecord>)derivedOptions.GetTypeInfo(typeof(JsonlDerivedOverrideValidatedRecord));

        var invalidDerived = new JsonlDerivedOverrideValidatedRecord { Count = 0 };
        var inheritedEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlDerivedOverrideValidatedRecord>().ToText([invalidDerived], derivedTypeInfo));
        Assert.Contains(inheritedEx.Errors, e => e.PropertyName == nameof(JsonlDerivedOverrideValidatedRecord.Count) && e.Rule == "Range");
    }

    [Fact]
    public async Task ReadValidation_MetadataPath_SetterOnlyAndPrivateGetterProperties_EnforceRulesAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var metadataOptions = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
        metadataOptions.MakeReadOnly(populateMissingResolver: true);
        var setterOnlyTypeInfo = (JsonTypeInfo<JsonlMetadataSetterOnlyValidatedRecord>)metadataOptions.GetTypeInfo(typeof(JsonlMetadataSetterOnlyValidatedRecord));
        var privateGetterTypeInfo = (JsonTypeInfo<JsonlMetadataPrivateGetterValidatedRecord>)metadataOptions.GetTypeInfo(typeof(JsonlMetadataPrivateGetterValidatedRecord));

        string badJson = /*lang=json,strict*/ """{"Quantity":-1}""";
        var syncEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Read<JsonlMetadataSetterOnlyValidatedRecord>().FromText(badJson, setterOnlyTypeInfo).ToList());
        Assert.Contains(syncEx.Errors, e => e.PropertyName == nameof(JsonlMetadataSetterOnlyValidatedRecord.Quantity) && e.Rule == "Range");

        await using (var badMs = new MemoryStream(Encoding.UTF8.GetBytes(badJson)))
        {
            await Assert.ThrowsAsync<ValidationException>(async () =>
            {
                await foreach (var _ in Jsonl.Read<JsonlMetadataSetterOnlyValidatedRecord>().FromStreamAsync(badMs, setterOnlyTypeInfo, leaveOpen: true, cancellationToken: ct))
                {
                }
            });
        }

        string okJson = /*lang=json,strict*/ """{"Quantity":5}""";
        var ok = Assert.Single(Jsonl.Read<JsonlMetadataSetterOnlyValidatedRecord>().FromText(okJson, setterOnlyTypeInfo).ToList());
        Assert.Equal(5, ok.ReadQuantity());

        string lastWinsJson = /*lang=json,strict*/ """{"Quantity":-1,"Quantity":5}""";
        var lastWins = Assert.Single(Jsonl.Read<JsonlMetadataSetterOnlyValidatedRecord>().FromText(lastWinsJson, setterOnlyTypeInfo).ToList());
        Assert.Equal(5, lastWins.ReadQuantity());

        string badPrivJson = /*lang=json,strict*/ """{"Score":-2}""";
        var privEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Read<JsonlMetadataPrivateGetterValidatedRecord>().FromText(badPrivJson, privateGetterTypeInfo).ToList());
        Assert.Contains(privEx.Errors, e => e.PropertyName == nameof(JsonlMetadataPrivateGetterValidatedRecord.Score) && e.Rule == "Range");

        string okPrivJson = /*lang=json,strict*/ """{"Score":7}""";
        var okPriv = Assert.Single(Jsonl.Read<JsonlMetadataPrivateGetterValidatedRecord>().FromText(okPrivJson, privateGetterTypeInfo).ToList());
        Assert.Equal(7, okPriv.ReadScore());
    }

    [Fact]
    public void ReadValidation_MetadataAndContextPath_SetterOnlyWithTypeNumberHandling_AcceptsQuotedNumberAndAllocatesZeroOnValidRange()
    {
        var metadataOptions = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
        metadataOptions.MakeReadOnly(populateMissingResolver: true);
        var metadataTypeInfo = (JsonTypeInfo<JsonlSetterOnlyStringNumberRecord>)metadataOptions.GetTypeInfo(typeof(JsonlSetterOnlyStringNumberRecord));
        var contextTypeInfo = JsonlSetterOnlyNumberHandlingContext.Default.JsonlSetterOnlyStringNumberRecord;

        foreach (JsonTypeInfo<JsonlSetterOnlyStringNumberRecord> typeInfo in new[] { metadataTypeInfo, contextTypeInfo })
        {
            string okJson = /*lang=json,strict*/ """{"Amount":"5"}""";
            var ok = Assert.Single(Jsonl.Read<JsonlSetterOnlyStringNumberRecord>().FromText(okJson, typeInfo).ToList());
            Assert.Equal(5, ok.ReadAmount());

            string badJson = /*lang=json,strict*/ """{"Amount":"0"}""";
            var ex = Assert.Throws<ValidationException>(() =>
                Jsonl.Read<JsonlSetterOnlyStringNumberRecord>().FromText(badJson, typeInfo).ToList());
            Assert.Contains(ex.Errors, e => e.PropertyName == nameof(JsonlSetterOnlyStringNumberRecord.Amount) && e.Rule == "Range");
        }

        // Verify WritePropertyValidation does not allocate formatted strings on passing decimal range checks
        var rangeValidation = WritePropertyValidation.FromAttribute(new ValidateAttribute { RangeMin = 1, RangeMax = 100 }, requireAnyRule: true)!;
        object boxedDecimal = 42.5m;
        var errors = new List<ValidationError>();
        CultureInfo culture = CultureInfo.InvariantCulture;

        // Warmup JIT
        Assert.False(rangeValidation.Validate(boxedDecimal, "Price", 1, 0, errors, culture));

#if !DEBUG
        long before = GC.GetAllocatedBytesForCurrentThread();
#endif
        for (int i = 0; i < 100; i++)
        {
            Assert.False(rangeValidation.Validate(boxedDecimal, "Price", i + 1, 0, errors, culture));
        }
#if !DEBUG
        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0L, after - before);
#endif
    }

    [Fact]
    public void WriteValidation_SyntheticPropertyInMetadataContract_ValidatesClrPropertiesAndAllowsWrite()
    {
        var opts = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers =
                {
                    ti =>
                    {
                        if (ti.Type == typeof(JsonlPlainValidatedDto))
                        {
                            var synth = ti.CreateJsonPropertyInfo(typeof(string), "SyntheticTag");
                            synth.Get = _ => "tag";
                            ti.Properties.Add(synth);
                        }
                    }
                }
            }
        };
        opts.MakeReadOnly(populateMissingResolver: true);
        var typeInfo = (JsonTypeInfo<JsonlPlainValidatedDto>)opts.GetTypeInfo(typeof(JsonlPlainValidatedDto));

        var valid = new JsonlPlainValidatedDto { Amount = 12, Name = "ok" };
        string json = Jsonl.Write<JsonlPlainValidatedDto>().ToText([valid], typeInfo);
        Assert.Contains("\"SyntheticTag\":\"tag\"", json, StringComparison.Ordinal);

        var invalid = new JsonlPlainValidatedDto { Amount = 5, Name = "ok" };
        Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlPlainValidatedDto>().ToText([invalid], typeInfo));
    }

    [Fact]
    public async Task Validation_PolymorphicDerivedType_EnforcesDerivedRulesOnReadAndWriteAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        JsonlPolymorphicBase validDerived = new JsonlPolymorphicDerived { BaseCode = "ok", Detail = "info" };
        JsonlPolymorphicBase invalidDerived = new JsonlPolymorphicDerived { BaseCode = "ok", Detail = null };

        // Write via TypeInfo/Context and reflection
        var baseTypeInfo = JsonlPolymorphicContext.Default.JsonlPolymorphicBase;
        string serialized = Jsonl.Write<JsonlPolymorphicBase>().ToText([validDerived], baseTypeInfo);
        Assert.Contains("\"Detail\":\"info\"", serialized, StringComparison.Ordinal);
        Assert.Equal(serialized, Jsonl.Write<JsonlPolymorphicBase>().ToText([validDerived]));

        Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlPolymorphicBase>().ToText([invalidDerived], baseTypeInfo));
        Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlPolymorphicBase>().ToText([invalidDerived]));

        // Read via TypeInfo/Context (sync + async) and reflection
        string badDerivedJson = /*lang=json,strict*/ """{"$type":"derived","BaseCode":"ok","Detail":null}""";
        var readEx1 = Assert.Throws<ValidationException>(() =>
            Jsonl.Read<JsonlPolymorphicBase>().FromText(badDerivedJson, baseTypeInfo).ToList());
        Assert.Contains(readEx1.Errors, e => e.PropertyName == nameof(JsonlPolymorphicDerived.Detail));

        var readEx2 = Assert.Throws<ValidationException>(() =>
            Jsonl.Read<JsonlPolymorphicBase>().WithJsonOptions(new JsonSerializerOptions()).FromText(badDerivedJson).ToList());
        Assert.Contains(readEx2.Errors, e => e.PropertyName == nameof(JsonlPolymorphicDerived.Detail));

        await using var ms = new MemoryStream(Encoding.UTF8.GetBytes(badDerivedJson));
        await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await foreach (var _ in Jsonl.DeserializeRecordsAsync(ms, baseTypeInfo, cancellationToken: ct)) { }
        });

        var roundTrip = Assert.Single(Jsonl.Read<JsonlPolymorphicBase>().FromText(serialized, baseTypeInfo).ToList());
        var typedDerived = Assert.IsType<JsonlPolymorphicDerived>(roundTrip);
        Assert.Equal("info", typedDerived.Detail);
    }

    [Fact]
    public void WriteValidation_IgnoreReadOnlyProperties_StillValidatesCollectionsNeverIgnoredAndHandlesNegativeHalfZero()
    {
        var ignoreRoOptions = new JsonSerializerOptions
        {
            IgnoreReadOnlyProperties = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
        ignoreRoOptions.MakeReadOnly(populateMissingResolver: true);
        var roTypeInfo = (JsonTypeInfo<JsonlReadOnlyCollectionAndNeverRecord>)ignoreRoOptions.GetTypeInfo(typeof(JsonlReadOnlyCollectionAndNeverRecord));

        // 1. Read-only float[] IS serialized despite IgnoreReadOnlyProperties = true, so MinLength=2 must fail on [1f]
        var shortVec = new JsonlReadOnlyCollectionAndNeverRecord([1f], "present");
        var vecEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlReadOnlyCollectionAndNeverRecord>().ToText([shortVec], roTypeInfo));
        Assert.Contains(vecEx.Errors, e => e.PropertyName == nameof(JsonlReadOnlyCollectionAndNeverRecord.Scores));

        // 2. [JsonIgnore(Condition = Never)] IS serialized despite IgnoreReadOnlyProperties = true, so NotNull must fail on null
        var nullNever = new JsonlReadOnlyCollectionAndNeverRecord([1f, 2f], null);
        var neverEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlReadOnlyCollectionAndNeverRecord>().ToText([nullNever], roTypeInfo));
        Assert.Contains(neverEx.Errors, e => e.PropertyName == nameof(JsonlReadOnlyCollectionAndNeverRecord.AlwaysWritten));

        // 3. Plain read-only scalar IgnoredScalar = null IS omitted by IgnoreReadOnlyProperties = true and valid record succeeds
        var validRo = new JsonlReadOnlyCollectionAndNeverRecord([1f, 2f], "present");
        string roJson = Jsonl.Write<JsonlReadOnlyCollectionAndNeverRecord>().ToText([validRo], roTypeInfo);
        Assert.DoesNotContain("IgnoredScalar", roJson, StringComparison.Ordinal);

        // 4. Half.NegativeZero is omitted under WhenWritingDefault and must not trip RangeMin = 1
        var halfTypeInfo = JsonlNegativeZeroHalfContext.Default.JsonlNegativeZeroHalfRecord;
        var negZero = new JsonlNegativeZeroHalfRecord { Metric = Half.NegativeZero };
        Assert.Equal("{}", Jsonl.Write<JsonlNegativeZeroHalfRecord>().ToText([negZero]));
        Assert.Equal("{}", Jsonl.Write<JsonlNegativeZeroHalfRecord>().ToText([negZero], halfTypeInfo));
    }

    [Fact]
    public async Task ReadValidation_UnenforceableSetterOnlyRule_StrictRejectsWhileLenientSkipsUnenforceableAndAppliesOtherRulesAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var opts = new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        opts.MakeReadOnly(populateMissingResolver: true);
        var typeInfo = (JsonTypeInfo<JsonlSetterOnlyConverterAndTitleRecord>)opts.GetTypeInfo(typeof(JsonlSetterOnlyConverterAndTitleRecord));

        string validPayload = /*lang=json,strict*/ """{"SecretCode":"5","Title":"ok"}""";
        var strictEx = Assert.Throws<JsonlException>(() =>
            Jsonl.Read<JsonlSetterOnlyConverterAndTitleRecord>().FromText(validPayload, typeInfo).ToList());
        Assert.Equal(JsonlErrorCode.InvalidOptions, strictEx.ErrorCode);

        string invalidTitlePayload = /*lang=json,strict*/ """{"SecretCode":"5","Title":""}""";
        string mixedPayload = $"{invalidTitlePayload}\n{validPayload}";
        var lenientRows = Jsonl.Read<JsonlSetterOnlyConverterAndTitleRecord>()
            .WithValidationMode(ValidationMode.Lenient)
            .FromText(mixedPayload, typeInfo)
            .ToList();
        var single = Assert.Single(lenientRows);
        Assert.Equal("ok", single.Title);

        await using var ms = new MemoryStream(Encoding.UTF8.GetBytes(mixedPayload));
        var asyncLenientRows = new List<JsonlSetterOnlyConverterAndTitleRecord>();
        await foreach (var item in Jsonl.Read<JsonlSetterOnlyConverterAndTitleRecord>()
            .WithValidationMode(ValidationMode.Lenient)
            .FromStreamAsync(ms, typeInfo, leaveOpen: true, cancellationToken: ct))
        {
            asyncLenientRows.Add(item);
        }
        Assert.Equal("ok", Assert.Single(asyncLenientRows).Title);
    }

    [Fact]
    public void WriteValidation_PolymorphicFallbackToNearestAncestor_ValidatesAncestorContract()
    {
        var baseTypeInfo = JsonlFallbackPolymorphicContext.Default.JsonlFallbackBase;

        JsonlFallbackBase validLeaf = new JsonlFallbackLeaf { BaseCode = "ok", MidTag = "v", Extra = "x" };
        string json = Jsonl.Write<JsonlFallbackBase>().ToText([validLeaf], baseTypeInfo);
        Assert.Contains("\"MidTag\":\"v\"", json, StringComparison.Ordinal);
        Assert.Equal(json, Jsonl.Write<JsonlFallbackBase>().ToText([validLeaf]));

        JsonlFallbackBase invalidLeaf = new JsonlFallbackLeaf { BaseCode = "ok", MidTag = null, Extra = "x" };
        var aotEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlFallbackBase>().ToText([invalidLeaf], baseTypeInfo));
        Assert.Contains(aotEx.Errors, e => e.PropertyName == nameof(JsonlFallbackMid.MidTag));

        var reflEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlFallbackBase>().ToText([invalidLeaf]));
        Assert.Contains(reflEx.Errors, e => e.PropertyName == nameof(JsonlFallbackMid.MidTag));

        IJsonlFallbackRoot validInterfaceLeaf = new JsonlFallbackInterfaceLeaf { RootCode = "ok", MidTag = "v" };
        string ifaceJson = Jsonl.Write<IJsonlFallbackRoot>().ToText([validInterfaceLeaf]);
        Assert.Contains("\"MidTag\":\"v\"", ifaceJson, StringComparison.Ordinal);

        IJsonlFallbackRoot invalidInterfaceLeaf = new JsonlFallbackInterfaceLeaf { RootCode = "ok", MidTag = null };
        var ifaceEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Write<IJsonlFallbackRoot>().ToText([invalidInterfaceLeaf]));
        Assert.Contains(ifaceEx.Errors, e => e.PropertyName == nameof(IJsonlFallbackMid.MidTag));
    }

    [Fact]
    public void WriteValidation_NullableStructRecords_ValidateUnderlyingContractInStrictMode()
    {
        var plainContextTypeInfo = JsonlNullableStructMetadataContext.Default.NullableJsonlPlainStruct;
        var validatedContextTypeInfo = JsonlNullableStructMetadataContext.Default.NullableJsonlValidatedStruct;
        var serializationOnlyTypeInfo = JsonlNullableStructSerializationContext.Default.NullableJsonlValidatedStruct;

        JsonlPlainStruct?[] plainItems = [new JsonlPlainStruct { Score = 5 }, null];
        string plainRefl = Jsonl.Write<JsonlPlainStruct?>().ToText(plainItems);
        string plainAot = Jsonl.Write<JsonlPlainStruct?>().ToText(plainItems, plainContextTypeInfo);
        Assert.Equal(plainRefl, plainAot);
        Assert.Contains("\"Score\":5", plainRefl, StringComparison.Ordinal);

        JsonlValidatedStruct?[] validItems = [new JsonlValidatedStruct { Score = 5 }, null];
        string validRefl = Jsonl.Write<JsonlValidatedStruct?>().ToText(validItems);
        string validAot = Jsonl.Write<JsonlValidatedStruct?>().ToText(validItems, validatedContextTypeInfo);
        Assert.Equal(validRefl, validAot);
        Assert.Contains("\"Score\":5", validRefl, StringComparison.Ordinal);

        JsonlValidatedStruct?[] invalidItems = [new JsonlValidatedStruct { Score = 0 }];
        var reflEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlValidatedStruct?>().ToText(invalidItems));
        Assert.Contains(reflEx.Errors, e => e.PropertyName == nameof(JsonlValidatedStruct.Score));

        var aotEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlValidatedStruct?>().ToText(invalidItems, validatedContextTypeInfo));
        Assert.Contains(aotEx.Errors, e => e.PropertyName == nameof(JsonlValidatedStruct.Score));

        var serOnlyEx = Assert.Throws<JsonlException>(() =>
            Jsonl.Write<JsonlValidatedStruct?>().ToText(validItems, serializationOnlyTypeInfo));
        Assert.Equal(JsonlErrorCode.InvalidOptions, serOnlyEx.ErrorCode);
    }

    [Fact]
    public async Task Write_HeterogeneousUnserializableRecord_HonorsOnErrorWithoutMaskingValidationAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        Action unserializableDelegate = () => { };
        object?[] mixedWithDelegate = [1, unserializableDelegate, 2];
        var skipOptions = new JsonlWriteOptions
        {
            OnError = (_, _) => JsonlSerializeErrorAction.SkipRecord
        };

        string syncResult = Jsonl.Write<object?>()
            .OnError((_, _) => JsonlSerializeErrorAction.SkipRecord)
            .ToText(mixedWithDelegate);
        Assert.Equal(["1", "2"], syncResult.Split('\n', StringSplitOptions.RemoveEmptyEntries));

        await using var asyncOut = new MemoryStream();
        await Jsonl.WriteEnumerableToStreamAsync<object?>(
            asyncOut,
            mixedWithDelegate,
            skipOptions,
            leaveOpen: true,
            cancellationToken: ct);
        string asyncResult = Encoding.UTF8.GetString(asyncOut.ToArray());
        Assert.Equal(["1", "2"], asyncResult.Split('\n', StringSplitOptions.RemoveEmptyEntries));

        var throwEx = Assert.Throws<JsonlException>(() =>
            Jsonl.Write<object?>().ToText([unserializableDelegate]));
        Assert.Equal(JsonlErrorCode.SerializeError, throwEx.ErrorCode);

        await using var invalidStream = new MemoryStream();
        await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await using var writer = new JsonlStreamWriter(invalidStream, skipOptions, leaveOpen: true);
            await writer.WriteRecordAsync<object>(new JsonlPlainValidatedDto { Amount = -1, Name = "ok" }, ct);
        });
        Assert.Equal(0, invalidStream.Length);
    }

    [Fact]
    public async Task WriteValidation_OnSerializedCallbackCannotBypassValidatedSerializedValuesAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var bad = new JsonlOnSerializedMutatingRecord { Amount = -1 };
        Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlOnSerializedMutatingRecord>().ToText([bad]));
        Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlOnSerializedMutatingRecord>().ToText(
                [new JsonlOnSerializedMutatingRecord { Amount = -1 }],
                JsonlReview26Context.Default.JsonlOnSerializedMutatingRecord));

        await using var outStream = new MemoryStream();
        await Assert.ThrowsAsync<ValidationException>(async () =>
        {
            await using var writer = new JsonlStreamWriter(outStream, leaveOpen: true);
            await writer.WriteRecordAsync(new JsonlOnSerializedMutatingRecord { Amount = -1 }, ct);
        });
        Assert.Equal(0, outStream.Length);

        var genBad = new JsonlGeneratedOnSerializedMutatingRecord { Amount = -1 };
        Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlGeneratedOnSerializedMutatingRecord>().ToText([genBad]));

        var serializingBad = new JsonlOnSerializingMutatingRecord { Initial = 5, Target = -2 };
        Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlOnSerializingMutatingRecord>().ToText([serializingBad]));

        var serializingFixed = new JsonlOnSerializingMutatingRecord { Initial = -1, Target = 5 };
        string okJson = Jsonl.Write<JsonlOnSerializingMutatingRecord>().ToText([serializingFixed]);
        Assert.Contains("\"Amount\":5", okJson, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteValidation_SourceGeneratedWithAddedModifier_RespectsShouldSerializeFalse()
    {
        var baseContext = new JsonlReview26Context();
        var resolver = baseContext.WithAddedModifier(ti =>
        {
            if (ti.Type != typeof(JsonlGeneratedModifierValidatedRecord))
                return;

            foreach (JsonPropertyInfo prop in ti.Properties)
            {
                if (string.Equals(prop.Name, nameof(JsonlGeneratedModifierValidatedRecord.OmittedWhenInvalid), StringComparison.Ordinal))
                    prop.ShouldSerialize = (_, _) => false;
            }
        });
        var opts = new JsonSerializerOptions { TypeInfoResolver = resolver };
        opts.MakeReadOnly(populateMissingResolver: true);
        var typeInfo = (JsonTypeInfo<JsonlGeneratedModifierValidatedRecord>)opts.GetTypeInfo(typeof(JsonlGeneratedModifierValidatedRecord));

        var omittedInvalid = new JsonlGeneratedModifierValidatedRecord
        {
            RequiredName = "valid",
            OmittedWhenInvalid = -10
        };
        string json = Jsonl.Write<JsonlGeneratedModifierValidatedRecord>().ToText([omittedInvalid], typeInfo);
        Assert.DoesNotContain("OmittedWhenInvalid", json, StringComparison.Ordinal);

        var badRequired = new JsonlGeneratedModifierValidatedRecord
        {
            RequiredName = "",
            OmittedWhenInvalid = -10
        };
        var ex = Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlGeneratedModifierValidatedRecord>().ToText([badRequired], typeInfo));
        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(JsonlGeneratedModifierValidatedRecord.RequiredName));
    }

    [Fact]
    public void WriteValidation_CustomObjectConverter_KeepsDeclaredConverterContract()
    {
        var converterOpts = new JsonSerializerOptions
        {
            Converters = { new JsonlConstantObjectConverter() }
        };
        object[] values = [new JsonlPlainValidatedDto { Amount = -1, Name = "" }];
        string json = Jsonl.Write<object>().WithJsonOptions(converterOpts).ToText(values);
        Assert.Equal("\"constant\"", json);

        converterOpts.MakeReadOnly(populateMissingResolver: true);
        var objectTypeInfo = (JsonTypeInfo<object>)converterOpts.GetTypeInfo(typeof(object));
        Assert.Equal("\"constant\"", Jsonl.Write<object>().ToText(values, objectTypeInfo));
    }

    [Fact]
    public void WriteValidation_WhenWritingDefaultCustomStruct_SkipsDefaultAndValidatesNonDefault()
    {
        var reflDefault = new JsonlCustomStructDefaultContainer { Tag = default };
        Assert.Equal("{}", Jsonl.Write<JsonlCustomStructDefaultContainer>().ToText([reflDefault]));
        Assert.Equal("{}", Jsonl.Write<JsonlCustomStructDefaultContainer>().ToText(
            [reflDefault],
            JsonlReview26Context.Default.JsonlCustomStructDefaultContainer));

        var invalid = new JsonlCustomStructDefaultContainer { Tag = new JsonlCustomTagStruct { Value = "toolong" } };
        var reflEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlCustomStructDefaultContainer>().ToText([invalid]));
        Assert.Contains(reflEx.Errors, e => e.PropertyName == nameof(JsonlCustomStructDefaultContainer.Tag));

        var aotEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlCustomStructDefaultContainer>().ToText(
                [invalid],
                JsonlReview26Context.Default.JsonlCustomStructDefaultContainer));
        Assert.Contains(aotEx.Errors, e => e.PropertyName == nameof(JsonlCustomStructDefaultContainer.Tag));
    }

    [Fact]
    public void WriteValidation_FloatingPointTinyNegative_ValidatesInDeclaredFloatingPointType()
    {
        var tinyNegative = new JsonlTinyFloatingPointRecord { Value = -1e-50, SmallFloat = 0f };
        var reflEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlTinyFloatingPointRecord>().ToText([tinyNegative]));
        Assert.Contains(reflEx.Errors, e => e.PropertyName == nameof(JsonlTinyFloatingPointRecord.Value));

        var aotEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlTinyFloatingPointRecord>().ToText(
                [tinyNegative],
                JsonlReview27Context.Default.JsonlTinyFloatingPointRecord));
        Assert.Contains(aotEx.Errors, e => e.PropertyName == nameof(JsonlTinyFloatingPointRecord.Value));

        var tinyNegativeFloat = new JsonlTinyFloatingPointRecord { Value = 0.0, SmallFloat = -1e-35f };
        var reflFloatEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlTinyFloatingPointRecord>().ToText([tinyNegativeFloat]));
        Assert.Contains(reflFloatEx.Errors, e => e.PropertyName == nameof(JsonlTinyFloatingPointRecord.SmallFloat));

        var zeroValid = new JsonlTinyFloatingPointRecord { Value = 0.0, SmallFloat = 0f };
        string json = Jsonl.Write<JsonlTinyFloatingPointRecord>().ToText(
            [zeroValid],
            JsonlReview27Context.Default.JsonlTinyFloatingPointRecord);
        Assert.Contains("\"Value\":0", json, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteValidation_DeepNestingBeyondDefaultDepth_HonorsConfiguredMaxDepth()
    {
        JsonlDeepNode? head = null;
        for (int i = 0; i < 80; i++)
            head = new JsonlDeepNode { Next = head };

        var deepOpts = new JsonSerializerOptions { MaxDepth = 100 };
        var aotDeepCtx = new JsonlDeepContext(new JsonSerializerOptions { MaxDepth = 100 });

        var valid = new JsonlDeepValidatedContainer { Name = "ok", Deep = head };
        string reflJson = Jsonl.Write<JsonlDeepValidatedContainer>().WithJsonOptions(deepOpts).ToText([valid]);
        string aotJson = Jsonl.Write<JsonlDeepValidatedContainer>().ToText([valid], aotDeepCtx.JsonlDeepValidatedContainer);
        Assert.Equal(reflJson, aotJson);

        var invalid = new JsonlDeepValidatedContainer { Name = "", Deep = head };
        var reflEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlDeepValidatedContainer>().WithJsonOptions(deepOpts).ToText([invalid]));
        Assert.Contains(reflEx.Errors, e => e.PropertyName == nameof(JsonlDeepValidatedContainer.Name));

        var aotEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlDeepValidatedContainer>().ToText([invalid], aotDeepCtx.JsonlDeepValidatedContainer));
        Assert.Contains(aotEx.Errors, e => e.PropertyName == nameof(JsonlDeepValidatedContainer.Name));
    }

    [Fact]
    public void Generators_MultidimensionalByteAndFloatArrays_EmitNoInvalidGeneratedCode()
    {
        var ct = TestContext.Current.CancellationToken;
        const string SOURCE = """
            #nullable enable
            using HeroParser;

            namespace MultiDimArrayNs;

            [GenerateBinder]
            public sealed class MultiDimMatrixRecord
            {
                public byte[,]? ByteGrid { get; set; }
                public float[,]? FloatGrid { get; set; }
            }
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(SOURCE, cancellationToken: ct);
        var platformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
        var references = platformAssemblies.Split(Path.PathSeparator)
            .Where(path => Path.GetFileName(path) is var name &&
                (name.StartsWith("System.", StringComparison.Ordinal) ||
                 name is "netstandard.dll" or "System.Private.CoreLib.dll"))
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(Csv).Assembly.Location))
            .ToList();
        var compilation = CSharpCompilation.Create(
            "MultiDimArrayTestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create(new CsvRecordBinderGenerator(), new JsonlRecordBinderGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation updatedCompilation, out var generatorDiagnostics, ct);

        Assert.DoesNotContain(generatorDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(updatedCompilation.GetDiagnostics(ct), d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void WriteValidation_WriteAsStringDecimal_PreservesFullDecimalPrecision()
    {
        var overMax = new JsonlWriteAsStringDecimalRecord { Rate = 0.1000000000000000000000000001m };
        var reflEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlWriteAsStringDecimalRecord>().ToText([overMax]));
        Assert.Contains(reflEx.Errors, e => e.PropertyName == nameof(JsonlWriteAsStringDecimalRecord.Rate) && e.Rule == "Range");

        var aotEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlWriteAsStringDecimalRecord>().ToText(
                [overMax],
                JsonlReview28Context.Default.JsonlWriteAsStringDecimalRecord));
        Assert.Contains(aotEx.Errors, e => e.PropertyName == nameof(JsonlWriteAsStringDecimalRecord.Rate) && e.Rule == "Range");

        var exactMax = new JsonlWriteAsStringDecimalRecord { Rate = 0.1m };
        string json = Jsonl.Write<JsonlWriteAsStringDecimalRecord>().ToText(
            [exactMax],
            JsonlReview28Context.Default.JsonlWriteAsStringDecimalRecord);
        Assert.Equal(/*lang=json,strict*/ """{"Rate":"0.1"}""", json);
    }

    [Fact]
    public void DecimalRangeValidation_InexactBounds_MatchRuntimeOnReadAndWrite()
    {
        // Valid boundaries:
        // SmallPosMax (<= 6e-29): 0m valid, 1e-28m invalid
        // MidPosMin (>= 1.4e-28): 2e-28m valid, 1e-28m invalid
        // SmallNegMax (<= -6e-29): -1e-28m valid, 0m invalid
        // MidNegMin (>= -1.4e-28): -1e-28m valid, -2e-28m invalid
        var validRecord = new JsonlInexactDecimalBoundsRecord
        {
            SmallPosMax = 0m,
            MidPosMin = 2e-28m,
            SmallNegMax = -1e-28m,
            MidNegMin = -1e-28m
        };

        string validJson = Jsonl.Write<JsonlInexactDecimalBoundsRecord>().ToText([validRecord], JsonlReview29Context.Default.JsonlInexactDecimalBoundsRecord);
        var readBack = Jsonl.Read<JsonlInexactDecimalBoundsRecord>().FromText(validJson).ToList();
        Assert.Single(readBack);
        Assert.Equal(validRecord.SmallPosMax, readBack[0].SmallPosMax);
        Assert.Equal(validRecord.MidPosMin, readBack[0].MidPosMin);
        Assert.Equal(validRecord.SmallNegMax, readBack[0].SmallNegMax);
        Assert.Equal(validRecord.MidNegMin, readBack[0].MidNegMin);

        // Generated write validator and runtime reflection write validator must both reject tightened violations.
        var badCases = new (JsonlInexactDecimalBoundsRecord Record, string ExpectedProp)[]
        {
            (new() { SmallPosMax = 1e-28m, MidPosMin = 2e-28m, SmallNegMax = -1e-28m, MidNegMin = -1e-28m }, nameof(JsonlInexactDecimalBoundsRecord.SmallPosMax)),
            (new() { SmallPosMax = 0m, MidPosMin = 1e-28m, SmallNegMax = -1e-28m, MidNegMin = -1e-28m }, nameof(JsonlInexactDecimalBoundsRecord.MidPosMin)),
            (new() { SmallPosMax = 0m, MidPosMin = 2e-28m, SmallNegMax = 0m, MidNegMin = -1e-28m }, nameof(JsonlInexactDecimalBoundsRecord.SmallNegMax)),
            (new() { SmallPosMax = 0m, MidPosMin = 2e-28m, SmallNegMax = -1e-28m, MidNegMin = -2e-28m }, nameof(JsonlInexactDecimalBoundsRecord.MidNegMin)),
        };

        foreach (var (badRecord, expectedProp) in badCases)
        {
            var aotWriteEx = Assert.Throws<ValidationException>(() =>
                Jsonl.Write<JsonlInexactDecimalBoundsRecord>().ToText([badRecord], JsonlReview29Context.Default.JsonlInexactDecimalBoundsRecord));
            Assert.Contains(aotWriteEx.Errors, e => e.PropertyName == expectedProp && e.Rule == "Range");

            var reflWriteEx = Assert.Throws<ValidationException>(() =>
                Jsonl.Write<JsonlInexactDecimalBoundsRecord>().ToText([badRecord]));
            Assert.Contains(reflWriteEx.Errors, e => e.PropertyName == expectedProp && e.Rule == "Range");
        }

        string invalidJson = /*lang=json,strict*/ """{"SmallPosMax":0.0000000000000000000000000001,"MidPosMin":0.0000000000000000000000000001,"SmallNegMax":0,"MidNegMin":-0.0000000000000000000000000002}""";
        var readEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Read<JsonlInexactDecimalBoundsRecord>().FromText(invalidJson).ToList());
        Assert.Equal(4, readEx.Errors.Count);
        Assert.All(readEx.Errors, e => Assert.Equal("Range", e.Rule));
    }

    [Fact]
    public void WriteValidation_NotNullByteArray_AcceptsEmptyArrayAndRejectsNull()
    {
        var emptyRecord = new JsonlNotNullByteArrayRecord { Payload = [] };

        string reflJson = Jsonl.Write<JsonlNotNullByteArrayRecord>().ToText([emptyRecord]);
        Assert.Equal(/*lang=json,strict*/ """{"Payload":""}""", reflJson);

        string aotJson = Jsonl.Write<JsonlNotNullByteArrayRecord>().ToText([emptyRecord], JsonlReview30Context.Default.JsonlNotNullByteArrayRecord);
        Assert.Equal(/*lang=json,strict*/ """{"Payload":""}""", aotJson);

        var readBack = Jsonl.Read<JsonlNotNullByteArrayRecord>().FromText(/*lang=json,strict*/ """{"Payload":""}""").ToList();
        Assert.Single(readBack);
        Assert.Empty(Assert.IsType<byte[]>(readBack[0].Payload));

        var nullRecord = new JsonlNotNullByteArrayRecord { Payload = null };
        var reflWriteEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlNotNullByteArrayRecord>().ToText([nullRecord]));
        Assert.Contains(reflWriteEx.Errors, e => e.PropertyName == nameof(JsonlNotNullByteArrayRecord.Payload) && e.Rule == "NotNull");

        var aotWriteEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Write<JsonlNotNullByteArrayRecord>().ToText([nullRecord], JsonlReview30Context.Default.JsonlNotNullByteArrayRecord));
        Assert.Contains(aotWriteEx.Errors, e => e.PropertyName == nameof(JsonlNotNullByteArrayRecord.Payload) && e.Rule == "NotNull");

        var readNullEx = Assert.Throws<ValidationException>(() =>
            Jsonl.Read<JsonlNotNullByteArrayRecord>().FromText(/*lang=json,strict*/ """{"Payload":null}""").ToList());
        Assert.Contains(readNullEx.Errors, e => e.PropertyName == nameof(JsonlNotNullByteArrayRecord.Payload) && e.Rule == "NotNull");
    }

    [Fact]
    public void JsonlRecord_PropertyTypeWithJsonConverterAndISpanParsable_HonorsConverterOnReadAndWrite()
    {
        var input = new JsonlConverterMoneyRecord
        {
            Price = new JsonlConverterMoney(42),
            OptionalPrice = new JsonlConverterMoney(7)
        };

        string written = Jsonl.Write<JsonlConverterMoneyRecord>().ToText([input], JsonlReview31Context.Default.JsonlConverterMoneyRecord);
        Assert.Equal(/*lang=json,strict*/ """{"Price":"USD 42","OptionalPrice":"USD 7"}""", written);

        var readBack = Jsonl.Read<JsonlConverterMoneyRecord>().FromText(written).ToList();
        Assert.Single(readBack);
        Assert.Equal(42, readBack[0].Price.Amount);
        Assert.Equal(7, readBack[0].OptionalPrice?.Amount);
    }

    [Fact]
    public void JsonlGeneratedBinder_UnannotatedByteArrayUnderNullableDisable_AcceptsJsonNull()
    {
        var records = Jsonl.Read<JsonlUnannotatedNullArrayRecord>()
            .FromText(/*lang=json,strict*/ """{"Payload":null,"Link":null,"Note":null}""")
            .ToList();

        Assert.Single(records);
        Assert.Null(records[0].Payload);
        Assert.Null(records[0].Link);
        Assert.Null(records[0].Note);
    }
}

public enum JsonlLongEnum : long
{
    None = 0,
    Large = 4_294_967_296L
}

public enum JsonlUlongEnum : ulong
{
    None = 0,
    Max = ulong.MaxValue
}

public enum JsonlUIntEnum : uint
{
    None = 0,
    High = 3_000_000_000u
}

public enum JsonlByteEnum : byte
{
    None = 0,
    High = 200
}

#if NET10_0_OR_GREATER
[GenerateBinder]
public sealed class JsonlWhenReadingIgnoredRecord
{
    public int Id { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenReading)]
    public string Secret { get; set; } = "initial";
}
#endif

[GenerateBinder]
public sealed class JsonlLargeEnumRecord
{
    public JsonlLongEnum BigSigned { get; set; }
    public JsonlUlongEnum BigUnsigned { get; set; }
    public JsonlUIntEnum UIntBacked { get; set; }
    public JsonlByteEnum ByteBacked { get; set; }
}

[GenerateBinder]
public sealed class JsonlGeneratedAlwaysIgnoredValidatedRecord
{
    public int Id { get; set; }

    [JsonIgnore]
    [Validate(NotNull = true)]
    public string? Ignored { get; set; }
}

public sealed class JsonlPlainValidatedDto
{
    [Validate(RangeMin = 10)]
    public int Amount { get; set; }

    [Validate(NotEmpty = true)]
    public string? Name { get; set; }
}

[JsonSerializable(typeof(JsonlGeneratedAlwaysIgnoredValidatedRecord))]
[JsonSerializable(typeof(JsonlPlainValidatedDto))]
internal sealed partial class JsonlReview15Context : JsonSerializerContext;

public sealed class JsonlReflectionContractDto
{
    [JsonIgnore]
    [Validate(NotNull = true)]
    public string? Ignored { get; set; }

    [JsonInclude]
    [Validate(NotEmpty = true)]
    public string Code = "";
}

public sealed class JsonlFloatArrayLengthDto
{
    [Validate(NotNull = true, MinLength = 2, MaxLength = 3)]
    public float[]? Embedding { get; set; }
}

[JsonSerializable(typeof(JsonlFloatArrayLengthDto))]
internal sealed partial class JsonlReview16Context : JsonSerializerContext;

[JsonConverter(typeof(JsonlCustomWidgetConverter))]
public readonly record struct JsonlCustomConverterDto(int Value);

public sealed class JsonlCustomWidgetConverter : JsonConverter<JsonlCustomConverterDto>
{
    public override JsonlCustomConverterDto Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => new(int.Parse(reader.GetString()!, CultureInfo.InvariantCulture));

    public override void Write(Utf8JsonWriter writer, JsonlCustomConverterDto value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value.ToString(CultureInfo.InvariantCulture));
}

[GenerateBinder]
public sealed class JsonlPartialContractRecord
{
    public int Id { get; set; }

    [Validate(NotNull = true)]
    public string? UnserializedField = null;

    [Validate(RangeMin = 10)]
    public int Computed => Id;
}

[JsonSerializable(typeof(JsonlCustomConverterDto))]
[JsonSerializable(typeof(JsonlPartialContractRecord))]
internal sealed partial class JsonlReview17Context : JsonSerializerContext;

[GenerateBinder]
public sealed class JsonlCustomPrunedRecord
{
    public int Id { get; set; }

    [Validate(NotNull = true)]
    public string? Optional { get; set; }
}

[GenerateBinder]
public sealed class JsonlReadOnlyValidatedRecord
{
    public int Id { get; set; }

    [Validate(NotNull = true)]
    public string? ComputedLabel => null;
}

public sealed class JsonlPlainReadOnlyValidatedRecord
{
    public int Id { get; set; }

    [Validate(NotNull = true)]
    public string? ComputedLabel => null;
}

[JsonSerializable(typeof(JsonlPlainReadOnlyValidatedRecord))]
internal sealed partial class JsonlIgnoreReadOnlyContext : JsonSerializerContext;

[GenerateBinder]
public sealed class JsonlPrivateGetterValidatedRecord
{
    [Validate(NotNull = true)]
    public string? Secret { private get; set; }

    [Validate(RangeMin = 10)]
    public int Score { private get; set; }

    public bool Matches(string? secret, int score) => Secret == secret && Score == score;
}

[GenerateBinder]
public sealed class JsonlConditionalIgnoreValidatedRecord
{
    public int Id { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [Validate(RangeMin = 10)]
    public int Score { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [Validate(NotNull = true)]
    public string? Note { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [Validate(RangeMin = 1)]
    public int? Amount { get; set; }
}

[JsonSerializable(typeof(JsonlConditionalIgnoreValidatedRecord))]
internal sealed partial class JsonlReview19Context : JsonSerializerContext;

public sealed class JsonlComputedAndPrivateSetRequiredRecord
{
    public int Id { get; set; }

    [Validate(NotNull = true)]
    public int Computed => Id + 1;

    [Validate(NotNull = true)]
    public int ReadOnlyProp { get; private set; }

    [JsonIgnore]
    [Validate(NotNull = true)]
    public int Ignored { get; set; }
}

public sealed record JsonlCtorRequiredIntRecord([property: Validate(NotNull = true)] int Id);

[JsonSerializable(typeof(JsonlComputedAndPrivateSetRequiredRecord))]
[JsonSerializable(typeof(JsonlCtorRequiredIntRecord))]
internal sealed partial class JsonlReview14Context : JsonSerializerContext;

[GenerateBinder]
public sealed class JsonlRequiredIntRecord
{
    [Validate(NotNull = true)]
    public int Id { get; set; }
}

public sealed class JsonlIgnoredValidatedRecord
{
    public int Id { get; set; }

    [JsonIgnore]
    [Validate(NotNull = true)]
    public string? Hidden { get; set; }
}

[GenerateBinder]
public sealed class JsonlUriRecord
{
    public Uri? Site { get; set; }
}

[JsonSerializable(typeof(JsonlRequiredIntRecord))]
[JsonSerializable(typeof(JsonlIgnoredValidatedRecord))]
internal sealed partial class JsonlReview13Context : JsonSerializerContext;

public sealed class HandleNullSentinelRecord
{
    public string Label { get; set; } = "";
}

public sealed class HandleNullSentinelConverter : JsonConverter<HandleNullSentinelRecord>
{
    public override bool HandleNull => true;

    public override HandleNullSentinelRecord Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return new HandleNullSentinelRecord { Label = "from-null-converter" };

        using var doc = JsonDocument.ParseValue(ref reader);
        string label = doc.RootElement.TryGetProperty("Label", out var prop) ? prop.GetString() ?? "" : "";
        return new HandleNullSentinelRecord { Label = label };
    }

    public override void Write(Utf8JsonWriter writer, HandleNullSentinelRecord value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("Label", value.Label);
        writer.WriteEndObject();
    }
}

public sealed class DisposableEmptyJsonlRecord(Action? onDispose = null) : IDisposable
{
    public void Dispose() => onDispose?.Invoke();
}

public class JsonlContractBaseRecord
{
    public int BaseId { get; set; }
}

[GenerateBinder]
public sealed class JsonlContractInheritedAndIgnoredRecord : JsonlContractBaseRecord
{
    [JsonIgnore]
    public int Ignored { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Optional { get; set; }

    [Validate(NotEmpty = true)]
    public string Name { get; set; } = "";
}

[GenerateBinder]
public sealed class JsonlKebabCaseTestRecord
{
    public int ValueId { get; set; }

    [Validate(NotEmpty = true)]
    public string DisplayName { get; set; } = "";
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.KebabCaseLower)]
[JsonSerializable(typeof(JsonlKebabCaseTestRecord))]
internal sealed partial class JsonlKebabCaseTestContext : JsonSerializerContext;

[GenerateBinder]
public sealed class JsonlFastPathBinderValidatedRecord
{
    [Validate(NotEmpty = true)]
    public string Name { get; set; } = "";

    [Validate(RangeMin = 1, RangeMax = 100)]
    public int Score { get; set; }
}

public sealed class JsonlTypeInfoOnlyValidatedRecord
{
    [Validate(NotEmpty = true)]
    public string Name { get; set; } = "";

    [Validate(RangeMin = 1, RangeMax = 100)]
    public int Score { get; set; }
}

[GenerateBinder]
public sealed class JsonlFastPathBinderUnvalidatedRecord
{
    public int Value { get; set; }
}

[GenerateBinder]
public sealed class JsonlEmptyJsonNameRecord
{
    [JsonPropertyName("")]
    public int EmptyKeyValue { get; set; }

    [JsonPropertyName("  ")]
    public string? BlankKeyValue { get; set; }
}

[GenerateBinder]
public sealed class JsonlCallbackValidatedRecord : IJsonOnDeserializing, IJsonOnDeserialized
{
    [Validate(NotEmpty = true)]
    public string Name { get; set; } = "";

    public bool SawDeserializing { get; set; }
    public bool SawDeserialized { get; set; }

    public void OnDeserializing() => SawDeserializing = true;
    public void OnDeserialized() => SawDeserialized = true;
}

[GenerateBinder]
public sealed class JsonlPublicIncludeFieldRecord
{
    public int Id { get; set; }

    [JsonInclude]
    [Validate(NotEmpty = true)]
    public string PublicCode = "";

    [JsonInclude]
    [Validate(RangeMin = 1, RangeMax = 100)]
    internal int InternalScore;
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Serialization)]
[JsonSerializable(typeof(JsonlPublicIncludeFieldRecord))]
internal sealed partial class JsonlIncludeFieldsSerializationContext : JsonSerializerContext;

public sealed record JsonlParameterizedSerializationRecord([property: Validate(NotEmpty = true)] string Name);

public struct JsonlEmptyMetadataStruct;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public struct JsonlStrictEmptyMetadataStruct;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(JsonlEmptyMetadataStruct))]
[JsonSerializable(typeof(JsonlStrictEmptyMetadataStruct))]
internal sealed partial class JsonlMetadataEmptyContext : JsonSerializerContext;

[JsonSerializable(typeof(JsonlTypeInfoOnlyValidatedRecord))]
internal sealed partial class JsonlTypeInfoOnlyContext : JsonSerializerContext;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Serialization)]
[JsonSerializable(typeof(JsonlTypeInfoOnlyValidatedRecord))]
[JsonSerializable(typeof(JsonlFastPathBinderValidatedRecord))]
[JsonSerializable(typeof(JsonlFastPathBinderUnvalidatedRecord))]
[JsonSerializable(typeof(JsonlParameterizedSerializationRecord))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(ReadOnlyMemory<byte>))]
[JsonSerializable(typeof(Memory<byte>))]
internal sealed partial class JsonlFastPathSerializationContext : JsonSerializerContext;

[GenerateBinder]
public sealed class JsonlTabularCollisionRecord
{
    [TabularMap(Name = "Y")]
    public int X { get; set; }

    public int Y { get; set; }
}

[GenerateBinder]
public sealed class JsonlTabularReadOnlyCollisionRecord
{
    [TabularMap(Name = "Y")]
    public int X { get; set; }

    public int Y => 99;
}

public sealed class JsonlMetadataShouldSerializeRecord
{
    [Validate(RangeMin = 1)]
    public int Score { get; set; }
}

public class JsonlBaseVirtualValidatedRecord
{
    [Validate(RangeMin = 1)]
    public virtual int Count { get; set; }
}

public sealed class JsonlDerivedOverrideValidatedRecord : JsonlBaseVirtualValidatedRecord
{
    public override int Count { get; set; }
}

public sealed class JsonlMetadataSetterOnlyValidatedRecord
{
    private int quantity;

    [Validate(RangeMin = 0)]
    public int Quantity { set => quantity = value; }

    public int ReadQuantity() => quantity;
}

public sealed class JsonlMetadataPrivateGetterValidatedRecord
{
    [Validate(RangeMin = 0)]
    public int Score { private get; set; }

    public int ReadScore() => Score;
}

[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
public sealed class JsonlSetterOnlyStringNumberRecord
{
    private int amount;

    [Validate(RangeMin = 1)]
    public int Amount { set => amount = value; }

    public int ReadAmount() => amount;
}

[JsonSerializable(typeof(JsonlSetterOnlyStringNumberRecord))]
internal sealed partial class JsonlSetterOnlyNumberHandlingContext : JsonSerializerContext;

[JsonDerivedType(typeof(JsonlPolymorphicDerived), "derived")]
public class JsonlPolymorphicBase
{
    [Validate(NotEmpty = true)]
    public string BaseCode { get; set; } = "";
}

public sealed class JsonlPolymorphicDerived : JsonlPolymorphicBase
{
    [Validate(NotNull = true)]
    public string? Detail { get; set; }
}

[JsonSerializable(typeof(JsonlPolymorphicBase))]
internal sealed partial class JsonlPolymorphicContext : JsonSerializerContext;

public sealed class JsonlReadOnlyCollectionAndNeverRecord(float[] scores, string? alwaysWritten)
{
    [Validate(MinLength = 2)]
    public float[] Scores { get; } = scores;

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    [Validate(NotNull = true)]
    public string? AlwaysWritten { get; } = alwaysWritten;

    [Validate(NotNull = true)]
    public string? IgnoredScalar => null;
}

public sealed class JsonlNegativeZeroHalfRecord
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [Validate(RangeMin = 1)]
    public Half Metric { get; set; }
}

[JsonSerializable(typeof(JsonlNegativeZeroHalfRecord))]
internal sealed partial class JsonlNegativeZeroHalfContext : JsonSerializerContext;

public sealed class JsonlStringToIntConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.String
            ? int.Parse(reader.GetString()!, CultureInfo.InvariantCulture)
            : reader.GetInt32();

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);
}

public sealed class JsonlSetterOnlyConverterAndTitleRecord
{
    private int secretCode;

    [JsonConverter(typeof(JsonlStringToIntConverter))]
    [Validate(RangeMin = 1)]
    public int SecretCode { set => secretCode = value; }

    [Validate(NotEmpty = true)]
    public string Title { get; set; } = "";

    public int ReadSecretCode() => secretCode;
}

[JsonPolymorphic(UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor)]
[JsonDerivedType(typeof(JsonlFallbackMid), "mid")]
public class JsonlFallbackBase
{
    [Validate(NotEmpty = true)]
    public string BaseCode { get; set; } = "";
}

public class JsonlFallbackMid : JsonlFallbackBase
{
    [Validate(NotNull = true)]
    public string? MidTag { get; set; }
}

public sealed class JsonlFallbackLeaf : JsonlFallbackMid
{
    public string? Extra { get; set; }
}

[JsonSerializable(typeof(JsonlFallbackBase))]
internal sealed partial class JsonlFallbackPolymorphicContext : JsonSerializerContext;

[JsonPolymorphic(UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor)]
[JsonDerivedType(typeof(IJsonlFallbackMid), "imid")]
public interface IJsonlFallbackRoot
{
    [Validate(NotEmpty = true)]
    string RootCode { get; }
}

public interface IJsonlFallbackMid : IJsonlFallbackRoot
{
    [Validate(NotNull = true)]
    string? MidTag { get; }
}

public sealed class JsonlFallbackInterfaceLeaf : IJsonlFallbackMid
{
    public string RootCode { get; set; } = "";
    public string? MidTag { get; set; }
}

public struct JsonlPlainStruct
{
    public int Score { get; set; }
}

public struct JsonlValidatedStruct
{
    [Validate(RangeMin = 1)]
    public int Score { get; set; }
}

[JsonSerializable(typeof(JsonlPlainStruct?))]
[JsonSerializable(typeof(JsonlValidatedStruct?))]
internal sealed partial class JsonlNullableStructMetadataContext : JsonSerializerContext;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Serialization)]
[JsonSerializable(typeof(JsonlValidatedStruct?))]
internal sealed partial class JsonlNullableStructSerializationContext : JsonSerializerContext;

public sealed class JsonlOnSerializedMutatingRecord : IJsonOnSerialized
{
    [Validate(RangeMin = 0)]
    public int Amount { get; set; }

    public void OnSerialized() => Amount = 1;
}

[GenerateBinder]
public sealed class JsonlGeneratedOnSerializedMutatingRecord : IJsonOnSerialized
{
    [Validate(RangeMin = 0)]
    public int Amount { get; set; }

    public void OnSerialized() => Amount = 1;
}

public sealed class JsonlOnSerializingMutatingRecord : IJsonOnSerializing
{
    [JsonIgnore]
    public int Initial { get; set; }

    [JsonIgnore]
    public int Target { get; set; }

    [Validate(RangeMin = 0)]
    public int Amount { get; set; }

    public void OnSerializing() => Amount = Target;
}

[GenerateBinder]
public sealed class JsonlGeneratedModifierValidatedRecord
{
    [Validate(NotEmpty = true)]
    public string RequiredName { get; set; } = "";

    [Validate(RangeMin = 0)]
    public int OmittedWhenInvalid { get; set; }
}

public sealed class JsonlConstantObjectConverter : JsonConverter<object>
{
    public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetString();

    public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        => writer.WriteStringValue("constant");
}

public struct JsonlCustomTagStruct
{
    public string? Value { get; set; }
    public override readonly string ToString() => Value ?? "default-tag";
}

public sealed class JsonlCustomStructDefaultContainer
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [Validate(MaxLength = 2)]
    public JsonlCustomTagStruct Tag { get; set; }
}

[JsonSerializable(typeof(JsonlOnSerializedMutatingRecord))]
[JsonSerializable(typeof(JsonlGeneratedModifierValidatedRecord))]
[JsonSerializable(typeof(JsonlCustomStructDefaultContainer))]
internal sealed partial class JsonlReview26Context : JsonSerializerContext;

public sealed class JsonlTinyFloatingPointRecord
{
    [Validate(RangeMin = 0)]
    public double Value { get; set; }

    [Validate(RangeMin = 0)]
    public float SmallFloat { get; set; }
}

public sealed class JsonlDeepNode
{
    public JsonlDeepNode? Next { get; set; }
}

public sealed class JsonlDeepValidatedContainer
{
    [Validate(NotEmpty = true)]
    public string Name { get; set; } = "";

    public JsonlDeepNode? Deep { get; set; }
}

[JsonSerializable(typeof(JsonlTinyFloatingPointRecord))]
internal sealed partial class JsonlReview27Context : JsonSerializerContext;

[JsonSerializable(typeof(JsonlDeepValidatedContainer))]
internal sealed partial class JsonlDeepContext : JsonSerializerContext;

public sealed class JsonlWriteAsStringDecimalRecord
{
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    [Validate(RangeMax = 0.1)]
    public decimal Rate { get; set; }
}

[JsonSerializable(typeof(JsonlWriteAsStringDecimalRecord))]
internal sealed partial class JsonlReview28Context : JsonSerializerContext;

[GenerateBinder]
public sealed class JsonlInexactDecimalBoundsRecord
{
    [Validate(RangeMax = 6e-29)]
    public decimal SmallPosMax { get; set; }

    [Validate(RangeMin = 1.4e-28)]
    public decimal MidPosMin { get; set; }

    [Validate(RangeMax = -6e-29)]
    public decimal SmallNegMax { get; set; }

    [Validate(RangeMin = -1.4e-28)]
    public decimal MidNegMin { get; set; }
}

[JsonSerializable(typeof(JsonlInexactDecimalBoundsRecord))]
internal sealed partial class JsonlReview29Context : JsonSerializerContext;

[GenerateBinder]
public sealed class JsonlNotNullByteArrayRecord
{
    [Validate(NotNull = true)]
    public byte[]? Payload { get; set; }
}

[JsonSerializable(typeof(JsonlNotNullByteArrayRecord))]
internal sealed partial class JsonlReview30Context : JsonSerializerContext;

[JsonConverter(typeof(JsonlConverterMoneyConverter))]
public readonly record struct JsonlConverterMoney(int Amount) : ISpanParsable<JsonlConverterMoney>
{
    public static JsonlConverterMoney Parse(string s, IFormatProvider? provider) => Parse(s.AsSpan(), provider);

    public static bool TryParse(string? s, IFormatProvider? provider, out JsonlConverterMoney result)
        => TryParse(s.AsSpan(), provider, out result);

    public static JsonlConverterMoney Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => TryParse(s, provider, out var result) ? result : throw new FormatException("Invalid money.");

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out JsonlConverterMoney result)
    {
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount))
        {
            result = new JsonlConverterMoney(amount);
            return true;
        }
        result = default;
        return false;
    }
}

public sealed class JsonlConverterMoneyConverter : JsonConverter<JsonlConverterMoney>
{
    public override JsonlConverterMoney Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? raw = reader.GetString();
        if (raw is null || !raw.StartsWith("USD ", StringComparison.Ordinal))
            throw new JsonException("Expected 'USD <amount>'.");
        return new JsonlConverterMoney(int.Parse(raw.AsSpan(4), CultureInfo.InvariantCulture));
    }

    public override void Write(Utf8JsonWriter writer, JsonlConverterMoney value, JsonSerializerOptions options)
        => writer.WriteStringValue($"USD {value.Amount}");
}

[GenerateBinder]
public sealed class JsonlConverterMoneyRecord
{
    public JsonlConverterMoney Price { get; set; }
    public JsonlConverterMoney? OptionalPrice { get; set; }
}

[JsonSerializable(typeof(JsonlConverterMoneyRecord))]
internal sealed partial class JsonlReview31Context : JsonSerializerContext;

#nullable disable
[GenerateBinder]
public sealed class JsonlUnannotatedNullArrayRecord
{
    public byte[] Payload { get; set; }
    public Uri Link { get; set; }
    public string Note { get; set; }
}
#nullable restore
