using HeroParser.Excels.Reading;

namespace HeroParser;

public static partial class Excel
{
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
}
