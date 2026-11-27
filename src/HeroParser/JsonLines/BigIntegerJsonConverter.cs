using System.Buffers;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HeroParser.JsonLines;

/// <summary>
/// Serializes and deserializes <see cref="BigInteger"/> values as raw JSON integers (and reads JSON integer strings)
/// so reflection-based and source-generated JSONL writers match HeroParser's binder format.
/// </summary>
/// <remarks>
/// Include <see cref="BigIntegerJsonConverter"/> in a source-generated <see cref="JsonSerializerContext"/> via
/// <c>[JsonSourceGenerationOptions(Converters = [typeof(BigIntegerJsonConverter)])]</c> for AOT-safe
/// <see cref="BigInteger"/> JSONL serialization.
/// </remarks>
public sealed class BigIntegerJsonConverter : JsonConverter<BigInteger>
{
    /// <summary>
    /// Gets a shared singleton instance of <see cref="BigIntegerJsonConverter"/>.
    /// </summary>
    public static BigIntegerJsonConverter Instance { get; } = new();

    private static readonly JsonSerializerOptions defaultOptions = CreateDefaultOptions();

    private static JsonSerializerOptions CreateDefaultOptions()
    {
        var opts = new JsonSerializerOptions();
        opts.Converters.Add(Instance);
        return opts;
    }

    internal static JsonSerializerOptions ResolveOptions(JsonSerializerOptions? userOptions)
    {
        if (userOptions is null)
            return defaultOptions;

        foreach (JsonConverter converter in userOptions.Converters)
        {
            if (converter.CanConvert(typeof(BigInteger)))
                return userOptions;
        }

        var clone = new JsonSerializerOptions(userOptions);
        clone.Converters.Add(Instance);
        return clone;
    }

    /// <inheritdoc/>
    public override BigInteger Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            if (!reader.HasValueSequence)
            {
                ReadOnlySpan<byte> utf8 = reader.ValueSpan;
                if (utf8.Length <= 128)
                {
                    Span<char> chars = stackalloc char[128];
                    int written = Encoding.UTF8.GetChars(utf8, chars);
                    if (BigInteger.TryParse(chars[..written], NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger parsedSpan))
                        return parsedSpan;
                }
                else
                {
                    string text = Encoding.UTF8.GetString(utf8);
                    if (BigInteger.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger parsedHeap))
                        return parsedHeap;
                }
            }
            else
            {
                string text = Encoding.UTF8.GetString(reader.ValueSequence.ToArray());
                if (BigInteger.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger parsedSeq))
                    return parsedSeq;
            }

            throw new JsonException("Expected integer numeric value for BigInteger.");
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            string? text = reader.GetString();
            if (text is not null && BigInteger.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger parsedStr))
                return parsedStr;

            throw new JsonException("Expected valid integer string value for BigInteger.");
        }

        throw new JsonException($"Expected numeric or string token for BigInteger, got {reader.TokenType}.");
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, BigInteger value, JsonSerializerOptions options)
    {
        Span<char> buffer = stackalloc char[128];
        if (value.TryFormat(buffer, out int written, default, CultureInfo.InvariantCulture))
        {
            writer.WriteRawValue(buffer[..written], skipInputValidation: true);
            return;
        }

        writer.WriteRawValue(value.ToString(null, CultureInfo.InvariantCulture), skipInputValidation: true);
    }
}
