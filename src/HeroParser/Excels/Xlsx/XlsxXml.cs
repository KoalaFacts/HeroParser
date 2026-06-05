using System.Xml;

namespace HeroParser.Excels.Xlsx;

/// <summary>
/// Shared XML configuration for .xlsx parsing.
/// </summary>
internal static class XlsxXml
{
    // Caps on parsed XML size to bound memory consumption from crafted .xlsx inputs (zip-bomb defence).
    // 100 million chars ≈ 200 MB UTF-16, which is well above any realistic legitimate sharedStrings/sheet
    // payload but stops a small attacker-controlled zip entry from expanding into multi-GB allocations.
    private const long MAX_CHARACTERS_IN_DOCUMENT = 100_000_000;
    private const long MAX_CHARACTERS_FROM_ENTITIES = 10_000_000;

    /// <summary>
    /// Creates secure <see cref="XmlReaderSettings"/> for parsing .xlsx XML content.
    /// Prohibits DTD processing and disables the XML resolver to prevent XXE attacks,
    /// and caps document/entity character counts to mitigate decompression-bomb DoS.
    /// </summary>
    internal static XmlReaderSettings CreateReaderSettings() => new()
    {
        IgnoreWhitespace = true,
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = MAX_CHARACTERS_IN_DOCUMENT,
        MaxCharactersFromEntities = MAX_CHARACTERS_FROM_ENTITIES
    };

    /// <summary>
    /// Zip-bomb mitigation: rejects entries whose decompressed size or compression ratio exceeds safe limits.
    /// </summary>
    internal static void ValidateEntrySize(System.IO.Compression.ZipArchiveEntry entry)
    {
        const long MAX_ENTRY_DECOMPRESSED_BYTES = 512L * 1024 * 1024; // 512 MB
        const int MAX_COMPRESSION_RATIO = 200;
        const long RATIO_CHECK_MIN_COMPRESSED_BYTES = 1024;

        if (entry.Length > MAX_ENTRY_DECOMPRESSED_BYTES)
        {
            throw new HeroParser.Excels.Core.ExcelException(
                $"Refusing to open .xlsx entry '{entry.FullName}': declared uncompressed size " +
                $"{entry.Length} exceeds limit of {MAX_ENTRY_DECOMPRESSED_BYTES} bytes (possible zip bomb).");
        }

        if (entry.CompressedLength >= RATIO_CHECK_MIN_COMPRESSED_BYTES
            && entry.Length / entry.CompressedLength > MAX_COMPRESSION_RATIO)
        {
            throw new HeroParser.Excels.Core.ExcelException(
                $"Refusing to open .xlsx entry '{entry.FullName}': compression ratio " +
                $"{entry.Length}/{entry.CompressedLength} exceeds {MAX_COMPRESSION_RATIO}:1 (possible zip bomb).");
        }
    }
}
