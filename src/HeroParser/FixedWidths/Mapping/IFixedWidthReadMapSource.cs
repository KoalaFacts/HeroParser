using HeroParser.FixedWidths.Records.Binding;

namespace HeroParser.FixedWidths.Mapping;

/// <summary>
/// Provides a <see cref="FixedWidthRecordDescriptor{T}"/> for reading fixed-width data.
/// This interface decouples the descriptor factory from the <c>class</c> constraint on <see cref="FixedWidthMap{T}"/>,
/// allowing the reader builder to work with maps without requiring the <c>class</c> constraint on its type parameter.
/// </summary>
/// <typeparam name="T">The record type.</typeparam>
public interface IFixedWidthReadMapSource<T> where T : new()
{
    /// <summary>
    /// Builds a read descriptor from the configured mappings.
    /// </summary>
    FixedWidthRecordDescriptor<T> BuildReadDescriptor();
}
