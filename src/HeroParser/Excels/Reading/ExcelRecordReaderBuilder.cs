using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using HeroParser.Excels.Core;
using HeroParser.Excels.Xlsx;
using HeroParser.SeparatedValues.Mapping;
using HeroParser.SeparatedValues.Reading.Binders;
using HeroParser.SeparatedValues.Reading.Records;
using HeroParser.SeparatedValues.Reading.Shared;
using HeroParser.Validation;

namespace HeroParser.Excels.Reading;

/// <summary>
/// Fluent builder for configuring and executing typed Excel reading operations.
/// </summary>
/// <typeparam name="T">The record type to deserialize rows into.</typeparam>
public sealed class ExcelRecordReaderBuilder<T> where T : new()
{
    private string? sheetName;
    private int? sheetIndex;
    private bool hasHeaderRow = true;
    private bool caseSensitiveHeaders;
    private bool allowMissingColumns;
    private IReadOnlyList<string>? nullValues;
    private CultureInfo culture = CultureInfo.InvariantCulture;
    private int? maxRows;
    private int skipRows;
    private IProgress<ExcelProgress>? progress;
    private int progressIntervalRows = 1000;
    private ValidationMode validationMode = ValidationMode.Strict;
    private ExcelDeserializeErrorHandler? onDeserializeError;
    private List<Func<CsvRecordOptions, CsvRecordOptions>>? converterRegistrations;
    private ICsvReadMapSource<T>? mapSource;

    internal ExcelRecordReaderBuilder() { }

    /// <summary>
    /// Selects the sheet to read by name.
    /// </summary>
    /// <param name="name">The name of the sheet to read.</param>
    /// <returns>This builder for method chaining.</returns>
    public ExcelRecordReaderBuilder<T> FromSheet(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        sheetName = name;
        sheetIndex = null;
        return this;
    }

    /// <summary>
    /// Selects the sheet to read by zero-based index.
    /// </summary>
    /// <param name="index">The zero-based index of the sheet to read.</param>
    /// <returns>This builder for method chaining.</returns>
    public ExcelRecordReaderBuilder<T> FromSheet(int index)
    {
        sheetIndex = index;
        sheetName = null;
        return this;
    }

    /// <summary>
    /// Indicates that the Excel data includes a header row (default).
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public ExcelRecordReaderBuilder<T> WithHeader()
    {
        hasHeaderRow = true;
        return this;
    }

    /// <summary>
    /// Indicates that the Excel data does not include a header row.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public ExcelRecordReaderBuilder<T> WithoutHeader()
    {
        hasHeaderRow = false;
        return this;
    }

    /// <summary>
    /// Sets the culture for parsing cell values.
    /// </summary>
    /// <param name="culture">The culture to use for parsing.</param>
    /// <returns>This builder for method chaining.</returns>
    public ExcelRecordReaderBuilder<T> WithCulture(CultureInfo culture)
    {
        this.culture = culture ?? CultureInfo.InvariantCulture;
        return this;
    }

    /// <summary>
    /// Sets the culture for parsing cell values using a culture name.
    /// </summary>
    /// <param name="cultureName">The culture name (e.g., "en-US", "de-DE").</param>
    /// <returns>This builder for method chaining.</returns>
    public ExcelRecordReaderBuilder<T> WithCulture(string cultureName)
    {
        culture = CultureInfo.GetCultureInfo(cultureName);
        return this;
    }

    /// <summary>
    /// Limits the maximum number of data rows to read.
    /// </summary>
    /// <param name="maxRows">The maximum number of data rows (excluding header).</param>
    /// <returns>This builder for method chaining.</returns>
    public ExcelRecordReaderBuilder<T> WithMaxRows(int maxRows)
    {
        this.maxRows = maxRows;
        return this;
    }

    /// <summary>
    /// Skips the specified number of rows before reading the header or data.
    /// </summary>
    /// <param name="count">The number of rows to skip.</param>
    /// <returns>This builder for method chaining.</returns>
    public ExcelRecordReaderBuilder<T> SkipRows(int count)
    {
        skipRows = count;
        return this;
    }

    /// <summary>
    /// Sets the progress reporter for receiving reading progress updates.
    /// </summary>
    /// <param name="progress">The progress reporter.</param>
    /// <param name="intervalRows">Rows between progress updates (default 1000).</param>
    /// <returns>This builder for method chaining.</returns>
    public ExcelRecordReaderBuilder<T> WithProgress(IProgress<ExcelProgress> progress, int intervalRows = 1000)
    {
        this.progress = progress;
        progressIntervalRows = intervalRows;
        return this;
    }

    /// <summary>
    /// Registers a custom type converter for a specific type.
    /// </summary>
    /// <typeparam name="TValue">The type to convert to.</typeparam>
    /// <param name="converter">The converter delegate.</param>
    /// <returns>This builder for method chaining.</returns>
    public ExcelRecordReaderBuilder<T> RegisterConverter<TValue>(CsvTypeConverter<TValue> converter)
    {
        ArgumentNullException.ThrowIfNull(converter);
        converterRegistrations ??= [];
        converterRegistrations.Add(options => options.RegisterConverter(converter));
        return this;
    }

    /// <summary>
    /// Configures the builder to use a fluent <see cref="CsvMap{T}"/> for column mapping.
    /// When set, terminal methods use descriptor binding instead of attribute/source-generator binding.
    /// </summary>
    /// <param name="map">The pre-configured CSV map instance.</param>
    /// <returns>This builder for method chaining.</returns>
    [RequiresUnreferencedCode("Fluent mapping uses reflection. Use [GenerateBinder] for AOT/trimming support.")]
    [RequiresDynamicCode("Fluent mapping uses expression compilation. Use [GenerateBinder] for AOT support.")]
    public ExcelRecordReaderBuilder<T> WithMap(ICsvReadMapSource<T> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        mapSource = map;
        return this;
    }

    /// <summary>
    /// Maps a property to an Excel column inline, creating a <see cref="CsvMap{T}"/> if one has not been set.
    /// Cannot be mixed with <see cref="WithMap"/>; use one approach or the other.
    /// </summary>
    /// <typeparam name="TProperty">The property type.</typeparam>
    /// <param name="property">An expression selecting the property to map (e.g., <c>t =&gt; t.Name</c>).</param>
    /// <param name="configure">Optional column configuration action.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when <see cref="WithMap"/> was already called.</exception>
    [RequiresUnreferencedCode("Fluent mapping uses reflection. Use [GenerateBinder] for AOT/trimming support.")]
    [RequiresDynamicCode("Fluent mapping uses expression compilation. Use [GenerateBinder] for AOT support.")]
    public ExcelRecordReaderBuilder<T> Map<TProperty>(
        Expression<Func<T, TProperty>> property,
        Action<CsvColumnBuilder>? configure = null)
    {
        if (mapSource is not null and not InlineCsvMapWrapper<T>)
        {
            throw new InvalidOperationException(
                "Cannot call Map() after WithMap(). Either use WithMap() with a fully configured map, " +
                "or use Map() calls exclusively for inline mapping.");
        }

        var wrapper = (mapSource as InlineCsvMapWrapper<T>) ?? CreateInlineWrapper();
        wrapper.Map(property, configure);
        return this;
    }

    [RequiresUnreferencedCode("Fluent mapping uses reflection.")]
    [RequiresDynamicCode("Fluent mapping uses expression compilation.")]
    private InlineCsvMapWrapper<T> CreateInlineWrapper()
    {
        var wrapper = new InlineCsvMapWrapper<T>();
        mapSource = wrapper;
        return wrapper;
    }

    /// <summary>
    /// Sets the validation mode for record reading. Default is <see cref="ValidationMode.Strict"/>.
    /// </summary>
    /// <param name="mode">The validation mode to use.</param>
    /// <returns>This builder for method chaining.</returns>
    public ExcelRecordReaderBuilder<T> WithValidationMode(ValidationMode mode)
    {
        validationMode = mode;
        return this;
    }

    /// <summary>
    /// Sets the error handler for deserialization errors.
    /// When set, the handler is called for each row that fails to deserialize, allowing the caller
    /// to skip the record or rethrow the exception.
    /// </summary>
    /// <param name="handler">The error handler delegate.</param>
    /// <returns>This builder for method chaining.</returns>
    public ExcelRecordReaderBuilder<T> OnError(ExcelDeserializeErrorHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        onDeserializeError = handler;
        return this;
    }

    /// <summary>
    /// Enables case-sensitive header matching.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public ExcelRecordReaderBuilder<T> CaseSensitiveHeaders()
    {
        caseSensitiveHeaders = true;
        return this;
    }

    /// <summary>
    /// Allows missing columns without throwing an exception.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public ExcelRecordReaderBuilder<T> AllowMissingColumns()
    {
        allowMissingColumns = true;
        return this;
    }

    /// <summary>
    /// Sets values that should be treated as null during parsing.
    /// </summary>
    /// <param name="values">The string values to treat as null.</param>
    /// <returns>This builder for method chaining.</returns>
    public ExcelRecordReaderBuilder<T> WithNullValues(params string[] values)
    {
        nullValues = values;
        return this;
    }

    /// <summary>
    /// Reads all sheets of the same record type and returns results keyed by sheet name.
    /// </summary>
    /// <returns>A builder for same-type multi-sheet reading.</returns>
    public ExcelAllSheetsBuilder<T> AllSheets()
    {
        return new ExcelAllSheetsBuilder<T>(hasHeaderRow, caseSensitiveHeaders, allowMissingColumns, nullValues, culture, maxRows, skipRows, progress, validationMode, onDeserializeError);
    }

    /// <summary>
    /// Reads typed records from an Excel file on disk.
    /// </summary>
    /// <param name="path">The path to the .xlsx file.</param>
    /// <returns>A list of deserialized records.</returns>
    public List<T> FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return FromStream(stream);
    }

    /// <summary>
    /// Reads typed records from a stream containing .xlsx data.
    /// </summary>
    /// <param name="stream">A seekable stream containing .xlsx data.</param>
    /// <returns>A list of deserialized records.</returns>
    public List<T> FromStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var xlsxReader = new XlsxReader(stream);
        var sheet = ResolveSheet(xlsxReader.Workbook);
        using var sheetReader = xlsxReader.OpenSheet(sheet);

        return ReadRecords(sheetReader, sheet.Name);
    }

    /// <summary>
    /// Asynchronously reads typed records from an Excel file, yielding each record as it is parsed
    /// rather than loading the entire result into memory.
    /// </summary>
    /// <param name="path">The path to the .xlsx file.</param>
    /// <param name="cancellationToken">Token to cancel enumeration between rows.</param>
    /// <returns>An async sequence of deserialized records.</returns>
    public async IAsyncEnumerable<T> FromFileAsync(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var fileStream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            await foreach (var record in FromStreamAsync(fileStream, cancellationToken).ConfigureAwait(false))
            {
                yield return record;
            }
        }
        finally
        {
            await fileStream.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Asynchronously reads typed records from a stream containing .xlsx data, yielding each record
    /// as it is parsed rather than loading the entire result into memory.
    /// </summary>
    /// <param name="stream">A seekable stream containing .xlsx data.</param>
    /// <param name="cancellationToken">Token to cancel enumeration between rows.</param>
    /// <returns>An async sequence of deserialized records.</returns>
    public async IAsyncEnumerable<T> FromStreamAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        await Task.Yield();

        using var xlsxReader = new XlsxReader(stream);
        var sheet = ResolveSheet(xlsxReader.Workbook);
        using var sheetReader = xlsxReader.OpenSheet(sheet);

        foreach (var record in EnumerateRecords(sheetReader, sheet.Name))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return record;
        }
    }

    internal List<T> ReadRecords(XlsxSheetReader sheetReader, string currentSheetName)
    {
        var results = new List<T>();
        foreach (var record in EnumerateRecords(sheetReader, currentSheetName))
        {
            results.Add(record);
        }
        return results;
    }

    private IEnumerable<T> EnumerateRecords(XlsxSheetReader sheetReader, string currentSheetName)
    {
        // Skip configured rows
        for (int i = 0; i < skipRows; i++)
        {
            if (sheetReader.ReadNextRow() is null)
                yield break;
        }

        // Get binder
        var recordOptions = new CsvRecordOptions
        {
            HasHeaderRow = hasHeaderRow,
            CaseSensitiveHeaders = caseSensitiveHeaders,
            AllowMissingColumns = allowMissingColumns,
            NullValues = nullValues,
            Culture = culture,
            ValidationMode = validationMode
        };

        if (converterRegistrations is { Count: > 0 })
        {
            foreach (var registration in converterRegistrations)
                recordOptions = registration(recordOptions);
        }

        ICsvBinder<char, T> binder;
        if (mapSource is not null)
        {
            var descriptor = mapSource.BuildReadDescriptor();
            binder = new CsvDescriptorBinder<T>(descriptor, recordOptions);
        }
        else
        {
            binder = CsvRecordBinderFactory.GetCharBinder<T>(recordOptions, delimiter: '\x01');
        }

        // Read header row if configured
        if (hasHeaderRow)
        {
            var headerCells = sheetReader.ReadNextRow();
            if (headerCells is null)
                yield break;

            if (binder.NeedsHeaderResolution)
            {
                var headerBuffer = new char[XlsxRowAdapter.CalculateBufferSize(headerCells) + 1];
                var headerColumnEnds = new int[headerCells.Length + 1];
                var headerRow = XlsxRowAdapter.CreateRow(headerCells, 0, headerBuffer, headerColumnEnds);
                binder.BindHeader(headerRow, 0);
            }
        }

        var errors = new List<ValidationError>();
        int rowsRead = 0;
        char[] buffer = [];
        int[] columnEnds = [];

        while (true)
        {
            var cells = sheetReader.ReadNextRow();
            if (cells is null)
                break;

            if (maxRows.HasValue && rowsRead >= maxRows.Value)
                break;

            XlsxRowAdapter.EnsureBuffers(cells, ref buffer, ref columnEnds);
            var csvRow = XlsxRowAdapter.CreateRow(cells, sheetReader.CurrentRowNumber, buffer, columnEnds);

            T record = default!;
            bool produced = false;
            bool skipDueToError = false;
            try
            {
                produced = binder.TryBind(csvRow, sheetReader.CurrentRowNumber, out record, errors);
            }
            catch (Exception ex) when (onDeserializeError is not null)
            {
                string? rawValue = ex is SeparatedValues.Core.CsvException csvEx ? csvEx.FieldValue : null;
                var context = new ExcelDeserializeErrorContext
                {
                    Row = sheetReader.CurrentRowNumber,
                    SheetName = currentSheetName,
                    RawValue = rawValue
                };

                var action = onDeserializeError(context, ex);
                if (action == ExcelDeserializeErrorAction.Throw)
                    throw;
                skipDueToError = true;
            }

            if (skipDueToError)
                continue; // SkipRecord: skip rowsRead++ so skipped rows don't count toward maxRows

            if (produced)
            {
                yield return record;
            }

            rowsRead++;

            if (progress is not null && rowsRead % progressIntervalRows == 0)
            {
                progress.Report(new ExcelProgress(rowsRead, currentSheetName));
            }
        }

        if (validationMode == ValidationMode.Strict && errors.Count > 0)
            throw new ValidationException(errors);

        // Final progress report
        if (progress is not null && rowsRead % progressIntervalRows != 0)
        {
            progress.Report(new ExcelProgress(rowsRead, currentSheetName));
        }
    }

    private XlsxWorkbook.SheetInfo ResolveSheet(XlsxWorkbook workbook)
    {
        if (sheetName is not null)
            return workbook.GetSheetByName(sheetName);

        if (sheetIndex.HasValue)
            return workbook.GetSheetByIndex(sheetIndex.Value);

        return workbook.GetFirstSheet();
    }
}
