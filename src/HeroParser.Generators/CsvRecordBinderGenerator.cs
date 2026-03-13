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

    private static readonly DiagnosticDescriptor missingNameOrIndexDiagnostic = new(
        "HERO008",
        "CsvColumn requires Name or Index",
        "Property '{0}' has [CsvColumn] but neither Name nor Index is specified. Set Name or Index explicitly.",
        "HeroParser.Generators",
        DiagnosticSeverity.Error,
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
        byteBuilder.AppendLine("using HeroParser.Validation;");
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

        // Emit static Regex fields for Pattern validation
        foreach (var member in members)
        {
            if (member.ValidationPattern != null)
            {
                var escapedPattern = EscapeString(member.ValidationPattern);
                builder.AppendLine($"private static readonly System.Text.RegularExpressions.Regex _pattern_{member.MemberName} = new(\"{escapedPattern}\", System.Text.RegularExpressions.RegexOptions.Compiled, TimeSpan.FromMilliseconds({member.ValidationPatternTimeoutMs}));");
            }
        }

        // Emit fields
        builder.AppendLine("private readonly CultureInfo _culture;");
        builder.AppendLine("private readonly bool _caseSensitiveHeaders;");
        builder.AppendLine("private readonly bool _allowMissingColumns;");
        builder.AppendLine("private readonly byte[][]? _nullValuesUtf8;");
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
        builder.AppendLine("_nullValuesUtf8 = options?.NullValues is { Count: > 0 }");
        builder.AppendLine("    ? CreateNullValues(options.NullValues)");
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
        EmitByteIsAllAsciiWhiteSpaceMethod(builder);

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
        builder.AppendLine($"public bool TryBind({BYTE_ROW_TYPE} row, int rowNumber, out {fullyQualifiedName} result, List<ValidationError>? errors = null)");
        builder.AppendLine("{");
        builder.Indent();
        builder.AppendLine($"result = new {fullyQualifiedName}();");
        builder.AppendLine("return BindInto(ref result, row, rowNumber, errors);");
        builder.Unindent();
        builder.AppendLine("}");
        builder.AppendLine();
    }

    private static void EmitByteBindIntoMethod(SourceBuilder builder, string fullyQualifiedName, IReadOnlyList<MemberDescriptor> members)
    {
        bool anyValidation = HasAnyValidation(members);

        builder.AppendLine("[MethodImpl(MethodImplOptions.AggressiveInlining)]");
        builder.AppendLine($"public bool BindInto(ref {fullyQualifiedName} instance, {BYTE_ROW_TYPE} row, int rowNumber, List<ValidationError>? errors = null)");
        builder.AppendLine("{");
        builder.Indent();
        builder.AppendLine("var columnCount = row.ColumnCount;");
        builder.AppendLine();

        if (anyValidation)
        {
            builder.AppendLine("bool valid = true;");
            builder.AppendLine();
        }

        foreach (var member in members)
        {
            EmitByteInlinePropertyBinding(builder, member);
        }

        builder.AppendLine(anyValidation ? "return valid;" : "return true;");
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
        builder.AppendLine("if (_nullValuesUtf8 is null || !IsNullValue(utf8, _nullValuesUtf8))");
        builder.AppendLine("{");
        builder.Indent();

        // Emit inline parsing logic
        EmitByteInlineParsingLogic(builder, member);

        // Emit validation checks after parsing
        if (HasAnyValidation(member))
        {
            EmitValidationChecks(builder, member);
        }

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
            // When ValidationNotNull is set, let the NotNull validation handle empty/whitespace with a soft error;
            // otherwise throw a hard parse error for any failure including empty/whitespace values.
            if (member.ValidationNotNull)
                builder.AppendLine("else if (!IsAllAsciiWhiteSpace(utf8))");
            else
                builder.AppendLine("else");
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
            if (member.ValidationNotNull)
                builder.AppendLine("else if (!IsAllAsciiWhiteSpace(utf8))");
            else
                builder.AppendLine("else");
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
            if (member.ValidationNotNull)
                builder.AppendLine("else if (!IsAllAsciiWhiteSpace(utf8))");
            else
                builder.AppendLine("else");
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
            if (member.ValidationNotNull)
                builder.AppendLine("else if (!IsAllAsciiWhiteSpace(utf8))");
            else
                builder.AppendLine("else");
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
            if (member.ValidationNotNull)
                builder.AppendLine("else if (!IsAllAsciiWhiteSpace(utf8))");
            else
                builder.AppendLine("else");
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
        builder.AppendLine("private static byte[][] CreateNullValues(IReadOnlyList<string> nullValues)");
        builder.AppendLine("{");
        builder.Indent();
        builder.AppendLine("var encoded = new byte[nullValues.Count][];");
        builder.AppendLine("for (int i = 0; i < nullValues.Count; i++)");
        builder.AppendLine("{");
        builder.Indent();
        builder.AppendLine("encoded[i] = Encoding.UTF8.GetBytes(nullValues[i]);");
        builder.Unindent();
        builder.AppendLine("}");
        builder.AppendLine("return encoded;");
        builder.Unindent();
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("[MethodImpl(MethodImplOptions.AggressiveInlining)]");
        builder.AppendLine("private static bool IsNullValue(ReadOnlySpan<byte> value, byte[][] nullValues)");
        builder.AppendLine("{");
        builder.Indent();
        builder.AppendLine("for (int i = 0; i < nullValues.Length; i++)");
        builder.AppendLine("{");
        builder.Indent();
        builder.AppendLine("if (value.SequenceEqual(nullValues[i]))");
        builder.AppendLine("    return true;");
        builder.Unindent();
        builder.AppendLine("}");
        builder.AppendLine("return false;");
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

    private static void EmitByteIsAllAsciiWhiteSpaceMethod(SourceBuilder builder)
    {
        builder.AppendLine("[MethodImpl(MethodImplOptions.AggressiveInlining)]");
        builder.AppendLine("private static bool IsAllAsciiWhiteSpace(ReadOnlySpan<byte> value)");
        builder.AppendLine("{");
        builder.Indent();
        builder.AppendLine("if (value.IsEmpty) return true;");
        builder.AppendLine("for (int i = 0; i < value.Length; i++)");
        builder.AppendLine("{");
        builder.Indent();
        builder.AppendLine("byte b = value[i];");
        builder.AppendLine("if (b != (byte)' ' && b != (byte)'\\t' && b != (byte)'\\r' && b != (byte)'\\n')");
        builder.AppendLine("    return false;");
        builder.Unindent();
        builder.AppendLine("}");
        builder.AppendLine("return true;");
        builder.Unindent();
        builder.AppendLine("}");
        builder.AppendLine();
    }

    #region Validation Code Generation

    private static bool HasAnyValidation(MemberDescriptor member)
    {
        return member.ValidationNotNull || member.ValidationNotEmpty
            || member.ValidationMaxLength >= 0 || member.ValidationMinLength >= 0
            || !double.IsNaN(member.ValidationRangeMin) || !double.IsNaN(member.ValidationRangeMax)
            || member.ValidationPattern != null;
    }

    private static bool HasAnyValidation(IReadOnlyList<MemberDescriptor> members)
    {
        foreach (var m in members) if (HasAnyValidation(m)) return true;
        return false;
    }

    private static void EmitValidationChecks(SourceBuilder builder, MemberDescriptor member)
    {
        // NotNull validation — catches both empty and whitespace-only values
        if (member.ValidationNotNull)
        {
            builder.AppendLine("if (IsAllAsciiWhiteSpace(utf8))");
            builder.AppendLine("{");
            builder.Indent();
            EmitAddValidationError(builder, member, "NotNull", "Value is required");
            builder.AppendLine("valid = false;");
            builder.Unindent();
            builder.AppendLine("}");
        }

        // NotEmpty validation (string only)
        if (member.ValidationNotEmpty)
        {
            builder.AppendLine($"if (!utf8.IsEmpty && string.IsNullOrWhiteSpace(instance.{member.MemberName}))");
            builder.AppendLine("{");
            builder.Indent();
            EmitAddValidationError(builder, member, "NotEmpty", "Value must not be empty or whitespace");
            builder.AppendLine("valid = false;");
            builder.Unindent();
            builder.AppendLine("}");
        }

        // MaxLength validation (string only)
        if (member.ValidationMaxLength >= 0)
        {
            builder.AppendLine($"if (instance.{member.MemberName} != null && instance.{member.MemberName}.Length > {member.ValidationMaxLength})");
            builder.AppendLine("{");
            builder.Indent();
            EmitAddValidationError(builder, member, "MaxLength", $"Value exceeds maximum length of {member.ValidationMaxLength}");
            builder.AppendLine("valid = false;");
            builder.Unindent();
            builder.AppendLine("}");
        }

        // MinLength validation (string only)
        if (member.ValidationMinLength >= 0)
        {
            builder.AppendLine($"if (instance.{member.MemberName} != null && instance.{member.MemberName}.Length < {member.ValidationMinLength})");
            builder.AppendLine("{");
            builder.Indent();
            EmitAddValidationError(builder, member, "MinLength", $"Value is shorter than minimum length of {member.ValidationMinLength}");
            builder.AppendLine("valid = false;");
            builder.Unindent();
            builder.AppendLine("}");
        }

        // Range validation (numeric only)
        if (!double.IsNaN(member.ValidationRangeMin) || !double.IsNaN(member.ValidationRangeMax))
        {
            var valueExpr = $"instance.{member.MemberName}";
            var conditions = new List<string>();
            if (!double.IsNaN(member.ValidationRangeMin))
                conditions.Add($"{valueExpr} < {FormatRangeLiteral(member.ValidationRangeMin, member.BaseTypeName)}");
            if (!double.IsNaN(member.ValidationRangeMax))
                conditions.Add($"{valueExpr} > {FormatRangeLiteral(member.ValidationRangeMax, member.BaseTypeName)}");

            builder.AppendLine($"if (!utf8.IsEmpty && ({string.Join(" || ", conditions)}))");
            builder.AppendLine("{");
            builder.Indent();
            var rangeMsg = FormatRangeMessage(member.ValidationRangeMin, member.ValidationRangeMax);
            EmitAddValidationError(builder, member, "Range", rangeMsg);
            builder.AppendLine("valid = false;");
            builder.Unindent();
            builder.AppendLine("}");
        }

        // Pattern validation (string only)
        if (member.ValidationPattern != null)
        {
            builder.AppendLine($"if (instance.{member.MemberName} != null && !_pattern_{member.MemberName}.IsMatch(instance.{member.MemberName}))");
            builder.AppendLine("{");
            builder.Indent();
            EmitAddValidationError(builder, member, "Pattern", "Value does not match pattern");
            builder.AppendLine("valid = false;");
            builder.Unindent();
            builder.AppendLine("}");
        }
    }

    private static void EmitAddValidationError(SourceBuilder builder, MemberDescriptor member, string rule, string message)
    {
        var columnName = member.AttributeIndex.HasValue ? "null" : $"\"{member.HeaderName}\"";
        var indexField = $"_{member.MemberName}Index";
        builder.AppendLine($"errors?.Add(new global::HeroParser.Validation.ValidationError");
        builder.AppendLine("{");
        builder.Indent();
        builder.AppendLine($"RowNumber = rowNumber,");
        builder.AppendLine($"ColumnIndex = {indexField},");
        builder.AppendLine($"ColumnName = {columnName},");
        builder.AppendLine($"PropertyName = \"{member.MemberName}\",");
        builder.AppendLine($"Rule = \"{rule}\",");
        builder.AppendLine($"Message = \"{EscapeString(message)}\",");
        builder.AppendLine($"RawValue = Encoding.UTF8.GetString(utf8)");
        builder.Unindent();
        builder.AppendLine("});");
    }

    private static string FormatRangeLiteral(double value, string baseType)
    {
        return baseType switch
        {
            "decimal" or "System.Decimal" => $"{value}m",
            "float" or "System.Single" => $"{value}f",
            "double" or "System.Double" => $"{value}d",
            "long" or "System.Int64" => $"{(long)value}L",
            "ulong" or "System.UInt64" => $"{(ulong)value}UL",
            "int" or "System.Int32" => $"{(int)value}",
            "uint" or "System.UInt32" => $"{(uint)value}U",
            "short" or "System.Int16" => $"(short){(short)value}",
            "ushort" or "System.UInt16" => $"(ushort){(ushort)value}",
            "byte" or "System.Byte" => $"(byte){(byte)value}",
            "sbyte" or "System.SByte" => $"(sbyte){(sbyte)value}",
            _ => $"{value}"
        };
    }

    private static string FormatRangeMessage(double min, double max)
    {
        if (!double.IsNaN(min) && !double.IsNaN(max))
            return $"Value must be between {min} and {max}";
        if (!double.IsNaN(min))
            return $"Value must be >= {min}";
        return $"Value must be <= {max}";
    }

    #endregion

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

            bool hasExplicitName = false;

            bool validationRequired = false;
            bool validationNotEmpty = false;
            int validationMaxLength = -1;
            int validationMinLength = -1;
            double validationRangeMin = double.NaN;
            double validationRangeMax = double.NaN;
            string? validationPattern = null;
            int validationPatternTimeoutMs = 1000;

            if (mapAttribute is not null)
            {
#pragma warning disable IDE0010 // Populate switch - intentionally not exhaustive
                foreach (var arg in mapAttribute.NamedArguments)
                {
                    switch (arg.Key)
                    {
                        case "Name" when arg.Value.Value is string s && !string.IsNullOrWhiteSpace(s):
                            headerName = s;
                            hasExplicitName = true;
                            break;
                        case "Index" when arg.Value.Value is int i && i >= 0:
                            attributeIndex = i;
                            break;
                        case "Format" when arg.Value.Value is string f && !string.IsNullOrWhiteSpace(f):
                            format = f;
                            break;
                        case "NotNull" when arg.Value.Value is bool r:
                            validationRequired = r; break;
                        case "NotEmpty" when arg.Value.Value is bool ne:
                            validationNotEmpty = ne; break;
                        case "MaxLength" when arg.Value.Value is int ml && ml >= 0:
                            validationMaxLength = ml; break;
                        case "MinLength" when arg.Value.Value is int mnl && mnl >= 0:
                            validationMinLength = mnl; break;
                        case "RangeMin" when arg.Value.Value is double rmin && !double.IsNaN(rmin):
                            validationRangeMin = rmin; break;
                        case "RangeMax" when arg.Value.Value is double rmax && !double.IsNaN(rmax):
                            validationRangeMax = rmax; break;
                        case "Pattern" when arg.Value.Value is string p && !string.IsNullOrWhiteSpace(p):
                            validationPattern = p; break;
                        case "PatternTimeoutMs" when arg.Value.Value is int pt && pt > 0:
                            validationPatternTimeoutMs = pt; break;
                    }
                }
#pragma warning restore IDE0010

                if (!hasExplicitName && attributeIndex is null)
                {
                    diagnostics.Add(Diagnostic.Create(missingNameOrIndexDiagnostic, property.Locations.FirstOrDefault() ?? Location.None, property.Name));
                    continue;
                }
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

            bool isStringType = baseTypeName == "string";
            bool isNumericType = baseTypeName is "int" or "System.Int32" or "long" or "System.Int64"
                or "short" or "System.Int16" or "byte" or "System.Byte" or "uint" or "System.UInt32"
                or "ulong" or "System.UInt64" or "ushort" or "System.UInt16" or "sbyte" or "System.SByte"
                or "decimal" or "System.Decimal" or "double" or "System.Double" or "float" or "System.Single";

            if (validationNotEmpty && !isStringType)
            {
                diagnostics.Add(Diagnostic.Create(NotEmptyOnNonStringDiagnostic, property.Locations.FirstOrDefault() ?? Location.None, property.Name, baseTypeName));
                validationNotEmpty = false;
            }
            if ((validationMaxLength >= 0 || validationMinLength >= 0) && !isStringType)
            {
                diagnostics.Add(Diagnostic.Create(LengthOnNonStringDiagnostic, property.Locations.FirstOrDefault() ?? Location.None, property.Name, baseTypeName));
                validationMaxLength = -1;
                validationMinLength = -1;
            }
            if ((!double.IsNaN(validationRangeMin) || !double.IsNaN(validationRangeMax)) && !isNumericType)
            {
                diagnostics.Add(Diagnostic.Create(RangeOnNonNumericDiagnostic, property.Locations.FirstOrDefault() ?? Location.None, property.Name, baseTypeName));
                validationRangeMin = double.NaN;
                validationRangeMax = double.NaN;
            }
            if (validationPattern != null && !isStringType)
            {
                diagnostics.Add(Diagnostic.Create(PatternOnNonStringDiagnostic, property.Locations.FirstOrDefault() ?? Location.None, property.Name, baseTypeName));
                validationPattern = null;
            }

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
                isReadOnlyMemoryChar,
                validationRequired,
                validationNotEmpty,
                validationMaxLength,
                validationMinLength,
                validationRangeMin,
                validationRangeMax,
                validationPattern,
                validationPatternTimeoutMs));
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
        bool IsReadOnlyMemoryChar,
        // Validation fields:
        bool ValidationNotNull,
        bool ValidationNotEmpty,
        int ValidationMaxLength,       // -1 = unchecked
        int ValidationMinLength,       // -1 = unchecked
        double ValidationRangeMin,     // NaN = unchecked
        double ValidationRangeMax,     // NaN = unchecked
        string? ValidationPattern,
        int ValidationPatternTimeoutMs);  // default 1000
}
