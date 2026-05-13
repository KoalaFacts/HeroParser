using System.Text;
using System.Text.Json;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Writing;

namespace HeroParser.Conversion;

/// <summary>
/// Converts JSONL to CSV. The CSV column set is inferred from the union of top-level keys observed in
/// the first <see cref="JsonlToCsvOptions.SchemaInferencePeekRows"/> lines. Nested objects/arrays are
/// emitted as JSON-encoded strings in their cell.
/// </summary>
public sealed class JsonlToCsvConverter
{
    private readonly JsonlToCsvOptions options;

    /// <summary>
    /// Initializes a new converter.
    /// </summary>
    /// <param name="options">Optional conversion options (defaults to <see cref="JsonlToCsvOptions.Default"/>).</param>
    public JsonlToCsvConverter(JsonlToCsvOptions? options = null)
    {
        this.options = options ?? JsonlToCsvOptions.Default;
    }

    /// <summary>Converts a JSONL string to a CSV string.</summary>
    public string Convert(string jsonlData)
    {
        ArgumentNullException.ThrowIfNull(jsonlData);
        byte[] bytes = Encoding.UTF8.GetBytes(jsonlData);
        using var input = new MemoryStream(bytes, writable: false);
        using var output = new StringWriter();
        ConvertCore(input, output);
        return output.ToString();
    }

    /// <summary>Converts a JSONL file to a CSV file.</summary>
    public void Convert(string jsonlPath, string csvPath)
    {
        ArgumentNullException.ThrowIfNull(jsonlPath);
        ArgumentNullException.ThrowIfNull(csvPath);
        using FileStream input = new(jsonlPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using StreamWriter output = new(new FileStream(csvPath, FileMode.Create, FileAccess.Write, FileShare.None), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        ConvertCore(input, output);
    }

    private void ConvertCore(Stream jsonlStream, TextWriter csvWriter)
    {
        List<string> peekedLines = [];
        List<string> columns = [];
        HashSet<string> seenCols = new(StringComparer.Ordinal);

        using (var reader = new StreamReader(jsonlStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true))
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
        }

        CsvWriteOptions writeOpts = new() { Delimiter = options.Delimiter };
        using var csv = new CsvStreamWriter(csvWriter, writeOpts, leaveOpen: true);

        csv.WriteRow(columns.ToArray());

        foreach (string line in peekedLines)
            EmitRow(csv, line, columns);

        if (jsonlStream.CanSeek)
        {
            jsonlStream.Position = 0;
            using var rereader = new StreamReader(jsonlStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            for (int i = 0; i < peekedLines.Count; i++)
                rereader.ReadLine();

            string? next;
            while ((next = rereader.ReadLine()) is not null)
                EmitRow(csv, next, columns);
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
