namespace HeroParser.SeparatedValues;

/// <summary>
/// Configures how HeroParser interprets CSV data.
/// </summary>
/// <remarks>
/// The defaults follow RFC 4180. Use <see cref="Validate"/> to catch invalid configurations before parsing.
/// </remarks>
public sealed record CsvParserOptions
{
    /// <summary>
    /// Gets or sets the field delimiter character (comma by default).
    /// </summary>
    /// <remarks>Delimiters must be ASCII (0-127) for SIMD acceleration.</remarks>
    public char Delimiter { get; init; } = ',';

    /// <summary>
    /// Gets or sets the quote character used to escape delimiters inside a field (double quote by default).
    /// </summary>
    /// <remarks>The value must be ASCII and cannot match <see cref="Delimiter"/>.</remarks>
    public char Quote { get; init; } = '"';

    /// <summary>
    /// Gets or sets the maximum number of columns a row may contain (defaults to 100).
    /// </summary>
    /// <remarks>Exceeding this value raises <see cref="CsvException"/> with <see cref="CsvErrorCode.TooManyColumns"/>.</remarks>
    public int MaxColumns { get; init; } = 100;

    /// <summary>
    /// Gets or sets the maximum number of rows to parse before aborting (defaults to 100,000).
    /// </summary>
    /// <remarks>Helps guard against malformed files with unbounded growth.</remarks>
    public int MaxRows { get; init; } = 100_000;

    /// <summary>
    /// Gets or sets a value indicating whether SIMD acceleration is used when available (enabled by default).
    /// </summary>
    /// <remarks>Disable only for diagnostics or when targeting CPUs that lack the required instructions.</remarks>
    public bool UseSimdIfAvailable { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether newline characters inside quoted fields are allowed.
    /// </summary>
    /// <remarks>
    /// Enable to fully support RFC 4180 newlines-in-quotes at a small performance cost. Disabled by default for speed.
    /// </remarks>
    public bool AllowNewlinesInsideQuotes { get; init; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether the parser treats the quote character specially.
    /// </summary>
    /// <remarks>Disable to maximize speed when you know the data never contains quoted fields.</remarks>
    public bool EnableQuotedFields { get; init; } = true;

    /// <summary>
    /// Gets or sets the comment character used to skip lines (null by default, meaning no comment support).
    /// </summary>
    /// <remarks>
    /// Lines that start with this character (after optional whitespace) will be skipped during parsing.
    /// The value must be ASCII and cannot match <see cref="Delimiter"/> or <see cref="Quote"/>.
    /// </remarks>
    public char? CommentCharacter { get; init; } = null;

    /// <summary>
    /// Gets or sets a value indicating whether whitespace should be trimmed from unquoted field values.
    /// </summary>
    /// <remarks>
    /// When enabled, leading and trailing whitespace is removed from unquoted fields only.
    /// Quoted fields are not affected. To trim content inside quotes, use the UnquoteToString method after parsing.
    /// </remarks>
    public bool TrimFields { get; init; } = false;

    /// <summary>
    /// Gets or sets the maximum length allowed for a single field value.
    /// </summary>
    /// <remarks>
    /// When set to a positive value, fields exceeding this length will cause a <see cref="CsvException"/>
    /// to be thrown with <see cref="CsvErrorCode.ParseError"/>. Set to <see langword="null"/> (the default)
    /// to disable this protection.
    /// </remarks>
    public int? MaxFieldLength { get; init; } = null;

    /// <summary>
    /// Gets or sets the escape character used for escaping special characters inside fields (null by default).
    /// </summary>
    /// <remarks>
    /// When set, the escape character allows escaping delimiters, quotes, and newlines inside fields.
    /// For example, with <c>EscapeCharacter = '\\'</c>, the sequence <c>\"</c> represents a literal quote.
    /// This is common in Excel-style CSV exports. When null (the default), only RFC 4180 doubled quotes are supported.
    /// The escape character must be ASCII and cannot match <see cref="Delimiter"/>, <see cref="Quote"/>, or <see cref="CommentCharacter"/>.
    /// </remarks>
    public char? EscapeCharacter { get; init; } = null;

    /// <summary>
    /// Gets a singleton representing the default configuration.
    /// </summary>
    /// <remarks>
    /// Equivalent to <c>new CsvParserOptions()</c>.
    /// Thread-Safety: This is an immutable singleton and is safe to access from multiple threads.
    /// </remarks>
    public static CsvParserOptions Default { get; } = new();

    /// <summary>
    /// Validates the option set and throws when an invalid value is detected.
    /// </summary>
    /// <exception cref="CsvException">Thrown when any property falls outside the supported range.</exception>
    internal void Validate()
    {
        if (Delimiter > 127)
        {
            throw new CsvException(
                CsvErrorCode.InvalidDelimiter,
                $"Delimiter '{Delimiter}' (U+{(int)Delimiter:X4}) must be ASCII (0-127)");
        }

        if (Quote > 127)
        {
            throw new CsvException(
                CsvErrorCode.InvalidOptions,
                $"Quote '{Quote}' (U+{(int)Quote:X4}) must be ASCII (0-127)");
        }

        if (Delimiter == Quote)
        {
            throw new CsvException(
                CsvErrorCode.InvalidOptions,
                $"Delimiter and Quote cannot be the same character ('{Delimiter}')");
        }

        if (MaxColumns <= 0)
        {
            throw new CsvException(
                CsvErrorCode.InvalidOptions,
                $"MaxColumns must be positive, got {MaxColumns}");
        }

        if (MaxRows <= 0)
        {
            throw new CsvException(
                CsvErrorCode.InvalidOptions,
                $"MaxRows must be positive, got {MaxRows}");
        }

        if (!EnableQuotedFields && AllowNewlinesInsideQuotes)
        {
            throw new CsvException(
                CsvErrorCode.InvalidOptions,
                "AllowNewlinesInsideQuotes requires EnableQuotedFields to be true.");
        }

        if (CommentCharacter.HasValue)
        {
            if (CommentCharacter.Value > 127)
            {
                throw new CsvException(
                    CsvErrorCode.InvalidOptions,
                    $"CommentCharacter '{CommentCharacter.Value}' (U+{(int)CommentCharacter.Value:X4}) must be ASCII (0-127)");
            }

            if (CommentCharacter.Value == Delimiter)
            {
                throw new CsvException(
                    CsvErrorCode.InvalidOptions,
                    $"CommentCharacter and Delimiter cannot be the same character ('{CommentCharacter.Value}')");
            }

            if (CommentCharacter.Value == Quote)
            {
                throw new CsvException(
                    CsvErrorCode.InvalidOptions,
                    $"CommentCharacter and Quote cannot be the same character ('{CommentCharacter.Value}')");
            }
        }

        if (MaxFieldLength.HasValue && MaxFieldLength.Value <= 0)
        {
            throw new CsvException(
                CsvErrorCode.InvalidOptions,
                $"MaxFieldLength must be positive when specified, got {MaxFieldLength.Value}");
        }

        if (EscapeCharacter.HasValue)
        {
            if (EscapeCharacter.Value > 127)
            {
                throw new CsvException(
                    CsvErrorCode.InvalidOptions,
                    $"EscapeCharacter '{EscapeCharacter.Value}' (U+{(int)EscapeCharacter.Value:X4}) must be ASCII (0-127)");
            }

            if (EscapeCharacter.Value == Delimiter)
            {
                throw new CsvException(
                    CsvErrorCode.InvalidOptions,
                    $"EscapeCharacter and Delimiter cannot be the same character ('{EscapeCharacter.Value}')");
            }

            if (EscapeCharacter.Value == Quote)
            {
                throw new CsvException(
                    CsvErrorCode.InvalidOptions,
                    $"EscapeCharacter and Quote cannot be the same character ('{EscapeCharacter.Value}')");
            }

            if (CommentCharacter.HasValue && EscapeCharacter.Value == CommentCharacter.Value)
            {
                throw new CsvException(
                    CsvErrorCode.InvalidOptions,
                    $"EscapeCharacter and CommentCharacter cannot be the same character ('{EscapeCharacter.Value}')");
            }
        }
    }
}
