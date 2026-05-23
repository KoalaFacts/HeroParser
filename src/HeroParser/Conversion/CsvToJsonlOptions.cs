using System.Text.Json;

namespace HeroParser.Conversion;

/// <summary>
/// Options for <see cref="CsvToJsonlConverter"/>.
/// </summary>
public sealed record CsvToJsonlOptions
{
    /// <summary>Gets or sets the CSV delimiter (default ',').</summary>
    public char Delimiter { get; init; } = ',';

    /// <summary>Gets or sets a value indicating whether the CSV has a header row (default <see langword="true"/>).</summary>
    public bool HasHeaderRow { get; init; } = true;

    /// <summary>Gets or sets the JSONL line separator (default <c>"\n"</c>).</summary>
    public string NewLine { get; init; } = "\n";

    /// <summary>Gets or sets serializer options for value escaping. Encoder defaults to default web encoder.</summary>
    public JsonSerializerOptions? JsonOptions { get; init; }

    /// <summary>Default options instance.</summary>
    public static CsvToJsonlOptions Default { get; } = new();
}

/// <summary>
/// Options for <see cref="JsonlToCsvConverter"/>.
/// </summary>
public sealed record JsonlToCsvOptions
{
    /// <summary>Gets or sets the CSV delimiter (default ',').</summary>
    public char Delimiter { get; init; } = ',';

    /// <summary>Gets or sets the number of leading lines to peek when inferring CSV columns (default 100).</summary>
    public int SchemaInferencePeekRows { get; init; } = 100;

    /// <summary>Default options instance.</summary>
    public static JsonlToCsvOptions Default { get; } = new();
}
