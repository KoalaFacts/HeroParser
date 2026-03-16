using System.Globalization;
using HeroParser.Validation;

namespace HeroParser.Excels.Core;

/// <summary>
/// Configuration options for Excel record reading.
/// </summary>
public sealed record ExcelReadOptions
{
    /// <summary>Gets or sets whether the first data row is a header row. Default is <see langword="true"/>.</summary>
    public bool HasHeaderRow { get; init; } = true;

    /// <summary>Gets or sets whether header matching is case-sensitive. Default is <see langword="false"/>.</summary>
    public bool CaseSensitiveHeaders { get; init; } = false;

    /// <summary>Gets or sets whether missing columns are tolerated. Default is <see langword="false"/>.</summary>
    public bool AllowMissingColumns { get; init; } = false;

    /// <summary>Gets or sets string values that should be treated as null during parsing.</summary>
    public IReadOnlyList<string>? NullValues { get; init; }

    /// <summary>Gets or sets the culture for parsing cell values. Default is <see cref="CultureInfo.InvariantCulture"/>.</summary>
    public CultureInfo Culture { get; init; } = CultureInfo.InvariantCulture;

    /// <summary>Gets or sets the maximum number of data rows to read. Null means no limit.</summary>
    public int? MaxRows { get; init; }

    /// <summary>Gets or sets the number of rows to skip before reading data.</summary>
    public int SkipRows { get; init; }

    /// <summary>Gets or sets the validation mode. Default is <see cref="ValidationMode.Strict"/>.</summary>
    public ValidationMode ValidationMode { get; init; } = ValidationMode.Strict;

    /// <summary>Gets a reusable default instance.</summary>
    public static ExcelReadOptions Default { get; } = new();
}
