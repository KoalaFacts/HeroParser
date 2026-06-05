namespace HeroParser.FixedWidths.Writing;

/// <summary>
/// Interface for fixed-width writers that write typed records without boxing.
/// </summary>
/// <typeparam name="T">The record type to write.</typeparam>
public interface IFixedWidthSourceWriter<in T>
{
    /// <summary>
    /// Writes a single record to the stream without boxing.
    /// </summary>
    /// <param name="writer">The underlying fixed-width stream writer.</param>
    /// <param name="record">The record instance to write.</param>
    /// <param name="options">The writer options.</param>
    void WriteRecord(FixedWidthStreamWriter writer, T record, FixedWidthWriteOptions options);
}
