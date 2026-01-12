using System;
using System.Collections.Generic;
using HeroParser.FixedWidths.Records.Binding;

namespace HeroParser.FixedWidths.Reading.Data;

/// <summary>
/// Helper methods for building fixed-width column definitions.
/// </summary>
public static class FixedWidthDataReaderColumns
{
    /// <summary>
    /// Creates columns from fixed lengths, using sequential start positions.
    /// </summary>
    /// <param name="lengths">Column lengths in characters.</param>
    /// <param name="names">Optional column names, matching the length count.</param>
    public static FixedWidthDataReaderColumn[] FromLengths(
        IReadOnlyList<int> lengths,
        IReadOnlyList<string>? names = null)
    {
        ArgumentNullException.ThrowIfNull(lengths);

        if (lengths.Count == 0)
            return [];

        if (names is { Count: > 0 } && names.Count != lengths.Count)
        {
            throw new ArgumentException(
                "Column names must match the number of lengths when provided.",
                nameof(names));
        }

        var columns = new FixedWidthDataReaderColumn[lengths.Count];
        long start = 0;

        for (int i = 0; i < lengths.Count; i++)
        {
            var length = lengths[i];
            if (length <= 0)
                throw new ArgumentOutOfRangeException(nameof(lengths), length, "Column length must be positive.");

            var end = start + length;
            if (end > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(lengths),
                    "Column definitions exceed the maximum record length.");
            }

            var name = names is { Count: > 0 } ? names[i] : null;

            columns[i] = new FixedWidthDataReaderColumn
            {
                Start = (int)start,
                Length = length,
                Name = name
            };

            start = end;
        }

        return columns;
    }

    /// <summary>
    /// Creates columns from fixed-width attributes, preserving the attribute order.
    /// </summary>
    /// <param name="attributes">Fixed-width column attributes.</param>
    /// <param name="names">Optional column names, matching the attribute count.</param>
    public static FixedWidthDataReaderColumn[] FromAttributes(
        IReadOnlyList<FixedWidthColumnAttribute> attributes,
        IReadOnlyList<string>? names = null)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        if (attributes.Count == 0)
            return [];

        if (names is { Count: > 0 } && names.Count != attributes.Count)
        {
            throw new ArgumentException(
                "Column names must match the number of attributes when provided.",
                nameof(names));
        }

        var columns = new FixedWidthDataReaderColumn[attributes.Count];
        for (int i = 0; i < attributes.Count; i++)
        {
            var attribute = attributes[i]
                ?? throw new ArgumentException("Column attribute entries cannot be null.", nameof(attributes));

            if (attribute.Start < 0)
                throw new ArgumentOutOfRangeException(nameof(attributes), attribute.Start, "Column start must be non-negative.");

            if (attribute.Length <= 0)
                throw new ArgumentOutOfRangeException(nameof(attributes), attribute.Length, "Column length must be positive.");

            var end = (long)attribute.Start + attribute.Length;
            if (end > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(attributes), "Column definitions exceed the maximum record length.");

            var name = names is { Count: > 0 } ? names[i] : null;

            columns[i] = new FixedWidthDataReaderColumn
            {
                Start = attribute.Start,
                Length = attribute.Length,
                Name = name,
                PadChar = attribute.PadChar == '\0' ? null : attribute.PadChar,
                Alignment = attribute.Alignment
            };
        }

        return columns;
    }
}
