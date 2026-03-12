using System.Diagnostics.CodeAnalysis;
using HeroParser.SeparatedValues.Writing;

namespace HeroParser.SeparatedValues.Mapping;

/// <summary>
/// Interface that bridges the constraint gap between <see cref="CsvWriterBuilder{T}"/> (unconstrained)
/// and <see cref="CsvMap{T}"/> (<c>class, new()</c> constrained).
/// Allows <see cref="CsvWriterBuilder{T}"/> to accept a map without requiring the <c>class, new()</c>
/// constraint on its own type parameter.
/// </summary>
/// <typeparam name="T">The record type.</typeparam>
public interface ICsvWriteMapSource<T>
{
    /// <summary>
    /// Builds write templates from the map configuration.
    /// </summary>
    [RequiresUnreferencedCode("Fluent mapping uses reflection. Use [CsvGenerateBinder] attribute for AOT/trimming support.")]
    [RequiresDynamicCode("Fluent mapping uses expression compilation. Use [CsvGenerateBinder] attribute for AOT/trimming support.")]
    CsvRecordWriter<T>.WriterTemplate[] BuildWriteTemplates();
}
