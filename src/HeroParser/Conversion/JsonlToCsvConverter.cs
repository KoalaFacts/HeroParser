using System.Text;
using System.Text.Json;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Writing;

namespace HeroParser.Conversion;

/// <summary>
/// Pure JSONL-to-CSV conversion functions. The CSV column set is inferred from the union of top-level
/// keys observed in the first <see cref="JsonlToCsvOptions.SchemaInferencePeekRows"/> lines. Nested
/// objects/arrays are emitted as JSON-encoded strings in their cell.
/// </summary>
public static class JsonlToCsvConverter
{
    /// <summary>Converts a JSONL string to a CSV string.</summary>
    public static string Convert(string jsonlData, JsonlToCsvOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(jsonlData);
        JsonlToCsvOptions opt = options ?? JsonlToCsvOptions.Default;
        byte[] bytes = Encoding.UTF8.GetBytes(jsonlData);
        using var input = new MemoryStream(bytes, writable: false);
        using var output = new StringWriter();
        ConvertCore(input, output, opt);
        return output.ToString();
    }

    /// <summary>Converts a JSONL file to a CSV file.</summary>
    public static void Convert(string jsonlPath, string csvPath, JsonlToCsvOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(jsonlPath);
        ArgumentNullException.ThrowIfNull(csvPath);
        JsonlToCsvOptions opt = options ?? JsonlToCsvOptions.Default;
        using FileStream input = new(jsonlPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using StreamWriter output = new(new FileStream(csvPath, FileMode.Create, FileAccess.Write, FileShare.None), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        ConvertCore(input, output, opt);
    }

    /// <summary>Converts a JSONL stream to a CSV writer.</summary>
    /// <param name="jsonlStream">The JSONL stream to read from.</param>
    /// <param name="csvWriter">The CSV TextWriter to write to.</param>
    /// <param name="options">Options control conversion, defaults to <see cref="JsonlToCsvOptions.Default"/>.</param>
    public static void Convert(Stream jsonlStream, TextWriter csvWriter, JsonlToCsvOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(jsonlStream);
        ArgumentNullException.ThrowIfNull(csvWriter);
        JsonlToCsvOptions opt = options ?? JsonlToCsvOptions.Default;
        ConvertCore(jsonlStream, csvWriter, opt);
    }

    /// <summary>Asynchronously converts a JSONL stream to a CSV stream.</summary>
    /// <param name="jsonlStream">The JSONL stream to read from.</param>
    /// <param name="csvStream">The CSV stream to write to.</param>
    /// <param name="options">Options control conversion, defaults to <see cref="JsonlToCsvOptions.Default"/>.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
    public static async ValueTask ConvertAsync(
        Stream jsonlStream,
        Stream csvStream,
        JsonlToCsvOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jsonlStream);
        ArgumentNullException.ThrowIfNull(csvStream);
        JsonlToCsvOptions opt = options ?? JsonlToCsvOptions.Default;
        await ConvertCoreAsync(jsonlStream, csvStream, opt, cancellationToken).ConfigureAwait(false);
    }

    private static void ConvertCore(Stream jsonlStream, TextWriter csvWriter, JsonlToCsvOptions options)
    {
        List<string> peekedLines = [];
        List<string> columns = [];
        HashSet<string> seenCols = new(StringComparer.Ordinal);

        var reader = new StreamReader(jsonlStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        try
        {
            string? line;
            while (peekedLines.Count < options.SchemaInferencePeekRows && (line = reader.ReadLine()) is not null)
            {
                peekedLines.Add(line);
                if (string.IsNullOrWhiteSpace(line)) continue;

                using JsonDocument doc = JsonDocument.Parse(line);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) continue;
                foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
                {
                    if (seenCols.Add(prop.Name))
                        columns.Add(prop.Name);
                }
            }

            CsvWriteOptions writeOpts = new() { Delimiter = options.Delimiter };
            using var csv = new CsvStreamWriter(csvWriter, writeOpts, leaveOpen: true);

            csv.WriteRow([.. columns]);

            foreach (string peeked in peekedLines)
                EmitRow(csv, peeked, columns);

            string? next;
            while ((next = reader.ReadLine()) is not null)
                EmitRow(csv, next, columns);
        }
        finally
        {
            reader.Dispose();
        }
    }

    private static async ValueTask ConvertCoreAsync(
        Stream jsonlStream,
        Stream csvStream,
        JsonlToCsvOptions options,
        CancellationToken cancellationToken)
    {
        List<string> peekedLines = [];
        List<string> columns = [];
        HashSet<string> seenCols = new(StringComparer.Ordinal);

        var reader = new StreamReader(jsonlStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        try
        {
            string? line;
            while (peekedLines.Count < options.SchemaInferencePeekRows && (line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
            {
                peekedLines.Add(line);
                if (string.IsNullOrWhiteSpace(line)) continue;

                using JsonDocument doc = JsonDocument.Parse(line);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) continue;
                foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
                {
                    if (seenCols.Add(prop.Name))
                        columns.Add(prop.Name);
                }
            }

            CsvWriteOptions writeOpts = new() { Delimiter = options.Delimiter };
            await using var csv = new CsvAsyncStreamWriter(csvStream, writeOpts, Encoding.UTF8, leaveOpen: true);

            await csv.WriteRowAsync([.. columns], cancellationToken).ConfigureAwait(false);

            foreach (string peeked in peekedLines)
                await EmitRowAsync(csv, peeked, columns, cancellationToken).ConfigureAwait(false);

            string? next;
            while ((next = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
                await EmitRowAsync(csv, next, columns, cancellationToken).ConfigureAwait(false);

            await csv.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            reader.Dispose();
        }
    }

    private static void EmitRow(CsvStreamWriter csv, string line, List<string> columns)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        using JsonDocument doc = JsonDocument.Parse(line);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return;

        string?[] row = new string?[columns.Count];
        for (int i = 0; i < columns.Count; i++)
        {
            row[i] = doc.RootElement.TryGetProperty(columns[i], out JsonElement value)
                ? StringifyValue(value)
                : null;
        }
        csv.WriteRow(row);
    }

    private static async ValueTask EmitRowAsync(
        CsvAsyncStreamWriter csv,
        string line,
        List<string> columns,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        using JsonDocument doc = JsonDocument.Parse(line);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return;

        string?[] row = new string?[columns.Count];
        for (int i = 0; i < columns.Count; i++)
        {
            row[i] = doc.RootElement.TryGetProperty(columns[i], out JsonElement value)
                ? StringifyValue(value)
                : null;
        }
        await csv.WriteRowAsync(row, cancellationToken).ConfigureAwait(false);
    }

    private static string? StringifyValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.Undefined => null,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Object or JsonValueKind.Array => value.GetRawText(),
        _ => value.GetRawText()
    };
}
