#if !NET9_0_OR_GREATER
using System.Diagnostics.CodeAnalysis;
#endif
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using HeroParser.JsonLines.Reading.Binders;
using HeroParser.Validation;

namespace HeroParser.JsonLines.Reading;

/// <summary>
/// Validates deserialized JSONL records against both post-deserialization <see cref="IJsonlRecordValidator{T}"/> rules
/// and <see cref="JsonTypeInfo"/> contract rules (required value-type key presence and fallback property validation).
/// </summary>
internal sealed class JsonlReadContractValidator
{
    private static readonly ConditionalWeakTable<JsonTypeInfo, ReadValidationContract> contractCache = [];

    public static JsonlReadContractValidator Instance { get; } = new();

    public static JsonTypeInfo? TryGetTypeInfo<T>(JsonSerializerOptions options)
        => options.TryGetTypeInfo(typeof(T), out JsonTypeInfo? typeInfo) ? typeInfo : null;

    public void ValidateDeserialized<T>(
        T value,
        ReadOnlySpan<byte> utf8,
        JsonTypeInfo? typeInfo,
        IJsonlRecordValidator<T>? validator,
        int rowNumber,
        ValidationMode validationMode = ValidationMode.Strict)
    {
        JsonTypeInfo? effectiveTypeInfo = typeInfo;
        if (effectiveTypeInfo is not null && value is not null)
        {
            Type runtimeType = value.GetType();
            if (runtimeType != effectiveTypeInfo.Type
                && effectiveTypeInfo.Options.TryGetTypeInfo(runtimeType, out JsonTypeInfo? derivedTypeInfo)
                && derivedTypeInfo is not null)
            {
                effectiveTypeInfo = derivedTypeInfo;
            }
        }

        ReadValidationContract contract = effectiveTypeInfo is not null
            ? contractCache.GetValue(effectiveTypeInfo, ResolveContract)
            : ReadValidationContract.Empty;

        if (validationMode == ValidationMode.Strict && contract.UnenforceableRuleError is not null)
            throw new JsonlException(JsonlErrorCode.InvalidOptions, contract.UnenforceableRuleError);

        List<ValidationError>? errors = null;
        object?[]? payloadValues = null;
        bool[]? hasPayloadValues = null;
        bool validatePayloadValues = value is not null && contract.PayloadValueRules.Length > 0;
        if (contract.RequiredFields.Length > 0 || validatePayloadValues)
        {
            errors = ValidateUtf8Payload(
                utf8,
                contract,
                validatePayloadValues,
                rowNumber,
                out payloadValues,
                out hasPayloadValues);
        }

        if (effectiveTypeInfo is not null && effectiveTypeInfo.Properties.Count > 0)
        {
            if (contract.ValueRules.Length > 0 && value is not null)
            {
                object record = value;
                for (int i = 0; i < contract.ValueRules.Length; i++)
                {
                    ValidatedProperty entry = contract.ValueRules[i];
                    if (HasPresenceError(errors, entry.PropertyName))
                        continue;

                    object? propValue = entry.Getter(record);
                    errors ??= [];
                    entry.Validation.Validate(propValue, entry.PropertyName, rowNumber, i, errors, CultureInfo.InvariantCulture);
                }
            }

            if (validatePayloadValues)
            {
                int columnOffset = contract.ValueRules.Length;
                for (int i = 0; i < contract.PayloadValueRules.Length; i++)
                {
                    PayloadValidatedProperty entry = contract.PayloadValueRules[i];
                    if (HasPresenceError(errors, entry.PropertyName))
                        continue;

                    object? propValue = hasPayloadValues is not null && hasPayloadValues[i]
                        ? payloadValues![i]
                        : entry.DefaultValue;
                    errors ??= [];
                    entry.Validation.Validate(propValue, entry.PropertyName, rowNumber, columnOffset + i, errors, CultureInfo.InvariantCulture);
                }
            }
        }

        if (validator is not null
            && (effectiveTypeInfo is null || effectiveTypeInfo.Properties.Count == 0 || contract.HasTrimmedMetadata))
        {
            try
            {
                validator.Validate(value, rowNumber);
            }
            catch (ValidationException vex) when (errors is { Count: > 0 })
            {
                errors.AddRange(vex.Errors);
                throw new ValidationException(errors);
            }
        }

        if (errors is { Count: > 0 })
            throw new ValidationException(errors);
    }

    private static bool HasPresenceError(List<ValidationError>? errors, string propertyName)
    {
        if (errors is null)
            return false;

        for (int i = 0; i < errors.Count; i++)
        {
            if (string.Equals(errors[i].PropertyName, propertyName, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static List<ValidationError>? ValidateUtf8Payload(
        ReadOnlySpan<byte> utf8,
        ReadValidationContract contract,
        bool validatePayloadValues,
        int rowNumber,
        out object?[]? payloadValues,
        out bool[]? hasPayloadValues)
    {
        payloadValues = null;
        hasPayloadValues = null;

        var reader = new Utf8JsonReader(utf8, contract.ReaderOptions);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            return null;

        int requiredCount = contract.RequiredFields.Length;
        bool[]? rentedSeen = requiredCount <= 64 ? null : new bool[requiredCount];
        Span<bool> seen = requiredCount <= 64 ? stackalloc bool[requiredCount] : rentedSeen;

        int payloadRuleCount = validatePayloadValues ? contract.PayloadValueRules.Length : 0;
        if (payloadRuleCount > 0)
        {
            payloadValues = new object?[payloadRuleCount];
            hasPayloadValues = new bool[payloadRuleCount];
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            int matchedPayloadIndex = -1;
            if (contract.CaseInsensitive)
            {
                string? propName = reader.GetString();
                if (propName is not null)
                {
                    for (int i = 0; i < requiredCount; i++)
                    {
                        if (!seen[i] && string.Equals(propName, contract.RequiredFields[i].JsonName, StringComparison.OrdinalIgnoreCase))
                            seen[i] = true;
                    }

                    for (int i = 0; i < payloadRuleCount; i++)
                    {
                        if (string.Equals(propName, contract.PayloadValueRules[i].JsonName, StringComparison.OrdinalIgnoreCase))
                        {
                            matchedPayloadIndex = i;
                            break;
                        }
                    }
                }
            }
            else
            {
                for (int i = 0; i < requiredCount; i++)
                {
                    if (!seen[i] && reader.ValueTextEquals(contract.RequiredFields[i].JsonNameUtf8))
                        seen[i] = true;
                }

                for (int i = 0; i < payloadRuleCount; i++)
                {
                    if (reader.ValueTextEquals(contract.PayloadValueRules[i].JsonNameUtf8))
                    {
                        matchedPayloadIndex = i;
                        break;
                    }
                }
            }

            if (!reader.Read())
                break;

            if (matchedPayloadIndex >= 0)
            {
                PayloadValidatedProperty payloadRule = contract.PayloadValueRules[matchedPayloadIndex];
                Utf8JsonReader valueReader = reader;
                payloadValues![matchedPayloadIndex] = payloadRule.PropertyTypeInfo is not null
                    ? JsonSerializer.Deserialize(ref valueReader, payloadRule.PropertyTypeInfo)
                    : ReadNumericWithHandling(ref valueReader, payloadRule.PropertyType, payloadRule.EffectiveNumberHandling);
                hasPayloadValues![matchedPayloadIndex] = true;
            }

            reader.Skip();
        }

        List<ValidationError>? errors = null;
        for (int i = 0; i < requiredCount; i++)
        {
            if (seen[i])
                continue;

            RequiredValueField req = contract.RequiredFields[i];
            errors ??= [];
            errors.Add(new ValidationError
            {
                RowNumber = rowNumber,
                ColumnIndex = 0,
                ColumnName = req.JsonName,
                PropertyName = req.ClrName,
                Rule = "NotNull",
                Message = "Value is required",
                RawValue = "null"
            });
        }

        return errors;
    }

    private static ReadValidationContract ResolveContract(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
            return ReadValidationContract.Empty;

        List<RequiredValueField>? fields = null;
        List<ValidatedProperty>? valueRules = null;
        List<PayloadValidatedProperty>? payloadValueRules = null;
        Dictionary<JsonNumberHandling, JsonSerializerOptions>? numberHandlingOptionsCache = null;
        string? unenforceableRuleError = null;
        bool hasTrimmedMetadata = false;
        if (typeInfo.Properties.Count > 0)
        {
#if !NET9_0_OR_GREATER
            JsonlContractMemberMap? net8MemberMap = null;
#endif
            foreach (JsonPropertyInfo prop in typeInfo.Properties)
            {
                if (prop.IsExtensionData)
                    continue;

                ICustomAttributeProvider? attrProvider = prop.AttributeProvider;
#if !NET9_0_OR_GREATER
                if (attrProvider is null)
                {
                    net8MemberMap ??= JsonlContractMemberMap.Create(typeInfo);
                    if (net8MemberMap.TryGetMember(prop.Name, prop.PropertyType, out MemberInfo? resolved))
                        attrProvider = resolved;
                    else
                        hasTrimmedMetadata = true;
                }
#endif
                if (attrProvider is null || !CanPopulateOnRead(typeInfo, prop, attrProvider))
                    continue;

                ValidateAttribute? validateAttr = GetValidateAttribute(attrProvider);
                if (validateAttr is null)
                    continue;

                string clrName = attrProvider is MemberInfo member ? member.Name : prop.Name;
                bool isNonNullableValueType = prop.PropertyType.IsValueType
                    && Nullable.GetUnderlyingType(prop.PropertyType) is null;

                if (validateAttr.NotNull && isNonNullableValueType)
                {
                    fields ??= [];
                    fields.Add(new RequiredValueField(clrName, prop.Name, Encoding.UTF8.GetBytes(prop.Name)));
                }

                WritePropertyValidation? rules = WritePropertyValidation.FromAttribute(validateAttr, requireAnyRule: true);
                if (rules is null)
                    continue;

                if (prop.Get is not null)
                {
                    valueRules ??= [];
                    valueRules.Add(new ValidatedProperty(clrName, prop.Get, rules));
                }
                else if (attrProvider is PropertyInfo propInfo && propInfo.GetGetMethod(nonPublic: true) is { } nonPublicGetter)
                {
                    valueRules ??= [];
                    valueRules.Add(new ValidatedProperty(clrName, record => nonPublicGetter.Invoke(record, null), rules));
                }
                else if (prop.CustomConverter is null
                    && TryGetDefaultValue(prop.PropertyType, out object? defaultValue))
                {
                    JsonNumberHandling effectiveNumberHandling = prop.NumberHandling
                        ?? typeInfo.NumberHandling
                        ?? typeInfo.Options.NumberHandling;

                    JsonTypeInfo? propTypeInfo = null;
                    bool resolved = false;
                    if (effectiveNumberHandling == typeInfo.Options.NumberHandling)
                    {
                        resolved = typeInfo.Options.TryGetTypeInfo(prop.PropertyType, out propTypeInfo)
                            && propTypeInfo is not null;
                    }
                    else
                    {
                        numberHandlingOptionsCache ??= [];
                        if (!numberHandlingOptionsCache.TryGetValue(effectiveNumberHandling, out JsonSerializerOptions? effectiveOptions))
                        {
                            effectiveOptions = new JsonSerializerOptions(typeInfo.Options)
                            {
                                NumberHandling = effectiveNumberHandling
                            };
                            effectiveOptions.MakeReadOnly();
                            numberHandlingOptionsCache[effectiveNumberHandling] = effectiveOptions;
                        }

                        if (effectiveOptions.TryGetTypeInfo(prop.PropertyType, out JsonTypeInfo? candidateTypeInfo)
                            && candidateTypeInfo is not null
                            && candidateTypeInfo.Options.NumberHandling == effectiveNumberHandling)
                        {
                            propTypeInfo = candidateTypeInfo;
                            resolved = true;
                        }
                        else if (IsSupportedNumericWithHandling(prop.PropertyType, effectiveNumberHandling))
                        {
                            propTypeInfo = null;
                            resolved = true;
                        }
                    }

                    if (!resolved)
                    {
                        unenforceableRuleError ??=
                            $"Strict JSONL read validation cannot enforce [Validate] rules on setter-only property '{clrName}' of '{typeInfo.Type.Name}'. Add a getter (including private get) or set ValidationMode.Lenient.";
                        continue;
                    }

                    payloadValueRules ??= [];
                    payloadValueRules.Add(new PayloadValidatedProperty(
                        clrName,
                        prop.Name,
                        Encoding.UTF8.GetBytes(prop.Name),
                        prop.PropertyType,
                        propTypeInfo,
                        effectiveNumberHandling,
                        defaultValue,
                        rules));
                }
                else
                {
                    unenforceableRuleError ??=
                        $"Strict JSONL read validation cannot enforce [Validate] rules on setter-only property '{clrName}' of '{typeInfo.Type.Name}'. Add a getter (including private get) or set ValidationMode.Lenient.";
                }
            }

            if (hasTrimmedMetadata && JsonlRecordBinderFactory.TryGetRequiredValueFields(typeInfo.Type, out var registeredRequiredFields))
            {
                JsonNamingPolicy? namingPolicy = typeInfo.Options.PropertyNamingPolicy;
                fields ??= new List<RequiredValueField>(registeredRequiredFields.Length);
                for (int i = 0; i < registeredRequiredFields.Length; i++)
                {
                    var (clrName, customJsonName) = registeredRequiredFields[i];
                    if (fields.Exists(f => string.Equals(f.ClrName, clrName, StringComparison.Ordinal)))
                        continue;

                    string jsonName = customJsonName ?? namingPolicy?.ConvertName(clrName) ?? clrName;
                    fields.Add(new RequiredValueField(clrName, jsonName, Encoding.UTF8.GetBytes(jsonName)));
                }
            }
        }
        else if (JsonlRecordBinderFactory.TryGetRequiredValueFields(typeInfo.Type, out var registered))
        {
            JsonNamingPolicy? namingPolicy = typeInfo.Options.PropertyNamingPolicy;
            fields = new List<RequiredValueField>(registered.Length);
            for (int i = 0; i < registered.Length; i++)
            {
                var (clrName, customJsonName) = registered[i];
                string jsonName = customJsonName ?? namingPolicy?.ConvertName(clrName) ?? clrName;
                fields.Add(new RequiredValueField(clrName, jsonName, Encoding.UTF8.GetBytes(jsonName)));
            }
        }

        if (fields is null && valueRules is null && payloadValueRules is null && unenforceableRuleError is null && !hasTrimmedMetadata)
            return ReadValidationContract.Empty;

        var readerOptions = new JsonReaderOptions
        {
            CommentHandling = typeInfo.Options.ReadCommentHandling,
            AllowTrailingCommas = typeInfo.Options.AllowTrailingCommas,
            MaxDepth = typeInfo.Options.MaxDepth
        };

        return new ReadValidationContract(
            fields is null ? [] : [.. fields],
            valueRules is null ? [] : [.. valueRules],
            payloadValueRules is null ? [] : [.. payloadValueRules],
            typeInfo.Options.PropertyNameCaseInsensitive,
            readerOptions,
            unenforceableRuleError,
            hasTrimmedMetadata);
    }

    private static bool CanPopulateOnRead(JsonTypeInfo typeInfo, JsonPropertyInfo prop, ICustomAttributeProvider attrProvider)
    {
        if (IsIgnoredOnRead(attrProvider))
            return false;

        if (prop.Set is not null)
            return true;

#if NET9_0_OR_GREATER
        _ = typeInfo;
        return prop.AssociatedParameter is not null;
#else
        return HasMatchingDeserializationConstructorParameter(typeInfo.Type, prop, attrProvider);
#endif
    }

#if !NET9_0_OR_GREATER
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2070",
        Justification = "Only executed on .NET 8 when JsonPropertyInfo.AssociatedParameter is unavailable (added in .NET 9); public constructors are inspected when resolving constructor-bound required members.")]
    private static bool HasMatchingDeserializationConstructorParameter(
        Type type,
        JsonPropertyInfo prop,
        ICustomAttributeProvider attrProvider)
    {
        ConstructorInfo? selectedCtor = null;
        foreach (ConstructorInfo ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (Attribute.IsDefined(ctor, typeof(JsonConstructorAttribute), inherit: false))
            {
                selectedCtor = ctor;
                break;
            }
        }

        if (selectedCtor is null && !type.IsValueType)
        {
            ConstructorInfo[] publicCtors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            bool hasParameterless = false;
            for (int i = 0; i < publicCtors.Length; i++)
            {
                if (publicCtors[i].GetParameters().Length == 0)
                {
                    hasParameterless = true;
                    break;
                }
            }

            if (!hasParameterless && publicCtors.Length == 1)
                selectedCtor = publicCtors[0];
        }

        if (selectedCtor is null)
            return false;

        string clrName = attrProvider is MemberInfo member ? member.Name : prop.Name;
        foreach (ParameterInfo param in selectedCtor.GetParameters())
        {
            if (param.ParameterType == prop.PropertyType &&
                (string.Equals(param.Name, clrName, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(param.Name, prop.Name, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }
#endif

    private static bool IsIgnoredOnRead(ICustomAttributeProvider attrProvider)
    {
        object[] attrs = attrProvider is MemberInfo member
            ? Attribute.GetCustomAttributes(member, typeof(JsonIgnoreAttribute), inherit: true)
            : attrProvider.GetCustomAttributes(typeof(JsonIgnoreAttribute), inherit: true);

        for (int i = 0; i < attrs.Length; i++)
        {
#if NET10_0_OR_GREATER
            if (attrs[i] is JsonIgnoreAttribute { Condition: JsonIgnoreCondition.Always or JsonIgnoreCondition.WhenReading })
                return true;
#else
            if (attrs[i] is JsonIgnoreAttribute { Condition: JsonIgnoreCondition.Always })
                return true;
#endif
        }
        return false;
    }

    private static ValidateAttribute? GetValidateAttribute(ICustomAttributeProvider attrProvider)
    {
        object[] attrs = attrProvider is MemberInfo member
            ? Attribute.GetCustomAttributes(member, typeof(ValidateAttribute), inherit: true)
            : attrProvider.GetCustomAttributes(typeof(ValidateAttribute), inherit: true);

        for (int i = 0; i < attrs.Length; i++)
        {
            if (attrs[i] is ValidateAttribute found)
                return found;
        }
        return null;
    }

    private static bool TryGetDefaultValue(Type propertyType, out object? defaultValue)
    {
        if (!propertyType.IsValueType || Nullable.GetUnderlyingType(propertyType) is not null)
        {
            defaultValue = null;
            return true;
        }

        if (propertyType.IsEnum)
        {
            defaultValue = Enum.ToObject(propertyType, 0);
            return true;
        }

        if (propertyType == typeof(bool)) { defaultValue = false; return true; }
        if (propertyType == typeof(byte)) { defaultValue = (byte)0; return true; }
        if (propertyType == typeof(sbyte)) { defaultValue = (sbyte)0; return true; }
        if (propertyType == typeof(short)) { defaultValue = (short)0; return true; }
        if (propertyType == typeof(ushort)) { defaultValue = (ushort)0; return true; }
        if (propertyType == typeof(int)) { defaultValue = 0; return true; }
        if (propertyType == typeof(uint)) { defaultValue = 0u; return true; }
        if (propertyType == typeof(long)) { defaultValue = 0L; return true; }
        if (propertyType == typeof(ulong)) { defaultValue = 0UL; return true; }
        if (propertyType == typeof(float)) { defaultValue = 0f; return true; }
        if (propertyType == typeof(double)) { defaultValue = 0d; return true; }
        if (propertyType == typeof(decimal)) { defaultValue = 0m; return true; }
        if (propertyType == typeof(char)) { defaultValue = '\0'; return true; }
        if (propertyType == typeof(DateTime)) { defaultValue = DateTime.MinValue; return true; }
        if (propertyType == typeof(DateTimeOffset)) { defaultValue = DateTimeOffset.MinValue; return true; }
        if (propertyType == typeof(DateOnly)) { defaultValue = DateOnly.MinValue; return true; }
        if (propertyType == typeof(TimeOnly)) { defaultValue = TimeOnly.MinValue; return true; }
        if (propertyType == typeof(TimeSpan)) { defaultValue = TimeSpan.Zero; return true; }
        if (propertyType == typeof(Guid)) { defaultValue = Guid.Empty; return true; }
        if (propertyType == typeof(Half)) { defaultValue = default(Half); return true; }
        if (propertyType == typeof(Int128)) { defaultValue = Int128.Zero; return true; }
        if (propertyType == typeof(UInt128)) { defaultValue = UInt128.Zero; return true; }
        if (propertyType == typeof(BigInteger)) { defaultValue = BigInteger.Zero; return true; }

        defaultValue = null;
        return false;
    }

    private static bool IsSupportedNumericWithHandling(Type propertyType, JsonNumberHandling handling)
    {
        if ((handling & ~(JsonNumberHandling.Strict | JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowNamedFloatingPointLiterals)) != 0)
            return false;

        Type underlying = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        return underlying == typeof(sbyte)
            || underlying == typeof(byte)
            || underlying == typeof(short)
            || underlying == typeof(ushort)
            || underlying == typeof(int)
            || underlying == typeof(uint)
            || underlying == typeof(long)
            || underlying == typeof(ulong)
            || underlying == typeof(float)
            || underlying == typeof(double)
            || underlying == typeof(decimal)
            || underlying == typeof(Half)
            || underlying == typeof(Int128)
            || underlying == typeof(UInt128);
    }

    private static object? ReadNumericWithHandling(
        ref Utf8JsonReader reader,
        Type propertyType,
        JsonNumberHandling handling)
    {
        Type? nullableUnderlying = Nullable.GetUnderlyingType(propertyType);
        if (reader.TokenType == JsonTokenType.Null)
        {
            if (nullableUnderlying is not null)
                return null;
            throw new JsonException($"Cannot convert null to non-nullable '{propertyType.Name}'.");
        }

        Type targetType = nullableUnderlying ?? propertyType;
        bool allowString = (handling & JsonNumberHandling.AllowReadingFromString) != 0;
        bool allowNamed = (handling & JsonNumberHandling.AllowNamedFloatingPointLiterals) != 0;

        if (reader.TokenType == JsonTokenType.String)
        {
            string text = reader.GetString()
                ?? throw new JsonException($"Invalid numeric string token for '{targetType.Name}'.");

            if (allowNamed)
            {
                if (targetType == typeof(float) && TryParseNamedSingle(text, out float namedF))
                    return namedF;
                if (targetType == typeof(double) && TryParseNamedDouble(text, out double namedD))
                    return namedD;
                if (targetType == typeof(Half) && TryParseNamedDouble(text, out double namedH))
                    return (Half)namedH;
            }

            if (!allowString)
                throw new JsonException($"String token not permitted for '{targetType.Name}' without AllowReadingFromString.");

            const NumberStyles INT_STYLES = NumberStyles.AllowLeadingSign;
            const NumberStyles FLOAT_STYLES = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

            if (targetType == typeof(sbyte)) return sbyte.Parse(text, INT_STYLES, CultureInfo.InvariantCulture);
            if (targetType == typeof(byte)) return byte.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
            if (targetType == typeof(short)) return short.Parse(text, INT_STYLES, CultureInfo.InvariantCulture);
            if (targetType == typeof(ushort)) return ushort.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
            if (targetType == typeof(int)) return int.Parse(text, INT_STYLES, CultureInfo.InvariantCulture);
            if (targetType == typeof(uint)) return uint.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
            if (targetType == typeof(long)) return long.Parse(text, INT_STYLES, CultureInfo.InvariantCulture);
            if (targetType == typeof(ulong)) return ulong.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
            if (targetType == typeof(Int128)) return Int128.Parse(text, INT_STYLES, CultureInfo.InvariantCulture);
            if (targetType == typeof(UInt128)) return UInt128.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
            if (targetType == typeof(decimal)) return decimal.Parse(text, FLOAT_STYLES, CultureInfo.InvariantCulture);
            if (targetType == typeof(float)) return float.Parse(text, FLOAT_STYLES, CultureInfo.InvariantCulture);
            if (targetType == typeof(double)) return double.Parse(text, FLOAT_STYLES, CultureInfo.InvariantCulture);
            if (targetType == typeof(Half)) return Half.Parse(text, FLOAT_STYLES, CultureInfo.InvariantCulture);
        }

        if (reader.TokenType == JsonTokenType.Number)
        {
            if (targetType == typeof(sbyte)) return reader.GetSByte();
            if (targetType == typeof(byte)) return reader.GetByte();
            if (targetType == typeof(short)) return reader.GetInt16();
            if (targetType == typeof(ushort)) return reader.GetUInt16();
            if (targetType == typeof(int)) return reader.GetInt32();
            if (targetType == typeof(uint)) return reader.GetUInt32();
            if (targetType == typeof(long)) return reader.GetInt64();
            if (targetType == typeof(ulong)) return reader.GetUInt64();
            if (targetType == typeof(decimal)) return reader.GetDecimal();
            if (targetType == typeof(float)) return reader.GetSingle();
            if (targetType == typeof(double)) return reader.GetDouble();
            if (targetType == typeof(Half)) return (Half)reader.GetDouble();
            if (targetType == typeof(Int128)) return Int128.Parse(Encoding.UTF8.GetString(reader.ValueSpan), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            if (targetType == typeof(UInt128)) return UInt128.Parse(Encoding.UTF8.GetString(reader.ValueSpan), NumberStyles.None, CultureInfo.InvariantCulture);
        }

        throw new JsonException($"Unexpected token '{reader.TokenType}' for '{targetType.Name}'.");
    }

    private static bool TryParseNamedSingle(string text, out float value)
    {
        if (string.Equals(text, "NaN", StringComparison.Ordinal)) { value = float.NaN; return true; }
        if (string.Equals(text, "Infinity", StringComparison.Ordinal)) { value = float.PositiveInfinity; return true; }
        if (string.Equals(text, "-Infinity", StringComparison.Ordinal)) { value = float.NegativeInfinity; return true; }
        value = 0f;
        return false;
    }

    private static bool TryParseNamedDouble(string text, out double value)
    {
        if (string.Equals(text, "NaN", StringComparison.Ordinal)) { value = double.NaN; return true; }
        if (string.Equals(text, "Infinity", StringComparison.Ordinal)) { value = double.PositiveInfinity; return true; }
        if (string.Equals(text, "-Infinity", StringComparison.Ordinal)) { value = double.NegativeInfinity; return true; }
        value = 0d;
        return false;
    }

    private readonly record struct RequiredValueField(string ClrName, string JsonName, byte[] JsonNameUtf8);

    private readonly record struct ValidatedProperty(string PropertyName, Func<object, object?> Getter, WritePropertyValidation Validation);

    private readonly record struct PayloadValidatedProperty(
        string PropertyName,
        string JsonName,
        byte[] JsonNameUtf8,
        Type PropertyType,
        JsonTypeInfo? PropertyTypeInfo,
        JsonNumberHandling EffectiveNumberHandling,
        object? DefaultValue,
        WritePropertyValidation Validation);

    private sealed class ReadValidationContract(
        RequiredValueField[] requiredFields,
        ValidatedProperty[] valueRules,
        PayloadValidatedProperty[] payloadValueRules,
        bool caseInsensitive,
        JsonReaderOptions readerOptions,
        string? unenforceableRuleError,
        bool hasTrimmedMetadata = false)
    {
        public static readonly ReadValidationContract Empty = new([], [], [], false, default, null);
        public RequiredValueField[] RequiredFields { get; } = requiredFields;
        public ValidatedProperty[] ValueRules { get; } = valueRules;
        public PayloadValidatedProperty[] PayloadValueRules { get; } = payloadValueRules;
        public bool CaseInsensitive { get; } = caseInsensitive;
        public JsonReaderOptions ReaderOptions { get; } = readerOptions;
        public string? UnenforceableRuleError { get; } = unenforceableRuleError;
        public bool HasTrimmedMetadata { get; } = hasTrimmedMetadata;
    }
}
