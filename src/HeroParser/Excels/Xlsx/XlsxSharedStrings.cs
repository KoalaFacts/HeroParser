using System.Xml;

namespace HeroParser.Excels.Xlsx;

/// <summary>
/// Parses the shared string table (xl/sharedStrings.xml) from an .xlsx file.
/// </summary>
internal sealed class XlsxSharedStrings
{
    private readonly string[] strings;

    private XlsxSharedStrings(string[] strings)
    {
        this.strings = strings;
    }

    /// <summary>Number of shared strings.</summary>
    public int Count => strings.Length;

    /// <summary>Gets the shared string at the specified index.</summary>
    public string this[int index] => strings[index];

    // Cap for the pre-allocation capacity derived from the attacker-controlled `uniqueCount`
    // attribute. Excel's own limit on distinct strings per workbook is well below this; values
    // above the cap let the List grow naturally instead of pre-reserving gigabytes of pointers.
    private const int MAX_PREALLOCATED_CAPACITY = 65_536;

    /// <summary>
    /// Parses the shared string table from the given stream.
    /// </summary>
    public static XlsxSharedStrings Parse(Stream stream)
    {
        using var reader = XmlReader.Create(stream, XlsxXml.CreateReaderSettings());

        // Pre-size from <sst uniqueCount="..."> if available
        List<string>? result = null;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "sst")
            {
                var uniqueCountAttr = reader.GetAttribute("uniqueCount");
                int capacity = 0;
                if (uniqueCountAttr is not null && int.TryParse(uniqueCountAttr, out int uc) && uc > 0)
                {
                    capacity = Math.Min(uc, MAX_PREALLOCATED_CAPACITY);
                }
                result = new List<string>(capacity);
                continue;
            }

            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "si")
            {
                result ??= [];
                result.Add(ReadStringItem(reader.ReadSubtree()));
            }
        }

        result ??= [];

        return new XlsxSharedStrings([.. result]);
    }

    /// <summary>
    /// Creates an empty shared strings instance.
    /// </summary>
    public static XlsxSharedStrings Empty => new([]);

    /// <summary>
    /// Returns the strings as a read-only list.
    /// </summary>
    public IReadOnlyList<string> ToList() => strings;

    private static string ReadStringItem(XmlReader reader)
    {
        if (reader.IsEmptyElement)
            return string.Empty;

        int depth = reader.Depth;
        string? simpleText = null;
        List<string>? runs = null;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                break;

            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "t")
            {
                string text = string.Empty;
                if (!reader.IsEmptyElement)
                {
                    while (reader.Read())
                    {
                        if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "t")
                            break;
                        if (reader.NodeType == XmlNodeType.Text || reader.NodeType == XmlNodeType.SignificantWhitespace)
                        {
                            text += reader.Value;
                        }
                    }
                }

                if (simpleText is null)
                {
                    simpleText = text;
                }
                else
                {
                    runs ??= [simpleText];
                    runs.Add(text);
                }
            }
        }

        if (runs is not null)
            return string.Concat(runs);

        return simpleText ?? string.Empty;
    }
}
