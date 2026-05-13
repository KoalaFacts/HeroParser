using System.Text;

namespace HeroParser.JsonLines.Reading;

/// <summary>
/// Represents a single raw JSONL line.
/// </summary>
public readonly struct JsonlLine
{
    /// <summary>
    /// Initializes a new <see cref="JsonlLine"/>.
    /// </summary>
    /// <param name="utf8">The UTF-8 bytes of the line (excluding the terminator).</param>
    /// <param name="lineNumber">The 1-based source line number.</param>
    public JsonlLine(ReadOnlyMemory<byte> utf8, long lineNumber)
    {
        Utf8 = utf8;
        LineNumber = lineNumber;
    }

    /// <summary>Gets the UTF-8 encoded bytes of the line (no trailing newline).</summary>
    public ReadOnlyMemory<byte> Utf8 { get; }

    /// <summary>Gets the 1-based source line number.</summary>
    public long LineNumber { get; }

    /// <summary>
    /// Decodes the line as UTF-8 text.
    /// </summary>
    public override string ToString() => Encoding.UTF8.GetString(Utf8.Span);
}
