namespace HeroParser.SeparatedValues.Reading.Records.MultiSchema;

/// <summary>
/// Specifies how to handle rows that don't match any registered discriminator value.
/// </summary>
public enum UnmatchedRowBehavior
{
    /// <summary>
    /// Skip unmatched rows silently. This is the default behavior.
    /// </summary>
    Skip = 0,

    /// <summary>
    /// Throw a <see cref="Core.CsvException"/> when an unmatched row is encountered.
    /// </summary>
    Throw = 1,

    /// <summary>
    /// Use a custom factory function to create records for unmatched rows.
    /// </summary>
    /// <remarks>
    /// NOTE: This option is not yet implemented and will throw NotSupportedException if used.
    /// Use Skip or Throw instead.
    /// </remarks>
    CustomFactory = 2
}
