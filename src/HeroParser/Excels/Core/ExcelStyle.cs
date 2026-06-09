namespace HeroParser.Excels.Core;

/// <summary>
/// Specifies the horizontal alignment of text within a cell.
/// </summary>
public enum ExcelHorizontalAlignment
{
    /// <summary>Default alignment based on cell type.</summary>
    General,
    /// <summary>Left-aligned text.</summary>
    Left,
    /// <summary>Centered text.</summary>
    Center,
    /// <summary>Right-aligned text.</summary>
    Right,
    /// <summary>Fill alignment.</summary>
    Fill,
    /// <summary>Justified text.</summary>
    Justify,
    /// <summary>Center continuous alignment.</summary>
    CenterContinuous,
    /// <summary>Distributed alignment.</summary>
    Distributed
}

/// <summary>
/// Specifies the vertical alignment of text within a cell.
/// </summary>
public enum ExcelVerticalAlignment
{
    /// <summary>Bottom-aligned text (default).</summary>
    Bottom,
    /// <summary>Top-aligned text.</summary>
    Top,
    /// <summary>Centered text.</summary>
    Center,
    /// <summary>Justified text.</summary>
    Justify,
    /// <summary>Distributed alignment.</summary>
    Distributed
}

/// <summary>
/// Specifies the border style for cell borders.
/// </summary>
public enum ExcelBorderStyle
{
    /// <summary>No border.</summary>
    None,
    /// <summary>Thin border.</summary>
    Thin,
    /// <summary>Medium border.</summary>
    Medium,
    /// <summary>Dashed border.</summary>
    Dashed,
    /// <summary>Dotted border.</summary>
    Dotted,
    /// <summary>Thick border.</summary>
    Thick,
    /// <summary>Double border.</summary>
    Double,
    /// <summary>Hair border.</summary>
    Hair,
    /// <summary>Medium dashed border.</summary>
    MediumDashed,
    /// <summary>Dash-dot border.</summary>
    DashDot,
    /// <summary>Medium dash-dot border.</summary>
    MediumDashDot,
    /// <summary>Dash-dot-dot border.</summary>
    DashDotDot,
    /// <summary>Medium dash-dot-dot border.</summary>
    MediumDashDotDot,
    /// <summary>Slanted dash-dot border.</summary>
    SlantedDashDot
}

/// <summary>
/// Represents the font configuration for a cell style.
/// </summary>
public record class ExcelFont
{
    /// <summary>Gets the name of the font family.</summary>
    public string? Name { get; init; } = "Calibri";

    /// <summary>Gets the font size in points.</summary>
    public double? Size { get; init; } = 11;

    /// <summary>Gets a value indicating whether the font is bold.</summary>
    public bool Bold { get; init; }

    /// <summary>Gets a value indicating whether the font is italic.</summary>
    public bool Italic { get; init; }

    /// <summary>Gets the font color in hex ARGB or RGB format (e.g., "FF0000").</summary>
    public string? Color { get; init; }

    /// <summary>Sets the font family name.</summary>
    public ExcelFont WithName(string name) => this with { Name = name };

    /// <summary>Sets the font size.</summary>
    public ExcelFont WithSize(double size) => this with { Size = size };

    /// <summary>Enables or disables bold.</summary>
    public ExcelFont WithBold(bool bold = true) => this with { Bold = bold };

    /// <summary>Enables or disables italic.</summary>
    public ExcelFont WithItalic(bool italic = true) => this with { Italic = italic };

    /// <summary>Sets the font color in ARGB/RGB hex format.</summary>
    public ExcelFont WithColor(string colorHex) => this with { Color = colorHex };
}

/// <summary>
/// Represents the fill pattern and background configuration for a cell style.
/// </summary>
public record class ExcelFill
{
    /// <summary>Gets the pattern fill type (e.g., "solid", "none").</summary>
    public string PatternType { get; init; } = "solid";

    /// <summary>Gets the foreground color of the fill in hex ARGB/RGB format.</summary>
    public string? ForegroundColor { get; init; }

    /// <summary>Gets the background color of the fill in hex ARGB/RGB format.</summary>
    public string? BackgroundColor { get; init; }

    /// <summary>Creates a solid color fill.</summary>
    public static ExcelFill Solid(string colorHex) => new() { PatternType = "solid", ForegroundColor = colorHex };

    /// <summary>Sets the pattern type.</summary>
    public ExcelFill WithPatternType(string patternType) => this with { PatternType = patternType };

    /// <summary>Sets the foreground color.</summary>
    public ExcelFill WithForegroundColor(string colorHex) => this with { ForegroundColor = colorHex };

    /// <summary>Sets the background color.</summary>
    public ExcelFill WithBackgroundColor(string colorHex) => this with { BackgroundColor = colorHex };

    /// <summary>Sets the solid fill color.</summary>
    public ExcelFill WithSolidColor(string colorHex) => this with { PatternType = "solid", ForegroundColor = colorHex };
}

/// <summary>
/// Represents a single border side configuration.
/// </summary>
public record class ExcelBorderItem
{
    /// <summary>Gets the style of the border.</summary>
    public ExcelBorderStyle Style { get; init; } = ExcelBorderStyle.None;

    /// <summary>Gets the color of the border in hex ARGB/RGB format.</summary>
    public string? Color { get; init; }
}

/// <summary>
/// Represents the border configuration for a cell style.
/// </summary>
public record class ExcelBorder
{
    /// <summary>Gets the left border.</summary>
    public ExcelBorderItem? Left { get; init; }

    /// <summary>Gets the right border.</summary>
    public ExcelBorderItem? Right { get; init; }

    /// <summary>Gets the top border.</summary>
    public ExcelBorderItem? Top { get; init; }

    /// <summary>Gets the bottom border.</summary>
    public ExcelBorderItem? Bottom { get; init; }

    /// <summary>Sets the left border.</summary>
    public ExcelBorder WithLeft(ExcelBorderItem item) => this with { Left = item };

    /// <summary>Sets the left border style and optional color.</summary>
    public ExcelBorder WithLeft(ExcelBorderStyle style, string? color = null) => this with { Left = new ExcelBorderItem { Style = style, Color = color } };

    /// <summary>Sets the right border.</summary>
    public ExcelBorder WithRight(ExcelBorderItem item) => this with { Right = item };

    /// <summary>Sets the right border style and optional color.</summary>
    public ExcelBorder WithRight(ExcelBorderStyle style, string? color = null) => this with { Right = new ExcelBorderItem { Style = style, Color = color } };

    /// <summary>Sets the top border.</summary>
    public ExcelBorder WithTop(ExcelBorderItem item) => this with { Top = item };

    /// <summary>Sets the top border style and optional color.</summary>
    public ExcelBorder WithTop(ExcelBorderStyle style, string? color = null) => this with { Top = new ExcelBorderItem { Style = style, Color = color } };

    /// <summary>Sets the bottom border.</summary>
    public ExcelBorder WithBottom(ExcelBorderItem item) => this with { Bottom = item };

    /// <summary>Sets the bottom border style and optional color.</summary>
    public ExcelBorder WithBottom(ExcelBorderStyle style, string? color = null) => this with { Bottom = new ExcelBorderItem { Style = style, Color = color } };
}

/// <summary>
/// Represents text alignment configuration within a cell.
/// </summary>
public record class ExcelAlignment
{
    /// <summary>Gets the horizontal alignment.</summary>
    public ExcelHorizontalAlignment? Horizontal { get; init; }

    /// <summary>Gets the vertical alignment.</summary>
    public ExcelVerticalAlignment? Vertical { get; init; }

    /// <summary>Gets a value indicating whether text wrapping is enabled.</summary>
    public bool? WrapText { get; init; }

    /// <summary>Sets the horizontal alignment.</summary>
    public ExcelAlignment WithHorizontal(ExcelHorizontalAlignment horizontal) => this with { Horizontal = horizontal };

    /// <summary>Sets the vertical alignment.</summary>
    public ExcelAlignment WithVertical(ExcelVerticalAlignment vertical) => this with { Vertical = vertical };

    /// <summary>Enables or disables text wrapping.</summary>
    public ExcelAlignment WithWrapText(bool wrapText = true) => this with { WrapText = wrapText };
}

/// <summary>
/// Represents a complete style configuration for a spreadsheet cell.
/// </summary>
public record class ExcelStyle
{
    /// <summary>Gets the font configuration.</summary>
    public ExcelFont? Font { get; init; }

    /// <summary>Gets the fill configuration.</summary>
    public ExcelFill? Fill { get; init; }

    /// <summary>Gets the border configuration.</summary>
    public ExcelBorder? Border { get; init; }

    /// <summary>Gets the alignment configuration.</summary>
    public ExcelAlignment? Alignment { get; init; }

    /// <summary>Gets a custom number format string.</summary>
    public string? NumberFormat { get; init; }

    /// <summary>Creates a new default style builder instance.</summary>
    public static ExcelStyle Create() => new();

    /// <summary>Sets the font configuration.</summary>
    public ExcelStyle WithFont(ExcelFont font) => this with { Font = font };

    /// <summary>Configures the font using a builder delegate.</summary>
    public ExcelStyle WithFont(Func<ExcelFont, ExcelFont> configure) => this with { Font = configure(Font ?? new ExcelFont()) };

    /// <summary>Sets the fill configuration.</summary>
    public ExcelStyle WithFill(ExcelFill fill) => this with { Fill = fill };

    /// <summary>Configures the fill using a builder delegate.</summary>
    public ExcelStyle WithFill(Func<ExcelFill, ExcelFill> configure) => this with { Fill = configure(Fill ?? new ExcelFill()) };

    /// <summary>Sets the border configuration.</summary>
    public ExcelStyle WithBorder(ExcelBorder border) => this with { Border = border };

    /// <summary>Configures the border using a builder delegate.</summary>
    public ExcelStyle WithBorder(Func<ExcelBorder, ExcelBorder> configure) => this with { Border = configure(Border ?? new ExcelBorder()) };

    /// <summary>Sets the alignment configuration.</summary>
    public ExcelStyle WithAlignment(ExcelAlignment alignment) => this with { Alignment = alignment };

    /// <summary>Configures the alignment using a builder delegate.</summary>
    public ExcelStyle WithAlignment(Func<ExcelAlignment, ExcelAlignment> configure) => this with { Alignment = configure(Alignment ?? new ExcelAlignment()) };

    /// <summary>Sets the number format string.</summary>
    public ExcelStyle WithNumberFormat(string format) => this with { NumberFormat = format };
}
