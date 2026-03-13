using System.Xml;

namespace HeroParser.Excels.Xlsx;

/// <summary>
/// Shared XML configuration for .xlsx parsing.
/// </summary>
internal static class XlsxXml
{
    /// <summary>
    /// Creates secure <see cref="XmlReaderSettings"/> for parsing .xlsx XML content.
    /// Prohibits DTD processing and disables the XML resolver to prevent XXE attacks.
    /// </summary>
    internal static XmlReaderSettings CreateReaderSettings() => new()
    {
        IgnoreWhitespace = true,
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null
    };
}
