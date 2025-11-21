namespace HeroParser.SeparatedValues;

/// <summary>
/// Represents CSV text loaded from an I/O source. Use <see cref="CreateReader"/> to enumerate rows.
/// </summary>
public sealed class CsvTextSource
{
    private readonly string text;
    private readonly CsvParserOptions options;

    internal CsvTextSource(string text, CsvParserOptions options)
    {
        this.text = text;
        this.options = options;
    }

    /// <summary>
    /// Creates a span-backed reader over the buffered text.
    /// </summary>
    /// <remarks>Use streaming APIs (<see cref="Csv.ReadFromStream(Stream, HeroParser.SeparatedValues.CsvParserOptions?, System.Text.Encoding?, bool, int)"/>) for large inputs to avoid buffering entire files.</remarks>
    public CsvCharSpanReader CreateReader() => Csv.ReadFromText(text, options);
}
