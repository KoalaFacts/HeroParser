namespace HeroParser.SeparatedValues.Records.MultiSchema;

/// <summary>
/// Specifies how to handle rows that don't match any registered discriminator value.
/// </summary>
public enum UnmatchedRowBehavior
{
    /// <summary>
    /// Throw an exception when an unmatched row is encountered (default).
    /// </summary>
    Throw,

    /// <summary>
    /// Skip unmatched rows silently.
    /// </summary>
    Skip,

    /// <summary>
    /// Use the fallback factory to create a record for unmatched rows.
    /// </summary>
    UseFallback
}
