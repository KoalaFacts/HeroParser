#if !NET9_0_OR_GREATER
using System.Diagnostics.CodeAnalysis;
#endif
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using HeroParser.JsonLines.Reading.Binders;
using HeroParser.Validation;

namespace HeroParser.JsonLines.Writing;

/// <summary>
/// AOT-safe fallback validator for JSONL records written via <see cref="JsonTypeInfo{T}"/>
/// without a generated <see cref="GenerateBinderAttribute"/> validator.
/// </summary>
internal sealed class JsonlTypeInfoRecordValidator
{
    private static readonly ConditionalWeakTable<JsonTypeInfo, ContractResolution> cache = [];

    public static JsonlTypeInfoRecordValidator Instance { get; } = new();

    public bool Validate<T>(
        T record,
        JsonTypeInfo typeInfo,
        ReadOnlySpan<byte> serializedUtf8,
        int rowNumber,
        IJsonlRecordValidator<T>? fallbackValidator = null)
    {
        if (record is null)
            return false;

        ContractResolution resolution = cache.GetValue(typeInfo, ResolveContract);
        if (resolution.HasTrimmedMetadata)
        {
            if (fallbackValidator is not null
                && typeInfo.Type == typeof(T)
                && record is not IJsonOnSerialized
                && typeInfo.OnSerialized is null)
            {
                fallbackValidator.Validate(record, rowNumber);
                return false;
            }

            throw CreateUnsupportedStrictContractException(typeInfo.Type);
        }

        ValidatedProperty[] properties = resolution.Properties;
        if (properties.Length == 0)
            return resolution.RequiresEmptyObjectCheck;

        var reader = new Utf8JsonReader(
            serializedUtf8,
            new JsonReaderOptions { MaxDepth = typeInfo.Options.MaxDepth });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            return false;

        bool hasOnSerialized = record is IJsonOnSerialized || typeInfo.OnSerialized is not null;
        List<ValidationError>? errors = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            int matchIdx = FindPropertyIndex(properties, ref reader);
            if (!reader.Read())
                break;

            if (matchIdx < 0)
            {
                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                    reader.Skip();
                continue;
            }

            ValidatedProperty entry = properties[matchIdx];
            errors ??= [];

            if (reader.TokenType == JsonTokenType.Null)
            {
                entry.Validation.Validate(null, entry.PropertyName, rowNumber, matchIdx, errors, CultureInfo.InvariantCulture);
            }
            else if (reader.TokenType == JsonTokenType.String)
            {
                // Pre-.NET 11 STJ source-gen fast path passes null byte[] to WriteBase64String(ReadOnlySpan<byte>), emitting "".
                if (!hasOnSerialized
                    && entry.PropertyType == typeof(byte[])
                    && !entry.HasCustomConverter
                    && typeInfo.OriginatingResolver is JsonSerializerContext
                    && typeInfo.Options.Converters.Count == 0
                    && reader.ValueSpan.IsEmpty
                    && entry.Getter(record) is null)
                {
                    entry.Validation.Validate(null, entry.PropertyName, rowNumber, matchIdx, errors, CultureInfo.InvariantCulture);
                    continue;
                }
                object? stringValue = ExtractJsonStringValue(ref reader, entry.PropertyType);
                entry.Validation.Validate(stringValue, entry.PropertyName, rowNumber, matchIdx, errors, CultureInfo.InvariantCulture);
            }
            else if (reader.TokenType is JsonTokenType.True or JsonTokenType.False)
            {
                entry.Validation.Validate(reader.TokenType == JsonTokenType.True, entry.PropertyName, rowNumber, matchIdx, errors, CultureInfo.InvariantCulture);
            }
            else if (reader.TokenType == JsonTokenType.Number)
            {
                object numberValue = ExtractJsonNumberValue(ref reader, entry.PropertyType);
                entry.Validation.Validate(numberValue, entry.PropertyName, rowNumber, matchIdx, errors, CultureInfo.InvariantCulture);
            }
            else if (reader.TokenType == JsonTokenType.StartArray && entry.PropertyType == typeof(float[]))
            {
                int elementCount = CountArrayElements(ref reader);
                entry.Validation.ValidateCollectionLength(elementCount, entry.PropertyName, rowNumber, matchIdx, errors);
            }
            else if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject)
            {
                int start = (int)reader.TokenStartIndex;
                reader.Skip();
                int end = (int)reader.BytesConsumed;
                object? structuredValue = hasOnSerialized
                    ? Encoding.UTF8.GetString(serializedUtf8[start..end])
                    : entry.Getter(record);
                entry.Validation.Validate(structuredValue, entry.PropertyName, rowNumber, matchIdx, errors, CultureInfo.InvariantCulture);
            }
        }

        if (errors is { Count: > 0 })
            throw new ValidationException(errors);

        return false;
    }

    private static int FindPropertyIndex(ValidatedProperty[] properties, ref Utf8JsonReader reader)
    {
        for (int i = 0; i < properties.Length; i++)
        {
            if (reader.ValueTextEquals(properties[i].JsonNameUtf8))
                return i;
        }

        return -1;
    }

    private static int CountArrayElements(ref Utf8JsonReader reader)
    {
        int count = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            count++;
            if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                reader.Skip();
        }

        return count;
    }

    private static object ExtractJsonNumberValue(ref Utf8JsonReader reader, Type propertyType)
    {
        Type underlying = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (underlying == typeof(double))
            return reader.TryGetDouble(out double d) ? d : Encoding.UTF8.GetString(reader.ValueSpan);
        if (underlying == typeof(float))
            return reader.TryGetSingle(out float f) ? f : Encoding.UTF8.GetString(reader.ValueSpan);
        if (underlying == typeof(Half))
            return Half.TryParse(reader.ValueSpan, NumberStyles.Float, CultureInfo.InvariantCulture, out Half h)
                ? h
                : Encoding.UTF8.GetString(reader.ValueSpan);
        if (underlying == typeof(decimal))
            return reader.TryGetDecimal(out decimal dec) ? dec : Encoding.UTF8.GetString(reader.ValueSpan);
        if (underlying == typeof(int))
            return reader.TryGetInt32(out int i32) ? i32 : Encoding.UTF8.GetString(reader.ValueSpan);
        if (underlying == typeof(long))
            return reader.TryGetInt64(out long i64) ? i64 : Encoding.UTF8.GetString(reader.ValueSpan);
        if (underlying == typeof(short))
            return reader.TryGetInt16(out short i16) ? i16 : Encoding.UTF8.GetString(reader.ValueSpan);
        if (underlying == typeof(sbyte))
            return reader.TryGetSByte(out sbyte i8) ? i8 : Encoding.UTF8.GetString(reader.ValueSpan);
        if (underlying == typeof(byte))
            return reader.TryGetByte(out byte u8) ? u8 : Encoding.UTF8.GetString(reader.ValueSpan);
        if (underlying == typeof(ushort))
            return reader.TryGetUInt16(out ushort u16) ? u16 : Encoding.UTF8.GetString(reader.ValueSpan);
        if (underlying == typeof(uint))
            return reader.TryGetUInt32(out uint u32) ? u32 : Encoding.UTF8.GetString(reader.ValueSpan);
        if (underlying == typeof(ulong))
            return reader.TryGetUInt64(out ulong u64) ? u64 : Encoding.UTF8.GetString(reader.ValueSpan);
        if (underlying == typeof(System.Numerics.BigInteger))
        {
            string raw = Encoding.UTF8.GetString(reader.ValueSpan);
            return System.Numerics.BigInteger.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bi)
                ? bi
                : raw;
        }
        if (underlying == typeof(Int128))
            return Int128.TryParse(reader.ValueSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out Int128 i128)
                ? i128
                : Encoding.UTF8.GetString(reader.ValueSpan);
        if (underlying == typeof(UInt128))
            return UInt128.TryParse(reader.ValueSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out UInt128 u128)
                ? u128
                : Encoding.UTF8.GetString(reader.ValueSpan);

        if (reader.TryGetInt64(out long l))
            return l;
        if (reader.TryGetUInt64(out ulong ul))
            return ul;
        return Encoding.UTF8.GetString(reader.ValueSpan);
    }

    private static object? ExtractJsonStringValue(ref Utf8JsonReader reader, Type propertyType)
    {
        Type underlying = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (underlying == typeof(byte[]))
            return reader.TryGetBytesFromBase64(out byte[]? bytes) ? bytes : reader.GetString();

        string? text = reader.GetString();
        if (text is null)
            return null;

        if (underlying == typeof(decimal))
            return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal dec) ? dec : text;
        if (underlying == typeof(double))
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : text;
        if (underlying == typeof(float))
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : text;
        if (underlying == typeof(Half))
            return Half.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out Half h) ? h : text;
        if (underlying == typeof(int))
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i32) ? i32 : text;
        if (underlying == typeof(long))
            return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long i64) ? i64 : text;
        if (underlying == typeof(short))
            return short.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out short i16) ? i16 : text;
        if (underlying == typeof(sbyte))
            return sbyte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out sbyte i8) ? i8 : text;
        if (underlying == typeof(byte))
            return byte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte u8) ? u8 : text;
        if (underlying == typeof(ushort))
            return ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort u16) ? u16 : text;
        if (underlying == typeof(uint))
            return uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint u32) ? u32 : text;
        if (underlying == typeof(ulong))
            return ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong u64) ? u64 : text;
        if (underlying == typeof(Int128))
            return Int128.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out Int128 i128) ? i128 : text;
        if (underlying == typeof(UInt128))
            return UInt128.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out UInt128 u128) ? u128 : text;
        if (underlying == typeof(System.Numerics.BigInteger))
            return System.Numerics.BigInteger.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bi) ? bi : text;

        return text;
    }

    internal static bool IsEmptyJsonObject(ReadOnlySpan<byte> utf8)
    {
        ReadOnlySpan<byte> trimmed = utf8.Trim(" \t\r\n"u8);
        return trimmed.Length >= 2
            && trimmed[0] == (byte)'{'
            && trimmed[^1] == (byte)'}'
            && trimmed[1..^1].Trim(" \t\r\n"u8).IsEmpty;
    }

    internal static JsonTypeInfo ResolveEffectiveWriteTypeInfo(object value, JsonTypeInfo declaredTypeInfo)
    {
        if (IsCustomConverter(declaredTypeInfo))
            return declaredTypeInfo;

        if (Nullable.GetUnderlyingType(declaredTypeInfo.Type) is { } underlyingType)
        {
            if (declaredTypeInfo.Options.TryGetTypeInfo(underlyingType, out JsonTypeInfo? underlyingTypeInfo)
                && underlyingTypeInfo is not null)
            {
                return underlyingTypeInfo;
            }

            if (IsBuiltInScalarOrJsonType(underlyingType))
                return declaredTypeInfo;

            throw CreateUnsupportedStrictContractException(underlyingType);
        }

        Type runtimeType = value.GetType();
        if (runtimeType == declaredTypeInfo.Type)
            return declaredTypeInfo;

        Type? targetType = declaredTypeInfo.Type == typeof(object)
            ? runtimeType
            : ResolvePolymorphicDerivedType(declaredTypeInfo, runtimeType);

        if (targetType is not null)
        {
            if (declaredTypeInfo.Options.TryGetTypeInfo(targetType, out JsonTypeInfo? derivedTypeInfo)
                && derivedTypeInfo is not null)
            {
                return derivedTypeInfo;
            }

            throw CreateUnsupportedStrictContractException(targetType);
        }

        return declaredTypeInfo;
    }

    private static Type? ResolvePolymorphicDerivedType(JsonTypeInfo typeInfo, Type runtimeType)
    {
        if (typeInfo.PolymorphismOptions is not { DerivedTypes: { Count: > 0 } derivedTypes } polymorphismOptions)
            return null;

        if (ContainsDerivedType(derivedTypes, runtimeType))
            return runtimeType;

        if (polymorphismOptions.UnknownDerivedTypeHandling != JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor)
            return null;

        for (Type? current = runtimeType.BaseType;
             current is not null && current != typeInfo.Type;
             current = current.BaseType)
        {
            if (ContainsDerivedType(derivedTypes, current))
                return current;
        }

        if (typeInfo.Type.IsInterface)
        {
            for (int i = 0; i < derivedTypes.Count; i++)
            {
                Type candidate = derivedTypes[i].DerivedType;
                if (!candidate.IsInterface || !candidate.IsAssignableFrom(runtimeType))
                    continue;

                bool isMostDerived = true;
                for (int j = 0; j < derivedTypes.Count; j++)
                {
                    Type other = derivedTypes[j].DerivedType;
                    if (other.IsInterface && other.IsAssignableFrom(runtimeType) && !other.IsAssignableFrom(candidate))
                    {
                        isMostDerived = false;
                        break;
                    }
                }

                if (isMostDerived)
                    return candidate;
            }
        }

        return null;
    }

    private static bool ContainsDerivedType(IList<JsonDerivedType> derivedTypes, Type candidate)
    {
        for (int i = 0; i < derivedTypes.Count; i++)
        {
            if (derivedTypes[i].DerivedType == candidate)
                return true;
        }

        return false;
    }

    internal static JsonlException CreateUnsupportedStrictContractException(Type type)
        => new(
            JsonlErrorCode.InvalidOptions,
            $"Strict JSONL write validation requires [GenerateBinder] on '{type.Name}' or a metadata-enabled JsonTypeInfo<T> (not JsonSourceGenerationMode.Serialization). Add [GenerateBinder], enable metadata generation, or set ValidationMode.Lenient.");

    private static ContractResolution ResolveContract(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind is JsonTypeInfoKind.Enumerable or JsonTypeInfoKind.Dictionary)
            return new([], false);

        if (typeInfo.Kind == JsonTypeInfoKind.None)
        {
            if (IsBuiltInScalarOrJsonType(typeInfo.Type) || IsCustomConverter(typeInfo))
                return new([], false);

            throw CreateUnsupportedStrictContractException(typeInfo.Type);
        }

        if (typeInfo.Properties.Count == 0)
        {
            if (IsSerializationOnlyContext(typeInfo))
                throw CreateUnsupportedStrictContractException(typeInfo.Type);

#if NET9_0_OR_GREATER
            if (typeInfo.ConstructorAttributeProvider is not null)
                return new([], false);
#endif
            return new([], true);
        }

        List<ValidatedProperty>? list = null;
        foreach (JsonPropertyInfo prop in typeInfo.Properties)
        {
            if (prop.IsExtensionData || prop.Get is null)
                continue;

            if (prop.AttributeProvider is null)
            {
#if NET9_0_OR_GREATER
                if (IsSerializationOnlyContext(typeInfo))
                    throw CreateUnsupportedStrictContractException(typeInfo.Type);
                continue;
#else
                if (typeInfo.OriginatingResolver is JsonSerializerContext)
                    return ResolveViaReflectionNet8(typeInfo);
                continue;
#endif
            }

            ValidateAttribute? validateAttr = GetValidateAttribute(prop.AttributeProvider);

            WritePropertyValidation? rules = WritePropertyValidation.FromAttribute(validateAttr, requireAnyRule: true);
            if (rules is null)
                continue;

            string propertyName = prop.AttributeProvider is MemberInfo member ? member.Name : prop.Name;
            list ??= [];
            list.Add(new ValidatedProperty(propertyName, Encoding.UTF8.GetBytes(prop.Name), prop.PropertyType, prop.CustomConverter is not null, prop.Get, rules));
        }

        return new(list is null ? [] : [.. list], false);
    }

    private static ValidateAttribute? GetValidateAttribute(ICustomAttributeProvider provider)
    {
        object[] attrs = provider is MemberInfo member
            ? Attribute.GetCustomAttributes(member, typeof(ValidateAttribute), inherit: true)
            : provider.GetCustomAttributes(typeof(ValidateAttribute), inherit: true);
        for (int i = 0; i < attrs.Length; i++)
        {
            if (attrs[i] is ValidateAttribute found)
                return found;
        }

        return null;
    }

    private static bool IsCustomConverter(JsonTypeInfo typeInfo)
        => typeInfo.Converter is not null && typeInfo.Converter.GetType().Assembly != typeof(JsonSerializer).Assembly;

    private static bool IsSerializationOnlyContext(JsonTypeInfo typeInfo)
    {
        if (typeInfo.OriginatingResolver is not JsonSerializerContext context)
            return false;

        object[] attrs = context.GetType().GetCustomAttributes(inherit: false);
        bool contextSerializationOnly = false;
        bool hasMetadataOverride = false;
        for (int i = 0; i < attrs.Length; i++)
        {
            if (attrs[i] is JsonSourceGenerationOptionsAttribute { GenerationMode: JsonSourceGenerationMode.Serialization })
                contextSerializationOnly = true;
            else if (attrs[i] is JsonSerializableAttribute { GenerationMode: JsonSourceGenerationMode.Metadata })
                hasMetadataOverride = true;
        }

        return contextSerializationOnly && !hasMetadataOverride;
    }

#if !NET9_0_OR_GREATER
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "Only executed on .NET 8 when System.Text.Json source generation omits JsonPropertyInfo.AttributeProvider (added in .NET 9); trimmed metadata is detected when a serialized property cannot be mapped back to CLR metadata and rejected with JsonlException.")]
    private static ContractResolution ResolveViaReflectionNet8(JsonTypeInfo typeInfo)
    {
        JsonlContractMemberMap memberMap = JsonlContractMemberMap.Create(typeInfo);
        List<ValidatedProperty>? list = null;

        foreach (JsonPropertyInfo jsonProp in typeInfo.Properties)
        {
            if (jsonProp.IsExtensionData || jsonProp.Get is null)
                continue;

            if (!memberMap.TryGetMember(jsonProp.Name, jsonProp.PropertyType, out MemberInfo? member))
                return new ContractResolution([], false, HasTrimmedMetadata: true);

            var validateAttr = Attribute.GetCustomAttribute(member, typeof(ValidateAttribute), inherit: true) as ValidateAttribute;
            WritePropertyValidation? rules = WritePropertyValidation.FromAttribute(validateAttr, requireAnyRule: true);
            if (rules is null)
                continue;

            list ??= [];
            list.Add(new ValidatedProperty(member.Name, Encoding.UTF8.GetBytes(jsonProp.Name), jsonProp.PropertyType, jsonProp.CustomConverter is not null, jsonProp.Get, rules));
        }

        return new ContractResolution(list is null ? [] : [.. list], false);
    }
#endif

    private static bool IsBuiltInScalarOrJsonType(Type type)
    {
        Type t = Nullable.GetUnderlyingType(type) ?? type;
        return t.IsPrimitive
            || t.IsEnum
            || t == typeof(string)
            || t == typeof(object)
            || t == typeof(decimal)
            || t == typeof(DateTime)
            || t == typeof(DateTimeOffset)
            || t == typeof(DateOnly)
            || t == typeof(TimeOnly)
            || t == typeof(TimeSpan)
            || t == typeof(Guid)
            || t == typeof(Uri)
            || t == typeof(Version)
            || t == typeof(Half)
            || t == typeof(Int128)
            || t == typeof(UInt128)
            || t == typeof(System.Numerics.BigInteger)
            || t == typeof(byte[])
            || t == typeof(ReadOnlyMemory<byte>)
            || t == typeof(Memory<byte>)
            || t == typeof(JsonElement)
            || t == typeof(JsonDocument)
            || typeof(JsonNode).IsAssignableFrom(t);
    }

    private readonly record struct ValidatedProperty(
        string PropertyName,
        byte[] JsonNameUtf8,
        Type PropertyType,
        bool HasCustomConverter,
        Func<object, object?> Getter,
        WritePropertyValidation Validation);

    private sealed record ContractResolution(ValidatedProperty[] Properties, bool RequiresEmptyObjectCheck, bool HasTrimmedMetadata = false);
}
