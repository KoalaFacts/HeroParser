using HeroParser.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace HeroParser.Generators.Tests;

/// <summary>
/// Every <c>[GenerateBinder]</c> type is processed by the CSV, Fixed-Width and JSONL binder
/// generators together. A property type that no generator can emit conversion code for must be
/// reported with the generator's "unsupported property type" diagnostic and left out, never
/// emitted as code that breaks the user's build.
/// </summary>
[Trait("Category", "Unit")]
public class TabularOnlyTypeGeneratorTests
{
    [Fact]
    public void Generators_SupportedProperties_GeneratedCodeCompiles()
    {
        var (diagnostics, generated, errors) = Run("int", "0");

        Assert.Empty(errors);
        Assert.DoesNotContain(diagnostics, d => d.Id is "HERO001" or "HERO002" or "HERO101");
        Assert.Contains("Extra", generated);
    }

    [Theory]
    [InlineData("System.TimeZoneInfo", "System.TimeZoneInfo.Utc")]
    [InlineData("System.TimeZoneInfo?", "null")]
    [InlineData("System.ReadOnlyMemory<char>", "default")]
    [InlineData("System.ReadOnlyMemory<char>?", "null")]
    public void Generators_PropertyTypeWithoutConversion_ReportDiagnosticAndStillCompile(string type, string initializer)
    {
        var (diagnostics, generated, errors) = Run(type, initializer);

        Assert.Empty(errors);
        Assert.Contains(diagnostics, d => d.Id == "HERO001" && d.GetMessage().Contains("'Extra'"));
        Assert.Contains(diagnostics, d => d.Id == "HERO002" && d.GetMessage().Contains("'Extra'"));
        Assert.Contains(diagnostics, d => d.Id == "HERO101" && d.GetMessage().Contains("'Extra'"));
        Assert.Contains("Name", generated);
        Assert.DoesNotContain(".Extra =", generated);
    }

    private static (List<Diagnostic> Diagnostics, string Generated, List<Diagnostic> GeneratedCodeErrors) Run(string type, string initializer)
    {
        string source = $$"""
            using HeroParser;
            namespace TestNamespace;
            [GenerateBinder]
            public class Mixed
            {
                [PositionalMap(Start = 0, Length = 10)]
                public string? Name { get; set; }

                [PositionalMap(Start = 10, Length = 10)]
                public {{type}} Extra { get; set; } = {{initializer}};
            }
            """;
        var cancellationToken = TestContext.Current.CancellationToken;
        var syntaxTree = CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken);
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

        var driver = CSharpGeneratorDriver.Create(
                new CsvRecordBinderGenerator(),
                new FixedWidthRecordBinderGenerator(),
                new JsonlRecordBinderGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _, cancellationToken);

        var runResult = driver.GetRunResult();
        var errors = output.GetDiagnostics(cancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Location.SourceTree != syntaxTree)
            .ToList();
        string generated = string.Join("\n", runResult.GeneratedTrees.Select(t => t.GetText(cancellationToken).ToString()));
        return ([.. runResult.Diagnostics], generated, errors);
    }
}
