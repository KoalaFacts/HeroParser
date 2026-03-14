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
                int capacity = uniqueCountAttr is not null && int.TryParse(uniqueCountAttr, out int uc) && uc > 0 ? uc : 0;
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

    private static string ReadStringItem(XmlReader subtree)
    {
        using var reader = subtree;

        // <si> can contain:
        //   <t>plain text</t>          — simple string
        //   <r><t>run1</t></r><r>...   — rich text (concatenate all <t> elements within <r> runs)

        string? simpleText = null;
        List<string>? runs = null;

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
                continue;

            if (reader.LocalName == "t")
            {
                simpleText = reader.IsEmptyElement ? string.Empty : reader.ReadElementContentAsString();
            }
            else if (reader.LocalName == "r")
            {
                var runText = ReadRichTextRun(reader);
                runs ??= [];
                runs.Add(runText);
            }
        }

        if (runs is not null)
            return string.Concat(runs);

        return simpleText ?? string.Empty;
    }

    private static string ReadRichTextRun(XmlReader reader)
    {
        // Navigate within <r> to find <t>
        if (reader.IsEmptyElement)
            return string.Empty;

        int depth = reader.Depth;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                break;

            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "t")
            {
                return reader.IsEmptyElement ? string.Empty : reader.ReadElementContentAsString();
            }
        }

        return string.Empty;
    }
}
