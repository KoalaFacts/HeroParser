using System.Diagnostics.CodeAnalysis;
using HeroParser.SeparatedValues.Reading.Span;

namespace HeroParser.SeparatedValues.Core;

/// <summary>
/// Provides a mapping from column names to column indices for efficient named field access.
/// </summary>
/// <remarks>
/// <para>This class builds a lookup structure from header names, enabling <c>row.GetField("ColumnName", headers)</c> access.</para>
/// <para>For optimal performance:</para>
/// <list type="bullet">
///   <item><description>For ≤16 columns: Uses array-based linear search (faster due to cache locality)</description></item>
///   <item><description>For &gt;16 columns: Uses dictionary-based O(1) lookup</description></item>
/// </list>
/// <para>Case-insensitive by default (matching <see cref="Reading.Records.CsvRecordOptions.CaseSensitiveHeaders"/>).</para>
/// </remarks>
public sealed class CsvHeaderIndex
{
    private const int LINEAR_SEARCH_THRESHOLD = 16;

    private readonly string[] headers;
    private readonly Dictionary<string, int>? headerLookup;
    private readonly StringComparer comparer;

    /// <summary>
    /// Creates a header index from a CSV row (typically the first row).
    /// </summary>
    /// <param name="headerRow">The row containing header names.</param>
    /// <param name="caseSensitive">When false (default), header lookups are case-insensitive.</param>
    public CsvHeaderIndex(CsvCharSpanRow headerRow, bool caseSensitive = false)
        : this(headerRow.ToStringArray(), caseSensitive)
    {
    }

    /// <summary>
    /// Creates a header index from a CSV row (typically the first row).
    /// </summary>
    /// <param name="headerRow">The row containing header names.</param>
    /// <param name="caseSensitive">When false (default), header lookups are case-insensitive.</param>
    public CsvHeaderIndex(CsvByteSpanRow headerRow, bool caseSensitive = false)
        : this(headerRow.ToStringArray(), caseSensitive)
    {
    }

    /// <summary>
    /// Creates a header index from a list of header names.
    /// </summary>
    /// <param name="headers">The header names.</param>
    /// <param name="caseSensitive">When false (default), header lookups are case-insensitive.</param>
    public CsvHeaderIndex(IReadOnlyList<string> headers, bool caseSensitive = false)
    {
        ArgumentNullException.ThrowIfNull(headers);

        comparer = caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

        // Copy headers to internal array
        this.headers = new string[headers.Count];
        for (int i = 0; i < headers.Count; i++)
        {
            this.headers[i] = headers[i];
        }

        // Use dictionary for larger header counts
        if (headers.Count > LINEAR_SEARCH_THRESHOLD)
        {
            headerLookup = new Dictionary<string, int>(headers.Count, comparer);
            for (int i = 0; i < headers.Count; i++)
            {
                // First occurrence wins for duplicate headers
                headerLookup.TryAdd(headers[i], i);
            }
        }
    }

    /// <summary>
    /// Creates a header index from an array of header names.
    /// </summary>
    /// <param name="headers">The header names.</param>
    /// <param name="caseSensitive">When false (default), header lookups are case-insensitive.</param>
    public CsvHeaderIndex(string[] headers, bool caseSensitive = false)
        : this((IReadOnlyList<string>)headers, caseSensitive)
    {
    }

    /// <summary>
    /// Gets the column index for the specified column name.
    /// </summary>
    /// <param name="columnName">The column name to look up.</param>
    /// <returns>The zero-based column index.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the column name is not found.</exception>
    public int this[string columnName]
    {
        get
        {
            if (TryGetIndex(columnName, out int index))
            {
                return index;
            }

            throw new KeyNotFoundException($"Column '{columnName}' not found in header. Available columns: {string.Join(", ", headers)}");
        }
    }

    /// <summary>
    /// Gets the number of headers.
    /// </summary>
    public int Count => headers.Length;

    /// <summary>
    /// Gets the header names.
    /// </summary>
    public IReadOnlyList<string> Headers => headers;

    /// <summary>
    /// Tries to get the column index for the specified column name.
    /// </summary>
    /// <param name="columnName">The column name to look up.</param>
    /// <param name="index">When successful, contains the zero-based column index.</param>
    /// <returns>True if the column was found; otherwise, false.</returns>
    public bool TryGetIndex(string columnName, out int index)
    {
        ArgumentNullException.ThrowIfNull(columnName);

        // Use dictionary for larger header counts
        if (headerLookup is not null)
        {
            return headerLookup.TryGetValue(columnName, out index);
        }

        // Linear search for small header counts (better cache locality)
        for (int i = 0; i < headers.Length; i++)
        {
            if (comparer.Equals(headers[i], columnName))
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    /// <summary>
    /// Checks if a column name exists in the header.
    /// </summary>
    /// <param name="columnName">The column name to check.</param>
    /// <returns>True if the column exists; otherwise, false.</returns>
    public bool Contains(string columnName)
    {
        return TryGetIndex(columnName, out _);
    }
}
