namespace HeroParser.Excels.Xlsx;

// Mutable shared string table used during .xlsx writing.
// Deduplicates strings so that identical values reference the same index
// in xl/sharedStrings.xml, reducing file size for repeated values.
internal sealed class XlsxSharedStringTable
{
    internal const int STREAMING_MAX_ENTRIES = 16_384;
    internal const int STREAMING_MAX_TOTAL_CHARS = 2 * 1024 * 1024;
    internal const int STREAMING_MAX_SINGLE_STRING_CHARS = 32_767;

    private readonly Dictionary<string, int> lookup = [];
    private readonly List<string> strings = [];
    private readonly int maxEntries;
    private readonly long maxTotalChars;
    private readonly int maxSingleStringChars;
    private long totalChars;

    internal XlsxSharedStringTable(
        int maxEntries = int.MaxValue,
        long maxTotalChars = long.MaxValue,
        int maxSingleStringChars = int.MaxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxEntries);
        ArgumentOutOfRangeException.ThrowIfNegative(maxTotalChars);
        ArgumentOutOfRangeException.ThrowIfNegative(maxSingleStringChars);

        this.maxEntries = maxEntries;
        this.maxTotalChars = maxTotalChars;
        this.maxSingleStringChars = maxSingleStringChars;
    }

    // Total number of unique strings collected.
    public int Count => strings.Count;

    // Ordered list of unique strings; index matches the shared-string index used in cell XML.
    public IReadOnlyList<string> Strings => strings;

    // Returns the shared-string index for value, adding it if within configured bounds.
    public bool TryGetOrAdd(string value, out int index)
    {
        if (lookup.TryGetValue(value, out index))
            return true;

        if (!CanCacheLength(value.Length))
        {
            index = -1;
            return false;
        }

        index = strings.Count;
        strings.Add(value);
        lookup[value] = index;
        totalChars += value.Length;
        return true;
    }

    // Returns the shared-string index for value span, adding it if within configured bounds.
    public bool TryGetOrAdd(ReadOnlySpan<char> value, out int index)
    {
#if NET9_0_OR_GREATER
        var alternateLookup = lookup.GetAlternateLookup<ReadOnlySpan<char>>();
        if (alternateLookup.TryGetValue(value, out index))
            return true;

        if (!CanCacheLength(value.Length))
        {
            index = -1;
            return false;
        }

        var str = value.ToString();
        index = strings.Count;
        strings.Add(str);
        alternateLookup[str] = index;
        totalChars += str.Length;
        return true;
#else
        return TryGetOrAdd(value.ToString(), out index);
#endif
    }

    // Returns the shared-string index for value, adding it if not present.
    public int GetOrAdd(string value)
        => TryGetOrAdd(value, out int index)
            ? index
            : throw new InvalidOperationException("Shared string table capacity limit exceeded.");

    // Returns the shared-string index for value span, adding it if not present.
    public int GetOrAdd(ReadOnlySpan<char> value)
        => TryGetOrAdd(value, out int index)
            ? index
            : throw new InvalidOperationException("Shared string table capacity limit exceeded.");

    private bool CanCacheLength(int length)
        => strings.Count < maxEntries
           && length <= maxSingleStringChars
           && length <= maxTotalChars - totalChars;
}
