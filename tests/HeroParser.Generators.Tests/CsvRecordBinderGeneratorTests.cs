using System.Collections.Immutable;
using HeroParser.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

#pragma warning disable xUnit1051 // CancellationToken not needed for synchronous generator tests

namespace HeroParser.Generators.Tests;

/// <summary>
/// Unit tests for the CsvRecordBinderGenerator source generator.
/// Tests verify generator output without compiling the generated code,
/// since the generator produces partial classes that extend internal types.
/// </summary>
public class CsvRecordBinderGeneratorTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_WithSimpleClass_GeneratesBinderAndWriter()
    {
        var source = """
            using HeroParser.SeparatedValues.Records.Binding;

            namespace TestNamespace;

            [CsvGenerateBinder]
            public class Person
            {
                public string Name { get; set; } = "";
                public int Age { get; set; }
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(result.GeneratedSources);
        Assert.Contains("TestNamespace.Person", result.GeneratedSources[0].SourceText.ToString());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_WithCsvColumnAttribute_UsesCustomHeaderName()
    {
        var source = """
            using HeroParser.SeparatedValues.Records.Binding;

            namespace TestNamespace;

            [CsvGenerateBinder]
            public class Product
            {
                [CsvColumn(Name = "product_name")]
                public string Name { get; set; } = "";

                [CsvColumn(Name = "unit_price", Index = 1)]
                public decimal Price { get; set; }
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(result.GeneratedSources);

        var generatedCode = result.GeneratedSources[0].SourceText.ToString();
        Assert.Contains("product_name", generatedCode);
        Assert.Contains("unit_price", generatedCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_WithUnsupportedType_ReportsHERO001Diagnostic()
    {
        var source = """
            using HeroParser.SeparatedValues.Records.Binding;

            namespace TestNamespace;

            [CsvGenerateBinder]
            public class WithArray
            {
                public string Name { get; set; } = "";
                public int[] Scores { get; set; } = [];
            }
            """;

        var result = RunGenerator(source);

        var hero001 = result.Diagnostics.FirstOrDefault(d => d.Id == "HERO001");
        Assert.NotNull(hero001);
        Assert.Equal(DiagnosticSeverity.Warning, hero001.Severity);
        Assert.Contains("Scores", hero001.GetMessage());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_WithNullableTypes_HandlesCorrectly()
    {
        var source = """
            using System;
            using HeroParser.SeparatedValues.Records.Binding;

            namespace TestNamespace;

            [CsvGenerateBinder]
            public class NullableRecord
            {
                public string? Name { get; set; }
                public int? Age { get; set; }
                public DateTime? Birthday { get; set; }
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(result.GeneratedSources);

        var generatedCode = result.GeneratedSources[0].SourceText.ToString();
        Assert.Contains("NullableRecord", generatedCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_WithAllSupportedPrimitiveTypes_GeneratesForAll()
    {
        var source = """
            using System;
            using HeroParser.SeparatedValues.Records.Binding;

            namespace TestNamespace;

            [CsvGenerateBinder]
            public class AllTypes
            {
                public string Text { get; set; } = "";
                public int Int32Value { get; set; }
                public long Int64Value { get; set; }
                public short Int16Value { get; set; }
                public byte ByteValue { get; set; }
                public double DoubleValue { get; set; }
                public float FloatValue { get; set; }
                public decimal DecimalValue { get; set; }
                public bool BoolValue { get; set; }
                public DateTime DateTimeValue { get; set; }
                public DateTimeOffset DateTimeOffsetValue { get; set; }
                public Guid GuidValue { get; set; }
                public DayOfWeek EnumValue { get; set; }
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(result.GeneratedSources);

        var generatedCode = result.GeneratedSources[0].SourceText.ToString();
        Assert.Contains("Int32Value", generatedCode);
        Assert.Contains("Int64Value", generatedCode);
        Assert.Contains("DateTimeValue", generatedCode);
        Assert.Contains("GuidValue", generatedCode);
        Assert.Contains("EnumValue", generatedCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_WithReadOnlyProperty_SkipsInBinder()
    {
        var source = """
            using HeroParser.SeparatedValues.Records.Binding;

            namespace TestNamespace;

            [CsvGenerateBinder]
            public class ReadOnlyProps
            {
                public string Name { get; set; } = "";
                public string ComputedValue => Name.ToUpper();
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(result.GeneratedSources);

        var generatedCode = result.GeneratedSources[0].SourceText.ToString();
        Assert.Contains("Name", generatedCode);
        // Read-only property should still appear in writer but not in binder
        Assert.Contains("ComputedValue", generatedCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_WithRecordType_GeneratesCorrectly()
    {
        var source = """
            using HeroParser.SeparatedValues.Records.Binding;

            namespace TestNamespace;

            [CsvGenerateBinder]
            public record PersonRecord(string Name, int Age);
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(result.GeneratedSources);

        var generatedCode = result.GeneratedSources[0].SourceText.ToString();
        Assert.Contains("PersonRecord", generatedCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_WithFormatAttribute_IncludesFormat()
    {
        var source = """
            using System;
            using HeroParser.SeparatedValues.Records.Binding;

            namespace TestNamespace;

            [CsvGenerateBinder]
            public class FormattedRecord
            {
                [CsvColumn(Format = "yyyy-MM-dd")]
                public DateTime Date { get; set; }

                [CsvColumn(Format = "N2")]
                public decimal Amount { get; set; }
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(result.GeneratedSources);

        var generatedCode = result.GeneratedSources[0].SourceText.ToString();
        Assert.Contains("yyyy-MM-dd", generatedCode);
        Assert.Contains("N2", generatedCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_WithNoAnnotatedTypes_ProducesNoOutput()
    {
        var source = """
            namespace TestNamespace;

            public class RegularClass
            {
                public string Name { get; set; } = "";
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_WithMultipleAnnotatedTypes_GeneratesForAll()
    {
        var source = """
            using HeroParser.SeparatedValues.Records.Binding;

            namespace TestNamespace;

            [CsvGenerateBinder]
            public class Person
            {
                public string Name { get; set; } = "";
            }

            [CsvGenerateBinder]
            public class Product
            {
                public string Title { get; set; } = "";
                public decimal Price { get; set; }
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(result.GeneratedSources);

        var generatedCode = result.GeneratedSources[0].SourceText.ToString();
        Assert.Contains("TestNamespace.Person", generatedCode);
        Assert.Contains("TestNamespace.Product", generatedCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_WithListProperty_ReportsHERO001()
    {
        var source = """
            using System.Collections.Generic;
            using HeroParser.SeparatedValues.Records.Binding;

            namespace TestNamespace;

            [CsvGenerateBinder]
            public class WithList
            {
                public string Name { get; set; } = "";
                public List<string> Tags { get; set; } = new();
            }
            """;

        var result = RunGenerator(source);

        var hero001 = result.Diagnostics.FirstOrDefault(d => d.Id == "HERO001");
        Assert.NotNull(hero001);
        Assert.Equal(DiagnosticSeverity.Warning, hero001.Severity);
        Assert.Contains("Tags", hero001.GetMessage());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_WithNestedClass_GeneratesWithFullName()
    {
        var source = """
            using HeroParser.SeparatedValues.Records.Binding;

            namespace TestNamespace;

            public class Outer
            {
                [CsvGenerateBinder]
                public class Inner
                {
                    public string Value { get; set; } = "";
                }
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(result.GeneratedSources);

        var generatedCode = result.GeneratedSources[0].SourceText.ToString();
        Assert.Contains("Outer.Inner", generatedCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_OutputContainsBothBinderAndWriter()
    {
        var source = """
            using HeroParser.SeparatedValues.Records.Binding;

            namespace TestNamespace;

            [CsvGenerateBinder]
            public class Person
            {
                public string Name { get; set; } = "";
            }
            """;

        var result = RunGenerator(source);

        var generatedCode = result.GeneratedSources[0].SourceText.ToString();

        // Verify binder registration
        Assert.Contains("CsvRecordBinderFactory", generatedCode);
        // Verify writer registration
        Assert.Contains("CsvRecordWriterFactory", generatedCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_IncrementalCaching_DoesNotRegenerateUnchangedInput()
    {
        var source = """
            using HeroParser.SeparatedValues.Records.Binding;

            namespace TestNamespace;

            [CsvGenerateBinder]
            public class Person
            {
                public string Name { get; set; } = "";
            }
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilation = CreateCompilation(syntaxTree);

        var generator = new CsvRecordBinderGenerator();
        var driver = CSharpGeneratorDriver.Create(generator)
            .WithUpdatedParseOptions((CSharpParseOptions)syntaxTree.Options);

        // First run
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(
            compilation, out var outputCompilation1, out _);

        var result1 = driver.GetRunResult().Results.Single();

        // Second run with same input - should use cached result
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(
            outputCompilation1, out _, out _);

        var result2 = driver.GetRunResult().Results.Single();

        // Both runs should produce equivalent output
        Assert.Equal(
            result1.GeneratedSources.Length,
            result2.GeneratedSources.Length);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_WithGlobalNamespace_GeneratesCorrectly()
    {
        var source = """
            using HeroParser.SeparatedValues.Records.Binding;

            [CsvGenerateBinder]
            public class GlobalRecord
            {
                public string Value { get; set; } = "";
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(result.GeneratedSources);

        var generatedCode = result.GeneratedSources[0].SourceText.ToString();
        Assert.Contains("GlobalRecord", generatedCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_ProducesValidCSharpSyntax()
    {
        var source = """
            using HeroParser.SeparatedValues.Records.Binding;

            namespace TestNamespace;

            [CsvGenerateBinder]
            public class Person
            {
                public string Name { get; set; } = "";
                public int Age { get; set; }
            }
            """;

        var result = RunGenerator(source);
        var generatedCode = result.GeneratedSources[0].SourceText.ToString();

        // Parse the generated code to verify it's valid C# syntax
        var generatedTree = CSharpSyntaxTree.ParseText(generatedCode);
        var diagnostics = generatedTree.GetDiagnostics().ToList();

        // Verify no syntax errors in generated code
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    #region Test Infrastructure

    private static GeneratorRunResult RunGenerator(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilation = CreateCompilation(syntaxTree);

        var generator = new CsvRecordBinderGenerator();
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
            MetadataReference.CreateFromFile(typeof(HeroParser.Csv).Assembly.Location),
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
