namespace HeroParser.FixedWidths;

/// <summary>
/// Specifies how data is aligned within a fixed-width field.
/// </summary>
public enum FieldAlignment
{
    /// <summary>
    /// Data is left-aligned with padding on the right (default for text fields).
    /// Trimming removes trailing pad characters.
    /// </summary>
    Left,

    /// <summary>
    /// Data is right-aligned with padding on the left (common for numeric fields).
    /// Trimming removes leading pad characters.
    /// </summary>
    Right,

    /// <summary>
    /// No trimming is performed; the raw field value is used as-is.
    /// </summary>
    None
}
