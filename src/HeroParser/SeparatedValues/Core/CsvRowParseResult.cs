namespace HeroParser.SeparatedValues.Core;

/// <summary>
/// Result of parsing a single CSV row.
/// </summary>
/// <param name="ColumnCount">Number of columns parsed in the row.</param>
/// <param name="RowLength">Length of the row in characters/bytes (excluding line endings).</param>
/// <param name="CharsConsumed">Total characters/bytes consumed including line endings.</param>
/// <param name="NewlineCount">Number of newline characters (\n) encountered during parsing (including those inside quoted fields).</param>
internal readonly record struct CsvRowParseResult(int ColumnCount, int RowLength, int CharsConsumed, int NewlineCount);
