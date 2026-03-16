using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using HeroParser.Excels.Core;
using HeroParser.Excels.Xlsx;
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
        var recordWriter = ExcelRecordWriterFactory.GetWriter<T>(options);

        using var xlsxWriter = new XlsxWriter(stream, leaveOpen: leaveOpen);
        using var sheetWriter = xlsxWriter.StartSheet(sheetName);
        recordWriter.WriteRecords(sheetWriter, records, options);
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

    #endregion

    #region Private Helpers

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
            WriteHeader = writeHeader
        };
    }

    #endregion
}
