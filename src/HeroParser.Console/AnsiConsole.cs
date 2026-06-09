using System;

namespace HeroParser.Console;

/// <summary>
/// Providing standard console rendering entry points and AOT-safe markup engines.
/// </summary>
public static class AnsiConsole
{
    static AnsiConsole()
    {
        Terminal.EnableVirtualTerminalProcessing();
    }

    /// <summary>
    /// Writes text to stdout with the default style.
    /// </summary>
    public static void Write(string text) => System.Console.Write(text);

    /// <summary>
    /// Writes text followed by a newline to stdout with the default style.
    /// </summary>
    public static void WriteLine(string text) => System.Console.WriteLine(text);

    /// <summary>
    /// Writes styled text to stdout.
    /// </summary>
    public static void Write(string text, Style style)
    {
        Span<char> charBuf = stackalloc char[4096];
        var buffer = new AnsiBuffer(charBuf);
        buffer.WriteStyled(text.AsSpan(), style);
        buffer.Flush();
    }

    /// <summary>
    /// Writes styled text followed by a newline to stdout.
    /// </summary>
    public static void WriteLine(string text, Style style)
    {
        Span<char> charBuf = stackalloc char[4096];
        var buffer = new AnsiBuffer(charBuf);
        buffer.WriteStyled(text.AsSpan(), style);
        buffer.Write(Environment.NewLine);
        buffer.Flush();
    }

    /// <summary>
    /// Renders markup text (e.g., "[bold red]text[/]") to standard output.
    /// </summary>
    public static void Markup(string markupText)
    {
        Span<char> charBuf = stackalloc char[4096];
        var buffer = new AnsiBuffer(charBuf);
        Markup(markupText.AsSpan(), ref buffer);
        buffer.Flush();
    }

    /// <summary>
    /// Renders markup text followed by a newline to standard output.
    /// </summary>
    public static void MarkupLine(string markupText)
    {
        Span<char> charBuf = stackalloc char[4096];
        var buffer = new AnsiBuffer(charBuf);
        Markup(markupText.AsSpan(), ref buffer);
        buffer.Write(Environment.NewLine);
        buffer.Flush();
    }

    /// <summary>
    /// Parses and renders markup text into an existing ANSI buffer.
    /// </summary>
    public static void Markup(ReadOnlySpan<char> markupText, ref AnsiBuffer buffer)
    {
        Span<Style> styleStack = stackalloc Style[16];
        int stackPtr = 0;
        styleStack[0] = Style.Default;

        int index = 0;
        while (index < markupText.Length)
        {
            int nextOpen = markupText[index..].IndexOf('[');
            if (nextOpen == -1)
            {
                buffer.WriteStyled(markupText[index..], styleStack[stackPtr]);
                break;
            }

            if (nextOpen > 0)
            {
                buffer.WriteStyled(markupText.Slice(index, nextOpen), styleStack[stackPtr]);
                index += nextOpen;
            }

            int nextClose = markupText[index..].IndexOf(']');
            if (nextClose == -1)
            {
                buffer.WriteStyled(markupText[index..], styleStack[stackPtr]);
                break;
            }

            ReadOnlySpan<char> tag = markupText.Slice(index + 1, nextClose - 1);
            if (tag.SequenceEqual("/"))
            {
                if (stackPtr > 0)
                {
                    stackPtr--;
                }
            }
            else
            {
                Style newStyle = ParseStyle(tag);
                Style parent = styleStack[stackPtr];

                Color fore = newStyle.Foreground.IsDefault ? parent.Foreground : newStyle.Foreground;
                Color back = newStyle.Background.IsDefault ? parent.Background : newStyle.Background;
                Decoration dec = parent.Decorations | newStyle.Decorations;

                if (stackPtr < styleStack.Length - 1)
                {
                    stackPtr++;
                    styleStack[stackPtr] = new Style(fore, back, dec);
                }
            }

            index += nextClose + 1;
        }
    }

    private static Style ParseStyle(ReadOnlySpan<char> tag)
    {
        if (tag.SequenceEqual("/"))
        {
            return Style.Default;
        }

        Color foreground = Color.Default;
        Color background = Color.Default;
        Decoration decorations = Decoration.None;

        int start = 0;
        bool isBackground = false;

        while (start < tag.Length)
        {
            int nextSpace = tag[start..].IndexOf(' ');
            int tokenLen = nextSpace == -1 ? tag.Length - start : nextSpace;
            ReadOnlySpan<char> token = tag.Slice(start, tokenLen);

            if (token.Equals("on", StringComparison.OrdinalIgnoreCase))
            {
                isBackground = true;
            }
            else if (TryParseColor(token, out var color))
            {
                if (isBackground)
                {
                    background = color;
                }
                else
                {
                    foreground = color;
                }
            }
            else if (TryParseDecoration(token, out var dec))
            {
                decorations |= dec;
            }

            start += tokenLen + 1;
        }

        return new Style(foreground, background, decorations);
    }

    private static bool TryParseColor(ReadOnlySpan<char> token, out Color color)
    {
        if (token.Equals("black", StringComparison.OrdinalIgnoreCase)) { color = Color.Black; return true; }
        if (token.Equals("red", StringComparison.OrdinalIgnoreCase)) { color = Color.Red; return true; }
        if (token.Equals("green", StringComparison.OrdinalIgnoreCase)) { color = Color.Green; return true; }
        if (token.Equals("yellow", StringComparison.OrdinalIgnoreCase)) { color = Color.Yellow; return true; }
        if (token.Equals("blue", StringComparison.OrdinalIgnoreCase)) { color = Color.Blue; return true; }
        if (token.Equals("magenta", StringComparison.OrdinalIgnoreCase)) { color = Color.Magenta; return true; }
        if (token.Equals("cyan", StringComparison.OrdinalIgnoreCase)) { color = Color.Cyan; return true; }
        if (token.Equals("white", StringComparison.OrdinalIgnoreCase)) { color = Color.White; return true; }
        if (token.Equals("grey", StringComparison.OrdinalIgnoreCase) || token.Equals("gray", StringComparison.OrdinalIgnoreCase)) { color = Color.Gray; return true; }

        if (token.Equals("darkred", StringComparison.OrdinalIgnoreCase)) { color = Color.DarkRed; return true; }
        if (token.Equals("darkgreen", StringComparison.OrdinalIgnoreCase)) { color = Color.DarkGreen; return true; }
        if (token.Equals("darkyellow", StringComparison.OrdinalIgnoreCase)) { color = Color.DarkYellow; return true; }
        if (token.Equals("darkblue", StringComparison.OrdinalIgnoreCase)) { color = Color.DarkBlue; return true; }
        if (token.Equals("darkmagenta", StringComparison.OrdinalIgnoreCase)) { color = Color.DarkMagenta; return true; }
        if (token.Equals("darkcyan", StringComparison.OrdinalIgnoreCase)) { color = Color.DarkCyan; return true; }
        if (token.Equals("darkgray", StringComparison.OrdinalIgnoreCase)) { color = Color.DarkGray; return true; }

        color = Color.Default;
        return false;
    }

    private static bool TryParseDecoration(ReadOnlySpan<char> token, out Decoration decoration)
    {
        if (token.Equals("bold", StringComparison.OrdinalIgnoreCase)) { decoration = Decoration.Bold; return true; }
        if (token.Equals("dim", StringComparison.OrdinalIgnoreCase)) { decoration = Decoration.Dim; return true; }
        if (token.Equals("italic", StringComparison.OrdinalIgnoreCase)) { decoration = Decoration.Italic; return true; }
        if (token.Equals("underline", StringComparison.OrdinalIgnoreCase)) { decoration = Decoration.Underline; return true; }
        if (token.Equals("strikethrough", StringComparison.OrdinalIgnoreCase)) { decoration = Decoration.Strikethrough; return true; }

        decoration = Decoration.None;
        return false;
    }

    /// <summary>
    /// Renders a widget directly to the standard output.
    /// </summary>
    public static void Write(Widgets.IConsoleWidget widget)
    {
        Span<char> charBuf = stackalloc char[16384];
        var buffer = new AnsiBuffer(charBuf);
        int width = 80;
        try
        {
            width = System.Console.WindowWidth;
            if (width <= 0) width = 80;
        }
        catch
        {
            width = 80;
        }
        widget.Render(ref buffer, width);
        buffer.Flush();
    }

    /// <summary>
    /// Prompts the user with a selection menu.
    /// </summary>
    public static T Prompt<T>(Prompts.SelectionPrompt<T> prompt) where T : notnull => prompt.Show();

    /// <summary>
    /// Prompts the user with a text input field.
    /// </summary>
    public static T Prompt<T>(Prompts.TextPrompt<T> prompt) => prompt.Show();

    /// <summary>
    /// Creates a status runner for background spinner animations.
    /// </summary>
    public static StatusRunner Status() => new();

    /// <summary>
    /// Creates a progress runner for live progress bars.
    /// </summary>
    public static ProgressRunner Progress() => new();
}
