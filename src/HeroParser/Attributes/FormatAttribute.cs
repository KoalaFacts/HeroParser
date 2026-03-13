namespace HeroParser;

/// <summary>
/// Specifies write-side serialization options. Overrides <see cref="ParseAttribute.Format"/>
/// for the write direction when <see cref="WriteFormat"/> is set.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class FormatAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the format string for writing. Overrides <see cref="ParseAttribute.Format"/>
    /// for write direction. When omitted, <see cref="ParseAttribute.Format"/> is used for both.
    /// </summary>
    public string? WriteFormat { get; init; }

    /// <summary>
    /// When <c>true</c>, this column is excluded from output if <b>all</b> records have
    /// empty values (<see langword="null"/> or <c>""</c>) for it.
    /// </summary>
    /// <remarks>
    /// Requires materializing all records before writing. Not suitable for unbounded streaming.
    /// </remarks>
    public bool ExcludeIfAllEmpty { get; init; }
}
