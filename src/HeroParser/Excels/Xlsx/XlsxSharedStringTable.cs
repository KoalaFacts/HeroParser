namespace HeroParser.Excels.Xlsx;

// Mutable shared string table used during .xlsx writing.
// Deduplicates strings so that identical values reference the same index
// in xl/sharedStrings.xml, reducing file size for repeated values.
internal sealed class XlsxSharedStringTable
{
    private readonly Dictionary<string, int> lookup = [];
    private readonly List<string> strings = [];

    // Total number of unique strings collected.
    public int Count => strings.Count;

    // Ordered list of unique strings; index matches the shared-string index used in cell XML.
    public IReadOnlyList<string> Strings => strings;

    // Returns the shared-string index for <paramref name="value"/>, adding it if not present.
    public int GetOrAdd(string value)
    {
        if (lookup.TryGetValue(value, out var index))
            return index;

        index = strings.Count;
        strings.Add(value);
        lookup[value] = index;
        return index;
    }
}
