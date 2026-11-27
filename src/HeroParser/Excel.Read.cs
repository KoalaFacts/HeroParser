using HeroParser.Excels.Reading;
using HeroParser.Excels.Xlsx;

namespace HeroParser;

public static partial class Excel
{
    /// <summary>
    /// Gets the worksheet names from an Excel (.xlsx) file in workbook order.
    /// </summary>
    /// <param name="path">The path to the .xlsx file.</param>
    /// <returns>The worksheet names in workbook order.</returns>
    public static IReadOnlyList<string> GetSheetNames(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return GetSheetNames(stream);
    }

    /// <summary>
    /// Gets the worksheet names from a stream containing Excel (.xlsx) data in workbook order.
    /// </summary>
    /// <param name="stream">A seekable stream containing .xlsx data. The stream is left open after reading.</param>
    /// <returns>The worksheet names in workbook order.</returns>
    public static IReadOnlyList<string> GetSheetNames(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return XlsxReader.ReadSheetNames(stream);
    }
    /// <summary>
    /// Creates a typed record reader builder for reading Excel rows as <typeparamref name="T"/> records.
    /// </summary>
    /// <typeparam name="T">The record type to deserialize rows into.</typeparam>
    /// <returns>A fluent builder for configuring and executing the read operation.</returns>
    public static ExcelRecordReaderBuilder<T> Read<T>() where T : new()
        => new();

    /// <summary>
    /// Creates a row-level reader builder for reading Excel rows as string arrays.
    /// </summary>
    /// <returns>A fluent builder for configuring and executing row-level reading.</returns>
    public static ExcelRowReaderBuilder Read()
        => new();

    /// <summary>
    /// Reads all rows from the first sheet of an Excel file and deserializes them as <typeparamref name="T"/> records.
    /// </summary>
    /// <typeparam name="T">The record type to deserialize rows into.</typeparam>
    /// <param name="path">The path to the .xlsx file.</param>
    /// <returns>A list of deserialized records.</returns>
    public static List<T> DeserializeRecords<T>(string path) where T : new()
        => Read<T>().FromFile(path);

    /// <summary>
    /// Reads all rows from the first sheet of a stream and deserializes them as <typeparamref name="T"/> records.
    /// </summary>
    /// <typeparam name="T">The record type to deserialize rows into.</typeparam>
    /// <param name="stream">A seekable stream containing .xlsx data. The stream is left open after reading.</param>
    /// <returns>A list of deserialized records.</returns>
    public static List<T> DeserializeRecords<T>(Stream stream) where T : new()
        => Read<T>().FromStream(stream);
}
