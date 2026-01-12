using System;
using System.Collections.Generic;

namespace HeroParser.FixedWidths.Reading.Data;

/// <summary>
/// Configures how <see cref="FixedWidthDataReader"/> exposes fixed-width data via <see cref="System.Data.IDataReader"/>.
/// </summary>
public sealed record FixedWidthDataReaderOptions
{
    /// <summary>
    /// Gets or sets a value that indicates whether the fixed-width data includes a header row.
    /// When <see langword="true"/>, the first parsed row is used for column names.
    /// </summary>
    public bool HasHeaderRow { get; init; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether header name lookup is case-sensitive.
    /// </summary>
    public bool CaseSensitiveHeaders { get; init; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether columns that extend beyond the row length are tolerated.
    /// When <see langword="true"/>, missing columns return <see cref="DBNull.Value"/>.
    /// </summary>
    public bool AllowMissingColumns { get; init; } = false;

    /// <summary>
    /// Gets or sets a list of string values that should be treated as null during parsing.
    /// </summary>
    public IReadOnlyList<string>? NullValues { get; init; } = null;

    /// <summary>
    /// Gets or sets the fixed-width column definitions. At least one column is required.
    /// </summary>
    public IReadOnlyList<FixedWidthDataReaderColumn> Columns { get; init; } = [];

    internal StringComparer HeaderComparer => CaseSensitiveHeaders ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Gets a reusable default instance.
    /// </summary>
    public static FixedWidthDataReaderOptions Default { get; } = new();
}
