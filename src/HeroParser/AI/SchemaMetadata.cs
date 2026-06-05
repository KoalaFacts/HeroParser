using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;

namespace HeroParser.AI;

/// <summary>
/// Thrown when an agent tool call argument violates a schema validation constraint.
/// </summary>
public sealed class LlmToolCallValidationException : Exception
{
    /// <summary>
    /// Gets the name of the property that failed validation.
    /// </summary>
    public string PropertyName { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="LlmToolCallValidationException"/> class.
    /// </summary>
    public LlmToolCallValidationException(string propertyName, string message) : base(message)
    {
        PropertyName = propertyName;
    }
}

/// <summary>
/// Provides access to source-generated LLM schemas for record types and reflection-free agent argument mapping.
/// </summary>
public static class SchemaMetadata
{
    private static readonly ConcurrentDictionary<Type, string> schemas = new();
    private static readonly ConcurrentDictionary<(string Pattern, int TimeoutMs), Regex> regexCache = new();

    /// <summary>
    /// Registers a JSON Schema for a specific type.
    /// </summary>
    public static void RegisterSchema<T>(string schemaJson)
    {
        schemas[typeof(T)] = schemaJson;
    }

    /// <summary>
    /// Gets the pre-rendered LLM JSON Schema for the specified type.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <returns>The pre-rendered JSON Schema string.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the type does not have a source-generated schema.</exception>
    public static string ToLlmSchema<T>()
    {
        if (schemas.TryGetValue(typeof(T), out var schema))
        {
            return schema;
        }

        throw new InvalidOperationException(
            $"No source-generated LLM schema found for type '{typeof(T).FullName}'. " +
            "Ensure the type is decorated with the [GenerateBinder] attribute and the source generator is active.");
    }

    /// <summary>
    /// Maps a case-insensitive, flat dictionary of tool call arguments delivered by an LLM into a strongly-typed record model, executing all <see cref="ValidateAttribute"/> rules.
    /// </summary>
    /// <typeparam name="T">The record type to bind to.</typeparam>
    /// <param name="arguments">The flat dictionary of tool call parameters.</param>
    /// <returns>A strongly-typed record populated and validated from the arguments.</returns>
    /// <exception cref="ArgumentNullException">Thrown if arguments dictionary is null.</exception>
    /// <exception cref="LlmToolCallValidationException">Thrown if a property violates a validation rule.</exception>
    /// <exception cref="InvalidOperationException">Thrown if property type conversion fails.</exception>
    public static T MapFromToolCall<T>(IReadOnlyDictionary<string, object?> arguments) where T : new()
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var result = new T();
        var type = typeof(T);
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        var dict = arguments is Dictionary<string, object?> d && d.Comparer == StringComparer.OrdinalIgnoreCase
            ? arguments
            : new Dictionary<string, object?>(arguments, StringComparer.OrdinalIgnoreCase);

        foreach (var prop in properties)
        {
            if (dict.TryGetValue(prop.Name, out var val))
            {
                if (val != null)
                {
                    try
                    {
                        var propType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                        val = propType == typeof(Guid)
                            ? (val is string sGuid ? Guid.Parse(sGuid) : val)
                            : propType == typeof(DateTime)
                            ? (val is string sDt ? DateTime.Parse(sDt, System.Globalization.CultureInfo.InvariantCulture) : Convert.ToDateTime(val))
                            : propType == typeof(DateTimeOffset)
                            ? (val is string sDto ? DateTimeOffset.Parse(sDto, System.Globalization.CultureInfo.InvariantCulture) : val)
                            : propType == typeof(TimeSpan)
                            ? (val is string sTs ? TimeSpan.Parse(sTs, System.Globalization.CultureInfo.InvariantCulture) : val)
                            : propType.IsEnum
                            ? (val is string strVal
                                ? Enum.Parse(propType, strVal, ignoreCase: true)
                                : Enum.ToObject(propType, Convert.ChangeType(val, Enum.GetUnderlyingType(propType))))
                            : Convert.ChangeType(val, propType);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException($"Failed to convert value for property '{prop.Name}' to type '{prop.PropertyType.Name}': {ex.Message}", ex);
                    }
                }

                prop.SetValue(result, val);
            }

            // Run [Validate] validations
            if (prop.GetCustomAttribute<ValidateAttribute>() is { } validate)
            {
                var finalVal = prop.GetValue(result);

                // 1. NotNull validation
                if (validate.NotNull && finalVal == null)
                {
                    throw new LlmToolCallValidationException(prop.Name, $"Property '{prop.Name}' cannot be null.");
                }

                if (finalVal != null)
                {
                    // 2. NotEmpty validation
                    if (validate.NotEmpty && finalVal is string s && string.IsNullOrWhiteSpace(s))
                    {
                        throw new LlmToolCallValidationException(prop.Name, $"Property '{prop.Name}' cannot be empty.");
                    }

                    // 3. MinLength validation
                    if (validate.MinLength > 0 && finalVal is string sMin && sMin.Length < validate.MinLength)
                    {
                        throw new LlmToolCallValidationException(prop.Name, $"Property '{prop.Name}' length must be at least {validate.MinLength}.");
                    }

                    // 4. MaxLength validation
                    if (validate.MaxLength > 0 && finalVal is string sMax && sMax.Length > validate.MaxLength)
                    {
                        throw new LlmToolCallValidationException(prop.Name, $"Property '{prop.Name}' length cannot exceed {validate.MaxLength}.");
                    }

                    // 5. RangeMin validation
                    if (!double.IsNaN(validate.RangeMin))
                    {
                        double numericVal = Convert.ToDouble(finalVal);
                        if (numericVal < validate.RangeMin)
                        {
                            throw new LlmToolCallValidationException(prop.Name, $"Property '{prop.Name}' value {numericVal} must be at least {validate.RangeMin}.");
                        }
                    }

                    // 6. RangeMax validation
                    if (!double.IsNaN(validate.RangeMax))
                    {
                        double numericVal = Convert.ToDouble(finalVal);
                        if (numericVal > validate.RangeMax)
                        {
                            throw new LlmToolCallValidationException(prop.Name, $"Property '{prop.Name}' value {numericVal} cannot exceed {validate.RangeMax}.");
                        }
                    }

                    // 7. Pattern validation
                    if (!string.IsNullOrEmpty(validate.Pattern) && finalVal is string sPat)
                    {
                        var timeoutMs = validate.PatternTimeoutMs > 0 ? validate.PatternTimeoutMs : 1000;
                        var key = (validate.Pattern, timeoutMs);
                        var regex = regexCache.GetOrAdd(key, k => new Regex(k.Pattern, RegexOptions.None, TimeSpan.FromMilliseconds(k.TimeoutMs)));

                        if (!regex.IsMatch(sPat))
                        {
                            throw new LlmToolCallValidationException(prop.Name, $"Property '{prop.Name}' value must match pattern '{validate.Pattern}'.");
                        }
                    }
                }
            }
        }

        return result;
    }
}
