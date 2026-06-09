using System;

namespace HeroParser.Console;

/// <summary>
/// Specifies text decorations (styles) for console output.
/// </summary>
[Flags]
public enum Decoration : byte
{
    /// <summary>
    /// No text decorations.
    /// </summary>
    None = 0,

    /// <summary>
    /// Bold text.
    /// </summary>
    Bold = 1 << 0,

    /// <summary>
    /// Dim or faint text.
    /// </summary>
    Dim = 1 << 1,

    /// <summary>
    /// Italic text.
    /// </summary>
    Italic = 1 << 2,

    /// <summary>
    /// Underlined text.
    /// </summary>
    Underline = 1 << 3,

    /// <summary>
    /// Blinking text.
    /// </summary>
    Blink = 1 << 4,

    /// <summary>
    /// Inverted (reversed foreground/background) text.
    /// </summary>
    Invert = 1 << 5,

    /// <summary>
    /// Strikethrough text.
    /// </summary>
    Strikethrough = 1 << 6
}

/// <summary>
/// Combines foreground color, background color, and text decorations into a single formatting unit.
/// </summary>
public readonly struct Style : IEquatable<Style>
{
    /// <summary>
    /// Gets the foreground color of the text.
    /// </summary>
    public Color Foreground { get; }

    /// <summary>
    /// Gets the background color of the text.
    /// </summary>
    public Color Background { get; }

    /// <summary>
    /// Gets the text decorations of the text.
    /// </summary>
    public Decoration Decorations { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Style"/> struct.
    /// </summary>
    public Style(Color foreground, Color background = default, Decoration decorations = Decoration.None)
    {
        Foreground = foreground;
        Background = background;
        Decorations = decorations;
    }

    /// <summary>
    /// Gets the default style with inherited terminal colors and no decorations.
    /// </summary>
    public static Style Default => new(Color.Default, Color.Default, Decoration.None);

    /// <summary>
    /// Returns a copy of the style with the specified foreground color.
    /// </summary>
    public Style WithForeground(Color color) => new(color, Background, Decorations);

    /// <summary>
    /// Returns a copy of the style with the specified background color.
    /// </summary>
    public Style WithBackground(Color color) => new(Foreground, color, Decorations);

    /// <summary>
    /// Returns a copy of the style with the specified decoration added.
    /// </summary>
    public Style WithDecoration(Decoration decoration) => new(Foreground, Background, Decorations | decoration);

    /// <summary>
    /// Returns a copy of the style with bold decoration enabled.
    /// </summary>
    public Style WithBold() => WithDecoration(Decoration.Bold);

    /// <summary>
    /// Returns a copy of the style with dim decoration enabled.
    /// </summary>
    public Style WithDim() => WithDecoration(Decoration.Dim);

    /// <summary>
    /// Returns a copy of the style with italic decoration enabled.
    /// </summary>
    public Style WithItalic() => WithDecoration(Decoration.Italic);

    /// <summary>
    /// Returns a copy of the style with underline decoration enabled.
    /// </summary>
    public Style WithUnderline() => WithDecoration(Decoration.Underline);

    /// <summary>
    /// Returns a copy of the style with strikethrough decoration enabled.
    /// </summary>
    public Style WithStrikethrough() => WithDecoration(Decoration.Strikethrough);

    /// <summary>
    /// Checks whether this style represents default formatting.
    /// </summary>
    public bool IsDefault => Foreground.IsDefault && Background.IsDefault && Decorations == Decoration.None;

    /// <summary>
    /// Formats the complete ANSI escape sequence string for this style (excluding prefix/suffix) into the destination.
    /// Returns the number of characters written.
    /// </summary>
    public int FormatAnsi(Span<char> destination)
    {
        if (IsDefault)
        {
            if (destination.Length >= 1)
            {
                destination[0] = '0';
                return 1;
            }
            return 0;
        }

        int written = 0;

        // 1. Write decorations
        if (Decorations != Decoration.None)
        {
            if (HasDecoration(Decoration.Bold)) written = AppendCode(1, destination, written);
            if (HasDecoration(Decoration.Dim)) written = AppendCode(2, destination, written);
            if (HasDecoration(Decoration.Italic)) written = AppendCode(3, destination, written);
            if (HasDecoration(Decoration.Underline)) written = AppendCode(4, destination, written);
            if (HasDecoration(Decoration.Blink)) written = AppendCode(5, destination, written);
            if (HasDecoration(Decoration.Invert)) written = AppendCode(7, destination, written);
            if (HasDecoration(Decoration.Strikethrough)) written = AppendCode(9, destination, written);
        }

        // 2. Write foreground
        if (!Foreground.IsDefault)
        {
            if (written > 0 && written < destination.Length)
            {
                destination[written++] = ';';
            }
            written += Foreground.FormatAnsi(destination[written..], isForeground: true);
        }

        // 3. Write background
        if (!Background.IsDefault)
        {
            if (written > 0 && written < destination.Length)
            {
                destination[written++] = ';';
            }
            written += Background.FormatAnsi(destination[written..], isForeground: false);
        }

        return written;
    }

    private bool HasDecoration(Decoration flag) => (Decorations & flag) == flag;

    private static int AppendCode(int code, Span<char> destination, int written)
    {
        if (written > 0 && written < destination.Length)
        {
            destination[written++] = ';';
        }
        if (written < destination.Length)
        {
            destination[written++] = (char)('0' + code);
        }
        return written;
    }

    /// <summary>
    /// Determines whether the specified object is equal to the current style.
    /// </summary>
    public override bool Equals(object? obj) => obj is Style other && Equals(other);

    /// <summary>
    /// Determines whether the specified style is equal to the current style.
    /// </summary>
    public bool Equals(Style other)
    {
        return Foreground == other.Foreground && Background == other.Background && Decorations == other.Decorations;
    }

    /// <summary>
    /// Serves as the default hash function.
    /// </summary>
    public override int GetHashCode() => HashCode.Combine(Foreground, Background, Decorations);

    /// <summary>
    /// Compares two Style values for equality.
    /// </summary>
    public static bool operator ==(Style left, Style right) => left.Equals(right);

    /// <summary>
    /// Compares two Style values for inequality.
    /// </summary>
    public static bool operator !=(Style left, Style right) => !left.Equals(right);
}
