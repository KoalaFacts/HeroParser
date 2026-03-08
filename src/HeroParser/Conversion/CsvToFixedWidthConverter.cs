using HeroParser.FixedWidths;

namespace HeroParser.Conversion;

/// <summary>
/// Defines a field in a fixed-width record for conversion purposes.
/// </summary>
/// <remarks>
/// Used by <see cref="CsvToFixedWidthConverter"/> and <see cref="FixedWidthToCsvConverter"/>
/// to describe the field layout for conversion between CSV and fixed-width formats.
/// </remarks>
public sealed class FixedWidthFieldDefinition
{
    /// <summary>
    /// Gets the column/field name (used for header matching).
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the field width in characters.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the field alignment.
    /// </summary>
    public FieldAlignment Alignment { get; }

    /// <summary>
    /// Gets the padding character.
    /// </summary>
    public char PadChar { get; }

    /// <summary>
    /// Creates a new field definition.
    /// </summary>
    /// <param name="name">The column name.</param>
    /// <param name="width">The field width in characters (must be positive).</param>
    /// <param name="alignment">The field alignment (default: Left).</param>
    /// <param name="padChar">The padding character (default: space).</param>
    public FixedWidthFieldDefinition(string name, int width, FieldAlignment alignment = FieldAlignment.Left, char padChar = ' ')
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);

        Name = name;
        Width = width;
        Alignment = alignment;
        PadChar = padChar;
    }
}

/// <summary>
/// Options for CSV-to-fixed-width conversion.
/// </summary>
public sealed record CsvToFixedWidthOptions
{
    /// <summary>
    /// Gets or sets the CSV delimiter character (default: comma).
    /// </summary>
    public char Delimiter { get; init; } = ',';

    /// <summary>
    /// Gets or sets whether to include a fixed-width header row (default: false).
    /// </summary>
    public bool IncludeHeader { get; init; }

    /// <summary>
    /// Gets or sets the newline sequence for output (default: CRLF).
    /// </summary>
    public string NewLine { get; init; } = "\r\n";

    /// <summary>
    /// Gets the default options.
    /// </summary>
    public static CsvToFixedWidthOptions Default { get; } = new();
}

/// <summary>
/// Converts CSV data to fixed-width format.
/// </summary>
/// <remarks>
/// <para>
/// Maps CSV columns to fixed-width fields by matching column names from the CSV header
/// to the field definitions. Values are padded or truncated to fit the specified widths.
/// </para>
/// <para>
/// Thread-Safety: All methods are thread-safe as they operate on local state only.
/// </para>
/// </remarks>
public static class CsvToFixedWidthConverter
{
    /// <summary>
    /// Converts CSV data to fixed-width format.
    /// </summary>
    /// <param name="csvData">The CSV input data.</param>
    /// <param name="columns">The fixed-width field definitions.</param>
    /// <param name="options">Optional conversion options.</param>
    /// <returns>The fixed-width formatted output.</returns>
    public static string Convert(string csvData, IReadOnlyList<FixedWidthFieldDefinition> columns, CsvToFixedWidthOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(csvData);
        ArgumentNullException.ThrowIfNull(columns);
        if (columns.Count == 0)
            throw new ArgumentException("At least one column definition is required.", nameof(columns));

        options ??= CsvToFixedWidthOptions.Default;

        var readOptions = new SeparatedValues.Core.CsvReadOptions { Delimiter = options.Delimiter };
        var reader = Csv.ReadFromText(csvData, readOptions);

        // Read header to build column index mapping
        if (!reader.MoveNext())
            return "";

        var headerRow = reader.Current;
        var headerNames = new string[headerRow.ColumnCount];
        for (int i = 0; i < headerRow.ColumnCount; i++)
            headerNames[i] = headerRow[i].ToString();

        // Map field definitions to CSV column indices
        var columnMap = new int[columns.Count];
        for (int i = 0; i < columns.Count; i++)
        {
            columnMap[i] = Array.IndexOf(headerNames, columns[i].Name);
        }

        int recordLength = 0;
        foreach (var col in columns)
            recordLength += col.Width;

        var sb = new System.Text.StringBuilder();

        // Optional header
        if (options.IncludeHeader)
        {
            WriteFixedWidthRow(sb, columns, [.. columns.Select(c => c.Name)]);
            sb.Append(options.NewLine);
        }

        // Data rows
        bool hasData = false;
        while (reader.MoveNext())
        {
            hasData = true;
            var row = reader.Current;
            var values = new string[columns.Count];
            for (int i = 0; i < columns.Count; i++)
            {
                var csvIndex = columnMap[i];
                values[i] = (csvIndex >= 0 && csvIndex < row.ColumnCount)
                    ? row[csvIndex].ToString()
                    : "";
            }

            WriteFixedWidthRow(sb, columns, values);
            sb.Append(options.NewLine);
        }

        return hasData ? sb.ToString() : "";
    }

    private static void WriteFixedWidthRow(System.Text.StringBuilder sb, IReadOnlyList<FixedWidthFieldDefinition> columns, string[] values)
    {
        for (int i = 0; i < columns.Count; i++)
        {
            var col = columns[i];
            var value = i < values.Length ? values[i] : "";

            // Truncate if too long
            if (value.Length > col.Width)
                value = value[..col.Width];

            switch (col.Alignment)
            {
                case FieldAlignment.Right:
                    sb.Append(new string(col.PadChar, col.Width - value.Length));
                    sb.Append(value);
                    break;
                case FieldAlignment.Center:
                    int leftPad = (col.Width - value.Length) / 2;
                    int rightPad = col.Width - value.Length - leftPad;
                    sb.Append(new string(col.PadChar, leftPad));
                    sb.Append(value);
                    sb.Append(new string(col.PadChar, rightPad));
                    break;
                case FieldAlignment.Left:
                case FieldAlignment.None:
                default:
                    sb.Append(value);
                    sb.Append(new string(col.PadChar, col.Width - value.Length));
                    break;
            }
        }
    }
}
