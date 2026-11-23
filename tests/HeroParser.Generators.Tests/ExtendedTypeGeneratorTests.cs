using System.Collections.Immutable;
using HeroParser.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace HeroParser.Generators.Tests;

public class ExtendedTypeGeneratorTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void Generators_ExtendedBuiltInAndCustomSpanParsableTypes_CompileCleanlyWithoutDiagnostics()
    {
        const string SOURCE = """
            #nullable enable
            using System;
            using System.Diagnostics.CodeAnalysis;
            using System.Numerics;
            using HeroParser;

            namespace GeneratorTestNs;

            public readonly struct ExplicitParsable : ISpanParsable<ExplicitParsable>, IFormattable
            {
                public string Raw { get; }
                public ExplicitParsable(string raw) => Raw = raw;

                static ExplicitParsable IParsable<ExplicitParsable>.Parse(string s, IFormatProvider? provider) => new(s);
                static bool IParsable<ExplicitParsable>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ExplicitParsable result)
                {
                    result = s is null ? default : new ExplicitParsable(s);
                    return s is not null;
                }
                static ExplicitParsable ISpanParsable<ExplicitParsable>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => new(new string(s));
                static bool ISpanParsable<ExplicitParsable>.TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out ExplicitParsable result)
                {
                    result = new ExplicitParsable(new string(s));
                    return true;
                }
                string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => Raw ?? "";
            }

            public readonly struct Utf8OnlyParsable : IUtf8SpanParsable<Utf8OnlyParsable>, ISpanFormattable
            {
                public int Value { get; }
                public Utf8OnlyParsable(int value) => Value = value;

                public static Utf8OnlyParsable Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider) => new(utf8Text.Length);
                public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, out Utf8OnlyParsable result)
                {
                    result = new Utf8OnlyParsable(utf8Text.Length);
                    return true;
                }
                public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
                    => Value.TryFormat(destination, out charsWritten, format, provider);
                public string ToString(string? format, IFormatProvider? formatProvider) => Value.ToString(format, formatProvider);
            }

            [GenerateBinder]
            public sealed class ExtendedTabularRecord
            {
                [PositionalMap(Start = 0, Length = 20)]
                public DateTimeOffset OffsetVal { get; set; }

                [PositionalMap(Start = 20, Length = 10)]
                public DateOnly DateVal { get; set; }

                [PositionalMap(Start = 30, Length = 10)]
                public TimeOnly TimeVal { get; set; }

                [PositionalMap(Start = 40, Length = 16)]
                public TimeSpan Duration { get; set; }

                [PositionalMap(Start = 56, Length = 10)]
                public uint UIntVal { get; set; }

                [PositionalMap(Start = 66, Length = 20)]
                public ulong ULongVal { get; set; }

                [PositionalMap(Start = 86, Length = 6)]
                public ushort UShortVal { get; set; }

                [PositionalMap(Start = 92, Length = 4)]
                public sbyte SByteVal { get; set; }

                [PositionalMap(Start = 96, Length = 1)]
                public char CharVal { get; set; }

                [PositionalMap(Start = 97, Length = 10)]
                public Half HalfVal { get; set; }

                [PositionalMap(Start = 107, Length = 40)]
                public Int128 Int128Val { get; set; }

                [PositionalMap(Start = 147, Length = 40)]
                public UInt128 UInt128Val { get; set; }

                [PositionalMap(Start = 187, Length = 40)]
                public BigInteger BigIntVal { get; set; }

                [PositionalMap(Start = 227, Length = 30)]
                public Uri? UriVal { get; set; }

                [PositionalMap(Start = 257, Length = 20)]
                public byte[]? BytesVal { get; set; }

                [PositionalMap(Start = 277, Length = 16)]
                public ExplicitParsable CustomExplicit { get; set; }

                [PositionalMap(Start = 293, Length = 16)]
                public Utf8OnlyParsable CustomUtf8 { get; set; }
            }
            """;

        var (diagnostics, generated, errors) = RunAllGenerators(SOURCE, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(diagnostics, d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning);
        Assert.Empty(errors);
        Assert.Contains("HtbInlineBinder_GeneratorTestNs_ExtendedTabularRecord", generated);
        Assert.Contains("TryParseBigInteger", generated);
        Assert.Contains("TryParseUtf8", generated);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Generators_UnsupportedTypesStillEmitHERO001()
    {
        const string SOURCE = """
            #nullable enable
            using System;
            using System.Collections.Generic;
            using HeroParser;

            namespace UnsupportedNs;

            [GenerateBinder]
            public sealed class UnsupportedProps
            {
                public string Ok { get; set; } = "";
                public TimeZoneInfo? Tz { get; set; }
                public ReadOnlyMemory<char> Slice { get; set; }
                public int[] Numbers { get; set; } = [];
                public List<string> Items { get; set; } = [];
            }
            """;

        var (diagnostics, _, _) = RunAllGenerators(SOURCE, TestContext.Current.CancellationToken);

        var hero001 = diagnostics.Where(d => d.Id == "HERO001").Select(d => d.GetMessage()).ToList();
        Assert.Contains(hero001, m => m.Contains("Tz"));
        Assert.Contains(hero001, m => m.Contains("Slice"));
        Assert.Contains(hero001, m => m.Contains("Numbers"));
        Assert.Contains(hero001, m => m.Contains("Items"));
    }

    private static (ImmutableArray<Diagnostic> Diagnostics, string GeneratedSource, IReadOnlyList<Diagnostic> CompilationErrors)
        RunAllGenerators(string source, CancellationToken cancellationToken)
    {
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
            "ExtendedGeneratorTestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create(
                new CsvRecordBinderGenerator(),
                new FixedWidthRecordBinderGenerator(),
                new JsonlRecordBinderGenerator(),
                new HtbRecordBinderGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _, cancellationToken);

        var runResult = driver.GetRunResult();
        var errors = output.GetDiagnostics(cancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        string generated = string.Join("\n", runResult.GeneratedTrees.Select(t => t.GetText(cancellationToken).ToString()));
        return ([.. runResult.Diagnostics], generated, errors);
    }
}
