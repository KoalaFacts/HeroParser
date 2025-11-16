using System;

namespace HeroParser.Simd;

/// <summary>
/// Interface for SIMD-optimized CSV parsers.
/// Implementations provide hardware-specific optimizations with RFC 4180 quote handling.
/// </summary>
internal interface ISimdParser
{
    /// <summary>
    /// Parse columns from a CSV line with RFC 4180 quote handling.
    /// </summary>
    /// <param name="line">The CSV line to parse</param>
    /// <param name="delimiter">Field delimiter character</param>
    /// <param name="quote">Quote character for RFC 4180 compliance</param>
    /// <param name="columnStarts">Output: starting positions of each column</param>
    /// <param name="columnLengths">Output: lengths of each column</param>
    /// <param name="maxColumns">Maximum allowed columns</param>
    /// <returns>Number of columns parsed</returns>
    int ParseColumns(
        ReadOnlySpan<char> line,
        char delimiter,
        char quote,
        Span<int> columnStarts,
        Span<int> columnLengths,
        int maxColumns);
}
