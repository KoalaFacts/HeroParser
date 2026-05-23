using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using HeroParser.Validation;

namespace HeroParser.JsonLines.Reading;

/// <summary>
/// Fluent builder for configuring and executing JSONL reading operations with record deserialization.
/// </summary>
/// <typeparam name="T">The record type to deserialize.</typeparam>
public sealed partial class JsonlRecordReaderBuilder<T>
{
    private JsonSerializerOptions? serializerOptions;
    private JsonTypeInfo<T>? typeInfo;
    private int maxLineSizeBytes = 1 * 1024 * 1024;
    private int maxRowCount = 1_000_000;
    private bool skipEmptyLines = true;
    private int skipRows;
    private ValidationMode validationMode = ValidationMode.Strict;
    private JsonlDeserializeErrorHandler? onError;
    private IProgress<JsonlProgress>? progress;
    private int progressIntervalRows = 1000;

    internal JsonlRecordReaderBuilder() { }

    private JsonlReadOptions BuildOptions() => new()
    {
        SerializerOptions = serializerOptions,
        MaxLineSizeBytes = maxLineSizeBytes,
        MaxRowCount = maxRowCount,
        SkipEmptyLines = skipEmptyLines,
        SkipRows = skipRows,
        ValidationMode = validationMode,
        OnError = onError,
        Progress = progress,
        ProgressIntervalRows = progressIntervalRows
    };

    private JsonlRecordReader<T> CreateReader(Stream stream, bool leaveOpen)
    {
        JsonlReadOptions options = BuildOptions();
        return typeInfo is not null
            ? new JsonlRecordReader<T>(stream, typeInfo, options, leaveOpen)
            : CreateReflectionReader(stream, options, leaveOpen);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Reflection path is gated by [RequiresUnreferencedCode] on the builder's reflection terminals; callers that pass JsonTypeInfo<T> never hit this branch.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Reflection path is gated by [RequiresDynamicCode] on the builder's reflection terminals; callers that pass JsonTypeInfo<T> never hit this branch.")]
    private static JsonlRecordReader<T> CreateReflectionReader(Stream stream, JsonlReadOptions options, bool leaveOpen)
        => new(stream, options, leaveOpen);
}
