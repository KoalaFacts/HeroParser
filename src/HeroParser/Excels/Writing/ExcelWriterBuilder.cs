using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using HeroParser.Excels.Core;
using HeroParser.Excels.Xlsx;
using HeroParser.SeparatedValues.Mapping;
using HeroParser.Validation;

namespace HeroParser.Excels.Writing;

/// <summary>
/// Fluent builder for configuring and executing Excel (.xlsx) writing operations.
/// </summary>
/// <typeparam name="T">The record type to write.</typeparam>
public sealed class ExcelWriterBuilder<T> where T : new()
{
    private CultureInfo culture = CultureInfo.InvariantCulture;
    private string nullValue = "";
    private string? dateTimeFormat;
    private string? dateOnlyFormat;
    private string? timeOnlyFormat;
    private string? numberFormat;
    private int? maxRowCount;
    private ValidationMode validationMode = ValidationMode.Strict;
    private bool writeHeader = true;
    private string sheetName = "Sheet1";

    // New parity features
    private ExcelSerializeErrorHandler? onSerializeError;
    private long? maxOutputSize;
    private IProgress<ExcelWriteProgress>? writeProgress;
    private int writeProgressIntervalRows = 1000;
    private ICsvWriteMapSource<T>? writeMapSource;

    // Cached options — invalidated when any setting changes
    private ExcelWriteOptions? cachedOptions;

    internal ExcelWriterBuilder() { }

    /// <summary>
    /// Sets the culture used for formatting values.
    /// </summary>
    /// <param name="culture">The culture to use.</param>
    /// <returns>This builder for method chaining.</returns>
    public ExcelWriterBuilder<T> WithCulture(CultureInfo culture)
    {
        this.culture = culture ?? CultureInfo.InvariantCulture;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the culture used for formatting values using a culture name.
    /// </summary>
    /// <param name="cultureName">The culture name (e.g., "en-US", "de-DE").</param>
    /// <returns>This builder for method chaining.</returns>
    public ExcelWriterBuilder<T> WithCulture(string cultureName)
    {
        culture = CultureInfo.GetCultureInfo(cultureName);
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the string written for null values.
    /// </summary>
    /// <param name="nullValue">The null representation.</param>
    /// <returns>This builder for method chaining.</returns>
    public ExcelWriterBuilder<T> WithNullValue(string nullValue)
    {
        this.nullValue = nullValue ?? "";
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the format string for <see cref="DateTime"/> values.
    /// </summary>
    /// <param name="format">The format string (e.g., "yyyy-MM-dd HH:mm:ss").</param>
    /// <returns>This builder for method chaining.</returns>
    public ExcelWriterBuilder<T> WithDateTimeFormat(string format)
    {
        dateTimeFormat = format;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the format string for <see cref="DateOnly"/> values.
    /// </summary>
    /// <param name="format">The format string (e.g., "yyyy-MM-dd").</param>
    /// <returns>This builder for method chaining.</returns>
    public ExcelWriterBuilder<T> WithDateOnlyFormat(string format)
    {
        dateOnlyFormat = format;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the format string for <see cref="TimeOnly"/> values.
    /// </summary>
    /// <param name="format">The format string (e.g., "HH:mm:ss").</param>
    /// <returns>This builder for method chaining.</returns>
    public ExcelWriterBuilder<T> WithTimeOnlyFormat(string format)
    {
        timeOnlyFormat = format;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the format string for numeric values.
    /// </summary>
    /// <param name="format">The format string (e.g., "N2", "F4", "C").</param>
    /// <returns>This builder for method chaining.</returns>
    public ExcelWriterBuilder<T> WithNumberFormat(string format)
    {
        numberFormat = format;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the maximum number of data rows to write.
    /// </summary>
    /// <param name="maxRows">The maximum number of rows, or <see langword="null"/> for unlimited.</param>
    /// <returns>This builder for method chaining.</returns>
    public ExcelWriterBuilder<T> WithMaxRowCount(int? maxRows)
    {
        maxRowCount = maxRows;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the validation mode for write operations.
    /// </summary>
    /// <param name="mode">The validation mode to use.</param>
    /// <returns>This builder for method chaining.</returns>
    public ExcelWriterBuilder<T> WithValidationMode(ValidationMode mode)
    {
        validationMode = mode;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Enables writing a header row with property names (default).
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public ExcelWriterBuilder<T> WithHeader()
    {
        writeHeader = true;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Disables writing a header row.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public ExcelWriterBuilder<T> WithoutHeader()
    {
        writeHeader = false;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the name of the worksheet to create. Defaults to "Sheet1".
    /// </summary>
    /// <param name="name">The sheet name.</param>
    /// <returns>This builder for method chaining.</returns>
    public ExcelWriterBuilder<T> WithSheetName(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        sheetName = name;
        // sheetName is not part of ExcelWriteOptions, so no need to invalidate cachedOptions
        return this;
    }

    /// <summary>
    /// Registers a callback invoked when a serialization error occurs while writing a record.
    /// </summary>
    /// <remarks>
    /// This callback is only invoked for reflection-based writing. Source-generated writers
    /// (via <c>[GenerateBinder]</c>) bypass this handler for performance.
    /// </remarks>
    /// <param name="handler">
    /// A delegate that receives the error context and returns the action to take
    /// (<see cref="ExcelSerializeErrorAction.Throw"/>, <see cref="ExcelSerializeErrorAction.SkipRow"/>,
    /// or <see cref="ExcelSerializeErrorAction.WriteEmpty"/>).
    /// </param>
    /// <returns>This builder for method chaining.</returns>
    public ExcelWriterBuilder<T> OnError(ExcelSerializeErrorHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        onSerializeError = handler;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Sets the maximum number of uncompressed worksheet XML bytes to write (DoS protection).
    /// </summary>
    /// <param name="maxBytes">
    /// The maximum size in bytes, or <see langword="null"/> to disable the limit (the default).
    /// </param>
    /// <returns>This builder for method chaining.</returns>
    public ExcelWriterBuilder<T> WithMaxOutputSize(long? maxBytes)
    {
        maxOutputSize = maxBytes;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Enables progress reporting during writing.
    /// </summary>
    /// <param name="progress">The progress reporter to notify.</param>
    /// <param name="intervalRows">The interval in rows between progress reports. Defaults to 1000.</param>
    /// <returns>This builder for method chaining.</returns>
    public ExcelWriterBuilder<T> WithProgress(IProgress<ExcelWriteProgress> progress, int intervalRows = 1000)
    {
        ArgumentNullException.ThrowIfNull(progress);
        writeProgress = progress;
        writeProgressIntervalRows = intervalRows > 0 ? intervalRows : 1000;
        cachedOptions = null;
        return this;
    }

    /// <summary>
    /// Overrides the property-to-column mapping using a fluent map source.
    /// </summary>
    /// <param name="mapSource">The map source that provides write templates.</param>
    /// <returns>This builder for method chaining.</returns>
    [RequiresUnreferencedCode("Fluent mapping uses reflection-based template building.")]
    [RequiresDynamicCode("Fluent mapping requires dynamic code for expression compilation.")]
    public ExcelWriterBuilder<T> WithMap(ICsvWriteMapSource<T> mapSource)
    {
        ArgumentNullException.ThrowIfNull(mapSource);
        writeMapSource = mapSource;
        return this;
    }

    #region Terminal Methods

    /// <summary>
    /// Writes records to an Excel file at the specified path.
    /// </summary>
    /// <param name="path">The file path to write to.</param>
    /// <param name="records">The records to write.</param>
    [RequiresUnreferencedCode("Falls back to reflection when no generated writer is registered. Use [GenerateBinder] for AOT/trimming support.")]
    [RequiresDynamicCode("Falls back to reflection when no generated writer is registered. Use [GenerateBinder] for AOT support.")]
    public void ToFile(string path, IEnumerable<T> records)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(records);

        using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        ToStream(fileStream, records, leaveOpen: false);
    }

    /// <summary>
    /// Writes records to a stream as an Excel (.xlsx) file.
    /// </summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="records">The records to write.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, the stream is not closed after writing.</param>
    [RequiresUnreferencedCode("Falls back to reflection when no generated writer is registered. Use [GenerateBinder] for AOT/trimming support.")]
    [RequiresDynamicCode("Falls back to reflection when no generated writer is registered. Use [GenerateBinder] for AOT support.")]
    public void ToStream(Stream stream, IEnumerable<T> records, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(records);

        var options = GetOptions();
        var recordWriter = GetRecordWriter(options);

        using var xlsxWriter = new XlsxWriter(stream, leaveOpen: leaveOpen);
        using var sheetWriter = xlsxWriter.StartSheet(sheetName);
        recordWriter.WriteRecords(sheetWriter, records, options, sheetName);
    }

    /// <summary>
    /// Writes records to an in-memory Excel (.xlsx) file and returns the bytes.
    /// </summary>
    /// <param name="records">The records to write.</param>
    /// <returns>The .xlsx file content as a byte array.</returns>
    [RequiresUnreferencedCode("Falls back to reflection when no generated writer is registered. Use [GenerateBinder] for AOT/trimming support.")]
    [RequiresDynamicCode("Falls back to reflection when no generated writer is registered. Use [GenerateBinder] for AOT support.")]
    public byte[] ToBytes(IEnumerable<T> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        using var ms = new MemoryStream();
        ToStream(ms, records, leaveOpen: true);
        return ms.ToArray();
    }

    /// <summary>
    /// Asynchronously writes records to an Excel file at the specified path.
    /// </summary>
    /// <param name="path">The file path to write to.</param>
    /// <param name="records">The records to write.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when writing is done.</returns>
    [RequiresUnreferencedCode("Falls back to reflection when no generated writer is registered. Use [GenerateBinder] for AOT/trimming support.")]
    [RequiresDynamicCode("Falls back to reflection when no generated writer is registered. Use [GenerateBinder] for AOT support.")]
    public Task ToFileAsync(string path, IEnumerable<T> records, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(records);

        return Task.Run(() => ToFile(path, records), ct);
    }

    /// <summary>
    /// Asynchronously writes records from an async sequence to an Excel file at the specified path.
    /// </summary>
    /// <param name="path">The file path to write to.</param>
    /// <param name="records">The async sequence of records to write.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when writing is done.</returns>
    [RequiresUnreferencedCode("Falls back to reflection when no generated writer is registered. Use [GenerateBinder] for AOT/trimming support.")]
    [RequiresDynamicCode("Falls back to reflection when no generated writer is registered. Use [GenerateBinder] for AOT support.")]
    public async Task ToFileAsync(string path, IAsyncEnumerable<T> records, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(records);

        var materialized = await MaterializeAsync(records, ct).ConfigureAwait(false);
        await Task.Run(() => ToFile(path, materialized), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously writes records to a stream as an Excel (.xlsx) file.
    /// </summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="records">The records to write.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, the stream is not closed after writing.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when writing is done.</returns>
    [RequiresUnreferencedCode("Falls back to reflection when no generated writer is registered. Use [GenerateBinder] for AOT/trimming support.")]
    [RequiresDynamicCode("Falls back to reflection when no generated writer is registered. Use [GenerateBinder] for AOT support.")]
    public Task ToStreamAsync(Stream stream, IEnumerable<T> records, bool leaveOpen = true, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(records);

        return Task.Run(() => ToStream(stream, records, leaveOpen), ct);
    }

    /// <summary>
    /// Asynchronously writes records from an async sequence to a stream as an Excel (.xlsx) file.
    /// </summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="records">The async sequence of records to write.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, the stream is not closed after writing.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when writing is done.</returns>
    [RequiresUnreferencedCode("Falls back to reflection when no generated writer is registered. Use [GenerateBinder] for AOT/trimming support.")]
    [RequiresDynamicCode("Falls back to reflection when no generated writer is registered. Use [GenerateBinder] for AOT support.")]
    public async Task ToStreamAsync(Stream stream, IAsyncEnumerable<T> records, bool leaveOpen = true, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(records);

        var materialized = await MaterializeAsync(records, ct).ConfigureAwait(false);
        await Task.Run(() => ToStream(stream, materialized, leaveOpen), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously writes records to an in-memory Excel (.xlsx) file and returns the bytes.
    /// </summary>
    /// <param name="records">The records to write.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that returns the .xlsx file content as a byte array.</returns>
    [RequiresUnreferencedCode("Falls back to reflection when no generated writer is registered. Use [GenerateBinder] for AOT/trimming support.")]
    [RequiresDynamicCode("Falls back to reflection when no generated writer is registered. Use [GenerateBinder] for AOT support.")]
    public Task<byte[]> ToBytesAsync(IEnumerable<T> records, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(records);

        return Task.Run(() => ToBytes(records), ct);
    }

    /// <summary>
    /// Asynchronously writes records from an async sequence to an in-memory Excel (.xlsx) file and returns the bytes.
    /// </summary>
    /// <param name="records">The async sequence of records to write.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that returns the .xlsx file content as a byte array.</returns>
    [RequiresUnreferencedCode("Falls back to reflection when no generated writer is registered. Use [GenerateBinder] for AOT/trimming support.")]
    [RequiresDynamicCode("Falls back to reflection when no generated writer is registered. Use [GenerateBinder] for AOT support.")]
    public async Task<byte[]> ToBytesAsync(IAsyncEnumerable<T> records, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(records);

        var materialized = await MaterializeAsync(records, ct).ConfigureAwait(false);
        return await Task.Run(() => ToBytes(materialized), ct).ConfigureAwait(false);
    }

    #endregion

    #region Private Helpers

    /// <summary>Gets the current options, building them if not already cached.</summary>
    internal ExcelWriteOptions GetOptions()
    {
        return cachedOptions ??= new ExcelWriteOptions
        {
            Culture = culture,
            NullValue = nullValue,
            DateTimeFormat = dateTimeFormat,
            DateOnlyFormat = dateOnlyFormat,
            TimeOnlyFormat = timeOnlyFormat,
            NumberFormat = numberFormat,
            MaxRowCount = maxRowCount,
            ValidationMode = validationMode,
            WriteHeader = writeHeader,
            OnSerializeError = onSerializeError,
            MaxOutputSize = maxOutputSize,
            WriteProgress = writeProgress,
            WriteProgressIntervalRows = writeProgressIntervalRows,
        };
    }

    [RequiresUnreferencedCode("Falls back to reflection when no generated writer is registered. Use [GenerateBinder] for AOT/trimming support.")]
    [RequiresDynamicCode("Falls back to reflection when no generated writer is registered. Use [GenerateBinder] for AOT support.")]
    private ExcelRecordWriter<T> GetRecordWriter(ExcelWriteOptions options)
    {
        if (writeMapSource is null)
            return ExcelRecordWriterFactory.GetWriter<T>(options);

        // Convert CSV WriterTemplate[] → Excel WriterTemplate[]
        var csvTemplates = writeMapSource.BuildWriteTemplates();
        var excelTemplates = new ExcelRecordWriter<T>.WriterTemplate[csvTemplates.Length];
        for (int i = 0; i < csvTemplates.Length; i++)
        {
            var csv = csvTemplates[i];
            excelTemplates[i] = new ExcelRecordWriter<T>.WriterTemplate(
                csv.MemberName,
                csv.SourceType,
                csv.HeaderName,
                csv.Format,
                csv.Getter,
                csv.Validation);
        }
        return ExcelRecordWriter<T>.CreateFromTemplates(options, excelTemplates);
    }

    private static async Task<List<T>> MaterializeAsync(IAsyncEnumerable<T> records, CancellationToken ct)
    {
        var list = new List<T>();
        await foreach (var record in records.WithCancellation(ct).ConfigureAwait(false))
            list.Add(record);
        return list;
    }

    #endregion
}
