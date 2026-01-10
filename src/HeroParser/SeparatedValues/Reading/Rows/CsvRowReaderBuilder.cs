using HeroParser.SeparatedValues.Core;

namespace HeroParser.SeparatedValues.Reading.Rows;

/// <summary>
/// Fluent builder for configuring and executing manual row-by-row CSV reading operations.
/// </summary>
public sealed partial class CsvRowReaderBuilder
{
    // Parser options
    private char delimiter = ',';
    private char quote = '"';
    private int maxColumnCount = 100;
    private int maxRowCount = 100_000;
    private bool useSimdIfAvailable = true;
    private bool allowNewlinesInQuotes = false;
    private bool enableQuotedFields = true;
    private char? commentCharacter = null;
    private bool trimFields = false;
    private int? maxFieldSize = null;
    private char? escapeCharacter = null;
    private int? maxRowSize = 512 * 1024;
    private bool trackSourceLineNumbers = false;

    // Row reading options
    private int skipRows = 0;

    internal CsvRowReaderBuilder() { }

    private CsvReadOptions GetOptions() => new()
    {
        Delimiter = delimiter,
        Quote = quote,
        MaxColumnCount = maxColumnCount,
        MaxRowCount = maxRowCount,
        UseSimdIfAvailable = useSimdIfAvailable,
        AllowNewlinesInsideQuotes = allowNewlinesInQuotes,
        EnableQuotedFields = enableQuotedFields,
        CommentCharacter = commentCharacter,
        TrimFields = trimFields,
        MaxFieldSize = maxFieldSize,
        EscapeCharacter = escapeCharacter,
        MaxRowSize = maxRowSize,
        TrackSourceLineNumbers = trackSourceLineNumbers
    };
}

