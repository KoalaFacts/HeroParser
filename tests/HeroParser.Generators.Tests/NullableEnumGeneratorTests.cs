using HeroParser.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace HeroParser.Generators.Tests;

/// <summary>
/// Runs the CSV, Fixed-Width and JSONL binder generators together on records with enum
/// properties and compiles their output, so a generated binder that does not build fails here.
/// </summary>
[Trait("Category", "Unit")]
public class NullableEnumGeneratorTests
{
    [Fact]
    public void Generators_NullableEnumProperty_GeneratedCodeCompiles()
    {
        var source = """
            using HeroParser;
            namespace ConsumerApp;
            public enum Status { Active, Inactive }
            [GenerateBinder]
            public class Item
            {
                [TabularMap(Name = "Id")]
                [PositionalMap(Start = 0, Length = 4)]
                public int Id { get; set; }

                [TabularMap(Name = "Status")]
                [PositionalMap(Start = 4, Length = 10)]
                public Status? Status { get; set; }
            }
            """;

        var errors = RunAllGeneratorsAndGetCompileErrors(source);

        Assert.Empty(errors);
    }

    [Fact]
    public void Generators_NonNullableEnumProperty_GeneratedCodeCompiles()
    {
        // Control: the compile check accepts the enum shape the generators already supported.
        var source = """
            using HeroParser;
            namespace ConsumerApp;
            public enum Status { Active, Inactive }
            [GenerateBinder]
            public class Item
            {
                [TabularMap(Name = "Id")]
                [PositionalMap(Start = 0, Length = 4)]
                public int Id { get; set; }

                [TabularMap(Name = "Status")]
                [PositionalMap(Start = 4, Length = 10)]
                public Status Status { get; set; }
            }
            """;

        var errors = RunAllGeneratorsAndGetCompileErrors(source);

        Assert.Empty(errors);
    }

    private static List<string> RunAllGeneratorsAndGetCompileErrors(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
            GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create(
            new CsvRecordBinderGenerator(),
            new FixedWidthRecordBinderGenerator(),
            new JsonlRecordBinderGenerator());

        driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics,
            TestContext.Current.CancellationToken);

        return [.. generatorDiagnostics
            .Concat(outputCompilation.GetDiagnostics(TestContext.Current.CancellationToken))
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Id} {d.Location.SourceTree?.FilePath}: {d.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}")];
    }

    private static List<MetadataReference> GetReferences()
    {
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        List<MetadataReference> references =
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Csv).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Text.Json.Utf8JsonReader).Assembly.Location),
        ];

        foreach (var name in new[] { "System.Runtime.dll", "System.Collections.dll", "System.Text.RegularExpressions.dll", "System.Memory.dll", "System.Text.Encodings.Web.dll" })
        {
            var path = Path.Combine(runtimeDir, name);
            if (File.Exists(path))
                references.Add(MetadataReference.CreateFromFile(path));
        }

        return references;
    }
}
