using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using static HeroParser.Generators.GeneratorHelpers;

namespace HeroParser.Generators;

/// <summary>
/// Source generator for CSV record binders.
/// Generates inline binder classes for maximum performance binding.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class CsvRecordBinderGenerator : IIncrementalGenerator
{
    private const string GENERATED_BYTE_NAMESPACE = "HeroParser.SeparatedValues.Reading.Binding";
    private const string BINDER_FACTORY_TYPE = "global::HeroParser.SeparatedValues.Reading.Binders.CsvRecordBinderFactory";
    private const string BYTE_BINDER_INTERFACE_TYPE = "global::HeroParser.SeparatedValues.Reading.Binders.ICsvBinder<byte, ";
    private const string BYTE_ROW_TYPE = "global::HeroParser.SeparatedValues.Reading.Rows.CsvRow<byte>";
    private const string OPTIONS_TYPE = "global::HeroParser.SeparatedValues.Reading.Records.CsvRecordOptions";
    private const string EXCEPTION_TYPE = "global::HeroParser.SeparatedValues.Core.CsvException";
    private const string ERROR_CODE_TYPE = "global::HeroParser.SeparatedValues.Core.CsvErrorCode";
    private const string WRITER_TYPE = "global::HeroParser.SeparatedValues.Writing.CsvRecordWriter";
    private const string WRITER_FACTORY_TYPE = "global::HeroParser.SeparatedValues.Writing.CsvRecordWriterFactory";

    private static readonly string[] generateAttributeNames =
    [
        "HeroParser.SeparatedValues.Reading.Shared.CsvGenerateBinderAttribute",
        "HeroParser.CsvGenerateBinderAttribute"
    ];

    private static readonly string[] columnAttributeNames =
    [
        "HeroParser.SeparatedValues.Reading.Shared.CsvColumnAttribute",
        "HeroParser.CsvColumnAttribute"
    ];

#pragma warning disable RS2008 // Enable analyzer release tracking - not needed for internal generator
    private static readonly DiagnosticDescriptor unsupportedPropertyTypeDiagnostic = new(
        "HERO001",
        "Unsupported property type",
        "Property '{0}' of type '{1}' is not supported by the CSV record binder generator. Supported types include primitives, DateTime, DateTimeOffset, DateOnly, TimeOnly, Guid, TimeZoneInfo, and enums.",
        "HeroParser.Generators",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
#pragma warning restore RS2008

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Use ForAttributeWithMetadataName for better caching - register for both attribute names
        var provider1 = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                generateAttributeNames[0],
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax or StructDeclarationSyntax,
                transform: static (ctx, ct) => TransformToDescriptor(ctx, ct))
            .Where(static x => x is not null);

        var provider2 = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                generateAttributeNames[1],
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax or StructDeclarationSyntax,
                transform: static (ctx, ct) => TransformToDescriptor(ctx, ct))
            .Where(static x => x is not null);

        // Combine both providers
        var combined = provider1.Collect().Combine(provider2.Collect());

        // Generate per-type typed binder files for better incrementality
        context.RegisterSourceOutput(provider1, static (spc, descriptor) => EmitTypedBinder(spc, descriptor!));
        context.RegisterSourceOutput(provider2, static (spc, descriptor) => EmitTypedBinder(spc, descriptor!));

        // Generate registration file
        context.RegisterSourceOutput(combined, static (spc, tuple) =>
        {
            var all = tuple.Left.Concat(tuple.Right).Where(x => x is not null).ToList();
            if (all.Count > 0)
                EmitRegistration(spc, all!);
        });
    }

    private static TypeDescriptor? TransformToDescriptor(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol symbol)
            return null;

        if (symbol.IsAbstract || !IsTypeAccessible(symbol))
            return null;

        return BuildDescriptor(symbol, ct);
    }

    private static void EmitTypedBinder(SourceProductionContext context, TypeDescriptor descriptor)
    {
        // Report diagnostics
        foreach (var diag in descriptor.Diagnostics)
        {
            context.ReportDiagnostic(diag);
        }

        var binderMembers = GetMembersWithSetters(descriptor.Members);
        if (binderMembers.Count == 0)
            return;

        // Emit byte-based binder only (UTF-8 is the primary path, char uses scalar fallback)
        var byteBuilder = new SourceBuilder(16384);
        byteBuilder.AppendLine("// <auto-generated/>");
        byteBuilder.AppendLine("#nullable enable");
        byteBuilder.AppendLine("using System;");
        byteBuilder.AppendLine("using System.Buffers.Text;");
        byteBuilder.AppendLine("using System.Collections.Generic;");
        byteBuilder.AppendLine("using System.Globalization;");
        byteBuilder.AppendLine("using System.Runtime.CompilerServices;");
        byteBuilder.AppendLine("using System.Text;");
        byteBuilder.AppendLine();
        byteBuilder.AppendLine($"namespace {GENERATED_BYTE_NAMESPACE};");
        byteBuilder.AppendLine();

        EmitInlineByteBinderClass(byteBuilder, descriptor.FullyQualifiedName, descriptor.SafeClassName, binderMembers);

        context.AddSource($"CsvInlineByteBinder.{descriptor.SafeClassName}.g.cs", byteBuilder.ToString());
    }

    #region Byte-Based Binder Generation

    private static void EmitInlineByteBinderClass(SourceBuilder builder, string fullyQualifiedName, string safeClassName, IReadOnlyList<MemberDescriptor> members)
    {
        var binderClassName = $"CsvInlineByteBinder_{safeClassName}";
        var allHaveExplicitIndex = members.All(m => m.AttributeIndex.HasValue);

        builder.AppendLine($"internal sealed class {binderClassName} : {BYTE_BINDER_INTERFACE_TYPE}{fullyQualifiedName}>");
        builder.AppendLine("{");
        builder.Indent();

        // Emit fields
        builder.AppendLine("private readonly CultureInfo _culture;");
        builder.AppendLine("private readonly bool _caseSensitiveHeaders;");
        builder.AppendLine("private readonly bool _allowMissingColumns;");
        builder.AppendLine("private readonly HashSet<string>? _nullValues;");
        builder.AppendLine();

        // Emit column index fields
        foreach (var member in members)
        {
            var defaultIndex = member.AttributeIndex?.ToString() ?? "-1";
            builder.AppendLine($"private int _{member.MemberName}Index = {defaultIndex};");
        }
        builder.AppendLine($"private bool _resolved = {(allHaveExplicitIndex ? "true" : "false")};");
        builder.AppendLine();

        // Emit constructor
        builder.AppendLine($"public {binderClassName}({OPTIONS_TYPE}? options)");
        builder.AppendLine("{");
        builder.Indent();
        builder.AppendLine("_culture = options?.Culture ?? CultureInfo.InvariantCulture;");
        builder.AppendLine("_caseSensitiveHeaders = options?.CaseSensitiveHeaders ?? false;");
        builder.AppendLine("_allowMissingColumns = options?.AllowMissingColumns ?? false;");
        builder.AppendLine("_nullValues = options?.NullValues is { Count: > 0 }");
        builder.AppendLine("    ? new HashSet<string>(options.NullValues, StringComparer.Ordinal)");
        builder.AppendLine("    : null;");
        builder.Unindent();
        builder.AppendLine("}");
        builder.AppendLine();

        // Emit NeedsHeaderResolution property
        builder.AppendLine("public bool NeedsHeaderResolution => !_resolved;");
        builder.AppendLine();

        // Emit BindHeader method
        EmitByteBindHeaderMethod(builder, members);

        // Emit Bind method
        EmitByteBindMethod(builder, fullyQualifiedName);

        // Emit BindInto method (the hot path with inline code)
        EmitByteBindIntoMethod(builder, fullyQualifiedName, members);

        // Emit helper methods
        EmitByteFindHeaderIndexMethod(builder);
        EmitByteIsNullValueMethod(builder);
        EmitByteCreateParseExceptionMethod(builder);

        // Emit factory method for registration
        builder.AppendLine($"public static {BYTE_BINDER_INTERFACE_TYPE}{fullyQualifiedName}> Create({OPTIONS_TYPE}? options)");
        builder.AppendLine("{");
        builder.Indent();
        builder.AppendLine($"return new {binderClassName}(options);");
        builder.Unindent();
        builder.AppendLine("}");

        builder.Unindent();
        builder.AppendLine("}");
        builder.AppendLine();
    }

    private static void EmitByteBindHeaderMethod(SourceBuilder builder, IReadOnlyList<MemberDescriptor> members)
    {
        builder.AppendLine($"public void BindHeader({BYTE_ROW_TYPE} headerRow, int rowNumber)");
        builder.AppendLine("{");
        builder.Indent();
        builder.AppendLine("if (_resolved) return;");
        builder.AppendLine();
        builder.AppendLine("var comparer = _caseSensitiveHeaders ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;");
        builder.AppendLine();

        foreach (var member in members)
        {
            builder.AppendLine($"_{member.MemberName}Index = FindHeaderIndex(headerRow, \"{member.HeaderName}\", comparer);");
        }
        builder.AppendLine();

        // Validate required columns
        foreach (var member in members)
        {
            var isRequired = !member.IsNullable && member.BaseTypeName != "string";
            if (isRequired)
            {
                builder.AppendLine($"if (_{member.MemberName}Index < 0 && !_allowMissingColumns)");
                builder.AppendLine("{");
                builder.Indent();
                builder.AppendLine($"throw new {EXCEPTION_TYPE}({ERROR_CODE_TYPE}.ParseError, \"Required column '{member.HeaderName}' not found in header row.\", rowNumber, 0);");
                builder.Unindent();
                builder.AppendLine("}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("_resolved = true;");
        builder.Unindent();
        builder.AppendLine("}");
        builder.AppendLine();
    }

    private static void EmitByteBindMethod(SourceBuilder builder, string fullyQualifiedName)
    {
        builder.AppendLine("[MethodImpl(MethodImplOptions.AggressiveInlining)]");
        builder.AppendLine($"public bool TryBind({BYTE_ROW_TYPE} row, int rowNumber, out {fullyQualifiedName} result)");
        builder.AppendLine("{");
        builder.Indent();
        builder.AppendLine($"result = new {fullyQualifiedName}();");
        builder.AppendLine("return BindInto(ref result, row, rowNumber);");
        builder.Unindent();
        builder.AppendLine("}");
        builder.AppendLine();
    }

    private static void EmitByteBindIntoMethod(SourceBuilder builder, string fullyQualifiedName, IReadOnlyList<MemberDescriptor> members)
    {
        builder.AppendLine("[MethodImpl(MethodImplOptions.AggressiveInlining)]");
        builder.AppendLine($"public bool BindInto(ref {fullyQualifiedName} instance, {BYTE_ROW_TYPE} row, int rowNumber)");
        builder.AppendLine("{");
        builder.Indent();
        builder.AppendLine("var columnCount = row.ColumnCount;");
        builder.AppendLine();

        foreach (var member in members)
        {
            EmitByteInlinePropertyBinding(builder, member);
        }

        builder.AppendLine("return true;");
        builder.Unindent();
        builder.AppendLine("}");
        builder.AppendLine();
    }

    private static void EmitByteInlinePropertyBinding(SourceBuilder builder, MemberDescriptor member)
    {
        var indexField = $"_{member.MemberName}Index";
        var isRequired = !member.IsNullable && member.BaseTypeName != "string";

        builder.AppendLine($"if ((uint){indexField} < (uint)columnCount)");
        builder.AppendLine("{");
        builder.Indent();
        builder.AppendLine($"var column = row[{indexField}];");
        builder.AppendLine("var utf8 = column.Span;");

        // Check for null values if configured
        builder.AppendLine("if (_nullValues is null || !IsNullValue(utf8, _nullValues))");
        builder.AppendLine("{");
        builder.Indent();

        // Emit inline parsing logic
        EmitByteInlineParsingLogic(builder, member);

        builder.Unindent();
        builder.AppendLine("}");
        builder.Unindent();
        builder.AppendLine("}");

        // Required column check for missing data
        if (isRequired)
        {
            builder.AppendLine($"else if ({indexField} >= 0 && !_allowMissingColumns)");
            builder.AppendLine("{");
            builder.Indent();
            builder.AppendLine($"throw new {EXCEPTION_TYPE}({ERROR_CODE_TYPE}.ParseError, $\"Row has only {{columnCount}} columns but '{member.HeaderName}' expects index {{{indexField}}}.\", rowNumber, {indexField} + 1);");
            builder.Unindent();
            builder.AppendLine("}");
        }
        builder.AppendLine();
    }

    private static void EmitByteInlineParsingLogic(SourceBuilder builder, MemberDescriptor member)
    {
        var baseType = member.BaseTypeName;
        var propertyName = member.MemberName;

        switch (baseType)
        {
            case "string":
                builder.AppendLine($"instance.{propertyName} = Encoding.UTF8.GetString(utf8);");
                break;

            case "int":
            case "System.Int32":
                EmitByteNumericParsing(builder, member, "TryParseInt32");
                break;

            case "long":
            case "System.Int64":
                EmitByteNumericParsing(builder, member, "TryParseInt64");
                break;

            case "short":
            case "System.Int16":
                EmitByteNumericParsing(builder, member, "TryParseInt16");
                break;

            case "byte":
            case "System.Byte":
                EmitByteNumericParsing(builder, member, "TryParseByte");
                break;

            case "uint":
            case "System.UInt32":
                EmitByteNumericParsing(builder, member, "TryParseUInt32");
                break;

            case "ulong":
            case "System.UInt64":
                EmitByteNumericParsing(builder, member, "TryParseUInt64");
                break;

            case "ushort":
            case "System.UInt16":
                EmitByteNumericParsing(builder, member, "TryParseUInt16");
                break;

            case "sbyte":
            case "System.SByte":
                EmitByteNumericParsing(builder, member, "TryParseSByte");
                break;

            case "decimal":
            case "System.Decimal":
                EmitByteNumericParsing(builder, member, "TryParseDecimal");
                break;

            case "double":
            case "System.Double":
                EmitByteNumericParsing(builder, member, "TryParseDouble");
                break;

            case "float":
            case "System.Single":
                EmitByteNumericParsing(builder, member, "TryParseSingle");
                break;

            case "bool":
            case "System.Boolean":
                EmitByteBooleanParsing(builder, member);
                break;

            case "System.DateTime":
                EmitByteDateTimeParsing(builder, member, "TryParseDateTime");
                break;

            case "System.DateTimeOffset":
                EmitByteDateTimeParsing(builder, member, "TryParseDateTimeOffset");
                break;

            case "System.DateOnly":
                EmitByteDateTimeParsing(builder, member, "TryParseDateOnly");
                break;

            case "System.TimeOnly":
                EmitByteDateTimeParsing(builder, member, "TryParseTimeOnly");
                break;

            case "System.Guid":
                EmitByteGuidParsing(builder, member);
                break;

            default:
                if (member.IsEnum)
                {
                    EmitByteEnumParsing(builder, member);
                }
                else
                {
                    builder.AppendLine($"instance.{propertyName} = Encoding.UTF8.GetString(utf8);");
                }
                break;
        }
    }

    private static void EmitByteNumericParsing(SourceBuilder builder, MemberDescriptor member, string parseMethod)
    {
        var propertyName = member.MemberName;
        var indexField = $"_{propertyName}Index";
        var isNullable = member.IsNullable;
        var baseType = member.BaseTypeName;

        // For floating-point types (decimal, double, float), use culture-aware parsing
        // Utf8Parser only supports invariant culture, but users may need locale-specific decimal separators
        if (baseType is "decimal" or "System.Decimal")
        {
            builder.AppendLine($"if (decimal.TryParse(column.ToString(), NumberStyles.Number, _culture, out var parsed_{propertyName}))");
            builder.AppendLine($"    instance.{propertyName} = parsed_{propertyName};");
        }
        else if (baseType is "double" or "System.Double")
        {
            builder.AppendLine($"if (double.TryParse(column.ToString(), NumberStyles.Float | NumberStyles.AllowThousands, _culture, out var parsed_{propertyName}))");
            builder.AppendLine($"    instance.{propertyName} = parsed_{propertyName};");
        }
        else if (baseType is "float" or "System.Single")
        {
            builder.AppendLine($"if (float.TryParse(column.ToString(), NumberStyles.Float | NumberStyles.AllowThousands, _culture, out var parsed_{propertyName}))");
            builder.AppendLine($"    instance.{propertyName} = parsed_{propertyName};");
        }
        else
        {
            // For integer types, Utf8Parser is efficient and invariant culture is standard
            builder.AppendLine($"if (column.{parseMethod}(out var parsed_{propertyName}))");
            builder.AppendLine($"    instance.{propertyName} = parsed_{propertyName};");
        }
        if (!isNullable)
        {
            builder.AppendLine($"else if (!utf8.IsEmpty)");
            builder.AppendLine($"    throw CreateParseException(utf8, \"{member.HeaderName}\", rowNumber, {indexField});");
        }
    }

    private static void EmitByteBooleanParsing(SourceBuilder builder, MemberDescriptor member)
    {
        var propertyName = member.MemberName;
        var indexField = $"_{propertyName}Index";
        var isNullable = member.IsNullable;

        builder.AppendLine($"if (column.TryParseBoolean(out var parsed_{propertyName}))");
        builder.AppendLine($"    instance.{propertyName} = parsed_{propertyName};");
        if (!isNullable)
        {
            builder.AppendLine($"else if (!utf8.IsEmpty)");
            builder.AppendLine($"    throw CreateParseException(utf8, \"{member.HeaderName}\", rowNumber, {indexField});");
        }
    }

    private static void EmitByteDateTimeParsing(SourceBuilder builder, MemberDescriptor member, string parseMethod)
    {
        var propertyName = member.MemberName;
        var indexField = $"_{propertyName}Index";
        var format = member.Format;
        var isNullable = member.IsNullable;

        if (format != null)
        {
            // Use format string with culture
            builder.AppendLine($"if (column.{parseMethod}(out var parsed_{propertyName}, \"{EscapeString(format)}\", _culture))");
        }
        else
        {
            // Use culture for parsing
            builder.AppendLine($"if (column.{parseMethod}(out var parsed_{propertyName}, _culture))");
        }
        builder.AppendLine($"    instance.{propertyName} = parsed_{propertyName};");
        if (!isNullable)
        {
            builder.AppendLine($"else if (!utf8.IsEmpty)");
            builder.AppendLine($"    throw CreateParseException(utf8, \"{member.HeaderName}\", rowNumber, {indexField});");
        }
    }

    private static void EmitByteGuidParsing(SourceBuilder builder, MemberDescriptor member)
    {
        var propertyName = member.MemberName;
        var indexField = $"_{propertyName}Index";
        var isNullable = member.IsNullable;

        builder.AppendLine($"if (column.TryParseGuid(out var parsed_{propertyName}))");
        builder.AppendLine($"    instance.{propertyName} = parsed_{propertyName};");
        if (!isNullable)
        {
            builder.AppendLine($"else if (!utf8.IsEmpty)");
            builder.AppendLine($"    throw CreateParseException(utf8, \"{member.HeaderName}\", rowNumber, {indexField});");
        }
    }

    private static void EmitByteEnumParsing(SourceBuilder builder, MemberDescriptor member)
    {
        var propertyName = member.MemberName;
        var indexField = $"_{propertyName}Index";
        var enumType = member.TypeofTypeName;
        var isNullable = member.IsNullable;

        builder.AppendLine($"if (column.TryParseEnum<{enumType}>(out var parsed_{propertyName}))");
        builder.AppendLine($"    instance.{propertyName} = parsed_{propertyName};");
        if (!isNullable)
        {
            builder.AppendLine($"else if (!utf8.IsEmpty)");
            builder.AppendLine($"    throw CreateParseException(utf8, \"{member.HeaderName}\", rowNumber, {indexField});");
        }
    }

    private static void EmitByteFindHeaderIndexMethod(SourceBuilder builder)
    {
        builder.AppendLine("[MethodImpl(MethodImplOptions.AggressiveInlining)]");
        builder.AppendLine($"private static int FindHeaderIndex({BYTE_ROW_TYPE} headerRow, string name, StringComparer comparer)");
        builder.AppendLine("{");
        builder.Indent();
        builder.AppendLine("var count = headerRow.ColumnCount;");
        builder.AppendLine("for (int i = 0; i < count; i++)");
        builder.AppendLine("{");
        builder.Indent();
        builder.AppendLine("if (comparer.Equals(Encoding.UTF8.GetString(headerRow[i].Span), name))");
        builder.AppendLine("    return i;");
        builder.Unindent();
        builder.AppendLine("}");
        builder.AppendLine("return -1;");
        builder.Unindent();
        builder.AppendLine("}");
        builder.AppendLine();
    }

    private static void EmitByteIsNullValueMethod(SourceBuilder builder)
    {
        builder.AppendLine("[MethodImpl(MethodImplOptions.AggressiveInlining)]");
        builder.AppendLine("private static bool IsNullValue(ReadOnlySpan<byte> value, HashSet<string> nullValues)");
        builder.AppendLine("{");
        builder.Indent();
        builder.AppendLine("return nullValues.Contains(Encoding.UTF8.GetString(value));");
        builder.Unindent();
        builder.AppendLine("}");
        builder.AppendLine();
    }

    private static void EmitByteCreateParseExceptionMethod(SourceBuilder builder)
    {
        builder.AppendLine("private static Exception CreateParseException(ReadOnlySpan<byte> value, string columnName, int rowNumber, int columnIndex)");
        builder.AppendLine("{");
        builder.Indent();
        builder.AppendLine("var str = Encoding.UTF8.GetString(value);");
        builder.AppendLine("var fieldValue = str.Length > 100 ? str[..100] + \"...\" : str;");
        builder.AppendLine($"return new {EXCEPTION_TYPE}({ERROR_CODE_TYPE}.ParseError, $\"Failed to parse '{{columnName}}' at row {{rowNumber}}, column {{columnIndex + 1}}. Value: '{{fieldValue}}'\", rowNumber, columnIndex + 1, fieldValue);");
        builder.Unindent();
        builder.AppendLine("}");
        builder.AppendLine();
    }

    #endregion

    private static void EmitRegistration(SourceProductionContext context, IReadOnlyList<TypeDescriptor> descriptors)
    {
        var builder = new SourceBuilder(8192);
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        builder.AppendLine("using System;");
        builder.AppendLine("using System.Collections.Generic;");
        builder.AppendLine("using System.Runtime.CompilerServices;");
        builder.AppendLine();
        builder.AppendLine($"namespace {GENERATED_BYTE_NAMESPACE};");
        builder.AppendLine();
        builder.AppendLine("file static class CsvRecordBinderGeneratedRegistration");
        builder.AppendLine("{");
        builder.Indent();
        builder.AppendLine("[ModuleInitializer]");
        builder.AppendLine("internal static void Register()");
        builder.AppendLine("{");
        builder.Indent();

        foreach (var descriptor in descriptors)
        {
            // Filter members for binder (those with setters)
            var binderMembers = GetMembersWithSetters(descriptor.Members);
            if (binderMembers.Count > 0)
            {
                // Register descriptor-based binder for performance
                EmitTypedBinderRegistration(builder, descriptor.FullyQualifiedName, descriptor.SafeClassName);
            }

            // Filter members for writer (those with getters)
            var writerMembers = GetMembersWithGetters(descriptor.Members);
            if (writerMembers.Count > 0)
            {
                EmitWriterRegistration(builder, descriptor.FullyQualifiedName, writerMembers);
            }
        }

        builder.Unindent();
        builder.AppendLine("}");
        builder.Unindent();
        builder.AppendLine("}");

        context.AddSource("CsvRecordBinderFactory.Registration.g.cs", builder.ToString());
    }

    private static void EmitTypedBinderRegistration(SourceBuilder builder, string fullyQualifiedName, string safeClassName)
    {
        // Register byte-based binder only (UTF-8 is the primary path)
        var byteBinderClassName = $"global::{GENERATED_BYTE_NAMESPACE}.CsvInlineByteBinder_{safeClassName}";
        builder.AppendLine($"{BINDER_FACTORY_TYPE}.RegisterByteBinder<{fullyQualifiedName}>({byteBinderClassName}.Create);");
    }

    private static List<MemberDescriptor> GetMembersWithSetters(IReadOnlyList<MemberDescriptor> members)
    {
        var result = new List<MemberDescriptor>(members.Count);
        foreach (var m in members)
        {
            if (m.SetterFactory != null)
                result.Add(m);
        }
        return result;
    }

    private static List<MemberDescriptor> GetMembersWithGetters(IReadOnlyList<MemberDescriptor> members)
    {
        var result = new List<MemberDescriptor>(members.Count);
        foreach (var m in members)
        {
            if (m.GetterFactory != null)
                result.Add(m);
        }
        return result;
    }

    private static void EmitWriterRegistration(SourceBuilder builder, string fullyQualifiedName, IReadOnlyList<MemberDescriptor> members)
    {
        builder.AppendLine($"{WRITER_FACTORY_TYPE}.RegisterGeneratedWriter(typeof({fullyQualifiedName}), options => {WRITER_TYPE}<{fullyQualifiedName}>.CreateFromTemplates(options, new {WRITER_TYPE}<{fullyQualifiedName}>.WriterTemplate[]");
        builder.AppendLine("{");
        builder.Indent();

        foreach (var member in members)
        {
            builder.AppendLine($"new {WRITER_TYPE}<{fullyQualifiedName}>.WriterTemplate(");
            builder.Indent();
            builder.AppendLine($"\"{member.MemberName}\",");
            builder.AppendLine($"typeof({member.TypeofTypeName}),");
            builder.AppendLine($"\"{member.HeaderName}\",");
            builder.AppendLine(member.AttributeIndex is null ? "null," : $"{member.AttributeIndex},");
            builder.AppendLine(member.Format is null ? "null," : $"\"{member.Format}\",");
            builder.AppendLine($"{member.GetterFactory}),");
            builder.Unindent();
        }

        builder.Unindent();
        builder.AppendLine("}));");
        builder.AppendLine();
    }

    private static TypeDescriptor? BuildDescriptor(INamedTypeSymbol type, CancellationToken ct)
    {
        var members = new List<MemberDescriptor>();
        var diagnostics = new List<Diagnostic>();

        foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
        {
            ct.ThrowIfCancellationRequested();

            if (property.IsStatic)
                continue;

            var hasSetter = property.SetMethod is { DeclaredAccessibility: Accessibility.Public };
            var hasGetter = property.GetMethod is { DeclaredAccessibility: Accessibility.Public };

            if (!hasSetter && !hasGetter)
                continue;

            var mapAttribute = GetFirstMatchingAttribute(property, columnAttributeNames);
            var headerName = property.Name;
            int? attributeIndex = null;
            string? format = null;

            if (mapAttribute is not null)
            {
#pragma warning disable IDE0010 // Populate switch - intentionally not exhaustive
                foreach (var arg in mapAttribute.NamedArguments)
                {
                    switch (arg.Key)
                    {
                        case "Name" when arg.Value.Value is string s && !string.IsNullOrWhiteSpace(s):
                            headerName = s;
                            break;
                        case "Index" when arg.Value.Value is int i && i >= 0:
                            attributeIndex = i;
                            break;
                        case "Format" when arg.Value.Value is string f && !string.IsNullOrWhiteSpace(f):
                            format = f;
                            break;
                    }
                }
#pragma warning restore IDE0010
            }

            if (!IsSupportedType(property.Type))
            {
                diagnostics.Add(Diagnostic.Create(
                    unsupportedPropertyTypeDiagnostic,
                    property.Locations.FirstOrDefault() ?? Location.None,
                    property.Name,
                    property.Type.ToDisplayString()));
                continue;
            }

            var typeName = property.Type.ToDisplayString(FullyQualifiedFormatWithNullable);
            var typeofTypeName = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var headerLiteral = EscapeString(headerName);
            var formatLiteral = format != null ? EscapeString(format) : null;

            var (baseTypeName, isNullable, isEnum) = GetBaseTypeInfo(property.Type);
            var isReadOnlyMemoryChar = IsReadOnlyMemoryChar(property.Type);

            var setterFactory = hasSetter ? CreateSetter(typeName, type, property.Name) : null;
            var getterFactory = hasGetter ? CreateGetter(type, property.Name) : null;

            members.Add(new MemberDescriptor(
                property.Name,
                headerLiteral,
                attributeIndex,
                typeName,
                typeofTypeName,
                formatLiteral,
                setterFactory,
                getterFactory,
                baseTypeName,
                isNullable,
                isEnum,
                isReadOnlyMemoryChar));
        }

        if (members.Count == 0 && diagnostics.Count == 0)
            return null;

        var fqName = type.ToDisplayString(FullyQualifiedFormatWithNullable);
        var safeClassName = CreateSafeClassName(type);
        return new TypeDescriptor(fqName, safeClassName, members, diagnostics);
    }

    private static (string baseTypeName, bool isNullable, bool isEnum) GetBaseTypeInfo(ITypeSymbol type)
    {
        var isNullable = false;
        var actualType = type;

        if (type is INamedTypeSymbol { IsGenericType: true, Name: "Nullable" } nullable)
        {
            isNullable = true;
            actualType = nullable.TypeArguments[0];
        }

        if (type.NullableAnnotation == NullableAnnotation.Annotated && type.OriginalDefinition.SpecialType == SpecialType.None)
        {
            isNullable = true;
        }

        var isEnum = actualType.TypeKind == TypeKind.Enum;
        var baseTypeName = actualType.ToDisplayString();

        return (baseTypeName, isNullable, isEnum);
    }

    private static string CreateSetter(string typeName, INamedTypeSymbol type, string propertyName)
        => $"({type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} target, object? val) => target.{propertyName} = ({typeName})val!";

    private static string CreateGetter(INamedTypeSymbol type, string propertyName)
        => $"({type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} target) => target.{propertyName}";

    private sealed record TypeDescriptor(
        string FullyQualifiedName,
        string SafeClassName,
        IReadOnlyList<MemberDescriptor> Members,
        IReadOnlyList<Diagnostic> Diagnostics);

    private sealed record MemberDescriptor(
        string MemberName,
        string HeaderName,
        int? AttributeIndex,
        string TypeName,
        string TypeofTypeName,
        string? Format,
        string? SetterFactory,
        string? GetterFactory,
        string BaseTypeName,
        bool IsNullable,
        bool IsEnum,
        bool IsReadOnlyMemoryChar);
}
