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
    bool TryBind(CsvRow<TElement> row, int rowNumber, out object? result);
}

/// <summary>
/// Typed wrapper that adapts an ICsvBinder to IMultiSchemaBinderWrapper.
/// </summary>
internal sealed class MultiSchemaBinderWrapper<TElement, T> : IMultiSchemaBinderWrapper<TElement>
    where TElement : unmanaged, IEquatable<TElement>
    where T : new()
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
    public bool TryBind(CsvRow<TElement> row, int rowNumber, out object? result)
    {
        if (binder.TryBind(row, rowNumber, out var typed))
        {
            result = typed;
            return true;
        }

        result = null;
        return false;
    }
}

/// <summary>
/// Internal binder that dispatches to type-specific binders based on a discriminator column value.
/// </summary>
/// <typeparam name="TElement">The element type: <see cref="char"/> for UTF-16 or <see cref="byte"/> for UTF-8.</typeparam>
internal sealed class CsvMultiSchemaBinder<TElement>
    where TElement : unmanaged, IEquatable<TElement>
{
    /// <summary>
    /// Invalid cached length value used to invalidate sticky binding cache.
    /// Set to 255 which exceeds MAX_PACKED_LENGTH (8) and won't match any valid span.
    /// </summary>
    private const byte INVALID_CACHED_LENGTH = 255;

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

    // Sticky binding: cache last wrapper to skip lookup for consecutive same-type rows
    // This is critical for banking formats where 95%+ rows are detail records
    private IMultiSchemaBinderWrapper<TElement>? lastWrapper;
    private int lastCharCode = -1;

    // For packed key sticky binding, store raw values for faster comparison
    // This avoids creating DiscriminatorKey on cache hit
    private long lastPackedValue;
    private byte lastPackedLength;

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
                    // Sticky binding: reuse last wrapper if same discriminator
                    // Check wrapper first - null check is fast and indicates no cache yet
                    var cached = lastWrapper;
                    if (cached is not null && charCode == lastCharCode)
                    {
                        return TryBindWithWrapper(cached, row, rowNumber);
                    }

                    var wrapper = singleCharLookup[charCode];
                    if (wrapper is not null)
                    {
                        // Cache for next row
                        lastCharCode = charCode;
                        lastWrapper = wrapper;
                        // Invalidate packed cache to prevent false positives with empty discriminators
                        lastPackedLength = INVALID_CACHED_LENGTH;
                        return TryBindWithWrapper(wrapper, row, rowNumber);
                    }
                }
            }
            else
            {
                return HandleUnmatchedRow(rowNumber, "Discriminator column index out of range");
            }
        }

        // Fast path for multi-char discriminators: use TryGetColumnSpan to avoid CsvColumn creation
        if (row.TryGetColumnSpan(resolvedDiscriminatorIndex, out var span))
        {
            // Fast path: packed key lookup
            if (TryBindWithPackedKeySpan(span, row, rowNumber, out var result))
            {
                return result;
            }

            // Fallback to string lookup if needed
            if (stringLookup is not null)
            {
                // Create column only when string lookup is needed
                var discriminatorColumn = row[resolvedDiscriminatorIndex];
                if (TryBindWithStringKey(discriminatorColumn, row, rowNumber, out result))
                {
                    return result;
                }

                // No match found
                return HandleUnmatchedRow(rowNumber, GetDiscriminatorString(discriminatorColumn));
            }

            // No match found - need to create string for error message
            return HandleUnmatchedRow(rowNumber, SpanToString(span));
        }

        return HandleUnmatchedRow(rowNumber, "Discriminator column index out of range");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string SpanToString(ReadOnlySpan<TElement> span)
    {
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
        return string.Empty;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryBindWithPackedKeySpan(ReadOnlySpan<TElement> span, CsvRow<TElement> row, int rowNumber, out object? result)
    {
        // Quick length check first - most cache misses fail here
        if (span.Length > DiscriminatorKey.MAX_PACKED_LENGTH)
        {
            result = null;
            return false;
        }

        // Sticky binding fast path: pack value inline and compare raw values
        // This avoids DiscriminatorKey creation on cache hit
        var cached = lastWrapper;
        if (cached is not null && span.Length == lastPackedLength)
        {
            // Try to match cached packed value directly
            if (TryMatchPackedValue(span, out bool matched) && matched)
            {
                result = TryBindWithWrapper(cached, row, rowNumber);
                return true;
            }
        }

        // Standard path: create key and lookup
        DiscriminatorKey key;
        if (typeof(TElement) == typeof(char))
        {
            var charSpan = System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<TElement, char>(ref Unsafe.AsRef(in span.GetPinnableReference())),
                span.Length);

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
            // Cache raw values for next row's sticky binding
            key.GetRawValues(out lastPackedValue, out lastPackedLength);
            lastWrapper = entry.Wrapper;
            result = TryBindWithWrapper(entry.Wrapper, row, rowNumber);
            return true;
        }

        result = null;
        return false;
    }

    /// <summary>
    /// Attempts to match the span against the cached packed value without creating a DiscriminatorKey.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryMatchPackedValue(ReadOnlySpan<TElement> span, out bool matched)
    {
        long packed = 0;

        if (typeof(TElement) == typeof(char))
        {
            var charSpan = System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<TElement, char>(ref Unsafe.AsRef(in span.GetPinnableReference())),
                span.Length);

            for (int i = 0; i < charSpan.Length; i++)
            {
                char c = charSpan[i];
                if (c > 127)
                {
                    matched = false;
                    return false; // Non-ASCII, can't pack
                }
                if (caseInsensitive && (uint)(c - 'A') <= 'Z' - 'A')
                {
                    c = (char)(c + 32);
                }
                packed |= ((long)c) << (i * 8);
            }
        }
        else if (typeof(TElement) == typeof(byte))
        {
            var byteSpan = System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<TElement, byte>(ref Unsafe.AsRef(in span.GetPinnableReference())),
                span.Length);

            for (int i = 0; i < byteSpan.Length; i++)
            {
                byte b = byteSpan[i];
                if (b > 127)
                {
                    matched = false;
                    return false; // Non-ASCII, can't pack
                }
                if (caseInsensitive && (uint)(b - 'A') <= 'Z' - 'A')
                {
                    b = (byte)(b + 32);
                }
                packed |= ((long)b) << (i * 8);
            }
        }
        else
        {
            matched = false;
            return false;
        }

        matched = packed == lastPackedValue;
        return true;
    }

    private bool TryBindWithStringKey(CsvColumn<TElement> column, CsvRow<TElement> row, int rowNumber, out object? result)
    {
        var discriminatorString = GetDiscriminatorString(column);
        var comparer = caseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        foreach (var kvp in stringLookup!)
        {
            if (comparer.Equals(kvp.Key, discriminatorString))
            {
                result = TryBindWithWrapper(kvp.Value.Wrapper, row, rowNumber);
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
        where T : new()
    {
        return new BinderEntry(
            new MultiSchemaBinderWrapper<TElement, T>(binder),
            typeof(T));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static object? TryBindWithWrapper(IMultiSchemaBinderWrapper<TElement> wrapper, CsvRow<TElement> row, int rowNumber)
    {
        return wrapper.TryBind(row, rowNumber, out var result) ? result : null;
    }
}
