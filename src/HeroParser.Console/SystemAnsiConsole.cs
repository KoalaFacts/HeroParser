using System;
using System.IO;

namespace HeroParser.Console;

/// <summary>
/// The default <see cref="IAnsiConsole"/>, targeting the process's real console.
/// </summary>
/// <remarks>
/// Output can be pointed at any <see cref="TextWriter"/> and input at any
/// <see cref="TextReader"/>; the parameterless constructor uses the process console.
/// Key reads fall back to reading characters from the reader when no real console is
/// attached, so a redirected host still drives selection prompts.
/// </remarks>
public sealed class SystemAnsiConsole : IAnsiConsole
{
    private readonly TextWriter? writer;
    private readonly TextReader? reader;

    /// <summary>
    /// Initializes a console bound to the process's standard output and input.
    /// </summary>
    public SystemAnsiConsole()
    {
        Terminal.EnableVirtualTerminalProcessing();
    }

    /// <summary>
    /// Initializes a console bound to the supplied writer and optional reader.
    /// </summary>
    /// <param name="writer">Destination for all output.</param>
    /// <param name="reader">Source for <see cref="ReadLine"/> and <see cref="ReadKey"/>.</param>
    /// <param name="width">Width reported to widgets.</param>
    public SystemAnsiConsole(TextWriter writer, TextReader? reader = null, int width = 80)
    {
        ArgumentNullException.ThrowIfNull(writer);
        this.writer = writer;
        this.reader = reader;
        FixedWidth = width;
    }

    private int FixedWidth { get; }

    /// <inheritdoc />
    public int Width
    {
        get
        {
            if (writer is not null) return FixedWidth;
            try
            {
                int width = System.Console.WindowWidth;
                return width > 0 ? width : 80;
            }
            catch
            {
                // No console attached (redirected or service host): fall back to a sane default.
                return 80;
            }
        }
    }

    private TextWriter Out => writer ?? System.Console.Out;

    /// <inheritdoc />
    public void Write(string text) => Out.Write(text);

    /// <inheritdoc />
    public void WriteLine(string text) => Out.WriteLine(text);

    /// <inheritdoc />
    public void Write(string text, Style style)
    {
        Span<char> charBuf = stackalloc char[4096];
        var buffer = new AnsiBuffer(charBuf, Out);
        buffer.WriteStyled(text.AsSpan(), style);
        buffer.Flush();
    }

    /// <inheritdoc />
    public void WriteLine(string text, Style style)
    {
        Span<char> charBuf = stackalloc char[4096];
        var buffer = new AnsiBuffer(charBuf, Out);
        buffer.WriteStyled(text.AsSpan(), style);
        buffer.Write(Environment.NewLine);
        buffer.Flush();
    }

    /// <inheritdoc />
    public void Markup(string markupText)
    {
        Span<char> charBuf = stackalloc char[4096];
        var buffer = new AnsiBuffer(charBuf, Out);
        AnsiConsole.Markup(markupText.AsSpan(), ref buffer);
        buffer.Flush();
    }

    /// <inheritdoc />
    public void MarkupLine(string markupText)
    {
        Span<char> charBuf = stackalloc char[4096];
        var buffer = new AnsiBuffer(charBuf, Out);
        AnsiConsole.Markup(markupText.AsSpan(), ref buffer);
        buffer.Write(Environment.NewLine);
        buffer.Flush();
    }

    /// <inheritdoc />
    public void Write(Widgets.IConsoleWidget widget)
    {
        ArgumentNullException.ThrowIfNull(widget);
        Span<char> charBuf = stackalloc char[16384];
        var buffer = new AnsiBuffer(charBuf, Out);
        widget.Render(ref buffer, Width);
        buffer.Flush();
    }

    /// <inheritdoc />
    public ConsoleKeyInfo ReadKey(bool intercept)
    {
        if (reader is null) return System.Console.ReadKey(intercept);

        // A redirected host has no key events, so characters stand in for them. This is
        // what lets a selection prompt be driven without a terminal.
        int read = reader.Read();
        if (read < 0) return new ConsoleKeyInfo('\0', ConsoleKey.Enter, false, false, false);

        char c = (char)read;
        ConsoleKey key = c switch
        {
            '\r' or '\n' => ConsoleKey.Enter,
            ' ' => ConsoleKey.Spacebar,
            '\t' => ConsoleKey.Tab,
            '\x1b' => ConsoleKey.Escape,
            >= 'a' and <= 'z' => ConsoleKey.A + (c - 'a'),
            >= 'A' and <= 'Z' => ConsoleKey.A + (c - 'A'),
            >= '0' and <= '9' => ConsoleKey.D0 + (c - '0'),
            _ => ConsoleKey.NoName,
        };
        return new ConsoleKeyInfo(c, key, false, false, false);
    }

    /// <inheritdoc />
    public string? ReadLine() => reader is null ? System.Console.ReadLine() : reader.ReadLine();
}
