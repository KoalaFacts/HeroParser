using System;

namespace HeroParser.Console;

/// <summary>
/// Providing standard console rendering entry points and AOT-safe markup engines.
/// </summary>
public static class AnsiConsole
{
    /// <summary>
    /// Gets or sets the console every static entry point on this class writes to.
    /// </summary>
    /// <remarks>
    /// Defaults to a <see cref="SystemAnsiConsole"/> over the process console. Assigning
    /// another implementation redirects all console UI, which is how a host embeds this
    /// output somewhere other than a terminal. Setting <see langword="null"/> restores
    /// the default.
    /// </remarks>
    public static IAnsiConsole Current
    {
        get;
        set => field = value ?? new SystemAnsiConsole();
    } = new SystemAnsiConsole();

    /// <summary>
    /// Writes text to the current console with the default style.
    /// </summary>
    public static void Write(string text) => Current.Write(text);

    /// <summary>
    /// Writes text followed by a newline to the current console with the default style.
    /// </summary>
    public static void WriteLine(string text) => Current.WriteLine(text);

    /// <summary>
    /// Writes styled text to the current console.
    /// </summary>
    public static void Write(string text, Style style) => Current.Write(text, style);

    /// <summary>
    /// Writes styled text followed by a newline to the current console.
    /// </summary>
    public static void WriteLine(string text, Style style) => Current.WriteLine(text, style);

    /// <summary>
    /// Renders markup text (e.g., "[bold red]text[/]") to the current console.
    /// </summary>
    public static void Markup(string markupText) => Current.Markup(markupText);

    /// <summary>
    /// Renders markup text followed by a newline to the current console.
    /// </summary>
    public static void MarkupLine(string markupText) => Current.MarkupLine(markupText);

    /// <summary>
    /// Computes the visual length of a markup string, excluding formatting tags.
    /// </summary>
    public static int GetMarkupVisualLength(string markupText) => GetMarkupVisualLength(markupText.AsSpan());

    /// <summary>
    /// Computes the visual length of a markup string, excluding formatting tags.
    /// </summary>
    public static int GetMarkupVisualLength(ReadOnlySpan<char> markupText)
    {
        int length = 0;
        int index = 0;
        while (index < markupText.Length)
        {
            char current = markupText[index];

            if (current == '[')
            {
                // "[[" is an escaped literal bracket, not the start of a tag.
                if (index + 1 < markupText.Length && markupText[index + 1] == '[')
                {
                    length++;
                    index += 2;
                    continue;
                }

                int nextClose = markupText[index..].IndexOf(']');
                if (nextClose == -1)
                {
                    // An unterminated tag is not a tag: the rest of the text is visible.
                    length += markupText.Length - index;
                    break;
                }

                index += nextClose + 1;
                continue;
            }

            if (current == ']' && index + 1 < markupText.Length && markupText[index + 1] == ']')
            {
                length++;
                index += 2;
                continue;
            }

            length++;
            index++;
        }
        return length;
    }

    /// <summary>
    /// Parses and renders markup text into an existing ANSI buffer.
    /// </summary>
    public static void Markup(ReadOnlySpan<char> markupText, ref AnsiBuffer buffer, Style baseStyle = default)
    {
        Span<Style> styleStack = stackalloc Style[16];
        int stackPtr = 0;
        styleStack[0] = baseStyle;

        int index = 0;
        while (index < markupText.Length)
        {
            char current = markupText[index];

            if (current == '[')
            {
                // "[[" is an escaped literal bracket — this is what Markup.Escape emits,
                // so text carrying brackets survives a round trip through the parser.
                if (index + 1 < markupText.Length && markupText[index + 1] == '[')
                {
                    buffer.WriteStyled(markupText.Slice(index, 1), styleStack[stackPtr]);
                    index += 2;
                    continue;
                }

                int nextClose = markupText[index..].IndexOf(']');
                if (nextClose == -1)
                {
                    // An unterminated tag is not a tag: write the rest as visible text.
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
                continue;
            }

            if (current == ']' && index + 1 < markupText.Length && markupText[index + 1] == ']')
            {
                buffer.WriteStyled(markupText.Slice(index, 1), styleStack[stackPtr]);
                index += 2;
                continue;
            }

            // Emit the run of ordinary characters up to the next bracket in one write.
            int runEnd = index;
            while (runEnd < markupText.Length && markupText[runEnd] != '[' && markupText[runEnd] != ']')
            {
                runEnd++;
            }
            if (runEnd == index)
            {
                // A lone ']' with no opening tag is ordinary text.
                runEnd++;
            }

            buffer.WriteStyled(markupText[index..runEnd], styleStack[stackPtr]);
            index = runEnd;
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
    public static void Write(Widgets.IConsoleWidget widget) => Current.Write(widget);

    /// <summary>
    /// Prompts the user with a selection menu.
    /// </summary>
    public static T Prompt<T>(Prompts.SelectionPrompt<T> prompt) where T : notnull => prompt.Show(Current);

    /// <summary>
    /// Prompts the user with a text input field.
    /// </summary>
    public static T Prompt<T>(Prompts.TextPrompt<T> prompt) => prompt.Show(Current);

    /// <summary>
    /// Creates a status runner for background spinner animations.
    /// </summary>
    public static StatusRunner Status() => new(Current);

    /// <summary>
    /// Creates a progress runner for live progress bars.
    /// </summary>
    public static ProgressRunner Progress() => new(Current);
}
