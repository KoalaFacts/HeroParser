using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using HeroParser.Generators;
using HeroParser.JsonLines;
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
}
