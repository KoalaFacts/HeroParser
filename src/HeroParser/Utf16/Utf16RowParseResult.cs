namespace HeroParser.Utf16;

/// <summary>
/// Streaming parser result for UTF-16 input.
/// </summary>
internal readonly record struct Utf16RowParseResult(int ColumnCount, int RowLength, int CharsConsumed);
