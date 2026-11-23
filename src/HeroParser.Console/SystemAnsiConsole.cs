using System;
using System.IO;
using System.Runtime.InteropServices;

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
        EnableVirtualTerminalProcessing();
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
        // Live progress/status rendering runs alongside command output. Match
        // System.Console.Out's synchronization for redirected writers as well.
        this.writer = TextWriter.Synchronized(writer);
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
            catch (IOException)
            {
                // No terminal behind stdout (redirected, or a service host): use a sane default.
                return 80;
            }
            catch (PlatformNotSupportedException)
            {
                // Some hosts (single-file/WASI, certain sandboxes) have no window metrics.
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
        if (c == '\x1b' && reader.Peek() == '[')
        {
            // A redirected host sends arrows as CSI sequences rather than key events;
            // without this, ESC would swallow the '[' and the direction character.
            reader.Read();
            int final = reader.Read();
            ConsoleKey arrow = final switch
            {
                'A' => ConsoleKey.UpArrow,
                'B' => ConsoleKey.DownArrow,
                'C' => ConsoleKey.RightArrow,
                'D' => ConsoleKey.LeftArrow,
                'H' => ConsoleKey.Home,
                'F' => ConsoleKey.End,
                _ => ConsoleKey.NoName,
            };
            return new ConsoleKeyInfo('\0', arrow, false, false, false);
        }

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

    private const int STD_OUTPUT_HANDLE = -11;
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    private static void EnableVirtualTerminalProcessing()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                var handle = GetStdHandle(STD_OUTPUT_HANDLE);
                if (handle != IntPtr.Zero && handle != new IntPtr(-1))
                {
                    if (GetConsoleMode(handle, out uint mode))
                    {
                        mode |= ENABLE_VIRTUAL_TERMINAL_PROCESSING;
                        SetConsoleMode(handle, mode);
                    }
                }
            }
            catch
            {
                // Gracefully ignore on failure (e.g. redirected stdout or environment constraints)
            }
        }
    }
}
