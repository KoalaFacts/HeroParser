using System.Diagnostics.CodeAnalysis;
using HeroParser.Excels.Core;
using HeroParser.Excels.Writing;
using HeroParser.Excels.Xlsx;

namespace HeroParser;

public static partial class Excel
{
    /// <summary>
    /// Creates a fluent builder for writing Excel (.xlsx) records of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The record type to serialize.</typeparam>
    /// <returns>An <see cref="ExcelWriterBuilder{T}"/> for configuring and executing the write operation.</returns>
    /// <example>
    /// <code>
    /// Excel.Write&lt;Person&gt;()
    ///     .WithSheetName("People")
    ///     .WithHeader()
    ///     .ToFile("people.xlsx", records);
    /// </code>
    /// </example>
    public static ExcelWriterBuilder<T> Write<T>() where T : new() => new();

    /// <summary>
    /// Creates a fluent builder for writing multiple typed sheets to a single Excel (.xlsx) file.
    /// </summary>
    /// <returns>An <see cref="ExcelMultiSheetWriterBuilder"/> for registering sheets and producing the output.</returns>
    /// <example>
    /// <code>
    /// Excel.WriteMultiSheet()
    ///     .WithSheet("Orders", orders)
    ///     .WithSheet("Customers", customers)
    ///     .ToFile("report.xlsx");
    /// </code>
    /// </example>
    public static ExcelMultiSheetWriterBuilder WriteMultiSheet() => new();

    /// <summary>
    /// Writes records to an Excel file at the specified path using default options.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="path">The file path to write to.</param>
    /// <param name="records">The records to write.</param>
    /// <param name="options">Optional writer configuration; uses <see cref="ExcelWriteOptions.Default"/> when <see langword="null"/>.</param>
    /// <param name="sheetName">Optional worksheet name; defaults to "Sheet1".</param>
    [RequiresUnreferencedCode("Falls back to reflection when no generated writer is registered. Use [GenerateBinder] for AOT/trimming support.")]
    [RequiresDynamicCode("Falls back to reflection when no generated writer is registered. Use [GenerateBinder] for AOT support.")]
    public static void WriteToFile<T>(string path, IEnumerable<T> records, ExcelWriteOptions? options = null, string sheetName = "Sheet1") where T : new()
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(records);

        options ??= ExcelWriteOptions.Default;
        var recordWriter = ExcelRecordWriterFactory.GetWriter<T>(options);

        using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var xlsxWriter = new XlsxWriter(fileStream, leaveOpen: false);
        using var sheetWriter = xlsxWriter.StartSheet(sheetName);
        recordWriter.WriteRecords(sheetWriter, records, options);
    }

    /// <summary>
    /// Writes records to a stream as an Excel (.xlsx) file using default options.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="records">The records to write.</param>
    /// <param name="options">Optional writer configuration; uses <see cref="ExcelWriteOptions.Default"/> when <see langword="null"/>.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, the stream is not closed after writing.</param>
    /// <param name="sheetName">Optional worksheet name; defaults to "Sheet1".</param>
    [RequiresUnreferencedCode("Falls back to reflection when no generated writer is registered. Use [GenerateBinder] for AOT/trimming support.")]
    [RequiresDynamicCode("Falls back to reflection when no generated writer is registered. Use [GenerateBinder] for AOT support.")]
    public static void WriteToStream<T>(Stream stream, IEnumerable<T> records, ExcelWriteOptions? options = null, bool leaveOpen = true, string sheetName = "Sheet1") where T : new()
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(records);

        options ??= ExcelWriteOptions.Default;
        var recordWriter = ExcelRecordWriterFactory.GetWriter<T>(options);

        using var xlsxWriter = new XlsxWriter(stream, leaveOpen: leaveOpen);
        using var sheetWriter = xlsxWriter.StartSheet(sheetName);
        recordWriter.WriteRecords(sheetWriter, records, options);
    }

    /// <summary>
    /// Serializes records to an in-memory Excel (.xlsx) file and returns the bytes.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="records">The records to serialize.</param>
    /// <param name="options">Optional writer configuration; uses <see cref="ExcelWriteOptions.Default"/> when <see langword="null"/>.</param>
    /// <param name="sheetName">Optional worksheet name; defaults to "Sheet1".</param>
    /// <returns>The .xlsx file content as a byte array.</returns>
    [RequiresUnreferencedCode("Falls back to reflection when no generated writer is registered. Use [GenerateBinder] for AOT/trimming support.")]
    [RequiresDynamicCode("Falls back to reflection when no generated writer is registered. Use [GenerateBinder] for AOT support.")]
    public static byte[] SerializeRecords<T>(IEnumerable<T> records, ExcelWriteOptions? options = null, string sheetName = "Sheet1") where T : new()
    {
        ArgumentNullException.ThrowIfNull(records);

        using var ms = new MemoryStream();
        WriteToStream(ms, records, options, leaveOpen: true, sheetName: sheetName);
        return ms.ToArray();
    }
}
