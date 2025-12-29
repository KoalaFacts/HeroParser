using System.Runtime.CompilerServices;
using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Binders;
using HeroParser.SeparatedValues.Reading.Rows;

namespace HeroParser.SeparatedValues.Reading.Records.MultiSchema;

/// <summary>
/// Internal non-generic binder wrapper interface for multi-schema dispatch.
/// </summary>
internal interface IMultiSchemaBinderWrapper<TElement>
    where TElement : unmanaged, IEquatable<TElement>
{
    bool NeedsHeaderResolution { get; }
    void BindHeader(CsvRow<TElement> headerRow, int rowNumber);
    object? Bind(CsvRow<TElement> row, int rowNumber);
}

/// <summary>
/// Typed wrapper that adapts an ICsvBinder to IMultiSchemaBinderWrapper.
/// </summary>
internal sealed class MultiSchemaBinderWrapper<TElement, T> : IMultiSchemaBinderWrapper<TElement>
    where TElement : unmanaged, IEquatable<TElement>
    where T : class, new()
{
    private readonly ICsvBinder<TElement, T> binder;

    public MultiSchemaBinderWrapper(ICsvBinder<TElement, T> binder)
    {
        this.binder = binder;
    }

    public bool NeedsHeaderResolution
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => binder.NeedsHeaderResolution;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void BindHeader(CsvRow<TElement> headerRow, int rowNumber)
        => binder.BindHeader(headerRow, rowNumber);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object? Bind(CsvRow<TElement> row, int rowNumber)
        => binder.Bind(row, rowNumber);
}

/// <summary>
/// Internal binder that dispatches to type-specific binders based on a discriminator column value.
/// </summary>
/// <typeparam name="TElement">The element type: <see cref="char"/> for UTF-16 or <see cref="byte"/> for UTF-8.</typeparam>
internal sealed class CsvMultiSchemaBinder<TElement>
    where TElement : unmanaged, IEquatable<TElement>
{
    private readonly string? discriminatorColumnName;
    private readonly bool caseInsensitive;
    private readonly UnmatchedRowBehavior unmatchedBehavior;

    // Ultra-fast path: single ASCII char lookup table (128 entries for 0x00-0x7F)
    // Null entry means no match for that character
    // Stores wrapper directly to avoid nullable struct overhead in hot path
    private readonly IMultiSchemaBinderWrapper<TElement>?[]? singleCharLookup;

    // Fast path: packed key lookup (zero allocation for <=8 ASCII chars)
    private readonly Dictionary<DiscriminatorKey, BinderEntry> packedLookup;

    // Fallback: string lookup (allocates per-row for long discriminators)
    private readonly Dictionary<string, BinderEntry>? stringLookup;

    // Resolved state
    private int resolvedDiscriminatorIndex = -1;
    private bool headerResolved;
    private bool allBindersResolved;

    /// <summary>
    /// Represents a registered binder with its wrapper.
    /// </summary>
    internal readonly struct BinderEntry
    {
        public readonly IMultiSchemaBinderWrapper<TElement> Wrapper;
        public readonly Type RecordType;

        public BinderEntry(IMultiSchemaBinderWrapper<TElement> wrapper, Type recordType)
        {
            Wrapper = wrapper;
            RecordType = recordType;
        }
    }

    internal CsvMultiSchemaBinder(
        int? discriminatorIndex,
        string? discriminatorColumnName,
        bool caseInsensitive,
        UnmatchedRowBehavior unmatchedBehavior,
        Dictionary<DiscriminatorKey, BinderEntry> packedLookup,
        Dictionary<string, BinderEntry>? stringLookup)
    {
        this.discriminatorColumnName = discriminatorColumnName;
        this.caseInsensitive = caseInsensitive;
        this.unmatchedBehavior = unmatchedBehavior;
        this.packedLookup = packedLookup;
        this.stringLookup = stringLookup;

        // Build ultra-fast single-char lookup table if all keys are single ASCII chars
        // This is the common case for banking formats (H, D, T, etc.)
        singleCharLookup = BuildSingleCharLookup(packedLookup, caseInsensitive);

        // If discriminator index is provided, we don't need header resolution for the discriminator
        // But individual binders may still need header resolution
        if (discriminatorIndex.HasValue)
        {
            resolvedDiscriminatorIndex = discriminatorIndex.Value;
            headerResolved = true;
        }

        // Check if any binders need header resolution
        allBindersResolved = true;
        foreach (var entry in packedLookup.Values)
        {
            if (entry.Wrapper.NeedsHeaderResolution)
            {
                allBindersResolved = false;
                break;
            }
        }
        if (allBindersResolved && stringLookup is not null)
        {
            foreach (var entry in stringLookup.Values)
            {
                if (entry.Wrapper.NeedsHeaderResolution)
                {
                    allBindersResolved = false;
                    break;
                }
            }
        }
    }

    private static IMultiSchemaBinderWrapper<TElement>?[]? BuildSingleCharLookup(
        Dictionary<DiscriminatorKey, BinderEntry> packedLookup,
        bool caseInsensitive)
    {
        // Check if all keys are single-character
        bool allSingleChar = true;
        foreach (var key in packedLookup.Keys)
        {
            if (key.Length != 1)
            {
                allSingleChar = false;
                break;
            }
        }

        if (!allSingleChar || packedLookup.Count == 0)
            return null;

        // Build 128-entry lookup table for ASCII characters
        // Store wrappers directly to avoid nullable struct overhead in hot path
        var lookup = new IMultiSchemaBinderWrapper<TElement>?[128];
        foreach (var (key, entry) in packedLookup)
        {
            int charCode = key.ToString()[0];
            if (charCode < 128)
            {
                lookup[charCode] = entry.Wrapper;
                // For case-insensitive, also set the opposite case
                if (caseInsensitive)
                {
                    if (charCode >= 'a' && charCode <= 'z')
                        lookup[charCode - 32] = entry.Wrapper; // Set uppercase
                    else if (charCode >= 'A' && charCode <= 'Z')
                        lookup[charCode + 32] = entry.Wrapper; // Set lowercase
                }
            }
        }
        return lookup;
    }

    /// <summary>
    /// Gets whether header resolution is still needed.
    /// </summary>
    public bool NeedsHeaderResolution => !headerResolved || !allBindersResolved;

    /// <summary>
    /// Processes the header row to resolve discriminator column and binder column indices.
    /// </summary>
    public void BindHeader(CsvRow<TElement> headerRow, int rowNumber)
    {
        // Resolve discriminator column index if using header name
        if (!headerResolved && discriminatorColumnName is not null)
        {
            resolvedDiscriminatorIndex = FindColumnIndex(headerRow, discriminatorColumnName);
            if (resolvedDiscriminatorIndex < 0)
            {
                throw new CsvException(
                    CsvErrorCode.ParseError,
                    $"Discriminator column '{discriminatorColumnName}' not found in header row.",
                    rowNumber, 0);
            }
            headerResolved = true;
        }

        // Let all binders process the header
        if (!allBindersResolved)
        {
            foreach (var entry in packedLookup.Values)
            {
                if (entry.Wrapper.NeedsHeaderResolution)
                {
                    entry.Wrapper.BindHeader(headerRow, rowNumber);
                }
            }
            if (stringLookup is not null)
            {
                foreach (var entry in stringLookup.Values)
                {
                    if (entry.Wrapper.NeedsHeaderResolution)
                    {
                        entry.Wrapper.BindHeader(headerRow, rowNumber);
                    }
                }
            }
            allBindersResolved = true;
        }
    }

    /// <summary>
    /// Binds a row to a record using the appropriate binder based on the discriminator value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object? Bind(CsvRow<TElement> row, int rowNumber)
    {
        // Ultra-fast path: single ASCII char lookup using specialized accessor
        // This avoids CsvColumn creation and span slicing overhead
        if (singleCharLookup is not null)
        {
            if (row.TryGetColumnFirstChar(resolvedDiscriminatorIndex, out int charCode, out int length))
            {
                if (length == 1 && (uint)charCode < 128)
                {
                    var wrapper = singleCharLookup[charCode];
                    if (wrapper is not null)
                    {
                        return wrapper.Bind(row, rowNumber);
                    }
                }
            }
            else
            {
                return HandleUnmatchedRow(rowNumber, "Discriminator column index out of range");
            }
        }

        // Standard path: get column for multi-char or non-ASCII discriminators
        if ((uint)resolvedDiscriminatorIndex >= (uint)row.ColumnCount)
        {
            return HandleUnmatchedRow(rowNumber, "Discriminator column index out of range");
        }

        var discriminatorColumn = row[resolvedDiscriminatorIndex];

        // Fast path: packed key lookup
        if (TryBindWithPackedKey(discriminatorColumn, row, rowNumber, out var result))
        {
            return result;
        }

        // Fallback to string lookup if needed
        if (stringLookup is not null)
        {
            if (TryBindWithStringKey(discriminatorColumn, row, rowNumber, out result))
            {
                return result;
            }
        }

        // No match found
        return HandleUnmatchedRow(rowNumber, GetDiscriminatorString(discriminatorColumn));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryBindWithPackedKey(CsvColumn<TElement> column, CsvRow<TElement> row, int rowNumber, out object? result)
    {
        var span = column.Span;
        DiscriminatorKey key;

        if (typeof(TElement) == typeof(char))
        {
            var charSpan = System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<TElement, char>(ref Unsafe.AsRef(in span.GetPinnableReference())),
                span.Length);

            // Use lowercase key creation for case-insensitive matching (zero allocation)
            bool created = caseInsensitive
                ? DiscriminatorKey.TryCreateLowercase(charSpan, out key)
                : DiscriminatorKey.TryCreate(charSpan, out key);

            if (!created)
            {
                result = null;
                return false;
            }
        }
        else if (typeof(TElement) == typeof(byte))
        {
            var byteSpan = System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<TElement, byte>(ref Unsafe.AsRef(in span.GetPinnableReference())),
                span.Length);

            // Use lowercase key creation for case-insensitive matching (zero allocation)
            bool created = caseInsensitive
                ? DiscriminatorKey.TryCreateLowercase(byteSpan, out key)
                : DiscriminatorKey.TryCreate(byteSpan, out key);

            if (!created)
            {
                result = null;
                return false;
            }
        }
        else
        {
            result = null;
            return false;
        }

        if (packedLookup.TryGetValue(key, out var entry))
        {
            result = entry.Wrapper.Bind(row, rowNumber);
            return true;
        }

        result = null;
        return false;
    }

    private bool TryBindWithStringKey(CsvColumn<TElement> column, CsvRow<TElement> row, int rowNumber, out object? result)
    {
        var discriminatorString = GetDiscriminatorString(column);
        var comparer = caseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        foreach (var kvp in stringLookup!)
        {
            if (comparer.Equals(kvp.Key, discriminatorString))
            {
                result = kvp.Value.Wrapper.Bind(row, rowNumber);
                return true;
            }
        }

        result = null;
        return false;
    }

    private static string GetDiscriminatorString(CsvColumn<TElement> column)
    {
        var span = column.Span;

        if (typeof(TElement) == typeof(char))
        {
            var charSpan = System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<TElement, char>(ref Unsafe.AsRef(in span.GetPinnableReference())),
                span.Length);
            return new string(charSpan);
        }
        else if (typeof(TElement) == typeof(byte))
        {
            var byteSpan = System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<TElement, byte>(ref Unsafe.AsRef(in span.GetPinnableReference())),
                span.Length);
            return Encoding.UTF8.GetString(byteSpan);
        }
        else
        {
            return string.Empty;
        }
    }

    private object? HandleUnmatchedRow(int rowNumber, string discriminatorValue)
    {
        return unmatchedBehavior switch
        {
            UnmatchedRowBehavior.Skip => null,
            UnmatchedRowBehavior.Throw => throw new CsvException(
                CsvErrorCode.ParseError,
                $"No record type registered for discriminator value '{discriminatorValue}'.",
                rowNumber, resolvedDiscriminatorIndex + 1),
            _ => null
        };
    }

    private int FindColumnIndex(CsvRow<TElement> headerRow, string columnName)
    {
        var comparer = caseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var count = headerRow.ColumnCount;

        for (int i = 0; i < count; i++)
        {
            var headerValue = GetDiscriminatorString(headerRow[i]);
            if (comparer.Equals(headerValue, columnName))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Creates a binder entry for a typed binder.
    /// </summary>
    public static BinderEntry CreateEntry<T>(ICsvBinder<TElement, T> binder)
        where T : class, new()
    {
        return new BinderEntry(
            new MultiSchemaBinderWrapper<TElement, T>(binder),
            typeof(T));
    }
}
