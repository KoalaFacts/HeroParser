namespace HeroParser;

/// <summary>
/// Provides factory methods for creating high-performance CSV readers and writers backed by SIMD parsing.
/// </summary>
/// <remarks>
/// <para>
/// HeroParser is a zero-dependency, high-performance CSV parsing library designed for modern .NET applications.
/// It leverages SIMD acceleration (AVX2/AVX-512) for optimal throughput while maintaining zero allocations
/// on hot paths through <see cref="Span{T}"/> and <see cref="Memory{T}"/> usage.
/// </para>
/// <para>
/// <strong>Key Features:</strong>
/// <list type="bullet">
///   <item>SIMD-accelerated parsing for maximum performance</item>
///   <item>Zero-allocation hot paths using ref structs and spans</item>
///   <item>Streaming support for large files without memory overhead</item>
///   <item>Source generator support for compile-time record binding</item>
///   <item>RFC 4180 compliance with configurable options</item>
///   <item>Built-in security protections (CSV injection, DoS limits)</item>
/// </list>
/// </para>
/// <para>
/// <strong>Quick Start:</strong>
/// <code>
/// // Read records from CSV text
/// foreach (var record in Csv.Read&lt;MyRecord&gt;().FromText(csvText))
/// {
///     Console.WriteLine(record.Name);
/// }
///
/// // Write records to CSV
/// var csv = Csv.Write&lt;MyRecord&gt;().ToString(records);
/// </code>
/// </para>
/// <para>
/// The returned readers stream the source spans without allocating intermediate rows.
/// Call <c>Dispose</c> (or use a <c>using</c> statement) when you are finished to return pooled buffers.
/// </para>
/// </remarks>
/// <seealso cref="SeparatedValues.Reading.Records.CsvReaderBuilder{T}"/>
/// <seealso cref="SeparatedValues.Writing.CsvWriterBuilder{T}"/>
public static partial class Csv
{
}
