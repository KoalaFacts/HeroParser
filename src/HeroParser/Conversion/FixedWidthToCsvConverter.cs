using HeroParser.FixedWidths;
using HeroParser.SeparatedValues.Writing;

namespace HeroParser.Conversion;

/// <summary>
/// Options for fixed-width-to-CSV conversion.
/// </summary>
public sealed record FixedWidthToCsvOptions
{
    /// <summary>
    /// Gets or sets the CSV delimiter character (default: comma).
    /// </summary>
    public char Delimiter { get; init; } = ',';

    /// <summary>
    /// Gets or sets the CSV quote character (default: double quote).
    /// </summary>
    public char Quote { get; init; } = '"';

    /// <summary>
    /// Gets or sets whether to include a CSV header row (default: true).
    /// </summary>
    public bool IncludeHeader { get; init; } = true;

    /// <summary>
    /// Gets or sets the newline sequence for output (default: CRLF).
    /// </summary>
    public string NewLine { get; init; } = "\r\n";

    /// <summary>
    /// Gets the default options.
    /// </summary>
    public static FixedWidthToCsvOptions Default { get; } = new();
}

/// <summary>
/// Converts fixed-width data to CSV format.
/// </summary>
/// <remarks>
/// <para>
/// Splits each fixed-width row into fields based on the column definitions,
/// trims padding characters based on alignment, and writes CSV output with
/// proper quoting when field values contain delimiters or quotes.
/// </para>
/// <para>
/// Thread-Safety: All methods are thread-safe as they operate on local state only.
/// </para>
/// </remarks>
public static class FixedWidthToCsvConverter
{
    /// <summary>
    /// Converts fixed-width data to CSV format.
    /// </summary>
    /// <param name="fixedWidthData">The fixed-width input data.</param>
    /// <param name="columns">The field definitions describing the layout.</param>
    /// <param name="options">Optional conversion options.</param>
    /// <returns>The CSV formatted output.</returns>
    public static string Convert(string fixedWidthData, IReadOnlyList<FixedWidthFieldDefinition> columns, FixedWidthToCsvOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(fixedWidthData);
        ArgumentNullException.ThrowIfNull(columns);
        if (columns.Count == 0)
            throw new ArgumentException("At least one column definition is required.", nameof(columns));

        options ??= FixedWidthToCsvOptions.Default;

        var sb = new System.Text.StringBuilder();

        // Write header
        if (options.IncludeHeader)
        {
            for (int i = 0; i < columns.Count; i++)
            {
                if (i > 0) sb.Append(options.Delimiter);
                WriteCsvField(sb, columns[i].Name, options.Delimiter, options.Quote);
            }
            sb.Append(options.NewLine);
        }

        // Parse fixed-width rows
        var lines = fixedWidthData.Split(["\r\n", "\n"], StringSplitOptions.None);
        int recordLength = columns.Sum(c => c.Width);

        foreach (var line in lines)
        {
            if (string.IsNullOrEmpty(line))
                continue;

            // Ensure line is long enough
            var paddedLine = line.Length >= recordLength
                ? line
                : line + new string(' ', recordLength - line.Length);

            int offset = 0;
            for (int i = 0; i < columns.Count; i++)
            {
                if (i > 0) sb.Append(options.Delimiter);

                var col = columns[i];
                var field = paddedLine.Substring(offset, Math.Min(col.Width, paddedLine.Length - offset));
                offset += col.Width;

                // Trim based on alignment
                var trimmed = col.Alignment switch
                {
                    FieldAlignment.Right => field.TrimStart(col.PadChar),
                    FieldAlignment.Center => field.Trim(col.PadChar),
                    _ => field.TrimEnd(col.PadChar)
                };

                WriteCsvField(sb, trimmed, options.Delimiter, options.Quote);
            }
            sb.Append(options.NewLine);
        }

        return sb.ToString();
    }

    private static void WriteCsvField(System.Text.StringBuilder sb, string value, char delimiter, char quote)
    {
        bool needsQuoting = value.Contains(delimiter) || value.Contains(quote) || value.Contains('\r') || value.Contains('\n');

        if (needsQuoting)
        {
            sb.Append(quote);
            foreach (var ch in value)
            {
                if (ch == quote)
                    sb.Append(quote);
                sb.Append(ch);
            }
            sb.Append(quote);
        }
        else
        {
            sb.Append(value);
        }
    }
}
