using System.Globalization;
using HeroParser.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace HeroParser.Generators.Tests;

/// <summary>
/// The dispatcher generator runs inside the compiler, under the developer's culture. Integer
/// <c>[CsvSchemaMapping]</c> discriminators must be emitted as invariant ASCII so a negative value
/// matches "-1" in the data under cultures whose negative sign is U+2212 (for example sv-SE on ICU).
/// </summary>
[Trait("Category", "Unit")]
public class CsvMultiSchemaDispatcherGeneratorCultureTests
{
    [Fact]
    public void Generator_NegativeIntegerMapping_EmitsAsciiMinusUnderUnicodeMinusCulture()
    {
        const string source = """
            using HeroParser;
            using HeroParser.SeparatedValues.Reading.Shared;

            namespace TestNamespace;

            [GenerateBinder]
            public class HeaderRecord
            {
                public string Name { get; set; } = "";
            }

            [CsvGenerateDispatcher(DiscriminatorIndex = 0)]
            [CsvSchemaMapping(-1, typeof(HeaderRecord))]
            public partial class Dispatcher
            {
            }
            """;

        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NegativeSign = "−";

        var original = CultureInfo.CurrentCulture;
        string generated;
        CultureInfo.CurrentCulture = culture;
        try
        {
            generated = RunDispatcherGenerator(source);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        Assert.Contains("\"-1\"u8", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("−", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Generator_ColumnNameDispatcher_CompilesUnderUnicodeMinusCulture()
    {
        // DiscriminatorIndex defaults to -1 when the dispatcher maps by column name; the emitted
        // field initializer must use an ASCII minus or the generated file does not compile.
        const string source = """
            using HeroParser;
            using HeroParser.SeparatedValues.Reading.Shared;

            namespace TestNamespace;

            [GenerateBinder]
            public class HeaderRecord
            {
                public string Name { get; set; } = "";
            }

            [CsvGenerateDispatcher(DiscriminatorColumn = "Type")]
            [CsvSchemaMapping("H", typeof(HeaderRecord))]
            public partial class Dispatcher
            {
            }
            """;

        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NegativeSign = "−";

        var original = CultureInfo.CurrentCulture;
        string generated;
        CultureInfo.CurrentCulture = culture;
        try
        {
            generated = RunDispatcherGenerator(source);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        Assert.Contains("_discriminatorIndex = -1;", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("−", generated, StringComparison.Ordinal);
    }

    private static string RunDispatcherGenerator(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Csv).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")),
        };
        var compilation = CSharpCompilation.Create(
            "DispatcherCultureTest",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new CsvMultiSchemaDispatcherGenerator());
        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation, TestContext.Current.CancellationToken);

        var result = driver.GetRunResult().Results.Single();
        Assert.Null(result.Exception);
        var dispatcher = Assert.Single(result.GeneratedSources, s => s.HintName.StartsWith("CsvMultiSchemaDispatcher.", StringComparison.Ordinal));
        return dispatcher.SourceText.ToString();
    }
}
