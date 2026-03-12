using HeroParser.SeparatedValues.Reading.Shared;

namespace HeroParser.SeparatedValues.Mapping;

/// <summary>
/// Provides a <see cref="CsvRecordDescriptor{T}"/> for reading CSV data.
/// This interface decouples the descriptor factory from the <c>class</c> constraint on <see cref="CsvMap{T}"/>,
/// allowing <see cref="Reading.Records.CsvRecordReaderBuilder{T}"/>
/// to work with maps without requiring the <c>class</c> constraint on its type parameter.
/// </summary>
/// <typeparam name="T">The record type.</typeparam>
public interface ICsvReadMapSource<T> where T : new()
{
    /// <summary>
    /// Builds a read descriptor from the configured mappings.
    /// </summary>
    CsvRecordDescriptor<T> BuildReadDescriptor();
}
