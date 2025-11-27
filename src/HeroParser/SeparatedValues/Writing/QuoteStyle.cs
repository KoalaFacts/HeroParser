namespace HeroParser.SeparatedValues.Writing;

/// <summary>
/// Specifies when fields should be quoted in CSV output.
/// </summary>
public enum QuoteStyle
{
    /// <summary>
    /// Quote fields only when they contain special characters (delimiter, quote, newline).
    /// This is the default and produces minimal, RFC 4180 compliant output.
    /// </summary>
    WhenNeeded = 0,

    /// <summary>
    /// Always quote all fields regardless of content.
    /// Use this for maximum compatibility with strict parsers.
    /// </summary>
    Always = 1,

    /// <summary>
    /// Never quote fields. The caller must ensure field values do not contain
    /// delimiters, quotes, or newlines.
    /// </summary>
    /// <remarks>
    /// This mode provides maximum performance but produces invalid CSV if fields
    /// contain special characters. Use only when you control the input data.
    /// </remarks>
    Never = 2
}
