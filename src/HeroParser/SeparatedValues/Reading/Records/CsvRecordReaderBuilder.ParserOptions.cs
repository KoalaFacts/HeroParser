namespace HeroParser.SeparatedValues.Reading.Records;

public sealed partial class CsvRecordReaderBuilder<T>
{
    /// <summary>
    /// Sets the field delimiter character.
    /// </summary>
    /// <param name="delimiter">The delimiter character (must be ASCII).</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvRecordReaderBuilder<T> WithDelimiter(char delimiter)
    {
        this.delimiter = delimiter;
        return this;
    }

    /// <summary>
    /// Sets the quote character used for escaping.
    /// </summary>
    /// <param name="quote">The quote character (must be ASCII).</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvRecordReaderBuilder<T> WithQuote(char quote)
    {
        this.quote = quote;
        return this;
    }

    /// <summary>
    /// Sets the maximum number of columns allowed per row.
    /// </summary>
    /// <param name="maxColumnCount">The maximum column count.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvRecordReaderBuilder<T> WithMaxColumns(int maxColumnCount)
    {
        this.maxColumnCount = maxColumnCount;
        return this;
    }

    /// <summary>
    /// Sets the maximum number of rows to parse.
    /// </summary>
    /// <param name="maxRowCount">The maximum row count.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvRecordReaderBuilder<T> WithMaxRows(int maxRowCount)
    {
        this.maxRowCount = maxRowCount;
        return this;
    }

    /// <summary>
    /// Disables SIMD acceleration for parsing.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public CsvRecordReaderBuilder<T> DisableSimd()
    {
        useSimdIfAvailable = false;
        return this;
    }

    /// <summary>
    /// Enables newline characters inside quoted fields (RFC 4180).
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public CsvRecordReaderBuilder<T> AllowNewlinesInQuotes()
    {
        allowNewlinesInQuotes = true;
        return this;
    }

    /// <summary>
    /// Disables quote handling for maximum speed.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public CsvRecordReaderBuilder<T> DisableQuotedFields()
    {
        enableQuotedFields = false;
        return this;
    }

    /// <summary>
    /// Sets the comment character to skip comment lines.
    /// </summary>
    /// <param name="commentChar">The comment character (e.g., '#').</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvRecordReaderBuilder<T> WithCommentCharacter(char commentChar)
    {
        commentCharacter = commentChar;
        return this;
    }

    /// <summary>
    /// Enables trimming of whitespace from unquoted fields.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public CsvRecordReaderBuilder<T> TrimFields()
    {
        trimFields = true;
        return this;
    }

    /// <summary>
    /// Sets the maximum allowed size for a single field (DoS protection).
    /// </summary>
    /// <param name="maxSize">The maximum field size in characters.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvRecordReaderBuilder<T> WithMaxFieldSize(int maxSize)
    {
        maxFieldSize = maxSize;
        return this;
    }

    /// <summary>
    /// Sets the escape character for escaping special characters.
    /// </summary>
    /// <param name="escapeChar">The escape character (e.g., '\\').</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvRecordReaderBuilder<T> WithEscapeCharacter(char escapeChar)
    {
        escapeCharacter = escapeChar;
        return this;
    }

    /// <summary>
    /// Sets the maximum row size for streaming readers (DoS protection).
    /// </summary>
    /// <param name="maxSize">The maximum row size in characters (UTF-16) or bytes (UTF-8), or null to disable.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvRecordReaderBuilder<T> WithMaxRowSize(int? maxSize)
    {
        maxRowSize = maxSize;
        return this;
    }
}
