using HeroParser;

namespace CsvPipeABModels;

/// <summary>The identical generated record model compiled against each parser assembly.</summary>
[GenerateBinder]
public sealed class PipeRecord
{
    /// <summary>Gets or sets the row identifier.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the row name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the integer amount.</summary>
    public int Amount { get; set; }

    /// <summary>Gets or sets the free-text note.</summary>
    public string Note { get; set; } = string.Empty;
}
