namespace HeroParser.JsonLines.Reading.Data;

/// <summary>
/// Options for <see cref="JsonlDataReader"/>.
/// </summary>
public sealed record JsonlDataReaderOptions
{
    /// <summary>
    /// Gets the explicit column projection. When <see langword="null"/>, schema is inferred from the first line.
    /// </summary>
    public IReadOnlyList<JsonlColumnDefinition>? Columns { get; init; }

    /// <summary>
    /// Gets a value indicating whether schema should be inferred from the first non-empty line
    /// when <see cref="Columns"/> is unset. Defaults to <see langword="true"/>.
    /// </summary>
    public bool InferSchemaFromFirstLine { get; init; } = true;

    /// <summary>
    /// Number of leading records to skip.
    /// </summary>
    public int SkipRows { get; init; }

    /// <summary>Default options instance.</summary>
    public static JsonlDataReaderOptions Default { get; } = new();
}
