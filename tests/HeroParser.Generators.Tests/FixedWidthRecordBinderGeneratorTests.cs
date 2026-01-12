using System.Collections.Immutable;
using HeroParser.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

#pragma warning disable xUnit1051 // CancellationToken not needed for synchronous generator tests

namespace HeroParser.Generators.Tests;

/// <summary>
/// Unit tests for the FixedWidthRecordBinderGenerator source generator.
/// </summary>
public class FixedWidthRecordBinderGeneratorTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_WithStruct_GeneratesDescriptorAndWriter()
    {
        var source = """
            using HeroParser.FixedWidths;
            using HeroParser.FixedWidths.Records.Binding;

            namespace TestNamespace;

            [FixedWidthGenerateBinder]
            public struct PersonStruct
            {
                [FixedWidthColumn(Start = 0, Length = 5)]
                public int Id { get; set; }

                [FixedWidthColumn(Start = 5, Length = 10)]
                public string? Name { get; set; }
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        // Generator produces: 1 descriptor file + 1 registration file
        Assert.Equal(2, result.GeneratedSources.Length);

        var allGeneratedCode = string.Join("\n", result.GeneratedSources.Select(s => s.SourceText.ToString()));
        Assert.Contains("TestNamespace.PersonStruct", allGeneratedCode);
        Assert.Contains("ref global::TestNamespace.PersonStruct", allGeneratedCode);
        Assert.Contains("FixedWidthRecordBinderFactory", allGeneratedCode);
        Assert.Contains("FixedWidthRecordWriterFactory", allGeneratedCode);
    }

    #region Test Infrastructure

    private static GeneratorRunResult RunGenerator(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilation = CreateCompilation(syntaxTree);

        var generator = new FixedWidthRecordBinderGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);

        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out _,
            out _);

        var runResult = driver.GetRunResult();
        return runResult.Results.Single();
    }

    private static CSharpCompilation CreateCompilation(SyntaxTree syntaxTree)
    {
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(HeroParser.FixedWidth).Assembly.Location),
        };

        // Add runtime assemblies
        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")));
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Collections.dll")));

        return CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    #endregion
}
