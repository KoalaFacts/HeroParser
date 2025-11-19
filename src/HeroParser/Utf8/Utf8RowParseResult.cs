namespace HeroParser.Utf8;

/// <summary>
/// Streaming parser result containing the number of columns, row length, and characters consumed.
/// </summary>
internal readonly record struct Utf8RowParseResult(int ColumnCount, int RowLength, int CharsConsumed);
