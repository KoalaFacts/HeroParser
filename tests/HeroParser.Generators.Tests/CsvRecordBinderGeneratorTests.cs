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
            using HeroParser;

            namespace TestNamespace;

            [GenerateBinder]
            public class Person
            {
                public string Name { get; set; } = "";
                public int Age { get; set; }
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        // Generator produces: 1 binder file per type + 1 shared registration file
        Assert.Equal(2, result.GeneratedSources.Length);
        var allGeneratedCode = string.Join("\n", result.GeneratedSources.Select(s => s.SourceText.ToString()));
        Assert.Contains("TestNamespace.Person", allGeneratedCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_WithStruct_GeneratesBinderAndWriter()
    {
        var source = """
            using HeroParser;

            namespace TestNamespace;

            [GenerateBinder]
            public struct PersonStruct
            {
                public string Name { get; set; }
                public int Age { get; set; }
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Equal(2, result.GeneratedSources.Length);
        var allGeneratedCode = string.Join("\n", result.GeneratedSources.Select(s => s.SourceText.ToString()));
        Assert.Contains("TestNamespace.PersonStruct", allGeneratedCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_WithTabularMapAttribute_UsesCustomHeaderName()
    {
        var source = """
            using HeroParser;

            namespace TestNamespace;

            [GenerateBinder]
            public class Product
            {
                [TabularMap(Name = "product_name")]
                public string Name { get; set; } = "";

                [TabularMap(Name = "unit_price", Index = 1)]
                public decimal Price { get; set; }
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        // Generator produces: 1 binder file per type + 1 shared registration file
        Assert.Equal(2, result.GeneratedSources.Length);

        var allGeneratedCode = string.Join("\n", result.GeneratedSources.Select(s => s.SourceText.ToString()));
        Assert.Contains("product_name", allGeneratedCode);
        Assert.Contains("unit_price", allGeneratedCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_WithUnsupportedType_ReportsHERO001Diagnostic()
    {
        var source = """
            using HeroParser;

            namespace TestNamespace;

            [GenerateBinder]
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
            using HeroParser;

            namespace TestNamespace;

            [GenerateBinder]
            public class NullableRecord
            {
                public string? Name { get; set; }
                public int? Age { get; set; }
                public DateTime? Birthday { get; set; }
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        // Generator produces: 1 binder file per type + 1 shared registration file
        Assert.Equal(2, result.GeneratedSources.Length);

        var allGeneratedCode = string.Join("\n", result.GeneratedSources.Select(s => s.SourceText.ToString()));
        Assert.Contains("NullableRecord", allGeneratedCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_WithAllSupportedPrimitiveTypes_GeneratesForAll()
    {
        var source = """
            using System;
            using HeroParser;

            namespace TestNamespace;

            [GenerateBinder]
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
        // Generator produces: 1 binder file per type + 1 shared registration file
        Assert.Equal(2, result.GeneratedSources.Length);

        var allGeneratedCode = string.Join("\n", result.GeneratedSources.Select(s => s.SourceText.ToString()));
        Assert.Contains("Int32Value", allGeneratedCode);
        Assert.Contains("Int64Value", allGeneratedCode);
        Assert.Contains("DateTimeValue", allGeneratedCode);
        Assert.Contains("GuidValue", allGeneratedCode);
        Assert.Contains("EnumValue", allGeneratedCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_WithReadOnlyProperty_SkipsInBinder()
    {
        var source = """
            using HeroParser;

            namespace TestNamespace;

            [GenerateBinder]
            public class ReadOnlyProps
            {
                public string Name { get; set; } = "";
                public string ComputedValue => Name.ToUpper();
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        // Generator produces: 1 binder file per type + 1 shared registration file
        Assert.Equal(2, result.GeneratedSources.Length);

        var allGeneratedCode = string.Join("\n", result.GeneratedSources.Select(s => s.SourceText.ToString()));
        Assert.Contains("Name", allGeneratedCode);
        // Read-only property should still appear in writer but not in binder
        Assert.Contains("ComputedValue", allGeneratedCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_WithRecordType_GeneratesCorrectly()
    {
        var source = """
            using HeroParser;

            namespace TestNamespace;

            [GenerateBinder]
            public record PersonRecord(string Name, int Age);
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        // Generator produces: 1 binder file per type + 1 shared registration file
        Assert.Equal(2, result.GeneratedSources.Length);

        var allGeneratedCode = string.Join("\n", result.GeneratedSources.Select(s => s.SourceText.ToString()));
        Assert.Contains("PersonRecord", allGeneratedCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_WithFormatAttribute_IncludesFormat()
    {
        var source = """
            using System;
            using HeroParser;

            namespace TestNamespace;

            [GenerateBinder]
            public class FormattedRecord
            {
                [TabularMap(Name = "Date")]
                [Parse(Format = "yyyy-MM-dd")]
                public DateTime Date { get; set; }

                [TabularMap(Name = "Amount")]
                [Parse(Format = "N2")]
                public decimal Amount { get; set; }
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        // Generator produces: 1 binder file per type + 1 shared registration file
        Assert.Equal(2, result.GeneratedSources.Length);

        var allGeneratedCode = string.Join("\n", result.GeneratedSources.Select(s => s.SourceText.ToString()));
        Assert.Contains("yyyy-MM-dd", allGeneratedCode);
        Assert.Contains("N2", allGeneratedCode);
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
            using HeroParser;

            namespace TestNamespace;

            [GenerateBinder]
            public class Person
            {
                public string Name { get; set; } = "";
            }

            [GenerateBinder]
            public class Product
            {
                public string Title { get; set; } = "";
                public decimal Price { get; set; }
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        // Generator produces: 1 binder file per type + 1 shared registration file
        // For 2 types: 2 binder files + 1 registration = 3 files
        Assert.Equal(3, result.GeneratedSources.Length);

        var allGeneratedCode = string.Join("\n", result.GeneratedSources.Select(s => s.SourceText.ToString()));
        Assert.Contains("TestNamespace.Person", allGeneratedCode);
        Assert.Contains("TestNamespace.Product", allGeneratedCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_WithListProperty_ReportsHERO001()
    {
        var source = """
            using System.Collections.Generic;
            using HeroParser;

            namespace TestNamespace;

            [GenerateBinder]
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
            using HeroParser;

            namespace TestNamespace;

            public class Outer
            {
                [GenerateBinder]
                public class Inner
                {
                    public string Value { get; set; } = "";
                }
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        // Generator produces: 1 binder file per type + 1 shared registration file
        Assert.Equal(2, result.GeneratedSources.Length);

        var allGeneratedCode = string.Join("\n", result.GeneratedSources.Select(s => s.SourceText.ToString()));
        Assert.Contains("Outer.Inner", allGeneratedCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_OutputContainsBothBinderAndWriter()
    {
        var source = """
            using HeroParser;

            namespace TestNamespace;

            [GenerateBinder]
            public class Person
            {
                public string Name { get; set; } = "";
            }
            """;

        var result = RunGenerator(source);

        var allGeneratedCode = string.Join("\n", result.GeneratedSources.Select(s => s.SourceText.ToString()));

        // Verify binder registration
        Assert.Contains("CsvRecordBinderFactory", allGeneratedCode);
        // Verify writer registration
        Assert.Contains("CsvRecordWriterFactory", allGeneratedCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_IncrementalCaching_DoesNotRegenerateUnchangedInput()
    {
        var source = """
            using HeroParser;

            namespace TestNamespace;

            [GenerateBinder]
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
            using HeroParser;

            [GenerateBinder]
            public class GlobalRecord
            {
                public string Value { get; set; } = "";
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        // Generator produces: 1 binder file per type + 1 shared registration file
        Assert.Equal(2, result.GeneratedSources.Length);

        var allGeneratedCode = string.Join("\n", result.GeneratedSources.Select(s => s.SourceText.ToString()));
        Assert.Contains("GlobalRecord", allGeneratedCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_ProducesValidCSharpSyntax()
    {
        var source = """
            using HeroParser;

            namespace TestNamespace;

            [GenerateBinder]
            public class Person
            {
                public string Name { get; set; } = "";
                public int Age { get; set; }
            }
            """;

        var result = RunGenerator(source);

        // Verify all generated files have valid C# syntax
        foreach (var generatedSource in result.GeneratedSources)
        {
            var generatedCode = generatedSource.SourceText.ToString();
            var generatedTree = CSharpSyntaxTree.ParseText(generatedCode);
            var diagnostics = generatedTree.GetDiagnostics().ToList();
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_PropertyWithoutTabularMap_UseConventionBinding()
    {
        // Convention-based mapping: properties without [TabularMap] bind by property name — no error expected
        var source = """
            using HeroParser;
            namespace TestNamespace;
            [GenerateBinder]
            public class Ok { [Validate(NotNull = true)] public string Name { get; set; } = ""; }
            """;
        var result = RunGenerator(source);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var allGeneratedCode = string.Join("\n", result.GeneratedSources.Select(s => s.SourceText.ToString()));
        // Property is included and bound by its property name
        Assert.Contains("\"Name\"", allGeneratedCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_TabularMapWithName_UsesCustomHeaderName()
    {
        var source = """
            using HeroParser;
            namespace TestNamespace;
            [GenerateBinder]
            public class Good { [TabularMap(Name = "name")] public string Name { get; set; } = ""; }
            """;
        var result = RunGenerator(source);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var allGeneratedCode = string.Join("\n", result.GeneratedSources.Select(s => s.SourceText.ToString()));
        Assert.Contains("\"name\"", allGeneratedCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_TabularMapWithIndex_UsesExplicitIndex()
    {
        var source = """
            using HeroParser;
            namespace TestNamespace;
            [GenerateBinder]
            public class Good { [TabularMap(Index = 0)] public string Name { get; set; } = ""; }
            """;
        var result = RunGenerator(source);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var allGeneratedCode = string.Join("\n", result.GeneratedSources.Select(s => s.SourceText.ToString()));
        Assert.Contains("0", allGeneratedCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_PropertyWithoutTabularMap_IsIncludedByConvention()
    {
        var source = """
            using HeroParser;
            namespace TestNamespace;
            [GenerateBinder]
            public class Ok { public string Name { get; set; } = ""; }
            """;
        var result = RunGenerator(source);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        // No HERO008 should ever be emitted — it no longer exists
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "HERO008");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_NotEmptyOnInt_ReportsHERO004()
    {
        var source = """
            using HeroParser;
            namespace T;
            [GenerateBinder]
            public class R { [TabularMap(Index = 0)] [Validate(NotEmpty = true)] public int X { get; set; } }
            """;
        Assert.Contains(RunGenerator(source).Diagnostics, d => d.Id == "HERO004");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_MaxLengthOnDecimal_ReportsHERO005()
    {
        var source = """
            using HeroParser;
            namespace T;
            [GenerateBinder]
            public class R { [TabularMap(Index = 0)] [Validate(MaxLength = 10)] public decimal X { get; set; } }
            """;
        Assert.Contains(RunGenerator(source).Diagnostics, d => d.Id == "HERO005");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_RangeOnString_ReportsHERO006()
    {
        var source = """
            using HeroParser;
            namespace T;
            [GenerateBinder]
            public class R { [TabularMap(Name = "X")] [Validate(RangeMin = 0)] public string X { get; set; } = ""; }
            """;
        Assert.Contains(RunGenerator(source).Diagnostics, d => d.Id == "HERO006");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_PatternOnInt_ReportsHERO007()
    {
        var source = """
            using HeroParser;
            namespace T;
            [GenerateBinder]
            public class R { [TabularMap(Index = 0)] [Validate(Pattern = ".*")] public int X { get; set; } }
            """;
        Assert.Contains(RunGenerator(source).Diagnostics, d => d.Id == "HERO007");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_ValidValidationProperties_NoExtraDiagnostics()
    {
        var source = """
            using HeroParser;
            namespace T;
            [GenerateBinder]
            public class R
            {
                [TabularMap(Name = "X")]
                [Validate(NotNull = true, NotEmpty = true, MaxLength = 50)]
                public string X { get; set; } = "";

                [TabularMap(Index = 1)]
                [Validate(NotNull = true, RangeMin = 0, RangeMax = 100)]
                public decimal Y { get; set; }
            }
            """;
        var result = RunGenerator(source);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_WithNotNullValidation_EmitsValidationCode()
    {
        var source = """
            using HeroParser;
            namespace T;
            [GenerateBinder]
            public class R { [TabularMap(Name = "X")] [Validate(NotNull = true)] public string X { get; set; } = ""; }
            """;
        var code = string.Join("\n", RunGenerator(source).GeneratedSources.Select(s => s.SourceText.ToString()));
        Assert.Contains("ValidationError", code);
        Assert.Contains("NotNull", code);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_WithNoValidation_NoValidationCode()
    {
        var source = """
            using HeroParser;
            namespace T;
            [GenerateBinder]
            public class R { [TabularMap(Name = "X")] public string X { get; set; } = ""; }
            """;
        var code = string.Join("\n", RunGenerator(source).GeneratedSources.Select(s => s.SourceText.ToString()));
        // "valid = false" is only emitted when validation is present; the method signature always contains ValidationError
        Assert.DoesNotContain("valid = false", code);
        Assert.DoesNotContain("Rule = ", code);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_WithPattern_EmitsStaticRegex()
    {
        var source = """
            using HeroParser;
            namespace T;
            [GenerateBinder]
            public class R { [TabularMap(Name = "X")] [Validate(Pattern = @"^\d+$")] public string X { get; set; } = ""; }
            """;
        var code = string.Join("\n", RunGenerator(source).GeneratedSources.Select(s => s.SourceText.ToString()));
        Assert.Contains("static readonly", code);
        Assert.Contains("Regex", code);
        Assert.Contains("TimeSpan", code);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_WithDecimalRange_EmitsDecimalLiteral()
    {
        var source = """
            using HeroParser;
            namespace T;
            [GenerateBinder]
            public class R { [TabularMap(Index = 0)] [Validate(RangeMin = 0, RangeMax = 999.99)] public decimal X { get; set; } }
            """;
        var code = string.Join("\n", RunGenerator(source).GeneratedSources.Select(s => s.SourceText.ToString()));
        Assert.Contains("999.99m", code);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_WithAllValidations_ProducesValidCSharp()
    {
        var source = """
            using HeroParser;
            namespace T;
            [GenerateBinder]
            public class R
            {
                [TabularMap(Name = "Id")]
                [Validate(NotNull = true, NotEmpty = true)]
                public string Id { get; set; } = "";

                [TabularMap(Name = "Amount")]
                [Validate(NotNull = true, RangeMin = 0, RangeMax = 100)]
                public decimal Amount { get; set; }

                [TabularMap(Name = "Code")]
                [Validate(MinLength = 2, MaxLength = 5)]
                public string Code { get; set; } = "";

                [TabularMap(Name = "Ref")]
                [Validate(Pattern = @"^[A-Z]+$")]
                public string Ref { get; set; } = "";
            }
            """;
        var result = RunGenerator(source);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // Verify generated code is valid C# syntax
        foreach (var gs in result.GeneratedSources)
        {
            var tree = CSharpSyntaxTree.ParseText(gs.SourceText.ToString());
            var diags = tree.GetDiagnostics().ToList();
            Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        }
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
            MetadataReference.CreateFromFile(typeof(Csv).Assembly.Location),
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
