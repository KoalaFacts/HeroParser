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
    /// Gets or sets how spreadsheet-formula injection in field values is handled.
    /// </summary>
    /// <remarks>
    /// Default is <see cref="CsvInjectionProtection.EscapeWithQuote"/>, matching <see cref="CsvWriteOptions"/>.
    /// Fixed-width data often originates from upstream systems where trimmed strings can start with
    /// <c>=</c>/<c>+</c>/<c>-</c>/<c>@</c>/<c>\t</c>/<c>\r</c>; without protection these would execute as
    /// formulas when the converted CSV is opened in a spreadsheet.
    /// </remarks>
    public CsvInjectionProtection InjectionProtection { get; init; } = CsvInjectionProtection.EscapeWithQuote;

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
                // Column names come from developer-supplied metadata, so injection protection isn't
                // applied to them — but quoting still happens if a name contains delimiter/quote/newline.
                WriteCsvField(sb, columns[i].Name, options.Delimiter, options.Quote, CsvInjectionProtection.None);
            }
            sb.Append(options.NewLine);
        }

        // Parse fixed-width rows
        var lines = fixedWidthData.Split(["\r\n", "\n"], StringSplitOptions.None);
        int recordLength = columns.Sum(c => c.Width);

        var paddedLines = lines
            .Where(l => !string.IsNullOrEmpty(l))
            .Select(l => l.Length >= recordLength ? l : l + new string(' ', recordLength - l.Length));

        foreach (var paddedLine in paddedLines)
        {
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

                WriteCsvField(sb, trimmed, options.Delimiter, options.Quote, options.InjectionProtection);
            }
            sb.Append(options.NewLine);
        }

        return sb.ToString();
    }

    private static void WriteCsvField(System.Text.StringBuilder sb, string value, char delimiter, char quote, CsvInjectionProtection injectionProtection)
    {
        if (injectionProtection != CsvInjectionProtection.None && IsDangerousField(value))
        {
            WriteFieldWithInjectionProtection(sb, value, quote, injectionProtection);
            return;
        }

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

    private static bool IsDangerousField(string value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        char first = value[0];
        switch (first)
        {
            case '=':
            case '@':
            case '\t':
            case '\r':
                return true;
            case '-':
            case '+':
                if (value.Length == 1) return false;
                char second = value[1];
                return !((uint)(second - '0') <= 9 || second == '.');
            default:
                return false;
        }
    }

    private static void WriteFieldWithInjectionProtection(System.Text.StringBuilder sb, string value, char quote, CsvInjectionProtection injectionProtection)
    {
        switch (injectionProtection)
        {
            case CsvInjectionProtection.EscapeWithQuote:
                WriteQuotedFieldWithPrefix(sb, value, '\'', quote);
                break;
            case CsvInjectionProtection.EscapeWithTab:
                WriteQuotedFieldWithPrefix(sb, value, '\t', quote);
                break;
            case CsvInjectionProtection.Sanitize:
                sb.Append(StripDangerousPrefix(value));
                break;
            case CsvInjectionProtection.Reject:
                throw new HeroParser.SeparatedValues.Core.CsvException(
                    HeroParser.SeparatedValues.Core.CsvErrorCode.InjectionDetected,
                    $"CSV injection detected: field starts with dangerous character '{value[0]}'");
            case CsvInjectionProtection.None:
            default:
                sb.Append(value);
                break;
        }
    }

    private static void WriteQuotedFieldWithPrefix(System.Text.StringBuilder sb, string value, char prefix, char quote)
    {
        sb.Append(quote);
        sb.Append(prefix);
        foreach (var ch in value)
        {
            if (ch == quote)
                sb.Append(quote);
            sb.Append(ch);
        }
        sb.Append(quote);
    }

    private static string StripDangerousPrefix(string value)
    {
        int start = 0;
        while (start < value.Length)
        {
            char c = value[start];
            bool dangerous = c == '=' || c == '@' || c == '\t' || c == '\r';
            if (!dangerous && (c == '-' || c == '+') && start + 1 < value.Length)
            {
                char next = value[start + 1];
                dangerous = !((uint)(next - '0') <= 9 || next == '.');
            }
            if (!dangerous)
                break;
            start++;
        }
        return start == 0 ? value : value[start..];
    }
}
