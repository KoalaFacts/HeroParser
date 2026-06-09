using System;

namespace HeroParser.Console;

/// <summary>
/// Represents a terminal color (4-bit standard, 8-bit palette, or 24-bit RGB TrueColor).
/// </summary>
public readonly struct Color : IEquatable<Color>
{
    private readonly byte type; // 0 = Default, 1 = ConsoleColor, 2 = Palette, 3 = RGB
    private readonly byte r;
    private readonly byte g;
    private readonly byte b;

    private Color(byte type, byte r, byte g, byte b)
    {
        this.type = type;
        this.r = r;
        this.g = g;
        this.b = b;
    }

    /// <summary>
    /// The default terminal color (inherited color).
    /// </summary>
    public static Color Default => new(0, 0, 0, 0);

    /// <summary>
    /// Creates a 24-bit RGB TrueColor.
    /// </summary>
    public static Color FromRgb(byte r, byte g, byte b) => new(3, r, g, b);

    /// <summary>
    /// Creates an 8-bit palette color.
    /// </summary>
    public static Color FromPalette(byte index) => new(2, index, 0, 0);

    /// <summary>
    /// Creates a standard 4-bit console color.
    /// </summary>
    public static Color FromConsoleColor(ConsoleColor color) => new(1, (byte)color, 0, 0);

    /// <summary>Gets the standard Black color.</summary>
    public static Color Black => FromConsoleColor(ConsoleColor.Black);
    /// <summary>Gets the standard DarkBlue color.</summary>
    public static Color DarkBlue => FromConsoleColor(ConsoleColor.DarkBlue);
    /// <summary>Gets the standard DarkGreen color.</summary>
    public static Color DarkGreen => FromConsoleColor(ConsoleColor.DarkGreen);
    /// <summary>Gets the standard DarkCyan color.</summary>
    public static Color DarkCyan => FromConsoleColor(ConsoleColor.DarkCyan);
    /// <summary>Gets the standard DarkRed color.</summary>
    public static Color DarkRed => FromConsoleColor(ConsoleColor.DarkRed);
    /// <summary>Gets the standard DarkMagenta color.</summary>
    public static Color DarkMagenta => FromConsoleColor(ConsoleColor.DarkMagenta);
    /// <summary>Gets the standard DarkYellow color.</summary>
    public static Color DarkYellow => FromConsoleColor(ConsoleColor.DarkYellow);
    /// <summary>Gets the standard Gray color.</summary>
    public static Color Gray => FromConsoleColor(ConsoleColor.Gray);
    /// <summary>Gets the standard DarkGray color.</summary>
    public static Color DarkGray => FromConsoleColor(ConsoleColor.DarkGray);
    /// <summary>Gets the standard Blue color.</summary>
    public static Color Blue => FromConsoleColor(ConsoleColor.Blue);
    /// <summary>Gets the standard Green color.</summary>
    public static Color Green => FromConsoleColor(ConsoleColor.Green);
    /// <summary>Gets the standard Cyan color.</summary>
    public static Color Cyan => FromConsoleColor(ConsoleColor.Cyan);
    /// <summary>Gets the standard Red color.</summary>
    public static Color Red => FromConsoleColor(ConsoleColor.Red);
    /// <summary>Gets the standard Magenta color.</summary>
    public static Color Magenta => FromConsoleColor(ConsoleColor.Magenta);
    /// <summary>Gets the standard Yellow color.</summary>
    public static Color Yellow => FromConsoleColor(ConsoleColor.Yellow);
    /// <summary>Gets the standard White color.</summary>
    public static Color White => FromConsoleColor(ConsoleColor.White);
    /// <summary>Gets the standard Aqua color (TrueColor RGB cyan).</summary>
    public static Color Aqua => FromRgb(0, 255, 255);

    /// <summary>
    /// Gets a value indicating whether this is the default terminal color.
    /// </summary>
    public bool IsDefault => type == 0;

    /// <summary>
    /// Formats the ANSI escape sequence parameter for this color into the destination span.
    /// Returns the number of characters written.
    /// </summary>
    public int FormatAnsi(Span<char> destination, bool isForeground)
    {
        if (type == 0)
        {
            // Default: 39 for foreground, 49 for background
            if (destination.Length >= 2)
            {
                destination[0] = isForeground ? '3' : '4';
                destination[1] = '9';
                return 2;
            }
            return 0;
        }

        if (type == 1) // Standard ConsoleColor
        {
            var code = GetConsoleColorAnsiCode((ConsoleColor)r);
            if (!isForeground)
            {
                code += 10;
            }
            return FormatInt(code, destination);
        }

        if (type == 2) // Palette index
        {
            // Format: "38;5;index" or "48;5;index"
            var prefix = isForeground ? "38;5;" : "48;5;";
            if (destination.Length >= prefix.Length + 3) // Safely pre-size
            {
                prefix.AsSpan().CopyTo(destination);
                int written = prefix.Length;
                written += FormatInt(r, destination[written..]);
                return written;
            }
            return 0;
        }

        if (type == 3) // RGB
        {
            // Format: "38;2;r;g;b" or "48;2;r;g;b"
            var prefix = isForeground ? "38;2;" : "48;2;";
            if (destination.Length >= prefix.Length + 15) // Max length for "38;2;255;255;255"
            {
                prefix.AsSpan().CopyTo(destination);
                int written = prefix.Length;
                written += FormatInt(r, destination[written..]);
                destination[written++] = ';';
                written += FormatInt(g, destination[written..]);
                destination[written++] = ';';
                written += FormatInt(b, destination[written..]);
                return written;
            }
            return 0;
        }

        return 0;
    }

    private static int GetConsoleColorAnsiCode(ConsoleColor color) => color switch
    {
        ConsoleColor.Black => 30,
        ConsoleColor.DarkRed => 31,
        ConsoleColor.DarkGreen => 32,
        ConsoleColor.DarkYellow => 33,
        ConsoleColor.DarkBlue => 34,
        ConsoleColor.DarkMagenta => 35,
        ConsoleColor.DarkCyan => 36,
        ConsoleColor.Gray => 37,
        ConsoleColor.DarkGray => 90,
        ConsoleColor.Red => 91,
        ConsoleColor.Green => 92,
        ConsoleColor.Yellow => 93,
        ConsoleColor.Blue => 94,
        ConsoleColor.Magenta => 95,
        ConsoleColor.Cyan => 96,
        ConsoleColor.White => 97,
        _ => 39
    };

    private static int FormatInt(int value, Span<char> destination)
    {
        if (value == 0)
        {
            destination[0] = '0';
            return 1;
        }

        int temp = value;
        int len = 0;
        while (temp > 0)
        {
            len++;
            temp /= 10;
        }

        for (int i = len - 1; i >= 0; i--)
        {
            destination[i] = (char)('0' + (value % 10));
            value /= 10;
        }
        return len;
    }

    /// <summary>
    /// Determines whether the specified object is equal to the current color.
    /// </summary>
    public override bool Equals(object? obj) => obj is Color other && Equals(other);

    /// <summary>
    /// Determines whether the specified color is equal to the current color.
    /// </summary>
    public bool Equals(Color other)
    {
        return type == other.type && r == other.r && g == other.g && b == other.b;
    }

    /// <summary>
    /// Serves as the default hash function.
    /// </summary>
    public override int GetHashCode() => HashCode.Combine(type, r, g, b);

    /// <summary>
    /// Compares two Color values for equality.
    /// </summary>
    public static bool operator ==(Color left, Color right) => left.Equals(right);

    /// <summary>
    /// Compares two Color values for inequality.
    /// </summary>
    public static bool operator !=(Color left, Color right) => !left.Equals(right);
}
