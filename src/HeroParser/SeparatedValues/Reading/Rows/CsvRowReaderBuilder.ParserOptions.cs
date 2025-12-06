namespace HeroParser.SeparatedValues.Reading.Rows;

public sealed partial class CsvRowReaderBuilder
{
    #region Parser Options

    /// <summary>
    /// Sets the field delimiter character.
    /// </summary>
    /// <param name="delimiter">The delimiter character (must be ASCII).</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvRowReaderBuilder WithDelimiter(char delimiter)
    {
        this.delimiter = delimiter;
        return this;
    }

    /// <summary>
    /// Sets the quote character used for escaping.
    /// </summary>
    /// <param name="quote">The quote character (must be ASCII).</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvRowReaderBuilder WithQuote(char quote)
    {
        this.quote = quote;
        return this;
    }

    /// <summary>
    /// Sets the maximum number of columns allowed per row.
    /// </summary>
    /// <param name="maxColumnCount">The maximum column count.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvRowReaderBuilder WithMaxColumns(int maxColumnCount)
    {
        this.maxColumnCount = maxColumnCount;
        return this;
    }

    /// <summary>
    /// Sets the maximum number of rows to parse.
    /// </summary>
    /// <param name="maxRowCount">The maximum row count.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvRowReaderBuilder WithMaxRows(int maxRowCount)
    {
        this.maxRowCount = maxRowCount;
        return this;
    }

    /// <summary>
    /// Disables SIMD acceleration for parsing.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public CsvRowReaderBuilder DisableSimd()
    {
        useSimdIfAvailable = false;
        return this;
    }

    /// <summary>
    /// Enables newline characters inside quoted fields (RFC 4180).
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public CsvRowReaderBuilder AllowNewlinesInQuotes()
    {
        allowNewlinesInQuotes = true;
        return this;
    }

    /// <summary>
    /// Disables quote handling for maximum speed.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public CsvRowReaderBuilder DisableQuotedFields()
    {
        enableQuotedFields = false;
        return this;
    }

    /// <summary>
    /// Sets the comment character to skip comment lines.
    /// </summary>
    /// <param name="commentChar">The comment character (e.g., '#').</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvRowReaderBuilder WithCommentCharacter(char commentChar)
    {
        commentCharacter = commentChar;
        return this;
    }

    /// <summary>
    /// Enables trimming of whitespace from unquoted fields.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public CsvRowReaderBuilder TrimFields()
    {
        trimFields = true;
        return this;
    }

    /// <summary>
    /// Sets the maximum allowed size for a single field (DoS protection).
    /// </summary>
    /// <param name="maxSize">The maximum field size in characters.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvRowReaderBuilder WithMaxFieldSize(int maxSize)
    {
        maxFieldSize = maxSize;
        return this;
    }

    /// <summary>
    /// Sets the escape character for escaping special characters.
    /// </summary>
    /// <param name="escapeChar">The escape character (e.g., '\\').</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvRowReaderBuilder WithEscapeCharacter(char escapeChar)
    {
        escapeCharacter = escapeChar;
        return this;
    }

    /// <summary>
    /// Sets the maximum row size for streaming readers (DoS protection).
    /// </summary>
    /// <param name="maxSize">The maximum row size in characters, or null to disable.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvRowReaderBuilder WithMaxRowSize(int? maxSize)
    {
        maxRowSize = maxSize;
        return this;
    }

    /// <summary>
    /// Enables tracking of source line numbers for error reporting.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    /// <remarks>
    /// When enabled, each row will track its original line number in the source file,
    /// accounting for multi-line quoted fields. This has a small performance overhead.
    /// </remarks>
    public CsvRowReaderBuilder TrackSourceLineNumbers()
    {
        trackSourceLineNumbers = true;
        return this;
    }

    #endregion

    #region Row Options

    /// <summary>
    /// Skips the specified number of rows before returning data.
    /// </summary>
    /// <param name="rowCount">The number of rows to skip.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <remarks>
    /// This is useful for skipping metadata rows at the beginning of a file.
    /// Note: For record reading with header support, use <see cref="Records.CsvRecordReaderBuilder{T}"/> instead.
    /// </remarks>
    public CsvRowReaderBuilder SkipRows(int rowCount)
    {
        skipRows = rowCount;
        return this;
    }

    #endregion
}
