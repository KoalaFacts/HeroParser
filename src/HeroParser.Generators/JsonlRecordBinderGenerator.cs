using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace HeroParser.Generators;

/// <summary>
/// Source generator for JSONL record binders.
/// Generates inline binder classes for maximum performance, Native AOT-safe parsing.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class JsonlRecordBinderGenerator : IIncrementalGenerator
{
    private const string GENERATED_NAMESPACE = "HeroParser.JsonLines.Reading.Binding";
    private const string BINDER_FACTORY_TYPE = "global::HeroParser.JsonLines.Reading.Binders.JsonlRecordBinderFactory";
    private const string BINDER_INTERFACE_TYPE = "global::HeroParser.JsonLines.Reading.Binders.IJsonlSourceBinder<";

    private static readonly string[] generateAttributeNames =
    [
        "HeroParser.GenerateBinderAttribute"
    ];

    private static readonly string[] columnAttributeNames =
    [
        "HeroParser.TabularMapAttribute"
    ];

    private static readonly string[] jsonPropertyAttributeNames =
    [
        "System.Text.Json.Serialization.JsonPropertyNameAttribute"
    ];

    private static readonly string[] jsonTypeContractAttributeNames =
    [
        "System.Text.Json.Serialization.JsonConverterAttribute",
        "System.Text.Json.Serialization.JsonPolymorphicAttribute",
        "System.Text.Json.Serialization.JsonDerivedTypeAttribute",
        "System.Text.Json.Serialization.JsonUnmappedMemberHandlingAttribute",
        "System.Text.Json.Serialization.JsonNumberHandlingAttribute",
        "System.Text.Json.Serialization.JsonObjectCreationHandlingAttribute"
    ];

    private static readonly string[] jsonMemberContractAttributeNames =
    [
        "System.Text.Json.Serialization.JsonIncludeAttribute",
        "System.Text.Json.Serialization.JsonExtensionDataAttribute"
    ];

    private static readonly string[] jsonPropertyContractAttributeNames =
    [
        "System.Text.Json.Serialization.JsonConverterAttribute",
        "System.Text.Json.Serialization.JsonNumberHandlingAttribute",
        "System.Text.Json.Serialization.JsonRequiredAttribute",
        "System.Text.Json.Serialization.JsonObjectCreationHandlingAttribute"
    ];

#pragma warning disable RS2008 // Enable analyzer release tracking - not needed for internal generator
    private static readonly DiagnosticDescriptor unsupportedPropertyTypeDiagnostic = new(
        "HERO101",
        "Unsupported property type for JSONL",
        "Property '{0}' of type '{1}' is supported only when its type is supported by the JSONL record binder generator. Supported types include primitives, float[], byte[], DateTime, DateTimeOffset, DateOnly, TimeOnly, TimeSpan, Guid, Half, Int128, UInt128, BigInteger, Uri, enums, string, ISpanParsable<T>, and IUtf8SpanParsable<T>.",
        "HeroParser.Generators",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
#pragma warning restore RS2008

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Use ForAttributeWithMetadataName for better caching
        var provider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                generateAttributeNames[0],
                predicate: static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax or StructDeclarationSyntax,
                transform: static (ctx, ct) => TransformToDescriptor(ctx, ct))
            .Where(static x => x is not null);

        // Generate per-type typed binder files for better incrementality
        context.RegisterSourceOutput(provider, static (spc, descriptor) => EmitTypedBinder(spc, descriptor!));

        // Generate registration file
        context.RegisterSourceOutput(provider.Collect(), static (spc, descriptors) =>
        {
            var all = descriptors.Where(x => x is not null).ToList();
            if (all.Count > 0)
                EmitRegistration(spc, all!);
        });
    }

    private static TypeDescriptor? TransformToDescriptor(GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol symbol)
            return null;

        var target = new RecordTypeTarget(symbol);
        if (symbol.IsAbstract || !target.IsAccessible)
            return null;

        return BuildDescriptor(target, ct);
    }

    private static void EmitTypedBinder(SourceProductionContext context, TypeDescriptor descriptor)
    {
        // Report diagnostics
        foreach (var diag in descriptor.Diagnostics)
        {
            context.ReportDiagnostic(diag);
        }

        var binderMembers = GetMembersWithSetters(descriptor.Members);
        var validatorMembers = GetValidatorMembers(descriptor.Members);
        bool emitBinder = descriptor.CanGenerateBinder && binderMembers.Count > 0;
        bool emitValidator = descriptor.CanRegisterValidator && validatorMembers.Count > 0;
        if (!emitBinder && !emitValidator)
            return;

        var builder = new SourceBuilder(16384);
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        builder.AppendLine("using System;");
        builder.AppendLine("using System.Text.Json;");
        builder.AppendLine("using HeroParser.JsonLines.Reading.Binders;");
        builder.AppendLine();
        builder.AppendLine($"namespace {GENERATED_NAMESPACE};");
        builder.AppendLine();

        var binderClassName = $"JsonlInlineByteSourceBinder_{descriptor.SafeClassName}";
        if (emitBinder)
        {
            bool hasCustomParsable = binderMembers.Any(IsCustomParsable);
            var byteInterface = hasCustomParsable
                ? $"global::HeroParser.JsonLines.Reading.Binders.IJsonlTryByteBinder<{descriptor.FullyQualifiedName}>"
                : $"{BINDER_INTERFACE_TYPE}byte, {descriptor.FullyQualifiedName}>";

            builder.AppendLine($"internal sealed class {binderClassName} : {byteInterface}");
            builder.AppendLine("{");
            builder.Indent();

            // Emit static Regex fields for Pattern validation
            foreach (var member in binderMembers)
            {
                if (member.Validation.Pattern != null && IsStringMember(member))
                {
                    var escapedPattern = new CSharpStringLiteral(member.Validation.Pattern).Escaped;
                    builder.AppendLine($"private static readonly global::System.Text.RegularExpressions.Regex _pattern_{member.MemberName} = new(\"{escapedPattern}\", global::System.Text.RegularExpressions.RegexOptions.None, global::System.TimeSpan.FromMilliseconds({member.Validation.PatternTimeoutMs}));");
                }
            }

            // Emit static property names keys (ReadOnlySpan<byte> properties)
            var keyCandidatesByMember = ResolveKeyCandidates(descriptor.Members, binderMembers);
            for (int i = 0; i < binderMembers.Count; i++)
            {
                var member = binderMembers[i];
                var candidates = keyCandidatesByMember[i];
                for (int k = 0; k < candidates.Count; k++)
                {
                    var candidate = new CSharpStringLiteral(candidates[k]).Escaped;
                    builder.AppendLine($"private static System.ReadOnlySpan<byte> Key_{member.MemberName}_{k} => \"{candidate}\"u8;");
                }
            }
            builder.AppendLine();

            builder.AppendLine($"internal static {BINDER_INTERFACE_TYPE}byte, {descriptor.FullyQualifiedName}> Instance {{ get; }} = new {binderClassName}();");
            builder.AppendLine();

            bool anyValidation = HasAnyValidation(binderMembers);
            var valueTypePresenceFlags = GetValueTypePresenceMembers(binderMembers);
            var inaccessibleGetterValidatedMembers = GetInaccessibleGetterValidatedMembers(binderMembers);

            if (hasCustomParsable)
            {
                builder.AppendLine($"public {descriptor.FullyQualifiedName} Bind(ReadOnlySpan<byte> line)");
                builder.AppendLine("{");
                builder.Indent();
                builder.AppendLine("if (TryBind(line, out var result))");
                builder.AppendLine("    return result;");
                builder.AppendLine("throw new JsonException(\"JSON object requires fallback serializer binding.\");");
                builder.Unindent();
                builder.AppendLine("}");
                builder.AppendLine();
                builder.AppendLine($"public bool TryBind(ReadOnlySpan<byte> line, [global::System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out {descriptor.FullyQualifiedName} result)");
            }
            else
            {
                builder.AppendLine($"public {descriptor.FullyQualifiedName} Bind(ReadOnlySpan<byte> line)");
            }

            builder.AppendLine("{");
            builder.Indent();
            builder.AppendLine("var reader = new Utf8JsonReader(line);");
            builder.AppendLine("if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)");
            builder.AppendLine("    throw new JsonException(\"Expected start of JSON object.\");");
            builder.AppendLine();
            builder.AppendLine($"var record = new {descriptor.FullyQualifiedName}();");
            foreach (var flagMember in valueTypePresenceFlags)
            {
                builder.AppendLine($"bool _has_{flagMember.MemberName} = false;");
            }
            foreach (var valMember in inaccessibleGetterValidatedMembers)
            {
                builder.AppendLine($"{valMember.TypeName} _val_{valMember.MemberName} = default!;");
            }
            if (NeedsCharDecodeHelper(binderMembers))
            {
                builder.AppendLine("global::System.Span<char> chars = stackalloc char[128];");
            }
            builder.AppendLine();
            builder.AppendLine("while (reader.Read())");
            builder.AppendLine("{");
            builder.Indent();
            builder.AppendLine("if (reader.TokenType == JsonTokenType.EndObject)");
            builder.AppendLine("{");
            builder.Indent();
            builder.AppendLine("if (reader.Read())");
            builder.AppendLine("    throw new JsonException(\"Unexpected trailing JSON content after object.\");");
            if (anyValidation)
            {
                var argList = new List<string> { "record" };
                foreach (var m in valueTypePresenceFlags)
                    argList.Add($"_has_{m.MemberName}");
                foreach (var m in inaccessibleGetterValidatedMembers)
                    argList.Add($"_val_{m.MemberName}");
                builder.AppendLine($"ValidateRecord({string.Join(", ", argList)});");
            }
            if (hasCustomParsable)
            {
                builder.AppendLine("result = record;");
                builder.AppendLine("return true;");
            }
            else
            {
                builder.AppendLine("return record;");
            }
            builder.Unindent();
            builder.AppendLine("}");
            builder.AppendLine();
            builder.AppendLine("if (reader.TokenType == JsonTokenType.PropertyName)");
            builder.AppendLine("{");
            builder.Indent();
            builder.AppendLine("int propIndex = -1;");
            for (int i = 0; i < binderMembers.Count; i++)
            {
                var member = binderMembers[i];
                var candidates = keyCandidatesByMember[i];
                var conditionParts = new List<string>();
                for (int k = 0; k < candidates.Count; k++)
                {
                    conditionParts.Add($"reader.ValueTextEquals(Key_{member.MemberName}_{k})");
                }

                var condition = string.Join(" || ", conditionParts);
                builder.AppendLine($"{(i == 0 ? "if" : "else if")} ({condition})");
                builder.AppendLine($"    propIndex = {i};");
            }
            builder.AppendLine("if (!reader.Read())");
            builder.AppendLine("    throw new JsonException(\"Expected property value.\");");
            builder.AppendLine();

            // Dispatch matched property
            for (int i = 0; i < binderMembers.Count; i++)
            {
                var member = binderMembers[i];
                builder.AppendLine($"{(i == 0 ? "if" : "else if")} (propIndex == {i})");
                builder.AppendLine("{");
                builder.Indent();
                EmitPropertyParsing(builder, member, !member.HasGetter && HasAnyValidation(member));
                builder.Unindent();
                builder.AppendLine("}");
            }

            builder.AppendLine("else");
            builder.AppendLine("{");
            builder.Indent();
            builder.AppendLine("reader.Skip();");
            builder.Unindent();
            builder.AppendLine("}");

            builder.Unindent();
            builder.AppendLine("}"); // end of PropertyName check
            builder.Unindent();
            builder.AppendLine("}"); // end of loop
            builder.AppendLine();
            builder.AppendLine("throw new JsonException(\"JSON object was truncated or malformed.\");");
            builder.Unindent();
            builder.AppendLine("}"); // end of Bind method

            if (anyValidation)
            {
                EmitValidateMethod(builder, descriptor, binderMembers, valueTypePresenceFlags, inaccessibleGetterValidatedMembers);
            }

            if (NeedsCharDecodeHelper(binderMembers))
            {
                builder.AppendLine();
                builder.AppendLine("private static bool TryDecodeUnescapedChars(ref Utf8JsonReader reader, scoped global::System.Span<char> destination, out int charsWritten)");
                builder.AppendLine("{");
                builder.Indent();
                builder.AppendLine("if (reader.TokenType == JsonTokenType.String && !reader.HasValueSequence && !reader.ValueIsEscaped && reader.ValueSpan.Length <= destination.Length)");
                builder.AppendLine("{");
                builder.Indent();
                builder.AppendLine("charsWritten = global::System.Text.Encoding.UTF8.GetChars(reader.ValueSpan, destination);");
                builder.AppendLine("return true;");
                builder.Unindent();
                builder.AppendLine("}");
                builder.AppendLine("charsWritten = 0;");
                builder.AppendLine("return false;");
                builder.Unindent();
                builder.AppendLine("}");
            }

            if (binderMembers.Any(m => m.IsSpanParsable))
            {
                builder.AppendLine();
                builder.AppendLine("private static bool TryParseSpan<TValue>(ref Utf8JsonReader reader, [global::System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out TValue result)");
                builder.AppendLine("    where TValue : global::System.ISpanParsable<TValue>");
                builder.AppendLine("{");
                builder.Indent();
                builder.AppendLine("if (reader.TokenType == JsonTokenType.String)");
                builder.AppendLine("{");
                builder.Indent();
                builder.AppendLine("if (!reader.HasValueSequence && !reader.ValueIsEscaped && reader.ValueSpan.Length <= 128)");
                builder.AppendLine("{");
                builder.Indent();
                builder.AppendLine("global::System.Span<char> chars = stackalloc char[128];");
                builder.AppendLine("int written = global::System.Text.Encoding.UTF8.GetChars(reader.ValueSpan, chars);");
                builder.AppendLine("return TValue.TryParse(chars[..written], global::System.Globalization.CultureInfo.InvariantCulture, out result);");
                builder.Unindent();
                builder.AppendLine("}");
                builder.AppendLine("string? value = reader.GetString();");
                builder.AppendLine("if (value is not null)");
                builder.AppendLine("    return TValue.TryParse(value.AsSpan(), global::System.Globalization.CultureInfo.InvariantCulture, out result);");
                builder.Unindent();
                builder.AppendLine("}");
                builder.AppendLine("result = default;");
                builder.AppendLine("return false;");
                builder.Unindent();
                builder.AppendLine("}");
            }

            if (binderMembers.Any(m => m.IsUtf8SpanParsable))
            {
                builder.AppendLine();
                builder.AppendLine("private static bool TryParseUtf8Span<TValue>(ref Utf8JsonReader reader, [global::System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out TValue result)");
                builder.AppendLine("    where TValue : global::System.IUtf8SpanParsable<TValue>");
                builder.AppendLine("{");
                builder.Indent();
                builder.AppendLine("if (reader.TokenType == JsonTokenType.String)");
                builder.AppendLine("{");
                builder.Indent();
                builder.AppendLine("if (!reader.HasValueSequence && !reader.ValueIsEscaped)");
                builder.AppendLine("    return TValue.TryParse(reader.ValueSpan, global::System.Globalization.CultureInfo.InvariantCulture, out result);");
                builder.AppendLine();
                builder.AppendLine("string? value = reader.GetString();");
                builder.AppendLine("if (value is not null)");
                builder.AppendLine("{");
                builder.Indent();
                builder.AppendLine("int maxBytes = global::System.Text.Encoding.UTF8.GetMaxByteCount(value.Length);");
                builder.AppendLine("byte[]? rented = maxBytes <= 256 ? null : global::System.Buffers.ArrayPool<byte>.Shared.Rent(maxBytes);");
                builder.AppendLine("global::System.Span<byte> buffer = rented is null ? stackalloc byte[256] : rented;");
                builder.AppendLine("try");
                builder.AppendLine("{");
                builder.Indent();
                builder.AppendLine("int written = global::System.Text.Encoding.UTF8.GetBytes(value, buffer);");
                builder.AppendLine("return TValue.TryParse(buffer[..written], global::System.Globalization.CultureInfo.InvariantCulture, out result);");
                builder.Unindent();
                builder.AppendLine("}");
                builder.AppendLine("finally");
                builder.AppendLine("{");
                builder.Indent();
                builder.AppendLine("if (rented is not null)");
                builder.AppendLine("    global::System.Buffers.ArrayPool<byte>.Shared.Return(rented);");
                builder.Unindent();
                builder.AppendLine("}");
                builder.Unindent();
                builder.AppendLine("}");
                builder.Unindent();
                builder.AppendLine("}");
                builder.AppendLine("result = default;");
                builder.AppendLine("return false;");
                builder.Unindent();
                builder.AppendLine("}");
            }

            builder.Unindent();
            builder.AppendLine("}"); // end of byte class

            // Emit Char Source Binder implementing IJsonlSourceBinder<char, T>
            var charBinderClassName = $"JsonlInlineCharSourceBinder_{descriptor.SafeClassName}";
            builder.AppendLine();
            builder.AppendLine($"internal sealed class {charBinderClassName} : {BINDER_INTERFACE_TYPE}char, {descriptor.FullyQualifiedName}>");
            builder.AppendLine("{");
            builder.Indent();
            builder.AppendLine($"internal static {BINDER_INTERFACE_TYPE}char, {descriptor.FullyQualifiedName}> Instance {{ get; }} = new {charBinderClassName}();");
            builder.AppendLine();
            builder.AppendLine($"public {descriptor.FullyQualifiedName} Bind(ReadOnlySpan<char> line)");
            builder.AppendLine("{");
            builder.Indent();
            builder.AppendLine("if (line.IsEmpty)");
            builder.AppendLine($"    return {binderClassName}.Instance.Bind(ReadOnlySpan<byte>.Empty);");
            builder.AppendLine();
            builder.AppendLine("int maxByteCount = global::System.Text.Encoding.UTF8.GetMaxByteCount(line.Length);");
            builder.AppendLine("byte[]? rented = null;");
            builder.AppendLine("Span<byte> buffer = maxByteCount <= 512");
            builder.AppendLine("    ? stackalloc byte[maxByteCount]");
            builder.AppendLine("    : (rented = global::System.Buffers.ArrayPool<byte>.Shared.Rent(maxByteCount));");
            builder.AppendLine();
            builder.AppendLine("try");
            builder.AppendLine("{");
            builder.Indent();
            builder.AppendLine("int bytesWritten = global::System.Text.Encoding.UTF8.GetBytes(line, buffer);");
            builder.AppendLine($"return {binderClassName}.Instance.Bind(buffer[..bytesWritten]);");
            builder.Unindent();
            builder.AppendLine("}");
            builder.AppendLine("finally");
            builder.AppendLine("{");
            builder.Indent();
            builder.AppendLine("if (rented is not null)");
            builder.AppendLine("    global::System.Buffers.ArrayPool<byte>.Shared.Return(rented);");
            builder.Unindent();
            builder.AppendLine("}");
            builder.Unindent();
            builder.AppendLine("}"); // end of Bind
            builder.Unindent();
            builder.AppendLine("}"); // end of char class
        }

        if (emitValidator)
        {
            EmitRecordValidatorClass(builder, descriptor, validatorMembers);
        }

        context.AddSource($"JsonlInlineSourceBinder.{descriptor.SafeClassName}.g.cs", builder.ToString());
    }

    private static List<string>[] ResolveKeyCandidates(
        IReadOnlyList<MemberDescriptor> allMembers,
        IReadOnlyList<MemberDescriptor> binderMembers)
    {
        var tier1Names = new HashSet<string>(StringComparer.Ordinal);
        var reservedJsonNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in allMembers)
        {
            var exactName = member.CustomJsonName ?? member.MemberName;
            tier1Names.Add(exactName);
            reservedJsonNames.Add(exactName);
        }

        var tier2ByAllMember = new List<string>[allMembers.Count];
        var tier2Counts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < allMembers.Count; i++)
        {
            var member = allMembers[i];
            var candidates = new List<string>(2);
            if (member.CustomJsonName is null)
            {
                string camel = JsonCamelCase(member.MemberName);
                if (!tier1Names.Contains(camel) && !candidates.Contains(camel))
                    candidates.Add(camel);

                string lower = member.MemberName.ToLowerInvariant();
                if (!tier1Names.Contains(lower) && !candidates.Contains(lower))
                    candidates.Add(lower);

                foreach (var candidate in candidates)
                {
                    reservedJsonNames.Add(candidate);
                    tier2Counts[candidate] = tier2Counts.TryGetValue(candidate, out int count) ? count + 1 : 1;
                }
            }
            tier2ByAllMember[i] = candidates;
        }

        var tier2ByBinderMember = new Dictionary<string, List<string>>(binderMembers.Count, StringComparer.Ordinal);
        for (int i = 0; i < allMembers.Count; i++)
        {
            tier2ByBinderMember[allMembers[i].MemberName] = tier2ByAllMember[i];
        }

        var tier3ByBinderMember = new List<string>[binderMembers.Count];
        var tier3Counts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < binderMembers.Count; i++)
        {
            var member = binderMembers[i];
            var aliases = new List<string>(3);
            if (member.CustomJsonName is null
                && member.HeaderName is not null
                && member.HeaderName != member.MemberName)
            {
                string[] rawAliases =
                [
                    member.HeaderName,
                    JsonCamelCase(member.HeaderName),
                    member.HeaderName.ToLowerInvariant()
                ];
                foreach (var alias in rawAliases)
                {
                    if (!reservedJsonNames.Contains(alias) && !aliases.Contains(alias))
                        aliases.Add(alias);
                }

                foreach (var alias in aliases)
                {
                    tier3Counts[alias] = tier3Counts.TryGetValue(alias, out int count) ? count + 1 : 1;
                }
            }
            tier3ByBinderMember[i] = aliases;
        }

        var result = new List<string>[binderMembers.Count];
        for (int i = 0; i < binderMembers.Count; i++)
        {
            var member = binderMembers[i];
            List<string> keys = [member.CustomJsonName ?? member.MemberName];

            foreach (var candidate in tier2ByBinderMember[member.MemberName])
            {
                if (tier2Counts[candidate] == 1 && !keys.Contains(candidate))
                    keys.Add(candidate);
            }

            foreach (var alias in tier3ByBinderMember[i])
            {
                if (tier3Counts[alias] == 1 && !keys.Contains(alias))
                    keys.Add(alias);
            }

            result[i] = keys;
        }

        return result;
    }

    private static string JsonCamelCase(string s)
    {
        if (string.IsNullOrEmpty(s) || !char.IsUpper(s[0]))
            return s;

        char[] chars = s.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (i == 1 && !char.IsUpper(chars[i]))
                break;

            bool hasNext = (i + 1 < chars.Length);
            if (i > 0 && hasNext && !char.IsUpper(chars[i + 1]))
                break;

            chars[i] = char.ToLowerInvariant(chars[i]);
        }
        return new string(chars);
    }

    private static void EmitPropertyParsing(SourceBuilder builder, MemberDescriptor member, bool useLocalForValidation)
    {
        var propertyName = member.MemberName;
        var baseType = member.BaseTypeName;
        string targetExpr = useLocalForValidation ? $"_val_{propertyName}" : $"record.{propertyName}";
        bool tracksPresence = RequiresValueTypePresenceFlag(member);
        bool handlesNullUpfront = member.IsNullable || member.IsReferenceType || member.Validation.NotNull;

        if (handlesNullUpfront)
        {
            builder.AppendLine("if (reader.TokenType == JsonTokenType.Null)");
            builder.AppendLine("{");
            builder.Indent();
            if (member.IsNullable)
            {
                builder.AppendLine($"{targetExpr} = null;");
            }
            else if (member.IsReferenceType)
            {
                builder.AppendLine($"{targetExpr} = null!;");
            }
            else
            {
                builder.AppendLine($"{targetExpr} = default;");
                if (tracksPresence)
                {
                    builder.AppendLine($"_has_{propertyName} = false;");
                }
            }
            builder.Unindent();
            builder.AppendLine("}");
            builder.AppendLine("else");
            builder.AppendLine("{");
            builder.Indent();
        }

        switch (baseType)
        {
            case "string":
            case "System.String":
                builder.AppendLine($"{targetExpr} = reader.GetString()!;");
                break;

            case "int":
            case "System.Int32":
                builder.AppendLine($"{targetExpr} = reader.GetInt32();");
                break;

            case "long":
            case "System.Int64":
                builder.AppendLine($"{targetExpr} = reader.GetInt64();");
                break;

            case "short":
            case "System.Int16":
                builder.AppendLine($"{targetExpr} = reader.GetInt16();");
                break;

            case "byte":
            case "System.Byte":
                builder.AppendLine($"{targetExpr} = reader.GetByte();");
                break;

            case "sbyte":
            case "System.SByte":
                builder.AppendLine($"{targetExpr} = reader.GetSByte();");
                break;

            case "uint":
            case "System.UInt32":
                builder.AppendLine($"{targetExpr} = reader.GetUInt32();");
                break;

            case "ulong":
            case "System.UInt64":
                builder.AppendLine($"{targetExpr} = reader.GetUInt64();");
                break;

            case "ushort":
            case "System.UInt16":
                builder.AppendLine($"{targetExpr} = reader.GetUInt16();");
                break;

            case "double":
            case "System.Double":
                builder.AppendLine($"{targetExpr} = reader.GetDouble();");
                break;

            case "float":
            case "System.Single":
                builder.AppendLine($"{targetExpr} = reader.GetSingle();");
                break;

            case "decimal":
            case "System.Decimal":
                builder.AppendLine($"{targetExpr} = reader.GetDecimal();");
                break;

            case "bool":
            case "System.Boolean":
                builder.AppendLine($"{targetExpr} = reader.GetBoolean();");
                break;

            case "float[]":
            case "System.Single[]":
                if (!handlesNullUpfront)
                {
                    builder.AppendLine("if (reader.TokenType == JsonTokenType.Null)");
                    builder.AppendLine("{");
                    builder.Indent();
                    builder.AppendLine($"{targetExpr} = null!;");
                    builder.Unindent();
                    builder.AppendLine("}");
                    builder.AppendLine("else if (reader.TokenType == JsonTokenType.StartArray)");
                }
                else
                {
                    builder.AppendLine("if (reader.TokenType == JsonTokenType.StartArray)");
                }
                builder.AppendLine("{");
                builder.Indent();
                builder.AppendLine("var pool = global::System.Buffers.ArrayPool<float>.Shared;");
                builder.AppendLine("float[] buf = pool.Rent(64);");
                builder.AppendLine("int count = 0;");
                builder.AppendLine("try");
                builder.AppendLine("{");
                builder.Indent();
                builder.AppendLine("while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)");
                builder.AppendLine("{");
                builder.Indent();
                builder.AppendLine("if (count == buf.Length)");
                builder.AppendLine("{");
                builder.Indent();
                builder.AppendLine("float[] grown = pool.Rent(buf.Length * 2);");
                builder.AppendLine("global::System.Array.Copy(buf, grown, count);");
                builder.AppendLine("pool.Return(buf);");
                builder.AppendLine("buf = grown;");
                builder.Unindent();
                builder.AppendLine("}");
                builder.AppendLine("buf[count++] = reader.GetSingle();");
                builder.Unindent();
                builder.AppendLine("}");
                builder.AppendLine("if (reader.TokenType != JsonTokenType.EndArray)");
                builder.AppendLine("    throw new JsonException(\"Unterminated JSON array.\");");
                builder.AppendLine($"{targetExpr} = count == 0 ? global::System.Array.Empty<float>() : global::System.MemoryExtensions.AsSpan(buf, 0, count).ToArray();");
                builder.Unindent();
                builder.AppendLine("}");
                builder.AppendLine("finally");
                builder.AppendLine("{");
                builder.Indent();
                builder.AppendLine("pool.Return(buf);");
                builder.Unindent();
                builder.AppendLine("}");
                builder.Unindent();
                builder.AppendLine("}");
                builder.AppendLine("else");
                builder.AppendLine("{");
                builder.Indent();
                builder.AppendLine("throw new JsonException(\"Expected JSON array for float[] property.\");");
                builder.Unindent();
                builder.AppendLine("}");
                break;

            case "System.DateTime":
                if (member.ParseFormat is { } dtFormat)
                {
                    var escapedDtFormat = new CSharpStringLiteral(dtFormat).Escaped;
                    builder.AppendLine("{");
                    builder.Indent();
                    builder.AppendLine("if (TryDecodeUnescapedChars(ref reader, chars, out int written))");
                    builder.AppendLine("{");
                    builder.Indent();
                    builder.AppendLine($"{targetExpr} = global::System.DateTime.ParseExact(chars[..written], \"{escapedDtFormat}\", global::System.Globalization.CultureInfo.InvariantCulture);");
                    builder.Unindent();
                    builder.AppendLine("}");
                    builder.AppendLine("else");
                    builder.AppendLine("{");
                    builder.Indent();
                    builder.AppendLine("string? s = reader.GetString();");
                    builder.AppendLine($"{targetExpr} = s is not null ? global::System.DateTime.ParseExact(s, \"{escapedDtFormat}\", global::System.Globalization.CultureInfo.InvariantCulture) : default;");
                    builder.Unindent();
                    builder.AppendLine("}");
                    builder.Unindent();
                    builder.AppendLine("}");
                }
                else
                {
                    builder.AppendLine($"{targetExpr} = reader.GetDateTime();");
                }
                break;

            case "System.DateTimeOffset":
                if (member.ParseFormat is { } dtoFormat)
                {
                    var escapedDtoFormat = new CSharpStringLiteral(dtoFormat).Escaped;
                    builder.AppendLine("{");
                    builder.Indent();
                    builder.AppendLine("if (TryDecodeUnescapedChars(ref reader, chars, out int written))");
                    builder.AppendLine("{");
                    builder.Indent();
                    builder.AppendLine($"{targetExpr} = global::System.DateTimeOffset.ParseExact(chars[..written], \"{escapedDtoFormat}\", global::System.Globalization.CultureInfo.InvariantCulture);");
                    builder.Unindent();
                    builder.AppendLine("}");
                    builder.AppendLine("else");
                    builder.AppendLine("{");
                    builder.Indent();
                    builder.AppendLine("string? s = reader.GetString();");
                    builder.AppendLine($"{targetExpr} = s is not null ? global::System.DateTimeOffset.ParseExact(s, \"{escapedDtoFormat}\", global::System.Globalization.CultureInfo.InvariantCulture) : default;");
                    builder.Unindent();
                    builder.AppendLine("}");
                    builder.Unindent();
                    builder.AppendLine("}");
                }
                else
                {
                    builder.AppendLine($"{targetExpr} = reader.GetDateTimeOffset();");
                }
                break;

            case "System.Guid":
                if (member.ParseFormat is { } guidFormat)
                {
                    var escapedGuidFormat = new CSharpStringLiteral(guidFormat).Escaped;
                    builder.AppendLine("{");
                    builder.Indent();
                    builder.AppendLine("if (TryDecodeUnescapedChars(ref reader, chars, out int written))");
                    builder.AppendLine("{");
                    builder.Indent();
                    builder.AppendLine($"{targetExpr} = global::System.Guid.ParseExact(chars[..written], \"{escapedGuidFormat}\");");
                    builder.Unindent();
                    builder.AppendLine("}");
                    builder.AppendLine("else");
                    builder.AppendLine("{");
                    builder.Indent();
                    builder.AppendLine("string? s = reader.GetString();");
                    builder.AppendLine($"{targetExpr} = s is not null ? global::System.Guid.ParseExact(s, \"{escapedGuidFormat}\") : default;");
                    builder.Unindent();
                    builder.AppendLine("}");
                    builder.Unindent();
                    builder.AppendLine("}");
                }
                else
                {
                    builder.AppendLine($"{targetExpr} = reader.GetGuid();");
                }
                break;

            case "System.DateOnly":
                builder.AppendLine("{");
                builder.Indent();
                builder.AppendLine("if (TryDecodeUnescapedChars(ref reader, chars, out int written))");
                builder.AppendLine("{");
                builder.Indent();
                if (member.ParseFormat is { } dateOnlySpanFormat)
                {
                    var escapedDateOnlyFormat = new CSharpStringLiteral(dateOnlySpanFormat).Escaped;
                    builder.AppendLine($"{targetExpr} = DateOnly.ParseExact(chars[..written], \"{escapedDateOnlyFormat}\", global::System.Globalization.CultureInfo.InvariantCulture);");
                }
                else
                {
                    builder.AppendLine($"{targetExpr} = DateOnly.Parse(chars[..written], global::System.Globalization.CultureInfo.InvariantCulture);");
                }
                builder.Unindent();
                builder.AppendLine("}");
                builder.AppendLine("else");
                builder.AppendLine("{");
                builder.Indent();
                builder.AppendLine("string? s = reader.GetString();");
                if (member.ParseFormat is { } dateOnlyFormat)
                {
                    builder.AppendLine($"{targetExpr} = s is not null ? DateOnly.ParseExact(s, \"{new CSharpStringLiteral(dateOnlyFormat).Escaped}\", global::System.Globalization.CultureInfo.InvariantCulture) : default;");
                }
                else
                {
                    builder.AppendLine($"{targetExpr} = s is not null ? DateOnly.Parse(s, global::System.Globalization.CultureInfo.InvariantCulture) : default;");
                }
                builder.Unindent();
                builder.AppendLine("}");
                builder.Unindent();
                builder.AppendLine("}");
                break;

            case "System.TimeOnly":
                builder.AppendLine("{");
                builder.Indent();
                builder.AppendLine("if (TryDecodeUnescapedChars(ref reader, chars, out int written))");
                builder.AppendLine("{");
                builder.Indent();
                if (member.ParseFormat is { } timeOnlySpanFormat)
                {
                    var escapedTimeOnlyFormat = new CSharpStringLiteral(timeOnlySpanFormat).Escaped;
                    builder.AppendLine($"{targetExpr} = TimeOnly.ParseExact(chars[..written], \"{escapedTimeOnlyFormat}\", global::System.Globalization.CultureInfo.InvariantCulture);");
                }
                else
                {
                    builder.AppendLine($"{targetExpr} = TimeOnly.Parse(chars[..written], global::System.Globalization.CultureInfo.InvariantCulture);");
                }
                builder.Unindent();
                builder.AppendLine("}");
                builder.AppendLine("else");
                builder.AppendLine("{");
                builder.Indent();
                builder.AppendLine("string? s = reader.GetString();");
                if (member.ParseFormat is { } timeOnlyFormat)
                {
                    builder.AppendLine($"{targetExpr} = s is not null ? TimeOnly.ParseExact(s, \"{new CSharpStringLiteral(timeOnlyFormat).Escaped}\", global::System.Globalization.CultureInfo.InvariantCulture) : default;");
                }
                else
                {
                    builder.AppendLine($"{targetExpr} = s is not null ? TimeOnly.Parse(s, global::System.Globalization.CultureInfo.InvariantCulture) : default;");
                }
                builder.Unindent();
                builder.AppendLine("}");
                builder.Unindent();
                builder.AppendLine("}");
                break;

            case "char":
            case "System.Char":
                builder.AppendLine("if (reader.TokenType == JsonTokenType.String && !reader.HasValueSequence && !reader.ValueIsEscaped && reader.ValueSpan.Length == 1 && reader.ValueSpan[0] < 0x80)");
                builder.AppendLine("{");
                builder.Indent();
                builder.AppendLine($"{targetExpr} = (char)reader.ValueSpan[0];");
                builder.Unindent();
                builder.AppendLine("}");
                builder.AppendLine("else");
                builder.AppendLine("{");
                builder.Indent();
                builder.AppendLine("string? s = reader.GetString();");
                builder.AppendLine("if (s is null || s.Length != 1)");
                builder.AppendLine("    throw new JsonException(\"Expected a single-character JSON string.\");");
                builder.AppendLine($"{targetExpr} = s[0];");
                builder.Unindent();
                builder.AppendLine("}");
                break;

            case "System.TimeSpan":
                builder.AppendLine("{");
                builder.Indent();
                builder.AppendLine("if (TryDecodeUnescapedChars(ref reader, chars, out int written))");
                builder.AppendLine("{");
                builder.Indent();
                if (member.ParseFormat is { } timeSpanSpanFormat)
                {
                    builder.AppendLine($"if (!global::System.TimeSpan.TryParseExact(chars[..written], \"{new CSharpStringLiteral(timeSpanSpanFormat).Escaped}\", global::System.Globalization.CultureInfo.InvariantCulture, out var ts_{propertyName}))");
                }
                else
                {
                    builder.AppendLine($"if (!global::System.TimeSpan.TryParse(chars[..written], global::System.Globalization.CultureInfo.InvariantCulture, out var ts_{propertyName}))");
                }
                builder.AppendLine("    throw new JsonException(\"Failed to parse TimeSpan value.\");");
                builder.AppendLine($"{targetExpr} = ts_{propertyName};");
                builder.Unindent();
                builder.AppendLine("}");
                builder.AppendLine("else");
                builder.AppendLine("{");
                builder.Indent();
                builder.AppendLine("string? s = reader.GetString();");
                if (member.ParseFormat is { } timeSpanFormat)
                {
                    builder.AppendLine($"if (s is null || !global::System.TimeSpan.TryParseExact(s, \"{new CSharpStringLiteral(timeSpanFormat).Escaped}\", global::System.Globalization.CultureInfo.InvariantCulture, out var ts_{propertyName}))");
                }
                else
                {
                    builder.AppendLine($"if (s is null || !global::System.TimeSpan.TryParse(s, global::System.Globalization.CultureInfo.InvariantCulture, out var ts_{propertyName}))");
                }
                builder.AppendLine("    throw new JsonException(\"Failed to parse TimeSpan value.\");");
                builder.AppendLine($"{targetExpr} = ts_{propertyName};");
                builder.Unindent();
                builder.AppendLine("}");
                builder.Unindent();
                builder.AppendLine("}");
                break;

            case "System.Half":
                builder.AppendLine("if (reader.TokenType == JsonTokenType.Number && global::System.Half.TryParse(reader.ValueSpan, global::System.Globalization.NumberStyles.Float | global::System.Globalization.NumberStyles.AllowThousands, global::System.Globalization.CultureInfo.InvariantCulture, out var halfNum))");
                builder.AppendLine($"    {targetExpr} = halfNum;");
                builder.AppendLine("else if (reader.TokenType == JsonTokenType.String && !reader.HasValueSequence && !reader.ValueIsEscaped && global::System.Half.TryParse(reader.ValueSpan, global::System.Globalization.NumberStyles.Float | global::System.Globalization.NumberStyles.AllowThousands, global::System.Globalization.CultureInfo.InvariantCulture, out var halfSpanStr))");
                builder.AppendLine($"    {targetExpr} = halfSpanStr;");
                builder.AppendLine("else if (reader.TokenType == JsonTokenType.String && global::System.Half.TryParse(reader.GetString(), global::System.Globalization.NumberStyles.Float | global::System.Globalization.NumberStyles.AllowThousands, global::System.Globalization.CultureInfo.InvariantCulture, out var halfStr))");
                builder.AppendLine($"    {targetExpr} = halfStr;");
                builder.AppendLine("else");
                builder.AppendLine("    throw new JsonException(\"Expected numeric or string value for Half property.\");");
                break;

            case "System.Int128":
                builder.AppendLine("if (reader.TokenType == JsonTokenType.Number && global::System.Int128.TryParse(reader.ValueSpan, global::System.Globalization.NumberStyles.Integer, global::System.Globalization.CultureInfo.InvariantCulture, out var i128Num))");
                builder.AppendLine($"    {targetExpr} = i128Num;");
                builder.AppendLine("else if (reader.TokenType == JsonTokenType.String && !reader.HasValueSequence && !reader.ValueIsEscaped && global::System.Int128.TryParse(reader.ValueSpan, global::System.Globalization.NumberStyles.Integer, global::System.Globalization.CultureInfo.InvariantCulture, out var i128SpanStr))");
                builder.AppendLine($"    {targetExpr} = i128SpanStr;");
                builder.AppendLine("else if (reader.TokenType == JsonTokenType.String && global::System.Int128.TryParse(reader.GetString(), global::System.Globalization.NumberStyles.Integer, global::System.Globalization.CultureInfo.InvariantCulture, out var i128Str))");
                builder.AppendLine($"    {targetExpr} = i128Str;");
                builder.AppendLine("else");
                builder.AppendLine("    throw new JsonException(\"Expected numeric or string value for Int128 property.\");");
                break;

            case "System.UInt128":
                builder.AppendLine("if (reader.TokenType == JsonTokenType.Number && global::System.UInt128.TryParse(reader.ValueSpan, global::System.Globalization.NumberStyles.Integer, global::System.Globalization.CultureInfo.InvariantCulture, out var u128Num))");
                builder.AppendLine($"    {targetExpr} = u128Num;");
                builder.AppendLine("else if (reader.TokenType == JsonTokenType.String && !reader.HasValueSequence && !reader.ValueIsEscaped && global::System.UInt128.TryParse(reader.ValueSpan, global::System.Globalization.NumberStyles.Integer, global::System.Globalization.CultureInfo.InvariantCulture, out var u128SpanStr))");
                builder.AppendLine($"    {targetExpr} = u128SpanStr;");
                builder.AppendLine("else if (reader.TokenType == JsonTokenType.String && global::System.UInt128.TryParse(reader.GetString(), global::System.Globalization.NumberStyles.Integer, global::System.Globalization.CultureInfo.InvariantCulture, out var u128Str))");
                builder.AppendLine($"    {targetExpr} = u128Str;");
                builder.AppendLine("else");
                builder.AppendLine("    throw new JsonException(\"Expected numeric or string value for UInt128 property.\");");
                break;

            case "System.Numerics.BigInteger":
                builder.AppendLine("if (reader.TokenType == JsonTokenType.Number)");
                builder.AppendLine("{");
                builder.Indent();
                builder.AppendLine("if (!reader.HasValueSequence && reader.ValueSpan.Length <= 128)");
                builder.AppendLine("{");
                builder.Indent();
                builder.AppendLine("int written = global::System.Text.Encoding.UTF8.GetChars(reader.ValueSpan, chars);");
                builder.AppendLine("if (global::System.Numerics.BigInteger.TryParse(chars[..written], global::System.Globalization.NumberStyles.Integer, global::System.Globalization.CultureInfo.InvariantCulture, out var biNumSpan))");
                builder.AppendLine($"    {targetExpr} = biNumSpan;");
                builder.AppendLine("else");
                builder.AppendLine("    throw new JsonException(\"Expected numeric or string value for BigInteger property.\");");
                builder.Unindent();
                builder.AppendLine("}");
                builder.AppendLine("else if (global::System.Numerics.BigInteger.TryParse(global::System.Text.Encoding.UTF8.GetString(reader.ValueSpan), global::System.Globalization.NumberStyles.Integer, global::System.Globalization.CultureInfo.InvariantCulture, out var biNum))");
                builder.AppendLine("{");
                builder.Indent();
                builder.AppendLine($"{targetExpr} = biNum;");
                builder.Unindent();
                builder.AppendLine("}");
                builder.AppendLine("else");
                builder.AppendLine("{");
                builder.Indent();
                builder.AppendLine("throw new JsonException(\"Expected numeric or string value for BigInteger property.\");");
                builder.Unindent();
                builder.AppendLine("}");
                builder.Unindent();
                builder.AppendLine("}");
                builder.AppendLine("else");
                builder.AppendLine("{");
                builder.Indent();
                builder.AppendLine("if (TryDecodeUnescapedChars(ref reader, chars, out int written))");
                builder.AppendLine("{");
                builder.Indent();
                builder.AppendLine("if (global::System.Numerics.BigInteger.TryParse(chars[..written], global::System.Globalization.NumberStyles.Integer, global::System.Globalization.CultureInfo.InvariantCulture, out var biStrSpan))");
                builder.AppendLine($"    {targetExpr} = biStrSpan;");
                builder.AppendLine("else");
                builder.AppendLine("    throw new JsonException(\"Expected numeric or string value for BigInteger property.\");");
                builder.Unindent();
                builder.AppendLine("}");
                builder.AppendLine("else if (reader.TokenType == JsonTokenType.String && global::System.Numerics.BigInteger.TryParse(reader.GetString(), global::System.Globalization.NumberStyles.Integer, global::System.Globalization.CultureInfo.InvariantCulture, out var biStr))");
                builder.AppendLine("{");
                builder.Indent();
                builder.AppendLine($"{targetExpr} = biStr;");
                builder.Unindent();
                builder.AppendLine("}");
                builder.AppendLine("else");
                builder.AppendLine("{");
                builder.Indent();
                builder.AppendLine("throw new JsonException(\"Expected numeric or string value for BigInteger property.\");");
                builder.Unindent();
                builder.AppendLine("}");
                builder.Unindent();
                builder.AppendLine("}");
                break;

            case "System.Uri":
                if (!handlesNullUpfront)
                {
                    builder.AppendLine("if (reader.TokenType == JsonTokenType.Null)");
                    builder.AppendLine("{");
                    builder.Indent();
                    builder.AppendLine($"{targetExpr} = null!;");
                    builder.Unindent();
                    builder.AppendLine("}");
                    builder.AppendLine("else");
                    builder.AppendLine("{");
                    builder.Indent();
                }
                builder.AppendLine("{");
                builder.Indent();
                builder.AppendLine("string? s = reader.GetString();");
                builder.AppendLine("if (s is null || !global::System.Uri.TryCreate(s, global::System.UriKind.RelativeOrAbsolute, out var parsedUri))");
                builder.AppendLine("    throw new JsonException(\"Failed to parse Uri value.\");");
                builder.AppendLine($"{targetExpr} = parsedUri;");
                builder.Unindent();
                builder.AppendLine("}");
                if (!handlesNullUpfront)
                {
                    builder.Unindent();
                    builder.AppendLine("}");
                }
                break;

            case "byte[]":
            case "System.Byte[]":
                builder.AppendLine($"{targetExpr} = reader.GetBytesFromBase64();");
                break;

            default:
                if (member.IsEnum)
                {
                    var enumType = member.UnderlyingTypeName;
                    var getMethod = member.EnumUnderlyingReaderMethod ?? "GetInt32";
                    builder.AppendLine("if (reader.TokenType == JsonTokenType.Number)");
                    builder.AppendLine($"    {targetExpr} = ({enumType})reader.{getMethod}();");
                    builder.AppendLine("else if (reader.TokenType == JsonTokenType.String)");
                    builder.AppendLine("{");
                    builder.Indent();
                    builder.AppendLine("if (TryDecodeUnescapedChars(ref reader, chars, out int written))");
                    builder.AppendLine("{");
                    builder.Indent();
                    builder.AppendLine($"if (Enum.TryParse<{enumType}>(chars[..written], ignoreCase: true, out var enumVal))");
                    builder.AppendLine($"    {targetExpr} = enumVal;");
                    builder.AppendLine("else");
                    builder.AppendLine($"    throw new JsonException($\"Failed to parse value '{{chars[..written].ToString()}}' as enum {enumType}.\");");
                    builder.Unindent();
                    builder.AppendLine("}");
                    builder.AppendLine("else");
                    builder.AppendLine("{");
                    builder.Indent();
                    builder.AppendLine("string s = reader.GetString()!;");
                    builder.AppendLine($"if (Enum.TryParse<{enumType}>(s, ignoreCase: true, out var enumVal))");
                    builder.AppendLine($"    {targetExpr} = enumVal;");
                    builder.AppendLine("else");
                    builder.AppendLine($"    throw new JsonException($\"Failed to parse value '{{s}}' as enum {enumType}.\");");
                    builder.Unindent();
                    builder.AppendLine("}");
                    builder.Unindent();
                    builder.AppendLine("}");
                    builder.AppendLine("else");
                    builder.AppendLine($"    throw new JsonException(\"Expected numeric or string value for enum {enumType}.\");");
                }
                else if (member.IsSpanParsable || member.IsUtf8SpanParsable)
                {
                    builder.AppendLine("if (reader.TokenType != JsonTokenType.String)");
                    builder.AppendLine("{");
                    builder.Indent();
                    builder.AppendLine("result = default!;");
                    builder.AppendLine("return false;");
                    builder.Unindent();
                    builder.AppendLine("}");
                    var parseCall = member.IsSpanParsable
                        ? $"TryParseSpan<{member.UnderlyingTypeName}>(ref reader, out var parsed_{propertyName})"
                        : $"TryParseUtf8Span<{member.UnderlyingTypeName}>(ref reader, out var parsed_{propertyName})";
                    var cond = member.IsReferenceType ? $"{parseCall} && parsed_{propertyName} is not null" : parseCall;
                    builder.AppendLine($"if ({cond})");
                    builder.AppendLine($"    {targetExpr} = parsed_{propertyName};");
                    builder.AppendLine("else");
                    builder.AppendLine($"    throw new JsonException(\"Failed to parse value for property '{propertyName}'.\");");
                }
                else
                {
                    builder.AppendLine($"{targetExpr} = reader.GetString()!;");
                }
                break;
        }

        if (tracksPresence)
        {
            builder.AppendLine($"_has_{propertyName} = true;");
        }

        if (handlesNullUpfront)
        {
            builder.Unindent();
            builder.AppendLine("}");
        }

        if (useLocalForValidation)
        {
            builder.AppendLine($"record.{propertyName} = {targetExpr};");
        }
    }

    private static void EmitRegistration(SourceProductionContext context, IReadOnlyList<TypeDescriptor> descriptors)
    {
        var builder = new SourceBuilder(8192);
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        builder.AppendLine("using System;");
        builder.AppendLine("using System.Runtime.CompilerServices;");
        builder.AppendLine();
        builder.AppendLine($"namespace {GENERATED_NAMESPACE};");
        builder.AppendLine();
        builder.AppendLine("file static class JsonlRecordBinderGeneratedRegistration");
        builder.AppendLine("{");
        builder.Indent();
        builder.AppendLine("[ModuleInitializer]");
        builder.AppendLine("internal static void Register()");
        builder.AppendLine("{");
        builder.Indent();

        foreach (var descriptor in descriptors)
        {
            var binderMembers = GetMembersWithSetters(descriptor.Members);
            if (descriptor.CanGenerateBinder && binderMembers.Count > 0)
            {
                var byteBinderClassName = $"global::{GENERATED_NAMESPACE}.JsonlInlineByteSourceBinder_{descriptor.SafeClassName}";
                builder.AppendLine($"{BINDER_FACTORY_TYPE}.RegisterByteBinder<{descriptor.FullyQualifiedName}>({byteBinderClassName}.Instance);");

                var charBinderClassName = $"global::{GENERATED_NAMESPACE}.JsonlInlineCharSourceBinder_{descriptor.SafeClassName}";
                builder.AppendLine($"{BINDER_FACTORY_TYPE}.RegisterCharBinder<{descriptor.FullyQualifiedName}>({charBinderClassName}.Instance);");
            }

            var validatorMembers = GetValidatorMembers(descriptor.Members);
            if (descriptor.CanRegisterValidator)
            {
                var requiredValueMembers = validatorMembers
                    .Where(m => m.Validation.NotNull && !m.IsNullable && !m.IsReferenceType && m.HasSetter)
                    .ToList();
                if (requiredValueMembers.Count > 0)
                {
                    var entries = string.Join(", ", requiredValueMembers.Select(m =>
                    {
                        var json = m.CustomJsonName is null ? "null" : $"\"{new CSharpStringLiteral(m.CustomJsonName).Escaped}\"";
                        return $"(\"{m.MemberName}\", {json})";
                    }));
                    builder.AppendLine($"{BINDER_FACTORY_TYPE}.RegisterRequiredValueFields<{descriptor.FullyQualifiedName}>(new (string, string?)[] {{ {entries} }});");
                }

                if (validatorMembers.Count > 0)
                {
                    var validatorClassName = $"global::{GENERATED_NAMESPACE}.JsonlRecordValidator_{descriptor.SafeClassName}";
                    builder.AppendLine($"{BINDER_FACTORY_TYPE}.RegisterValidator<{descriptor.FullyQualifiedName}>({validatorClassName}.Instance);");
                }
                else
                {
                    builder.AppendLine($"{BINDER_FACTORY_TYPE}.RegisterNoOpValidator<{descriptor.FullyQualifiedName}>();");
                }
            }
        }

        builder.Unindent();
        builder.AppendLine("}");
        builder.Unindent();
        builder.AppendLine("}");

        context.AddSource("JsonlRecordBinderFactory.Registration.g.cs", builder.ToString());
    }

    private static TypeDescriptor? BuildDescriptor(RecordTypeTarget target, CancellationToken ct)
    {
        var type = target.Symbol;
        var members = new List<MemberDescriptor>();
        var diagnostics = new List<Diagnostic>();
        bool canGenerateBinder = true;
        bool canRegisterValidator = true;

        foreach (var iface in type.AllInterfaces)
        {
            ct.ThrowIfCancellationRequested();
            var ifaceName = iface.ToDisplayString();
            if (ifaceName is "System.Text.Json.Serialization.IJsonOnDeserializing" or "System.Text.Json.Serialization.IJsonOnDeserialized")
            {
                canGenerateBinder = false;
                break;
            }
        }

        bool hasPublicParameterlessCtor = type.IsValueType;
        foreach (var ctor in type.InstanceConstructors)
        {
            ct.ThrowIfCancellationRequested();
            if (ctor.DeclaredAccessibility == Accessibility.Public && ctor.Parameters.Length == 0)
                hasPublicParameterlessCtor = true;

            if (new SymbolAttributes(ctor).FindFirst("System.Text.Json.Serialization.JsonConstructorAttribute") is not null)
                canGenerateBinder = false;
        }
        if (!hasPublicParameterlessCtor)
            canGenerateBinder = false;

        // Fields and properties share one name set so a derived member hiding a base member of the same name is validated once.
        var seenMembers = new HashSet<string>(StringComparer.Ordinal);
        for (INamedTypeSymbol? current = type;
             current is not null && current.SpecialType is not (SpecialType.System_Object or SpecialType.System_ValueType);
             current = current.BaseType)
        {
            ct.ThrowIfCancellationRequested();
            if (new SymbolAttributes(current).FindFirst(jsonTypeContractAttributeNames) is not null)
                canGenerateBinder = false;

            bool sameAssembly = SymbolEqualityComparer.Default.Equals(current.ContainingAssembly, type.ContainingAssembly);
            foreach (var rawMember in current.GetMembers())
            {
                if (rawMember.IsStatic)
                    continue;

                if (rawMember is IFieldSymbol or IPropertySymbol &&
                    new SymbolAttributes(rawMember).FindFirst(jsonMemberContractAttributeNames) is not null)
                {
                    canGenerateBinder = false;
                }
            }

            foreach (var field in current.GetMembers().OfType<IFieldSymbol>())
            {
                ct.ThrowIfCancellationRequested();
                if (field.IsStatic || field.IsImplicitlyDeclared || seenMembers.Contains(field.Name))
                    continue;

                bool accessible = field.DeclaredAccessibility == Accessibility.Public
                    || (sameAssembly && field.DeclaredAccessibility is Accessibility.Internal or Accessibility.ProtectedOrInternal);
                var fieldAttrs = new SymbolAttributes(field);
                bool hasJsonInclude = fieldAttrs.FindFirst("System.Text.Json.Serialization.JsonIncludeAttribute") is not null;
                if (!hasJsonInclude)
                    continue;

                string? fieldIgnoreCondition = GetJsonIgnoreConditionName(fieldAttrs);
                if (fieldIgnoreCondition == "Always")
                {
                    if (accessible)
                        seenMembers.Add(field.Name);
                    continue;
                }

                var fieldValidation = ParsedValidationRules.ExtractWithoutDiagnostics(field);
                if (!HasAnyValidationRules(fieldValidation))
                {
                    if (accessible)
                        seenMembers.Add(field.Name);
                    continue;
                }

                seenMembers.Add(field.Name);
                if (fieldIgnoreCondition is "WhenReading" or "WhenWriting" or "WhenWritingDefault" or "WhenWritingNull")
                {
                    canRegisterValidator = false;
                    continue;
                }

                var fieldMeta = new TypeMetadata(field.Type);
                if (accessible && IsJsonlSupportedType(fieldMeta))
                {
                    var (headerName, customJsonName) = ExtractMemberNames(fieldAttrs);
                    var parsedAttrs = ParsedPropertyAttributes.Extract(field);
                    members.Add(new MemberDescriptor(
                        field.Name,
                        headerName,
                        fieldMeta.FullyQualifiedName,
                        field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        fieldMeta.BaseTypeName,
                        fieldMeta.IsNullable,
                        fieldMeta.IsEnum,
                        fieldMeta.EnumUnderlyingReaderMethod,
                        field.Type.IsReferenceType,
                        fieldMeta.UnderlyingFullyQualifiedName,
                        fieldMeta.IsSpanParsable,
                        fieldMeta.IsUtf8SpanParsable,
                        customJsonName,
                        HasSetter: false,
                        HasGetter: true,
                        parsedAttrs.ParseFormat,
                        fieldValidation));
                }
                else
                {
                    canRegisterValidator = false;
                }
            }

            foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
            {
                ct.ThrowIfCancellationRequested();

                if (property.IsStatic || property.IsIndexer)
                    continue;

                if (!seenMembers.Add(property.Name))
                    continue;

                var attrs = new SymbolAttributes(property);
                string? ignoreCondition = GetJsonIgnoreConditionName(attrs);
                if (ignoreCondition == "Always")
                    continue;

                bool isJsonIgnoredOnRead = ignoreCondition == "WhenReading";
                bool hasAsymmetricIgnore = ignoreCondition is "WhenReading" or "WhenWriting" or "WhenWritingDefault" or "WhenWritingNull";

                var hasSetter = property.SetMethod is { DeclaredAccessibility: Accessibility.Public, IsInitOnly: false };
                var hasInitSetter = property.SetMethod is { DeclaredAccessibility: Accessibility.Public, IsInitOnly: true };
                var hasGetter = property.GetMethod is { DeclaredAccessibility: Accessibility.Public };
                var hasAccessibleGetter = hasGetter
                    || (sameAssembly && property.GetMethod is { DeclaredAccessibility: Accessibility.Internal or Accessibility.ProtectedOrInternal });

                if (!hasSetter && !hasGetter && !hasInitSetter)
                {
                    var hiddenValidation = ParsedValidationRules.ExtractWithoutDiagnostics(property);
                    if (HasAnyValidationRules(hiddenValidation))
                    {
                        if (hasAsymmetricIgnore)
                        {
                            canRegisterValidator = false;
                            continue;
                        }

                        var hiddenMeta = new TypeMetadata(property.Type);
                        if (hasAccessibleGetter && IsJsonlSupportedType(hiddenMeta))
                        {
                            var (hiddenHeaderName, hiddenCustomJsonName) = ExtractMemberNames(attrs);
                            var hiddenParsedAttrs = ParsedPropertyAttributes.Extract(property);
                            members.Add(new MemberDescriptor(
                                property.Name,
                                hiddenHeaderName,
                                hiddenMeta.FullyQualifiedName,
                                property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                hiddenMeta.BaseTypeName,
                                hiddenMeta.IsNullable,
                                hiddenMeta.IsEnum,
                                hiddenMeta.EnumUnderlyingReaderMethod,
                                property.Type.IsReferenceType,
                                hiddenMeta.UnderlyingFullyQualifiedName,
                                hiddenMeta.IsSpanParsable,
                                hiddenMeta.IsUtf8SpanParsable,
                                hiddenCustomJsonName,
                                HasSetter: false,
                                HasGetter: true,
                                hiddenParsedAttrs.ParseFormat,
                                hiddenValidation));
                        }
                        else
                        {
                            canRegisterValidator = false;
                        }
                    }
                    continue;
                }

                if (!isJsonIgnoredOnRead &&
                    (hasInitSetter || property.IsRequired || attrs.FindFirst(jsonPropertyContractAttributeNames) is not null))
                {
                    canGenerateBinder = false;
                }

                var (headerName, customJsonName) = ExtractMemberNames(attrs);

                bool binderSetter = !isJsonIgnoredOnRead && hasSetter;
                var typeMeta = new TypeMetadata(property.Type);
                if (!isJsonIgnoredOnRead && HasTypeJsonConverterAttribute(typeMeta.UnderlyingSymbol))
                {
                    canGenerateBinder = false;
                }
                var propertyValidation = ParsedValidationRules.ExtractWithoutDiagnostics(property);
                if (hasAsymmetricIgnore && HasAnyValidationRules(propertyValidation))
                    canRegisterValidator = false;

                if (!IsJsonlSupportedType(typeMeta))
                {
                    if (HasAnyValidationRules(propertyValidation))
                        canRegisterValidator = false;

                    if (!isJsonIgnoredOnRead)
                    {
                        diagnostics.Add(Diagnostic.Create(
                            unsupportedPropertyTypeDiagnostic,
                            property.Locations.FirstOrDefault() ?? Location.None,
                            property.Name,
                            property.Type.ToDisplayString()));
                        if (binderSetter)
                            canGenerateBinder = false;
                    }
                    continue;
                }

                var typeName = typeMeta.FullyQualifiedName;
                var typeofTypeName = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var parsedAttrs = ParsedPropertyAttributes.Extract(property);

                members.Add(new MemberDescriptor(
                    property.Name,
                    headerName,
                    typeName,
                    typeofTypeName,
                    typeMeta.BaseTypeName,
                    typeMeta.IsNullable,
                    typeMeta.IsEnum,
                    typeMeta.EnumUnderlyingReaderMethod,
                    property.Type.IsReferenceType,
                    typeMeta.UnderlyingFullyQualifiedName,
                    typeMeta.IsSpanParsable,
                    typeMeta.IsUtf8SpanParsable,
                    customJsonName,
                    binderSetter,
                    hasAccessibleGetter,
                    parsedAttrs.ParseFormat,
                    propertyValidation));
            }
        }

        if (members.Count == 0 && diagnostics.Count == 0 && canRegisterValidator)
            return null;

        return new TypeDescriptor(target.FullyQualifiedName, target.SafeClassName, type.IsValueType, canGenerateBinder, canRegisterValidator, members, diagnostics);
    }

    private static (string? HeaderName, string? CustomJsonName) ExtractMemberNames(SymbolAttributes attrs)
    {
        var mapAttribute = attrs.FindFirst(columnAttributeNames);
        string? headerName = null;
        if (mapAttribute is not null)
        {
            foreach (var arg in mapAttribute.NamedArguments)
            {
                if (arg.Key == "Name" && arg.Value.Value is string s && !string.IsNullOrWhiteSpace(s))
                {
                    headerName = s;
                }
            }
        }

        var jsonPropertyAttribute = attrs.FindFirst(jsonPropertyAttributeNames);
        string? customJsonName = null;
        if (jsonPropertyAttribute is not null)
        {
            if (jsonPropertyAttribute.ConstructorArguments.Length > 0 && jsonPropertyAttribute.ConstructorArguments[0].Value is string s)
            {
                customJsonName = s;
            }
        }

        return (headerName, customJsonName);
    }

    private static bool HasAnyValidationRules(ParsedValidationRules validation)
        => validation.NotNull
            || validation.NotEmpty
            || validation.MaxLength >= 0
            || validation.MinLength >= 0
            || validation.HasRange
            || validation.Pattern != null;

    private static string? GetJsonIgnoreConditionName(SymbolAttributes attrs)
    {
        var ignoreAttr = attrs.FindFirst("System.Text.Json.Serialization.JsonIgnoreAttribute");
        if (ignoreAttr is null)
            return null;

        foreach (var arg in ignoreAttr.NamedArguments)
        {
            if (arg.Key == "Condition")
            {
                if (arg.Value.Type is INamedTypeSymbol enumType)
                {
                    foreach (var enumField in enumType.GetMembers().OfType<IFieldSymbol>())
                    {
                        if (enumField.HasConstantValue && Equals(enumField.ConstantValue, arg.Value.Value))
                            return enumField.Name;
                    }
                }

                return arg.Value.Value is int condition ? (condition == 1 ? "Always" : null) : null;
            }
        }

        return "Always";
    }

    private static bool HasTypeJsonConverterAttribute(ITypeSymbol typeSymbol)
    {
        for (ITypeSymbol? current = typeSymbol;
             current is not null && current.SpecialType is not (SpecialType.System_Object or SpecialType.System_ValueType);
             current = current.BaseType)
        {
            if (new SymbolAttributes(current).FindFirst("System.Text.Json.Serialization.JsonConverterAttribute") is not null)
                return true;
        }
        return false;
    }

    private static bool IsJsonlSupportedType(TypeMetadata typeMeta)
        => typeMeta.IsSupported || IsFloatArray(typeMeta.UnderlyingSymbol);

    private static bool IsFloatArray(ITypeSymbol type)
        => type is IArrayTypeSymbol { Rank: 1, ElementType.SpecialType: SpecialType.System_Single };

    private static bool NeedsCharDecodeHelper(IReadOnlyList<MemberDescriptor> members)
    {
        foreach (var m in members)
        {
            if (m.IsEnum)
                return true;
            if ((m.BaseTypeName is "System.DateTime" or "System.DateTimeOffset" or "System.Guid") && m.ParseFormat is not null)
                return true;
            if (m.BaseTypeName is "System.DateOnly" or "System.TimeOnly" or "System.TimeSpan" or "System.Numerics.BigInteger")
                return true;
        }
        return false;
    }

    private static bool HasAnyValidation(MemberDescriptor member)
        => member.Validation.NotNull
            || (member.Validation.NotEmpty && IsStringMember(member))
            || ((member.Validation.MaxLength >= 0 || member.Validation.MinLength >= 0) && IsLengthMember(member))
            || (member.Validation.HasRange && IsNumericType(member.BaseTypeName))
            || (member.Validation.Pattern != null && IsStringMember(member));

    private static bool HasAnyValidation(IReadOnlyList<MemberDescriptor> members)
    {
        foreach (var m in members)
        {
            if (HasAnyValidation(m))
                return true;
        }
        return false;
    }

    private static bool RequiresValueTypePresenceFlag(MemberDescriptor member)
        => member.Validation.NotNull && !member.IsNullable && !member.IsReferenceType;

    private static List<MemberDescriptor> GetInaccessibleGetterValidatedMembers(IReadOnlyList<MemberDescriptor> members)
    {
        var list = new List<MemberDescriptor>();
        foreach (var m in members)
        {
            if (!m.HasGetter && HasAnyValidation(m))
                list.Add(m);
        }
        return list;
    }

    private static List<MemberDescriptor> GetValueTypePresenceMembers(IReadOnlyList<MemberDescriptor> members)
    {
        var list = new List<MemberDescriptor>();
        foreach (var m in members)
        {
            if (RequiresValueTypePresenceFlag(m))
                list.Add(m);
        }
        return list;
    }

    private static bool IsStringMember(MemberDescriptor member)
        => member.BaseTypeName is "string" or "System.String";

    private static bool IsLengthMember(MemberDescriptor member)
        => IsStringMember(member) || member.BaseTypeName is "float[]" or "System.Single[]";

    private static bool IsNumericType(string baseTypeName)
        => baseTypeName is "int" or "System.Int32"
            or "long" or "System.Int64"
            or "short" or "System.Int16"
            or "byte" or "System.Byte"
            or "sbyte" or "System.SByte"
            or "uint" or "System.UInt32"
            or "ulong" or "System.UInt64"
            or "ushort" or "System.UInt16"
            or "double" or "System.Double"
            or "float" or "System.Single"
            or "decimal" or "System.Decimal"
            or "System.Half"
            or "System.Int128"
            or "System.UInt128"
            or "System.Numerics.BigInteger";

    private static void EmitValidateMethod(
        SourceBuilder builder,
        TypeDescriptor descriptor,
        IReadOnlyList<MemberDescriptor> members,
        IReadOnlyList<MemberDescriptor> valueTypePresenceFlags,
        IReadOnlyList<MemberDescriptor> inaccessibleGetterValidatedMembers)
    {
        builder.AppendLine();
        var paramList = new List<string> { $"{descriptor.FullyQualifiedName} record" };
        foreach (var m in valueTypePresenceFlags)
            paramList.Add($"bool has_{m.MemberName}");
        foreach (var m in inaccessibleGetterValidatedMembers)
            paramList.Add($"{m.TypeName} _val_{m.MemberName}");
        builder.AppendLine($"private static void ValidateRecord({string.Join(", ", paramList)})");
        builder.AppendLine("{");
        builder.Indent();
        EmitValidationBody(builder, members, hasPresenceFlags: true, useLocalForInaccessibleGetters: true, rowNumberExpr: "0");
        builder.Unindent();
        builder.AppendLine("}");
    }

    private static void EmitRecordValidatorClass(
        SourceBuilder builder,
        TypeDescriptor descriptor,
        IReadOnlyList<MemberDescriptor> validatorMembers)
    {
        builder.AppendLine();
        builder.AppendLine($"internal sealed class JsonlRecordValidator_{descriptor.SafeClassName} : global::HeroParser.JsonLines.Reading.Binders.IJsonlRecordValidator<{descriptor.FullyQualifiedName}>");
        builder.AppendLine("{");
        builder.Indent();

        foreach (var member in validatorMembers)
        {
            if (member.Validation.Pattern != null && IsStringMember(member))
            {
                var escapedPattern = new CSharpStringLiteral(member.Validation.Pattern).Escaped;
                builder.AppendLine($"private static readonly global::System.Text.RegularExpressions.Regex _pattern_{member.MemberName} = new(\"{escapedPattern}\", global::System.Text.RegularExpressions.RegexOptions.None, global::System.TimeSpan.FromMilliseconds({member.Validation.PatternTimeoutMs}));");
            }
        }

        builder.AppendLine($"internal static global::HeroParser.JsonLines.Reading.Binders.IJsonlRecordValidator<{descriptor.FullyQualifiedName}> Instance {{ get; }} = new JsonlRecordValidator_{descriptor.SafeClassName}();");
        builder.AppendLine();
        builder.AppendLine($"public void Validate({descriptor.FullyQualifiedName} record, int rowNumber = 0)");
        builder.AppendLine("{");
        builder.Indent();
        if (!descriptor.IsValueType)
        {
            builder.AppendLine("if (record is null)");
            builder.AppendLine("    return;");
            builder.AppendLine();
        }
        EmitValidationBody(builder, validatorMembers, hasPresenceFlags: false, useLocalForInaccessibleGetters: false, rowNumberExpr: "rowNumber");
        builder.Unindent();
        builder.AppendLine("}");
        builder.Unindent();
        builder.AppendLine("}");
    }

    private static void EmitValidationBody(
        SourceBuilder builder,
        IReadOnlyList<MemberDescriptor> members,
        bool hasPresenceFlags,
        bool useLocalForInaccessibleGetters,
        string rowNumberExpr)
    {
        builder.AppendLine("global::System.Collections.Generic.List<global::HeroParser.Validation.ValidationError>? errors = null;");

        foreach (var member in members)
        {
            if (!HasAnyValidation(member))
                continue;

            var colName = member.CustomJsonName ?? member.HeaderName ?? member.MemberName;
            string memberExpr = (!member.HasGetter && useLocalForInaccessibleGetters)
                ? $"_val_{member.MemberName}"
                : $"record.{member.MemberName}";

            if (member.Validation.NotNull)
            {
                if (IsStringMember(member))
                {
                    builder.AppendLine($"if (string.IsNullOrWhiteSpace({memberExpr}))");
                    builder.AppendLine("{");
                    builder.Indent();
                    EmitAddJsonlValidationError(builder, member.MemberName, colName, "NotNull", "Value is required", memberExpr, rowNumberExpr);
                    builder.Unindent();
                    builder.AppendLine("}");
                }
                else if (member.IsNullable || member.IsReferenceType)
                {
                    builder.AppendLine($"if ({memberExpr} is null)");
                    builder.AppendLine("{");
                    builder.Indent();
                    EmitAddJsonlValidationError(builder, member.MemberName, colName, "NotNull", "Value is required", "null", rowNumberExpr);
                    builder.Unindent();
                    builder.AppendLine("}");
                }
                else if (hasPresenceFlags)
                {
                    builder.AppendLine($"if (!has_{member.MemberName})");
                    builder.AppendLine("{");
                    builder.Indent();
                    EmitAddJsonlValidationError(builder, member.MemberName, colName, "NotNull", "Value is required", "null", rowNumberExpr);
                    builder.Unindent();
                    builder.AppendLine("}");
                }
            }

            if (member.Validation.NotEmpty && IsStringMember(member))
            {
                builder.AppendLine($"if ({memberExpr} is string strEmpty_{member.MemberName} && string.IsNullOrWhiteSpace(strEmpty_{member.MemberName}))");
                builder.AppendLine("{");
                builder.Indent();
                EmitAddJsonlValidationError(builder, member.MemberName, colName, "NotEmpty", "Value must not be empty or whitespace", $"strEmpty_{member.MemberName}", rowNumberExpr);
                builder.Unindent();
                builder.AppendLine("}");
            }

            if (member.Validation.MaxLength >= 0 && IsLengthMember(member))
            {
                builder.AppendLine($"if ({memberExpr} is not null && {memberExpr}.Length > {member.Validation.MaxLength})");
                builder.AppendLine("{");
                builder.Indent();
                var rawExpr = IsStringMember(member) ? memberExpr : "null";
                EmitAddJsonlValidationError(builder, member.MemberName, colName, "MaxLength", $"Value exceeds maximum length of {member.Validation.MaxLength}", rawExpr, rowNumberExpr);
                builder.Unindent();
                builder.AppendLine("}");
            }

            if (member.Validation.MinLength >= 0 && IsLengthMember(member))
            {
                builder.AppendLine($"if ({memberExpr} is not null && {memberExpr}.Length < {member.Validation.MinLength})");
                builder.AppendLine("{");
                builder.Indent();
                var rawExpr = IsStringMember(member) ? memberExpr : "null";
                EmitAddJsonlValidationError(builder, member.MemberName, colName, "MinLength", $"Value is shorter than minimum length of {member.Validation.MinLength}", rawExpr, rowNumberExpr);
                builder.Unindent();
                builder.AppendLine("}");
            }

            if (member.Validation.HasRange && IsNumericType(member.BaseTypeName))
            {
                var valueExpr = member.IsNullable ? $"val_{member.MemberName}" : memberExpr;
                if (member.Validation.BuildRangeViolationCondition(valueExpr, member.BaseTypeName) is { } rangeCondition)
                {
                    bool wrapBlock = true;
                    if (member.IsNullable)
                    {
                        builder.AppendLine(rangeCondition == "true"
                            ? $"if ({memberExpr} is {{ }} {valueExpr})"
                            : $"if ({memberExpr} is {{ }} {valueExpr} && ({rangeCondition}))");
                    }
                    else if (hasPresenceFlags && RequiresValueTypePresenceFlag(member))
                    {
                        builder.AppendLine(rangeCondition == "true"
                            ? $"if (has_{member.MemberName})"
                            : $"if (has_{member.MemberName} && ({rangeCondition}))");
                    }
                    else if (rangeCondition != "true")
                    {
                        builder.AppendLine($"if ({rangeCondition})");
                    }
                    else
                    {
                        wrapBlock = false;
                    }

                    if (wrapBlock)
                    {
                        builder.AppendLine("{");
                        builder.Indent();
                    }
                    var rangeMsg = member.Validation.FormatRangeMessage();
                    EmitAddJsonlValidationError(builder, member.MemberName, colName, "Range", rangeMsg, $"{valueExpr}.ToString(global::System.Globalization.CultureInfo.InvariantCulture)", rowNumberExpr);
                    if (wrapBlock)
                    {
                        builder.Unindent();
                        builder.AppendLine("}");
                    }
                }
            }

            if (member.Validation.Pattern != null && IsStringMember(member))
            {
                builder.AppendLine($"if ({memberExpr} is string strPat_{member.MemberName} && !_pattern_{member.MemberName}.IsMatch(strPat_{member.MemberName}))");
                builder.AppendLine("{");
                builder.Indent();
                EmitAddJsonlValidationError(builder, member.MemberName, colName, "Pattern", "Value does not match pattern", $"strPat_{member.MemberName}", rowNumberExpr);
                builder.Unindent();
                builder.AppendLine("}");
            }
        }

        builder.AppendLine("if (errors is { Count: > 0 })");
        builder.AppendLine("    throw new global::HeroParser.Validation.ValidationException(errors);");
    }

    private static void EmitAddJsonlValidationError(
        SourceBuilder builder,
        string memberName,
        string columnName,
        string rule,
        string message,
        string rawValueExpr,
        string rowNumberExpr)
    {
        builder.AppendLine("(errors ??= new global::System.Collections.Generic.List<global::HeroParser.Validation.ValidationError>()).Add(new global::HeroParser.Validation.ValidationError");
        builder.AppendLine("{");
        builder.Indent();
        builder.AppendLine($"RowNumber = {rowNumberExpr},");
        builder.AppendLine("ColumnIndex = 0,");
        builder.AppendLine($"ColumnName = \"{new CSharpStringLiteral(columnName).Escaped}\",");
        builder.AppendLine($"PropertyName = \"{memberName}\",");
        builder.AppendLine($"Rule = \"{rule}\",");
        builder.AppendLine($"Message = \"{new CSharpStringLiteral(message).Escaped}\",");
        builder.AppendLine($"RawValue = {rawValueExpr}");
        builder.Unindent();
        builder.AppendLine("});");
    }

    private static List<MemberDescriptor> GetMembersWithSetters(IReadOnlyList<MemberDescriptor> members)
    {
        var list = new List<MemberDescriptor>();
        foreach (var m in members) if (m.HasSetter) list.Add(m);
        return list;
    }

    private static List<MemberDescriptor> GetValidatorMembers(IReadOnlyList<MemberDescriptor> members)
    {
        var list = new List<MemberDescriptor>();
        foreach (var m in members)
        {
            if (m.HasGetter && HasAnyValidation(m))
                list.Add(m);
        }
        return list;
    }

    private static bool IsCustomParsable(MemberDescriptor member)
        => !member.IsEnum
           && (member.IsSpanParsable || member.IsUtf8SpanParsable)
           && !IsExplicitlyHandledType(member.BaseTypeName);

    private static bool IsExplicitlyHandledType(string baseTypeName)
        => baseTypeName is "string" or "System.String"
            or "int" or "System.Int32"
            or "long" or "System.Int64"
            or "short" or "System.Int16"
            or "byte" or "System.Byte"
            or "sbyte" or "System.SByte"
            or "uint" or "System.UInt32"
            or "ulong" or "System.UInt64"
            or "ushort" or "System.UInt16"
            or "double" or "System.Double"
            or "float" or "System.Single"
            or "decimal" or "System.Decimal"
            or "bool" or "System.Boolean"
            or "float[]" or "System.Single[]"
            or "System.DateTime" or "System.DateTimeOffset" or "System.Guid"
            or "System.DateOnly" or "System.TimeOnly"
            or "char" or "System.Char"
            or "System.TimeSpan" or "System.Half" or "System.Int128" or "System.UInt128"
            or "System.Numerics.BigInteger" or "System.Uri"
            or "byte[]" or "System.Byte[]";

    private sealed record TypeDescriptor(
        string FullyQualifiedName,
        string SafeClassName,
        bool IsValueType,
        bool CanGenerateBinder,
        bool CanRegisterValidator,
        IReadOnlyList<MemberDescriptor> Members,
        IReadOnlyList<Diagnostic> Diagnostics);

    private sealed record MemberDescriptor(
        string MemberName,
        string? HeaderName,
        string TypeName,
        string TypeOfTypeName,
        string BaseTypeName,
        bool IsNullable,
        bool IsEnum,
        string? EnumUnderlyingReaderMethod,
        bool IsReferenceType,
        string UnderlyingTypeName,
        bool IsSpanParsable,
        bool IsUtf8SpanParsable,
        string? CustomJsonName,
        bool HasSetter,
        bool HasGetter,
        string? ParseFormat,
        ParsedValidationRules Validation);
}
