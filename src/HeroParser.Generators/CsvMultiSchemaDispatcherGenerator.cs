using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using static HeroParser.Generators.GeneratorHelpers;

namespace HeroParser.Generators;

/// <summary>
/// Source generator for multi-schema CSV dispatchers.
/// Generates optimized switch-based dispatch code that eliminates interface dispatch,
/// dictionary lookups, and boxing overhead.
/// </summary>
/// <remarks>
/// <para>
/// This generator produces AOT-compatible code. It requires that all record types
/// used in the dispatcher have the [CsvGenerateBinder] attribute to ensure the
/// corresponding binder classes are generated at compile-time.
/// </para>
/// <para>
/// Generated methods:
/// <list type="bullet">
/// <item><c>DispatchBytes(CsvRow&lt;byte&gt;, int)</c> - UTF-8 dispatch (primary, SIMD-accelerated path)</item>
/// <item><c>Bind{RecordName}Bytes(CsvRow&lt;byte&gt;, int)</c> - Direct byte binding per record type</item>
/// </list>
/// </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class CsvMultiSchemaDispatcherGenerator : IIncrementalGenerator
{
    private const string DISPATCHER_ATTRIBUTE = "HeroParser.SeparatedValues.Reading.Shared.CsvGenerateDispatcherAttribute";
    private const string SCHEMA_MAPPING_ATTRIBUTE = "HeroParser.SeparatedValues.Reading.Shared.CsvSchemaMappingAttribute";
    private const string BINDER_NAMESPACE = "HeroParser.SeparatedValues.Reading.Binding";
    private const string BYTE_ROW_TYPE = "global::HeroParser.SeparatedValues.Reading.Rows.CsvRow<byte>";

    private static readonly string[] generateBinderAttributeNames =
    [
        "HeroParser.SeparatedValues.Reading.Shared.CsvGenerateBinderAttribute",
        "HeroParser.CsvGenerateBinderAttribute"
    ];

#pragma warning disable RS2008 // Enable analyzer release tracking - not needed for internal generator
    private static readonly DiagnosticDescriptor missingBinderAttributeDiagnostic = new(
        "HERO003",
        "Record type missing [CsvGenerateBinder] attribute",
        "Record type '{0}' used in multi-schema dispatcher does not have [CsvGenerateBinder] attribute. This is required for AOT compatibility.",
        "HeroParser.Generators",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
#pragma warning restore RS2008

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var provider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                DISPATCHER_ATTRIBUTE,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => TransformToDescriptor(ctx, ct))
            .Where(static x => x is not null);

        context.RegisterSourceOutput(provider, static (spc, descriptor) => EmitDispatcher(spc, descriptor!));
    }

    private static DispatcherDescriptor? TransformToDescriptor(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol classSymbol)
            return null;

        // Get dispatcher attribute data
        var dispatcherAttr = ctx.Attributes.FirstOrDefault();
        if (dispatcherAttr is null)
            return null;

        int discriminatorIndex = -1;
        string? discriminatorColumn = null;
        bool caseInsensitive = false;

#pragma warning disable IDE0010 // Populate switch - intentionally not exhaustive
        foreach (var arg in dispatcherAttr.NamedArguments)
        {
            switch (arg.Key)
            {
                case "DiscriminatorIndex" when arg.Value.Value is int i:
                    discriminatorIndex = i;
                    break;
                case "DiscriminatorColumn" when arg.Value.Value is string s:
                    discriminatorColumn = s;
                    break;
                case "CaseInsensitive" when arg.Value.Value is bool b:
                    caseInsensitive = b;
                    break;
            }
        }
#pragma warning restore IDE0010

        // Find [CsvSchemaMapping] attributes on the class
        var mappings = new List<DiscriminatorMapping>();
        var classLocation = classSymbol.Locations.FirstOrDefault();

        foreach (var attr in classSymbol.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() == SCHEMA_MAPPING_ATTRIBUTE))
        {
            ct.ThrowIfCancellationRequested();

            if (attr.ConstructorArguments.Length < 2)
                continue;

            var discriminatorValue = attr.ConstructorArguments[0].Value?.ToString();
            if (string.IsNullOrEmpty(discriminatorValue) ||
                attr.ConstructorArguments[1].Value is not INamedTypeSymbol recordTypeSymbol)
                continue;

            var returnTypeName = recordTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var safeTypeName = CreateSafeClassName(recordTypeSymbol);

            // Check if record type has [CsvGenerateBinder] attribute (required for AOT)
            bool hasGenerateBinderAttribute = recordTypeSymbol.GetAttributes()
                .Any(a => a.AttributeClass != null &&
                          generateBinderAttributeNames.Contains(a.AttributeClass.ToDisplayString()));

            mappings.Add(new DiscriminatorMapping(
                DiscriminatorValue: discriminatorValue!,
                MethodName: $"Bind{recordTypeSymbol.Name}",
                ReturnTypeName: returnTypeName,
                SafeTypeName: safeTypeName,
                HasGenerateBinderAttribute: hasGenerateBinderAttribute,
                Location: classLocation));
        }

        if (mappings.Count == 0)
            return null;

        var className = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var namespaceName = classSymbol.ContainingNamespace?.ToDisplayString() ?? "";
        var simpleClassName = classSymbol.Name;

        return new DispatcherDescriptor(
            className,
            namespaceName,
            simpleClassName,
            discriminatorIndex,
            discriminatorColumn,
            caseInsensitive,
            mappings);
    }

    private static void EmitDispatcher(SourceProductionContext context, DispatcherDescriptor descriptor)
    {
        // Report diagnostics for missing [CsvGenerateBinder] attributes
        foreach (var mapping in descriptor.Mappings.Where(m => !m.HasGenerateBinderAttribute))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                missingBinderAttributeDiagnostic,
                mapping.Location,
                mapping.ReturnTypeName));
        }

        var builder = new SourceBuilder(8192);
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("// AOT-compatible multi-schema dispatcher - uses compile-time generated binders");
        builder.AppendLine("// All record types must have [CsvGenerateBinder] attribute for AOT support");
        builder.AppendLine("#nullable enable");
        builder.AppendLine("using System;");
        builder.AppendLine("using System.Runtime.CompilerServices;");
        builder.AppendLine();

        if (!string.IsNullOrEmpty(descriptor.Namespace))
        {
            builder.AppendLine($"namespace {descriptor.Namespace};");
            builder.AppendLine();
        }

        builder.AppendLine($"partial class {descriptor.SimpleClassName}");
        builder.AppendLine("{");
        builder.Indent();

        // Emit byte binder fields (cached for performance) - UTF-8 is the primary path
        foreach (var mapping in descriptor.Mappings)
        {
            var binderClass = $"global::{BINDER_NAMESPACE}.CsvInlineByteBinder_{mapping.SafeTypeName}";
            builder.AppendLine($"private static readonly {binderClass} _byteBinder_{mapping.SafeTypeName} = new(null);");
        }
        builder.AppendLine();

        // Emit byte binding methods
        foreach (var mapping in descriptor.Mappings)
        {
            builder.AppendLine($"public static {mapping.ReturnTypeName}? {mapping.MethodName}Bytes({BYTE_ROW_TYPE} row, int rowNumber)");
            builder.AppendLine($"    => _byteBinder_{mapping.SafeTypeName}.Bind(row, rowNumber);");
            builder.AppendLine();
        }

        // Emit optimized DispatchBytes method (UTF-8 only)
        EmitDispatchMethod(builder, descriptor);

        builder.Unindent();
        builder.AppendLine("}");

        context.AddSource($"CsvMultiSchemaDispatcher.{descriptor.SimpleClassName}.g.cs", builder.ToString());
    }

    private static void EmitDispatchMethod(SourceBuilder builder, DispatcherDescriptor descriptor)
    {
        // UTF-8 byte dispatch only (primary SIMD-accelerated path)
        builder.AppendLine("/// <summary>");
        builder.AppendLine("/// Dispatches a row to the appropriate binder based on the discriminator value.");
        builder.AppendLine("/// This method uses switch expressions for optimal performance (JIT compiles to jump table).");
        builder.AppendLine("/// </summary>");
        builder.AppendLine("[MethodImpl(MethodImplOptions.AggressiveInlining)]");
        builder.AppendLine($"public static object? DispatchBytes({BYTE_ROW_TYPE} row, int rowNumber)");
        builder.AppendLine("{");
        builder.Indent();

        // Check if all discriminators are single ASCII chars
        bool allSingleChar = descriptor.Mappings.All(m => m.DiscriminatorValue.Length == 1 && m.DiscriminatorValue[0] < 128);

        if (allSingleChar && descriptor.DiscriminatorIndex >= 0)
        {
            // Ultra-fast path: single char dispatch with TryGetColumnFirstChar
            builder.AppendLine($"if (!row.TryGetColumnFirstChar({descriptor.DiscriminatorIndex}, out int charCode, out int length))");
            builder.AppendLine("    return null;");
            builder.AppendLine();
            builder.AppendLine("if (length != 1)");
            builder.AppendLine("    return null;");
            builder.AppendLine();

            if (descriptor.CaseInsensitive)
            {
                builder.AppendLine("// Case-insensitive: normalize to lowercase");
                builder.AppendLine("if ((uint)(charCode - 'A') <= 'Z' - 'A')");
                builder.AppendLine("    charCode += 32;");
                builder.AppendLine();
            }

            builder.AppendLine("return charCode switch");
            builder.AppendLine("{");
            builder.Indent();

            foreach (var mapping in descriptor.Mappings)
            {
                var charValue = descriptor.CaseInsensitive
                    ? char.ToLowerInvariant(mapping.DiscriminatorValue[0])
                    : mapping.DiscriminatorValue[0];

                builder.AppendLine($"'{charValue}' => _byteBinder_{mapping.SafeTypeName}.Bind(row, rowNumber),");
            }

            builder.AppendLine("_ => null");
            builder.Unindent();
            builder.AppendLine("};");
        }
        else
        {
            // Multi-char path: use TryGetColumnSpan and string comparison
            builder.AppendLine($"if (!row.TryGetColumnSpan({descriptor.DiscriminatorIndex}, out var span))");
            builder.AppendLine("    return null;");
            builder.AppendLine();

            // Group mappings by length for efficient comparison
            var byLength = descriptor.Mappings.GroupBy(m => m.DiscriminatorValue.Length).OrderBy(g => g.Key).ToList();

            builder.AppendLine("return span.Length switch");
            builder.AppendLine("{");
            builder.Indent();

            foreach (var group in byLength)
            {
                var length = group.Key;
                var mappings = group.ToList();

                if (mappings.Count == 1)
                {
                    var mapping = mappings[0];
                    builder.AppendLine($"{length} when SpanEqualsBYTE(span, \"{EscapeString(mapping.DiscriminatorValue)}\"u8) => _byteBinder_{mapping.SafeTypeName}.Bind(row, rowNumber),");
                }
                else
                {
                    // Multiple mappings with same length - need nested checks
                    builder.AppendLine($"{length} => Dispatch{length}BYTE(span, row, rowNumber),");
                }
            }

            builder.AppendLine("_ => null");
            builder.Unindent();
            builder.AppendLine("};");

            // Emit helper methods for same-length groups
            builder.Unindent();
            builder.AppendLine("}");
            builder.AppendLine();

            foreach (var group in byLength.Where(g => g.Count() > 1))
            {
                EmitSameLengthDispatcher(builder, group.Key, [.. group]);
            }

            // Emit SpanEquals helper
            EmitSpanEqualsHelper(builder, descriptor.CaseInsensitive);

            // Re-open class for proper formatting
            return;
        }

        builder.Unindent();
        builder.AppendLine("}");
        builder.AppendLine();
    }

    private static void EmitSameLengthDispatcher(SourceBuilder builder, int length, List<DiscriminatorMapping> mappings)
    {
        builder.AppendLine("[MethodImpl(MethodImplOptions.AggressiveInlining)]");
        builder.AppendLine($"private static object? Dispatch{length}BYTE(ReadOnlySpan<byte> span, {BYTE_ROW_TYPE} row, int rowNumber)");
        builder.AppendLine("{");
        builder.Indent();

        foreach (var mapping in mappings)
        {
            builder.AppendLine($"if (SpanEqualsBYTE(span, \"{EscapeString(mapping.DiscriminatorValue)}\"u8))");
            builder.AppendLine($"    return _byteBinder_{mapping.SafeTypeName}.Bind(row, rowNumber);");
        }

        builder.AppendLine("return null;");
        builder.Unindent();
        builder.AppendLine("}");
        builder.AppendLine();
    }

    private static void EmitSpanEqualsHelper(SourceBuilder builder, bool caseInsensitive)
    {
        builder.AppendLine("[MethodImpl(MethodImplOptions.AggressiveInlining)]");
        // For bytes, use ReadOnlySpan<byte> and u8 string literals for zero-allocation comparison
        builder.AppendLine("private static bool SpanEqualsBYTE(ReadOnlySpan<byte> span, ReadOnlySpan<byte> value)");
        builder.AppendLine("{");
        builder.Indent();

        if (caseInsensitive)
        {
            // Case-insensitive byte comparison requires manual ASCII lowercasing
            builder.AppendLine("if (span.Length != value.Length) return false;");
            builder.AppendLine("for (int i = 0; i < span.Length; i++)");
            builder.AppendLine("{");
            builder.AppendLine("    byte a = span[i];");
            builder.AppendLine("    byte b = value[i];");
            builder.AppendLine("    if ((uint)(a - 'A') <= 'Z' - 'A') a = (byte)(a + 32);");
            builder.AppendLine("    if ((uint)(b - 'A') <= 'Z' - 'A') b = (byte)(b + 32);");
            builder.AppendLine("    if (a != b) return false;");
            builder.AppendLine("}");
            builder.AppendLine("return true;");
        }
        else
        {
            builder.AppendLine("return span.SequenceEqual(value);");
        }

        builder.Unindent();
        builder.AppendLine("}");
        builder.AppendLine();
    }

    private sealed record DispatcherDescriptor(
        string ClassName,
        string Namespace,
        string SimpleClassName,
        int DiscriminatorIndex,
        string? DiscriminatorColumn,
        bool CaseInsensitive,
        List<DiscriminatorMapping> Mappings);

    private sealed record DiscriminatorMapping(
        string DiscriminatorValue,
        string MethodName,
        string ReturnTypeName,
        string SafeTypeName,
        bool HasGenerateBinderAttribute,
        Location? Location);
}
