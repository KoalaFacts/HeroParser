using System.Globalization;
using HeroParser.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace HeroParser.Generators.Tests;

/// <summary>
/// Source generators run inside the compiler under the developer's (or build agent's) culture.
/// <c>[Validate(RangeMin/RangeMax)]</c> bounds are emitted as C# literals and JSON numbers, so they
/// must be formatted with the invariant culture: a decimal comma (de-DE) or a U+2212 minus sign
/// (sv-SE on ICU) would otherwise produce code that does not compile or a schema that is not JSON.
/// </summary>
[Trait("Category", "Unit")]
public class GeneratorCultureTests
{
    private const string SOURCE = """
        using HeroParser;

        namespace CultureTest;

        [GenerateBinder]
        public class Measurement
        {
            [TabularMap(Index = 0)]
            [PositionalMap(Start = 0, Length = 10)]
            [Validate(RangeMin = -1.5, RangeMax = 999.99)]
            public decimal Amount { get; set; }

            [TabularMap(Index = 1)]
            [PositionalMap(Start = 10, Length = 10)]
            [Validate(RangeMin = -0.25, RangeMax = 2.5)]
            public double Ratio { get; set; }

            [TabularMap(Index = 2)]
            [PositionalMap(Start = 20, Length = 5)]
            [Validate(RangeMin = -5, RangeMax = 50)]
            public int Count { get; set; }
        }
        """;

    public static TheoryData<string> Cultures => ["invariant", "comma-decimal-unicode-minus"];

    [Theory]
    [MemberData(nameof(Cultures))]
    public void BinderGenerators_RangeLiterals_CompileUnderAnyBuildCulture(string cultureName)
    {
        var (generated, errors) = RunUnderCulture(cultureName, SOURCE,
            new CsvRecordBinderGenerator(),
            new FixedWidthRecordBinderGenerator(),
            new HtbRecordBinderGenerator());

        Assert.Empty(errors);
        Assert.Contains("999.99m", generated, StringComparison.Ordinal);
        Assert.Contains("-1.5m", generated, StringComparison.Ordinal);
        Assert.Contains("-0.25", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("−", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("999,99", generated, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void LlmSchemaGenerator_RangeBounds_AreJsonNumbersUnderAnyBuildCulture(string cultureName)
    {
        var (generated, errors) = RunUnderCulture(cultureName, SOURCE, new LlmSchemaGenerator());

        Assert.Empty(errors);
        Assert.Contains("\\\"minimum\\\": -1.5", generated, StringComparison.Ordinal);
        Assert.Contains("\\\"maximum\\\": 999.99", generated, StringComparison.Ordinal);
        Assert.Contains("\\\"minimum\\\": -5", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("−", generated, StringComparison.Ordinal);
    }

    private static (string Generated, IReadOnlyList<Diagnostic> Errors) RunUnderCulture(
        string cultureName, string source, params IIncrementalGenerator[] generators)
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        if (cultureName == "comma-decimal-unicode-minus")
        {
            culture.NumberFormat.NumberDecimalSeparator = ",";
            culture.NumberFormat.NumberGroupSeparator = ".";
            culture.NumberFormat.NegativeSign = "−";
        }

        var compilation = CreateCompilation(source);
        var driver = CSharpGeneratorDriver.Create(generators);

        var original = CultureInfo.CurrentCulture;
        Compilation output;
        CultureInfo.CurrentCulture = culture;
        try
        {
            driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(
                compilation, out output, out _, TestContext.Current.CancellationToken);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        var runResult = driver.GetRunResult();
        Assert.All(runResult.Results, r => Assert.Null(r.Exception));
        var generated = string.Join("\n", runResult.Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => s.SourceText.ToString()));
        var errors = output.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        return (generated, errors);
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var trustedAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var references = new List<MetadataReference>();
        foreach (var path in trustedAssemblies)
            references.Add(MetadataReference.CreateFromFile(path));
        if (!trustedAssemblies.Contains(typeof(Csv).Assembly.Location, StringComparer.Ordinal))
            references.Add(MetadataReference.CreateFromFile(typeof(Csv).Assembly.Location));

        return CSharpCompilation.Create(
            "GeneratorCultureTest",
            [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }
}
