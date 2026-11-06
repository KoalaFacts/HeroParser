using HeroParser.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace HeroParser.Generators.Tests;

/// <summary>
/// Unit tests for the JSONL record binder generator. Every <c>[GenerateBinder]</c> type is also
/// processed by this generator, so a property type that only CSV/Fixed-Width support must not
/// make the generated JSONL binder fail to compile.
/// </summary>
[Trait("Category", "Unit")]
public class JsonlRecordBinderGeneratorTests
{
    [Fact]
    public void Generator_SupportedProperties_GeneratedCodeCompiles()
    {
        var (result, errors) = Run("""
            using HeroParser;
            namespace TestNamespace;
            [GenerateBinder]
            public class Plain
            {
                public string? Name { get; set; }
                public int Age { get; set; }
            }
            """);

        Assert.NotEmpty(result.GeneratedSources);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("System.TimeZoneInfo", "System.TimeZoneInfo.Utc")]
    [InlineData("System.ReadOnlyMemory<char>", "default")]
    public void Generator_TabularOnlyPropertyType_ReportsHero101AndStillCompiles(string type, string initializer)
    {
        var (result, errors) = Run($$"""
            using HeroParser;
            namespace TestNamespace;
            [GenerateBinder]
            public class Mixed
            {
                public string? Name { get; set; }
                public {{type}} Extra { get; set; } = {{initializer}};
            }
            """);

        Assert.Empty(errors);
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Id == "HERO101");
        Assert.Contains("'Extra'", diagnostic.GetMessage());
        string generated = string.Join("\n", result.GeneratedSources.Select(s => s.SourceText.ToString()));
        Assert.Contains("record.Name", generated);
        Assert.DoesNotContain("record.Extra", generated);
    }

    private static (GeneratorRunResult Result, List<Diagnostic> GeneratedCodeErrors) Run(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken);
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = Directory.GetFiles(runtimeDir, "*.dll")
            .Where(path => Path.GetFileName(path) is var name && (name.StartsWith("System.", StringComparison.Ordinal) || name == "netstandard.dll"))
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(Csv).Assembly.Location))
            .ToList();
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create(new JsonlRecordBinderGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _, TestContext.Current.CancellationToken);

        var errors = output.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Location.SourceTree != syntaxTree)
            .ToList();
        return (driver.GetRunResult().Results.Single(), errors);
    }
}
