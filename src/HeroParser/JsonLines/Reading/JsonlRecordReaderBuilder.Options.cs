using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using HeroParser.Validation;

namespace HeroParser.JsonLines.Reading;

public sealed partial class JsonlRecordReaderBuilder<T>
{
    /// <summary>
    /// Sets the <see cref="JsonSerializerOptions"/> used for deserialization.
    /// </summary>
    public JsonlRecordReaderBuilder<T> WithJsonOptions(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        serializerOptions = options;
        return this;
    }

    /// <summary>
    /// Sets the <see cref="JsonTypeInfo{T}"/> used for AOT-safe deserialization.
    /// When set, the reflection-based deserialization path is skipped.
    /// </summary>
    public JsonlRecordReaderBuilder<T> WithTypeInfo(JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        this.typeInfo = typeInfo;
        return this;
    }

    /// <summary>
    /// Controls whether blank lines are silently skipped (default <see langword="true"/>).
    /// </summary>
    public JsonlRecordReaderBuilder<T> SkipEmptyLines(bool value = true)
    {
        skipEmptyLines = value;
        return this;
    }

    /// <summary>
    /// Sets the maximum allowed length of a single JSONL line in bytes.
    /// Defaults to 1 MiB.
    /// </summary>
    public JsonlRecordReaderBuilder<T> WithMaxLineSize(int bytes)
    {
        maxLineSizeBytes = bytes;
        return this;
    }

    /// <summary>
    /// Sets the maximum number of records that may be read.
    /// </summary>
    public JsonlRecordReaderBuilder<T> WithMaxRowCount(int rows)
    {
        maxRowCount = rows;
        return this;
    }

    /// <summary>
    /// Skips the specified number of leading data records.
    /// </summary>
    public JsonlRecordReaderBuilder<T> SkipRows(int n)
    {
        skipRows = n;
        return this;
    }

    /// <summary>
    /// Sets a progress reporter invoked every <paramref name="intervalRows"/> records.
    /// </summary>
    public JsonlRecordReaderBuilder<T> WithProgress(IProgress<JsonlProgress> progress, int intervalRows = 1000)
    {
        ArgumentNullException.ThrowIfNull(progress);
        this.progress = progress;
        progressIntervalRows = intervalRows;
        return this;
    }

    /// <summary>
    /// Sets the validation mode (Strict by default — throws after enumeration if errors were collected).
    /// </summary>
    public JsonlRecordReaderBuilder<T> WithValidationMode(ValidationMode mode)
    {
        validationMode = mode;
        return this;
    }

    /// <summary>
    /// Registers a callback invoked when a line fails to deserialize.
    /// </summary>
    public JsonlRecordReaderBuilder<T> OnError(JsonlDeserializeErrorHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        onError = handler;
        return this;
    }
}
