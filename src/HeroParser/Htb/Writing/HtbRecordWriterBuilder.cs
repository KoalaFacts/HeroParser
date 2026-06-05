using System.Diagnostics.CodeAnalysis;
using HeroParser.Htbs;

namespace HeroParser.Htbs.Writing;

/// <summary>
/// Fluent builder for configuring and executing HTB writing operations.
/// </summary>
/// <typeparam name="T">The record type to serialize.</typeparam>
public sealed class HtbRecordWriterBuilder<T> where T : new()
{
    private int? maxRowCount;
    private long? maxOutputSize;
    private HtbSerializeErrorHandler? onError;
    private IProgress<HtbWriteProgress>? progress;
    private int progressIntervalRows = 1000;

    internal HtbRecordWriterBuilder() { }

    /// <summary>
    /// Sets the maximum number of rows allowed to be written.
    /// </summary>
    public HtbRecordWriterBuilder<T> WithMaxRowCount(int count)
    {
        maxRowCount = count;
        return this;
    }

    /// <summary>
    /// Sets the maximum size of the output stream in bytes.
    /// </summary>
    public HtbRecordWriterBuilder<T> WithMaxOutputSize(long size)
    {
        maxOutputSize = size;
        return this;
    }

    /// <summary>
    /// Sets a per-record serialization error handler.
    /// </summary>
    public HtbRecordWriterBuilder<T> OnError(HtbSerializeErrorHandler handler)
    {
        onError = handler;
        return this;
    }

    /// <summary>
    /// Sets a progress reporter.
    /// </summary>
    public HtbRecordWriterBuilder<T> WithProgress(IProgress<HtbWriteProgress> progressReporter, int intervalRows = 1000)
    {
        progress = progressReporter;
        progressIntervalRows = intervalRows;
        return this;
    }

    private HtbWriteOptions BuildOptions() => new()
    {
        MaxRowCount = maxRowCount,
        MaxOutputSize = maxOutputSize,
        OnError = onError,
        Progress = progress,
        ProgressIntervalRows = progressIntervalRows
    };

    /// <summary>
    /// Serializes records synchronously into the specified stream.
    /// </summary>
    [RequiresUnreferencedCode("Reflection-based HTB serialization is not Native AOT-safe.")]
    public void ToStream(Stream stream, IEnumerable<T> records, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(records);

        var options = BuildOptions();
        options.Validate();

        using var writer = new HtbRecordWriter<T>(stream, options, leaveOpen);
        writer.WriteRecords(records);
    }

    /// <summary>
    /// Serializes records synchronously to the specified file path.
    /// </summary>
    [RequiresUnreferencedCode("Reflection-based HTB serialization is not Native AOT-safe.")]
    public void ToFile(string path, IEnumerable<T> records)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(records);

        using var stream = File.Create(path);
        ToStream(stream, records, leaveOpen: false);
    }

    /// <summary>
    /// Serializes records asynchronously into the specified stream.
    /// </summary>
    [RequiresUnreferencedCode("Reflection-based HTB serialization is not Native AOT-safe.")]
    public async Task ToStreamAsync(Stream stream, IEnumerable<T> records, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(records);

        var options = BuildOptions();
        options.Validate();

        using var writer = new HtbRecordWriter<T>(stream, options, leaveOpen);
        await writer.WriteRecordsAsync(records);
    }

    /// <summary>
    /// Serializes records asynchronously to the specified file path.
    /// </summary>
    [RequiresUnreferencedCode("Reflection-based HTB serialization is not Native AOT-safe.")]
    public async Task ToFileAsync(string path, IEnumerable<T> records)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(records);

        using var stream = File.Create(path);
        await ToStreamAsync(stream, records, leaveOpen: false);
    }

    /// <summary>
    /// Serializes an async enumerable sequence of records into the specified stream.
    /// </summary>
    [RequiresUnreferencedCode("Reflection-based HTB serialization is not Native AOT-safe.")]
    public async Task ToStreamAsync(Stream stream, IAsyncEnumerable<T> records, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(records);

        var options = BuildOptions();
        options.Validate();

        using var writer = new HtbRecordWriter<T>(stream, options, leaveOpen);
        await writer.WriteRecordsAsync(records);
    }

    /// <summary>
    /// Serializes an async enumerable sequence of records to the specified file path.
    /// </summary>
    [RequiresUnreferencedCode("Reflection-based HTB serialization is not Native AOT-safe.")]
    public async Task ToFileAsync(string path, IAsyncEnumerable<T> records)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(records);

        using var stream = File.Create(path);
        await ToStreamAsync(stream, records, leaveOpen: false);
    }
}
