using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using HeroParser.Internal;
using HeroParser.Validation;
using HeroParser.Htbs;

namespace HeroParser.Htbs.Reading;

/// <summary>
/// Fluent builder for configuring and executing HTB reading operations.
/// </summary>
/// <typeparam name="T">The record type to deserialize.</typeparam>
public sealed class HtbRecordReaderBuilder<T> where T : new()
{
    private int maxRowCount = 1_000_000;
    private int skipRows;
    private ValidationMode validationMode = ValidationMode.Strict;
    private HtbDeserializeErrorHandler? onError;
    private IProgress<HtbProgress>? progress;
    private int progressIntervalRows = 1000;

    internal HtbRecordReaderBuilder() { }

    /// <summary>
    /// Sets the maximum number of rows allowed in the file.
    /// </summary>
    public HtbRecordReaderBuilder<T> WithMaxRowCount(int count)
    {
        maxRowCount = count;
        return this;
    }

    /// <summary>
    /// Sets the number of rows to skip.
    /// </summary>
    public HtbRecordReaderBuilder<T> SkipRows(int rows)
    {
        skipRows = rows;
        return this;
    }

    /// <summary>
    /// Sets the validation mode for checking field constraints.
    /// </summary>
    public HtbRecordReaderBuilder<T> WithValidationMode(ValidationMode mode)
    {
        validationMode = mode;
        return this;
    }

    /// <summary>
    /// Sets an error handler for deserialization failures.
    /// </summary>
    public HtbRecordReaderBuilder<T> OnError(HtbDeserializeErrorHandler handler)
    {
        onError = handler;
        return this;
    }

    /// <summary>
    /// Sets a progress reporter.
    /// </summary>
    public HtbRecordReaderBuilder<T> WithProgress(IProgress<HtbProgress> progressReporter, int intervalRows = 1000)
    {
        progress = progressReporter;
        progressIntervalRows = intervalRows;
        return this;
    }

    private HtbReadOptions BuildOptions() => new()
    {
        MaxRowCount = maxRowCount,
        SkipRows = skipRows,
        ValidationMode = validationMode,
        OnError = onError,
        Progress = progress,
        ProgressIntervalRows = progressIntervalRows
    };

    /// <summary>
    /// Reads records from the specified stream.
    /// </summary>
    [RequiresUnreferencedCode("Reflection-based HTB deserialization is not Native AOT-safe.")]
    public IEnumerable<T> FromStream(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var options = BuildOptions();

        // Construct the reader before validating so an owned stream (leaveOpen: false) is
        // disposed when validation fails.
        using var reader = new HtbRecordReader<T>(stream, options, leaveOpen);
        options.Validate();
        while (reader.ReadNext(out T? record))
        {
            if (record != null)
                yield return record;
        }
    }

    /// <summary>
    /// Reads records from the specified file path.
    /// </summary>
    [RequiresUnreferencedCode("Reflection-based HTB deserialization is not Native AOT-safe.")]
    public IEnumerable<T> FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var stream = new CompressedFile(path).OpenRead();
        return FromStream(stream, leaveOpen: false);
    }

    /// <summary>
    /// Streams records asynchronously from the specified stream.
    /// </summary>
    [RequiresUnreferencedCode("Reflection-based HTB deserialization is not Native AOT-safe.")]
    public IAsyncEnumerable<T> FromStreamAsync(Stream stream, bool leaveOpen = false)
        => FromStreamAsync(stream, leaveOpen, CancellationToken.None);

    /// <summary>
    /// Streams records asynchronously from the specified stream with explicit stream ownership and cancellation support.
    /// </summary>
    /// <param name="stream">The HTB stream to read from.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, leaves <paramref name="stream"/> open after enumeration completes.</param>
    /// <param name="cancellationToken">Token used to cancel async I/O and enumeration.</param>
    [RequiresUnreferencedCode("Reflection-based HTB deserialization is not Native AOT-safe.")]
    public async IAsyncEnumerable<T> FromStreamAsync(
        Stream stream,
        bool leaveOpen,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var options = BuildOptions();

        // Construct the reader before validating so an owned stream (leaveOpen: false) is
        // disposed when validation fails.
        var reader = new HtbRecordReader<T>(stream, options, leaveOpen);
        await using (reader.ConfigureAwait(false))
        {
            options.Validate();
            while (await reader.ReadNextAsync(cancellationToken).ConfigureAwait(false))
            {
                if (reader.CurrentRecord != null)
                    yield return reader.CurrentRecord;
            }
        }
    }

    /// <summary>
    /// Streams records asynchronously from the specified file path.
    /// </summary>
    [RequiresUnreferencedCode("Reflection-based HTB deserialization is not Native AOT-safe.")]
    public IAsyncEnumerable<T> FromFileAsync(string path)
        => FromFileAsync(path, CancellationToken.None);

    /// <summary>
    /// Streams records asynchronously from the specified file path with cancellation support.
    /// </summary>
    /// <param name="path">The HTB file path to read from.</param>
    /// <param name="cancellationToken">Token used to cancel async I/O and enumeration.</param>
    [RequiresUnreferencedCode("Reflection-based HTB deserialization is not Native AOT-safe.")]
    public async IAsyncEnumerable<T> FromFileAsync(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        var options = BuildOptions();

        var stream = new CompressedFile(path).OpenRead(bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using (stream.ConfigureAwait(false))
        {
            var reader = new HtbRecordReader<T>(stream, options, leaveOpen: true);
            await using (reader.ConfigureAwait(false))
            {
                options.Validate();
                while (await reader.ReadNextAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (reader.CurrentRecord != null)
                        yield return reader.CurrentRecord;
                }
            }
        }
    }
}
