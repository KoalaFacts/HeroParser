namespace HeroParser;

/// <summary>
/// Provides factory methods for creating high-performance CSV readers and writers backed by SIMD parsing.
/// </summary>
/// <remarks>
/// The returned readers stream the source spans without allocating intermediate rows.
/// Call <c>Dispose</c> (or use a <c>using</c> statement) when you are finished to return pooled buffers.
/// </remarks>
public static partial class Csv
{
}
