namespace HeroParser;

/// <summary>
/// Result of parsing a single CSV row.
/// </summary>
internal readonly record struct RowParseResult(int ColumnCount, int RowLength, int CharsConsumed);
