using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using HeroParser.JsonLines.Reading;

namespace HeroParser;

public static partial class Jsonl
{
    /// <summary>
    /// Creates a fluent reader builder for the specified record type.
    /// </summary>
    public static JsonLines.Reading.JsonlRecordReaderBuilder<T> Read<T>() => new();

    /// <summary>
    /// Deserializes records from a JSONL string using reflection-based <c>System.Text.Json</c>.
    /// </summary>
    [RequiresUnreferencedCode("JSONL deserialization without a JsonTypeInfo<T> uses reflection. Use the overload taking a JsonTypeInfo<T> for AOT/trimming.")]
    [RequiresDynamicCode("JSONL deserialization without a JsonTypeInfo<T> uses runtime code generation.")]
    public static IEnumerable<T> DeserializeRecords<T>(string jsonl, JsonlReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(jsonl);
        byte[] bytes = Encoding.UTF8.GetBytes(jsonl);
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new JsonlRecordReader<T>(stream, options);
        foreach (T record in reader)
            yield return record;
    }

    /// <summary>
    /// Deserializes records from a JSONL string using a <see cref="JsonTypeInfo{T}"/> for AOT support.
    /// </summary>
    public static IEnumerable<T> DeserializeRecords<T>(string jsonl, JsonTypeInfo<T> typeInfo, JsonlReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(jsonl);
        ArgumentNullException.ThrowIfNull(typeInfo);
        byte[] bytes = Encoding.UTF8.GetBytes(jsonl);
        return DeserializeRecordsFromBytes(bytes, typeInfo, options);
    }

    /// <summary>
    /// Deserializes records from UTF-8 JSONL bytes using reflection-based <c>System.Text.Json</c>.
    /// </summary>
    [RequiresUnreferencedCode("JSONL deserialization without a JsonTypeInfo<T> uses reflection. Use the overload taking a JsonTypeInfo<T> for AOT/trimming.")]
    [RequiresDynamicCode("JSONL deserialization without a JsonTypeInfo<T> uses runtime code generation.")]
    public static IEnumerable<T> DeserializeRecordsFromBytes<T>(ReadOnlyMemory<byte> utf8, JsonlReadOptions? options = null)
    {
        var stream = new MemoryStream(utf8.ToArray(), writable: false);
        return EnumerateAndDispose(new JsonlRecordReader<T>(stream, options));
    }

    /// <summary>
    /// Deserializes records from UTF-8 JSONL bytes using a <see cref="JsonTypeInfo{T}"/> for AOT support.
    /// </summary>
    public static IEnumerable<T> DeserializeRecordsFromBytes<T>(ReadOnlyMemory<byte> utf8, JsonTypeInfo<T> typeInfo, JsonlReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        var stream = new MemoryStream(utf8.ToArray(), writable: false);
        return EnumerateAndDispose(new JsonlRecordReader<T>(stream, typeInfo, options));
    }

    private static IEnumerable<T> EnumerateAndDispose<T>(JsonlRecordReader<T> reader)
    {
        try
        {
            foreach (T record in reader)
                yield return record;
        }
        finally
        {
            reader.Dispose();
        }
    }

    /// <summary>
    /// Provides a <see cref="JsonSerializerOptions"/> tuned for permissive JSONL parsing (case-insensitive properties).
    /// </summary>
    internal static JsonSerializerOptions DefaultSerializerOptions { get; } = new(JsonSerializerDefaults.Web);
}
