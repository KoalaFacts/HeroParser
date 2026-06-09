using System;

namespace HeroParser.Console;

/// <summary>
/// A high-performance, ref-structured buffer that batches console character writes and flushes them to stdout.
/// </summary>
public ref struct AnsiBuffer
{
    private Span<char> buffer;
    private readonly TextWriter? writer;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnsiBuffer"/> ref struct with a destination span.
    /// </summary>
    public AnsiBuffer(Span<char> buffer, TextWriter? writer = null)
    {
        this.buffer = buffer;
        this.writer = writer;
        Position = 0;
    }

    /// <summary>
    /// Gets the length of the written contents in the buffer.
    /// </summary>
    public int Position { get; private set; }

    /// <summary>
    /// Gets the remaining capacity of the buffer.
    /// </summary>
    public readonly int FreeCapacity => buffer.Length - Position;

    /// <summary>
    /// Writes a single character to the buffer, flushing if full.
    /// </summary>
    public void Write(char c)
    {
        if (Position >= buffer.Length)
        {
            Flush();
        }
        buffer[Position++] = c;
    }

    /// <summary>
    /// Writes a string or span of characters to the buffer, flushing as needed.
    /// </summary>
    public void Write(scoped ReadOnlySpan<char> text)
    {
        if (text.IsEmpty) return;

        int written = 0;
        while (written < text.Length)
        {
            int remainingInText = text.Length - written;
            int remainingInBuf = buffer.Length - Position;

            if (remainingInBuf == 0)
            {
                Flush();
                remainingInBuf = buffer.Length;
            }

            int toCopy = Math.Min(remainingInText, remainingInBuf);
            text.Slice(written, toCopy).CopyTo(buffer[Position..]);
            Position += toCopy;
            written += toCopy;
        }
    }

    /// <summary>
    /// Writes a string literal directly.
    /// </summary>
    public void Write(string text) => Write(text.AsSpan());

    /// <summary>
    /// Writes a raw ANSI escape code sequence parameter (e.g. "1;31" for bold red).
    /// </summary>
    public void WriteAnsiParameter(scoped ReadOnlySpan<char> parameter)
    {
        Write("\x1b[");
        Write(parameter);
        Write('m');
    }

    /// <summary>
    /// Writes text styled with the specified console style.
    /// </summary>
    public void WriteStyled(scoped ReadOnlySpan<char> text, Style style)
    {
        if (text.IsEmpty) return;

        if (style.IsDefault)
        {
            Write(text);
            return;
        }

        // Format escape parameters: e.g. "1;31;44"
        Span<char> styleParam = stackalloc char[128];
        int written = style.FormatAnsi(styleParam);
        if (written > 0)
        {
            WriteAnsiParameter(styleParam[..written]);
        }

        Write(text);

        // Reset text decoration and color back to default: \x1b[0m
        Write("\x1b[0m");
    }

    /// <summary>
    /// Flushes all buffered characters to the standard console output or custom target.
    /// </summary>
    public void Flush()
    {
        if (Position > 0)
        {
            var target = writer ?? System.Console.Out;
            target.Write(buffer[..Position]);
            Position = 0;
        }
    }
}
