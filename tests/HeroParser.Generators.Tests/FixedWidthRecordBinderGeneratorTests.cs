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
            using HeroParser;

            namespace TestNamespace;

            [GenerateBinder]
            public struct PersonStruct
            {
                [PositionalMap(Start = 0, Length = 5)]
                public int Id { get; set; }

                [PositionalMap(Start = 5, Length = 10)]
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

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_WithNoValidation_EmitsDescriptorAndGeneratedBinder()
    {
        var source = """
            using HeroParser;

            namespace TestNamespace;

            [GenerateBinder]
            public class Employee
            {
                [PositionalMap(Start = 0, Length = 5)]
                public int Id { get; set; }

                [PositionalMap(Start = 5, Length = 20)]
                public string? Name { get; set; }
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var descriptorSource = result.GeneratedSources
            .FirstOrDefault(s => s.HintName.Contains("FixedWidthDescriptor."))
            .SourceText?.ToString() ?? "";

        // Both descriptor class and generated binder should be emitted
        Assert.Contains("FixedWidthDescriptor_", descriptorSource);
        Assert.Contains("FixedWidthGeneratedBinder_", descriptorSource);
        Assert.Contains("IFixedWidthBinder<global::TestNamespace.Employee>", descriptorSource);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_NotNullValidation_EmitsNotNullCheck()
    {
        var source = """
            using HeroParser;

            namespace TestNamespace;

            [GenerateBinder]
            public class Record
            {
                [PositionalMap(Start = 0, Length = 10)]
                [Validate(NotNull = true)]
                public string? Name { get; set; }
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var generatedCode = GetDescriptorSourceCode(result);
        Assert.Contains("valid = true", generatedCode);
        Assert.Contains("Rule = \"NotNull\"", generatedCode);
        Assert.Contains("return valid;", generatedCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_NotEmptyValidation_EmitsNotEmptyCheck()
    {
        var source = """
            using HeroParser;

            namespace TestNamespace;

            [GenerateBinder]
            public class Record
            {
                [PositionalMap(Start = 0, Length = 10)]
                [Validate(NotEmpty = true)]
                public string? Name { get; set; }
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var generatedCode = GetDescriptorSourceCode(result);
        Assert.Contains("Rule = \"NotEmpty\"", generatedCode);
        Assert.Contains("string.IsNullOrWhiteSpace", generatedCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_MaxLengthValidation_EmitsMaxLengthCheck()
    {
        var source = """
            using HeroParser;

            namespace TestNamespace;

            [GenerateBinder]
            public class Record
            {
                [PositionalMap(Start = 0, Length = 10)]
                [Validate(MaxLength = 5)]
                public string? Name { get; set; }
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var generatedCode = GetDescriptorSourceCode(result);
        Assert.Contains("Rule = \"MaxLength\"", generatedCode);
        Assert.Contains("> 5", generatedCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_MinLengthValidation_EmitsMinLengthCheck()
    {
        var source = """
            using HeroParser;

            namespace TestNamespace;

            [GenerateBinder]
            public class Record
            {
                [PositionalMap(Start = 0, Length = 10)]
                [Validate(MinLength = 3)]
                public string? Name { get; set; }
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var generatedCode = GetDescriptorSourceCode(result);
        Assert.Contains("Rule = \"MinLength\"", generatedCode);
        Assert.Contains("< 3", generatedCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_RangeValidation_EmitsRangeCheck()
    {
        var source = """
            using HeroParser;

            namespace TestNamespace;

            [GenerateBinder]
            public class Record
            {
                [PositionalMap(Start = 0, Length = 5)]
                [Validate(RangeMin = 1, RangeMax = 100)]
                public int Age { get; set; }
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var generatedCode = GetDescriptorSourceCode(result);
        Assert.Contains("Rule = \"Range\"", generatedCode);
        Assert.Contains("< 1", generatedCode);
        Assert.Contains("> 100", generatedCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_PatternValidation_EmitsRegexCheck()
    {
        var source = """
            using HeroParser;

            namespace TestNamespace;

            [GenerateBinder]
            public class Record
            {
                [PositionalMap(Start = 0, Length = 10)]
                [Validate(Pattern = @"^\d+$")]
                public string? Code { get; set; }
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var generatedCode = GetDescriptorSourceCode(result);
        Assert.Contains("Rule = \"Pattern\"", generatedCode);
        Assert.Contains("_pattern_Code", generatedCode);
        Assert.Contains("IsMatch", generatedCode);
        // Static Regex field with compiled option
        Assert.Contains("RegexOptions.Compiled", generatedCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_PatternValidation_EmitsCustomTimeoutMs()
    {
        var source = """
            using HeroParser;

            namespace TestNamespace;

            [GenerateBinder]
            public class Record
            {
                [PositionalMap(Start = 0, Length = 10)]
                [Validate(Pattern = @"^\d+$", PatternTimeoutMs = 500)]
                public string? Code { get; set; }
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var generatedCode = GetDescriptorSourceCode(result);
        Assert.Contains("500", generatedCode);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_NotEmptyOnNonString_EmitsDiagnostic()
    {
        var source = """
            using HeroParser;

            namespace TestNamespace;

            [GenerateBinder]
            public class Record
            {
                [PositionalMap(Start = 0, Length = 5)]
                [Validate(NotEmpty = true)]
                public int Age { get; set; }
            }
            """;

        var result = RunGenerator(source);

        // Should emit diagnostic for NotEmpty on non-string
        Assert.Contains(result.Diagnostics, d => d.Id == "HERO004");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_MaxLengthOnNonString_EmitsDiagnostic()
    {
        var source = """
            using HeroParser;

            namespace TestNamespace;

            [GenerateBinder]
            public class Record
            {
                [PositionalMap(Start = 0, Length = 5)]
                [Validate(MaxLength = 3)]
                public int Age { get; set; }
            }
            """;

        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => d.Id == "HERO005");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_RangeOnNonNumeric_EmitsDiagnostic()
    {
        var source = """
            using HeroParser;

            namespace TestNamespace;

            [GenerateBinder]
            public class Record
            {
                [PositionalMap(Start = 0, Length = 10)]
                [Validate(RangeMin = 1)]
                public string? Name { get; set; }
            }
            """;

        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => d.Id == "HERO006");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_PatternOnNonString_EmitsDiagnostic()
    {
        var source = """
            using HeroParser;

            namespace TestNamespace;

            [GenerateBinder]
            public class Record
            {
                [PositionalMap(Start = 0, Length = 5)]
                [Validate(Pattern = @"^\d+$")]
                public int Age { get; set; }
            }
            """;

        var result = RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => d.Id == "HERO007");
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_GeneratedBinder_RegistersWithFactory()
    {
        var source = """
            using HeroParser;

            namespace TestNamespace;

            [GenerateBinder]
            public class Employee
            {
                [PositionalMap(Start = 0, Length = 5)]
                public int Id { get; set; }

                [PositionalMap(Start = 5, Length = 20)]
                public string? Name { get; set; }
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var registrationSource = result.GeneratedSources
            .FirstOrDefault(s => s.HintName.Contains("Registration"))
            .SourceText?.ToString() ?? "";

        // Both descriptor and generated binder should be registered
        Assert.Contains("RegisterDescriptor", registrationSource);
        Assert.Contains("RegisterGeneratedBinder", registrationSource);
        Assert.Contains("FixedWidthGeneratedBinder_", registrationSource);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generator_ValidationError_CollectsRowNumberAndFieldInfo()
    {
        var source = """
            using HeroParser;

            namespace TestNamespace;

            [GenerateBinder]
            public class Record
            {
                [PositionalMap(Start = 0, Length = 10)]
                [Validate(NotNull = true)]
                public string? Name { get; set; }
            }
            """;

        var result = RunGenerator(source);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var generatedCode = GetDescriptorSourceCode(result);
        // Validation error should include RowNumber, ColumnIndex, PropertyName
        Assert.Contains("RowNumber = rowNumber", generatedCode);
        Assert.Contains("ColumnIndex = 0", generatedCode);
        Assert.Contains("PropertyName = \"Name\"", generatedCode);
        Assert.Contains("RawValue = new string(span)", generatedCode);
    }

    #region Test Infrastructure

    private static string GetDescriptorSourceCode(GeneratorRunResult result)
    {
        return result.GeneratedSources
            .FirstOrDefault(s => s.HintName.Contains("FixedWidthDescriptor."))
            .SourceText?.ToString() ?? "";
    }

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
            MetadataReference.CreateFromFile(typeof(FixedWidth).Assembly.Location),
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
