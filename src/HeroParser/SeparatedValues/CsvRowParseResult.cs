namespace HeroParser.SeparatedValues;

/// <summary>
/// Result of parsing a single CSV row.
/// </summary>
internal readonly record struct CsvRowParseResult(int ColumnCount, int RowLength, int CharsConsumed);
