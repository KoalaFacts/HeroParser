using System;
using System.Collections.Generic;

namespace HeroParser.SeparatedValues.Reading.Data;

/// <summary>
/// Configures how <see cref="CsvDataReader"/> exposes CSV data via <see cref="System.Data.IDataReader"/>.
/// </summary>
public sealed record CsvDataReaderOptions
{
    /// <summary>
    /// Gets or sets a value that indicates whether the CSV includes a header row.
    /// When <see langword="true"/>, the first parsed row is used for column names.
    /// </summary>
    public bool HasHeaderRow { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether header name lookup is case-sensitive.
    /// </summary>
    public bool CaseSensitiveHeaders { get; init; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether rows with fewer columns are tolerated.
    /// When <see langword="false"/>, missing columns throw <see cref="Core.CsvException"/>.
    /// </summary>
    public bool AllowMissingColumns { get; init; } = false;

    /// <summary>
    /// Gets or sets the number of rows to skip before reading the header or data rows.
    /// </summary>
    public int SkipRows { get; init; } = 0;

    /// <summary>
    /// Gets or sets a list of string values that should be treated as null during parsing.
    /// </summary>
    public IReadOnlyList<string>? NullValues { get; init; } = null;

    /// <summary>
    /// Gets or sets explicit column names to use instead of headers.
    /// </summary>
    /// <remarks>
    /// When provided, these names override header values (if present).
    /// If <see cref="HasHeaderRow"/> is <see langword="false"/>, these names define the schema.
    /// </remarks>
    public IReadOnlyList<string>? ColumnNames { get; init; } = null;

    internal StringComparer HeaderComparer => CaseSensitiveHeaders ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Gets a reusable default instance.
    /// </summary>
    public static CsvDataReaderOptions Default { get; } = new();
}
