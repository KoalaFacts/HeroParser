using System.Text;

namespace HeroParser.JsonLines.Reading;

/// <summary>
/// Represents a single raw JSONL line.
/// </summary>
/// <remarks>
/// Lines yielded by <see cref="HeroParser.Jsonl.ReadLinesAsync"/> borrow the active pipe/pooled buffer
/// directly and are valid only until the next <c>MoveNextAsync</c> call or enumerator disposal;
/// call <see cref="Clone"/> to retain an independent copy beyond the current iteration.
/// </remarks>
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
    /// Creates an independent copy of this line whose UTF-8 buffer remains valid after the enumerator advances or is disposed.
    /// </summary>
    /// <returns>A detached <see cref="JsonlLine"/> owning its own backing buffer.</returns>
    public JsonlLine Clone()
    {
        if (Utf8.IsEmpty)
        {
            return new JsonlLine(ReadOnlyMemory<byte>.Empty, LineNumber);
        }

        byte[] copy = GC.AllocateUninitializedArray<byte>(Utf8.Length);
        Utf8.Span.CopyTo(copy);
        return new JsonlLine(copy, LineNumber);
    }

    /// <summary>
    /// Decodes the line as UTF-8 text.
    /// </summary>
    public override string ToString() => Encoding.UTF8.GetString(Utf8.Span);
}
