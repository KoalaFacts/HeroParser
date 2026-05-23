using System.Diagnostics.CodeAnalysis;
using HeroParser.Excels.Core;
using HeroParser.Excels.Xlsx;

namespace HeroParser.Excels.Writing;

/// <summary>
/// Fluent builder for writing multiple typed sheets to a single Excel (.xlsx) file.
/// </summary>
/// <remarks>
/// Use <see cref="WithSheet{T}"/> to register each sheet, then call a terminal method
/// (<see cref="ToFile"/>, <see cref="ToStream"/>, or <see cref="ToBytes"/>) to produce the output.
/// </remarks>
public sealed class ExcelMultiSheetWriterBuilder
{
    private readonly List<ISheetRegistration> sheets = [];
    private ExcelWriteOptions? options;

    internal ExcelMultiSheetWriterBuilder() { }

    /// <summary>
    /// Sets the write options applied to all sheets.
    /// </summary>
    /// <param name="opts">The options to use.</param>
    /// <returns>This builder for method chaining.</returns>
    public ExcelMultiSheetWriterBuilder WithOptions(ExcelWriteOptions opts)
    {
        ArgumentNullException.ThrowIfNull(opts);
        options = opts;
        return this;
    }

    /// <summary>
    /// Registers a sheet with the given name and records.
    /// Sheets are written in the order they are registered.
    /// </summary>
    /// <typeparam name="T">The record type for this sheet.</typeparam>
    /// <param name="name">The worksheet name.</param>
    /// <param name="records">The records to write to this sheet.</param>
    /// <returns>This builder for method chaining.</returns>
    public ExcelMultiSheetWriterBuilder WithSheet<T>(string name, IEnumerable<T> records) where T : new()
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(records);
        sheets.Add(new SheetRegistration<T>(name, records));
        return this;
    }

    /// <summary>
    /// Writes all registered sheets to an Excel file at the specified path.
    /// </summary>
    /// <param name="path">The file path to write to.</param>
    [RequiresUnreferencedCode("Falls back to reflection when no generated writer is registered. Use [GenerateBinder] for AOT/trimming support.")]
    [RequiresDynamicCode("Falls back to reflection when no generated writer is registered. Use [GenerateBinder] for AOT support.")]
    public void ToFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        ToStream(fileStream, leaveOpen: false);
    }

    /// <summary>
    /// Writes all registered sheets to a stream as an Excel (.xlsx) file.
    /// </summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, the stream is not closed after writing.</param>
    [RequiresUnreferencedCode("Falls back to reflection when no generated writer is registered. Use [GenerateBinder] for AOT/trimming support.")]
    [RequiresDynamicCode("Falls back to reflection when no generated writer is registered. Use [GenerateBinder] for AOT support.")]
    public void ToStream(Stream stream, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var effectiveOptions = options ?? ExcelWriteOptions.Default;

        using var xlsxWriter = new XlsxWriter(stream, leaveOpen: leaveOpen, injectionProtection: effectiveOptions.InjectionProtection);
        foreach (var sheet in sheets)
            sheet.Write(xlsxWriter, effectiveOptions);
    }

    /// <summary>
    /// Writes all registered sheets to an in-memory Excel (.xlsx) file and returns the bytes.
    /// </summary>
    /// <returns>The .xlsx file content as a byte array.</returns>
    [RequiresUnreferencedCode("Falls back to reflection when no generated writer is registered. Use [GenerateBinder] for AOT/trimming support.")]
    [RequiresDynamicCode("Falls back to reflection when no generated writer is registered. Use [GenerateBinder] for AOT support.")]
    public byte[] ToBytes()
    {
        using var ms = new MemoryStream();
        ToStream(ms, leaveOpen: true);
        return ms.ToArray();
    }

    // Internal abstraction so the non-generic builder can hold typed registrations
    private interface ISheetRegistration
    {
        [RequiresUnreferencedCode("Falls back to reflection when no generated writer is registered.")]
        [RequiresDynamicCode("Falls back to reflection when no generated writer is registered.")]
        void Write(XlsxWriter xlsxWriter, ExcelWriteOptions options);
    }

    private sealed class SheetRegistration<T>(string name, IEnumerable<T> records) : ISheetRegistration
        where T : new()
    {
        [RequiresUnreferencedCode("Falls back to reflection when no generated writer is registered.")]
        [RequiresDynamicCode("Falls back to reflection when no generated writer is registered.")]
        public void Write(XlsxWriter xlsxWriter, ExcelWriteOptions options)
        {
            var writer = ExcelRecordWriterFactory.GetWriter<T>(options);
            using var sheetWriter = xlsxWriter.StartSheet(name);
            writer.WriteRecords(sheetWriter, records, options);
        }
    }
}
