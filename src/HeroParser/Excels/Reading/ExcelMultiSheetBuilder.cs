using HeroParser.Excels.Xlsx;
using HeroParser.SeparatedValues.Reading.Binders;
using HeroParser.SeparatedValues.Reading.Records;
using HeroParser.Validation;

namespace HeroParser.Excels.Reading;

/// <summary>
/// Builder for reading multiple sheets with different record types per sheet.
/// </summary>
public sealed class ExcelMultiSheetBuilder
{
    private readonly List<SheetRegistration> registrations = [];
    private ValidationMode validationMode = ValidationMode.Strict;

    internal ExcelMultiSheetBuilder() { }

    /// <summary>
    /// Sets the validation mode for record reading. Default is <see cref="ValidationMode.Strict"/>.
    /// </summary>
    /// <param name="mode">The validation mode to use.</param>
    /// <returns>This builder for method chaining.</returns>
    public ExcelMultiSheetBuilder WithValidationMode(ValidationMode mode)
    {
        validationMode = mode;
        return this;
    }

    /// <summary>
    /// Registers a sheet to be read with the specified record type.
    /// </summary>
    /// <typeparam name="T">The record type for this sheet.</typeparam>
    /// <param name="sheetName">The name of the sheet to read.</param>
    /// <returns>This builder for method chaining.</returns>
    public ExcelMultiSheetBuilder WithSheet<T>(string sheetName) where T : new()
    {
        ArgumentException.ThrowIfNullOrEmpty(sheetName);

        var recordType = typeof(T);
        if (registrations.Exists(r => r.RecordType == recordType))
            throw new ArgumentException($"Type '{recordType.Name}' is already registered. Each type can only be registered once.", nameof(T));

        var capturedMode = validationMode;
        registrations.Add(new SheetRegistration(
            sheetName,
            typeof(T),
            validationMode,
            sheetReader => ReadSheet<T>(sheetReader, capturedMode)));
        return this;
    }

    /// <summary>
    /// Reads all registered sheets from an Excel file on disk.
    /// </summary>
    /// <param name="path">The path to the .xlsx file.</param>
    /// <returns>The multi-sheet result containing typed record lists.</returns>
    public ExcelMultiSheetResult FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return FromStream(stream);
    }

    /// <summary>
    /// Reads all registered sheets from a stream containing .xlsx data.
    /// </summary>
    /// <param name="stream">A seekable stream containing .xlsx data.</param>
    /// <returns>The multi-sheet result containing typed record lists.</returns>
    public ExcelMultiSheetResult FromStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var xlsxReader = new XlsxReader(stream);
        var resultData = new Dictionary<Type, object>();

        foreach (var registration in registrations)
        {
            var sheet = xlsxReader.Workbook.GetSheetByName(registration.SheetName);
            using var sheetReader = xlsxReader.OpenSheet(sheet);
            var records = registration.ReadFunc(sheetReader);
            resultData[registration.RecordType] = records;
        }

        return new ExcelMultiSheetResult(resultData);
    }

    private static List<T> ReadSheet<T>(XlsxSheetReader sheetReader, ValidationMode mode) where T : new()
    {
        var recordOptions = new CsvRecordOptions
        {
            HasHeaderRow = true,
            ValidationMode = mode
        };
        var binder = CsvRecordBinderFactory.GetCharBinder<T>(recordOptions, delimiter: '\x01');

        // Read header row
        var headerCells = sheetReader.ReadNextRow();
        if (headerCells is null)
            return [];

        if (binder.NeedsHeaderResolution)
        {
            var headerBuffer = new char[XlsxRowAdapter.CalculateBufferSize(headerCells) + 1];
            var headerColumnEnds = new int[headerCells.Length + 1];
            var headerRow = XlsxRowAdapter.CreateRow(headerCells, 0, headerBuffer, headerColumnEnds);
            binder.BindHeader(headerRow, 0);
        }

        var results = new List<T>();
        var errors = new List<ValidationError>();
        char[] buffer = [];
        int[] columnEnds = [];

        while (true)
        {
            var cells = sheetReader.ReadNextRow();
            if (cells is null)
                break;

            XlsxRowAdapter.EnsureBuffers(cells, ref buffer, ref columnEnds);
            var csvRow = XlsxRowAdapter.CreateRow(cells, sheetReader.CurrentRowNumber, buffer, columnEnds);

            if (binder.TryBind(csvRow, sheetReader.CurrentRowNumber, out var record, errors))
            {
                results.Add(record);
            }
        }

        if (mode == ValidationMode.Strict && errors.Count > 0)
            throw new ValidationException(errors);

        return results;
    }

    private sealed record SheetRegistration(
        string SheetName,
        Type RecordType,
        ValidationMode ValidationMode,
        Func<XlsxSheetReader, object> ReadFunc);
}
