using HeroParser.FixedWidths.Writing;

namespace HeroParser.FixedWidths.Mapping;

/// <summary>
/// Provides writer templates for writing fixed-width data.
/// This interface decouples the template factory from the <c>class</c> constraint on <see cref="FixedWidthMap{T}"/>,
/// allowing the writer builder to work with maps without requiring the <c>class</c> constraint on its type parameter.
/// </summary>
/// <typeparam name="T">The record type.</typeparam>
public interface IFixedWidthWriteMapSource<T>
{
    /// <summary>
    /// Builds write templates from the configured mappings.
    /// </summary>
    FixedWidthRecordWriter<T>.WriterTemplate[] BuildWriteTemplates();
}
