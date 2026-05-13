using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using HeroParser.SeparatedValues.Core;

namespace HeroParser.Conversion;

/// <summary>
/// Pure CSV-to-JSONL conversion functions. Used most often to project a tabular Q/A dataset into
/// OpenAI/Anthropic chat-completion fine-tuning JSONL.
/// </summary>
public static class CsvToJsonlConverter
{
    /// <summary>Converts a CSV string to a JSONL string.</summary>
    public static string Convert(string csvData, CsvToJsonlShape shape, CsvToJsonlOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(csvData);
        ArgumentNullException.ThrowIfNull(shape);
        CsvToJsonlOptions opt = options ?? CsvToJsonlOptions.Default;
        using var stream = new MemoryStream();
        ConvertCore(csvData.AsSpan(), shape, opt, stream);
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    /// <summary>Converts a CSV file to a JSONL file.</summary>
    public static void Convert(string csvPath, string jsonlPath, CsvToJsonlShape shape, CsvToJsonlOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(csvPath);
        ArgumentNullException.ThrowIfNull(jsonlPath);
        ArgumentNullException.ThrowIfNull(shape);
        CsvToJsonlOptions opt = options ?? CsvToJsonlOptions.Default;
        string csvText = File.ReadAllText(csvPath, Encoding.UTF8);
        using FileStream output = new(jsonlPath, FileMode.Create, FileAccess.Write, FileShare.None);
        ConvertCore(csvText.AsSpan(), shape, opt, output);
    }

    /// <summary>Asynchronously converts a CSV stream to a JSONL stream.</summary>
    public static async ValueTask ConvertAsync(
        Stream csvStream,
        Stream jsonlStream,
        CsvToJsonlShape shape,
        CsvToJsonlOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(csvStream);
        ArgumentNullException.ThrowIfNull(jsonlStream);
        ArgumentNullException.ThrowIfNull(shape);
        CsvToJsonlOptions opt = options ?? CsvToJsonlOptions.Default;

        using var ms = new MemoryStream();
        await csvStream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        string csvText = Encoding.UTF8.GetString(StripBom(ms.GetBuffer().AsSpan(0, (int)ms.Length)));
        ConvertCore(csvText.AsSpan(), shape, opt, jsonlStream);
    }

    private static ReadOnlySpan<byte> StripBom(ReadOnlySpan<byte> bytes)
        => bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? bytes[3..] : bytes;

    private static void ConvertCore(ReadOnlySpan<char> csvText, CsvToJsonlShape shape, CsvToJsonlOptions options, Stream output)
    {
        CsvReadOptions parser = new() { Delimiter = options.Delimiter };
        ReadOnlySpan<byte> newlineBytes = Encoding.UTF8.GetBytes(options.NewLine);

        JsonWriterOptions writerOptions = new()
        {
            Indented = false,
            SkipValidation = false,
            Encoder = options.JsonOptions?.Encoder ?? JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        string[]? headers = null;
        bool isFirst = true;
        long emitted = 0;

        using var writer = new Utf8JsonWriter(output, writerOptions);
        var rowReader = Csv.ReadFromCharSpan(csvText, parser);
        try
        {
            while (rowReader.MoveNext())
            {
                var row = rowReader.Current;

                if (isFirst && options.HasHeaderRow)
                {
                    isFirst = false;
                    headers = new string[row.ColumnCount];
                    for (int i = 0; i < row.ColumnCount; i++)
                        headers[i] = row[i].UnquoteToString();
                    continue;
                }
                isFirst = false;

                string[] values = new string[row.ColumnCount];
                for (int i = 0; i < row.ColumnCount; i++)
                    values[i] = row[i].UnquoteToString();

                if (emitted > 0)
                {
                    writer.Flush();
                    output.Write(newlineBytes);
                }

                EmitRecord(writer, shape, headers, values);
                writer.Flush();
                writer.Reset();
                emitted++;
            }
        }
        finally
        {
            rowReader.Dispose();
        }
    }

    private static void EmitRecord(Utf8JsonWriter writer, CsvToJsonlShape shape, string[]? headers, string[] values)
    {
        switch (shape)
        {
            case CsvToJsonlShape.FlatObjectShape:
                EmitFlat(writer, headers, values);
                break;
            case CsvToJsonlShape.OpenAiChatShape openai:
                EmitOpenAi(writer, openai, headers, values);
                break;
            case CsvToJsonlShape.AnthropicMessagesShape anthropic:
                EmitAnthropic(writer, anthropic, headers, values);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape), $"Unknown shape: {shape.GetType().Name}");
        }
    }

    private static void EmitFlat(Utf8JsonWriter writer, string[]? headers, string[] values)
    {
        writer.WriteStartObject();
        if (headers is null)
        {
            for (int i = 0; i < values.Length; i++)
                writer.WriteString($"column{i + 1}", values[i]);
        }
        else
        {
            int n = Math.Min(headers.Length, values.Length);
            for (int i = 0; i < n; i++)
                writer.WriteString(headers[i], values[i]);
        }
        writer.WriteEndObject();
    }

    private static void EmitOpenAi(Utf8JsonWriter writer, CsvToJsonlShape.OpenAiChatShape shape, string[]? headers, string[] values)
    {
        RequireHeaders(headers);
        writer.WriteStartObject();
        writer.WriteStartArray("messages");

        if (!string.IsNullOrEmpty(shape.SystemColumn))
        {
            string? sys = LookupOrNull(headers!, values, shape.SystemColumn);
            if (sys is not null)
                WriteMessage(writer, "system", sys);
        }

        WriteMessage(writer, "user", LookupRequired(headers!, values, shape.UserColumn));
        WriteMessage(writer, "assistant", LookupRequired(headers!, values, shape.AssistantColumn));

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void EmitAnthropic(Utf8JsonWriter writer, CsvToJsonlShape.AnthropicMessagesShape shape, string[]? headers, string[] values)
    {
        RequireHeaders(headers);
        writer.WriteStartObject();
        writer.WriteStartArray("messages");
        WriteMessage(writer, "user", LookupRequired(headers!, values, shape.UserColumn));
        WriteMessage(writer, "assistant", LookupRequired(headers!, values, shape.AssistantColumn));
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteMessage(Utf8JsonWriter writer, string role, string content)
    {
        writer.WriteStartObject();
        writer.WriteString("role", role);
        writer.WriteString("content", content);
        writer.WriteEndObject();
    }

    private static void RequireHeaders(string[]? headers)
    {
        if (headers is null)
            throw new InvalidOperationException("Shape requires a CSV header row (set HasHeaderRow = true).");
    }

    private static string LookupRequired(string[] headers, string[] values, string name)
    {
        for (int i = 0; i < headers.Length; i++)
            if (string.Equals(headers[i], name, StringComparison.OrdinalIgnoreCase))
                return i < values.Length ? values[i] : string.Empty;
        throw new InvalidOperationException($"CSV does not contain a column named '{name}'.");
    }

    private static string? LookupOrNull(string[] headers, string[] values, string name)
    {
        for (int i = 0; i < headers.Length; i++)
            if (string.Equals(headers[i], name, StringComparison.OrdinalIgnoreCase))
                return i < values.Length ? values[i] : null;
        return null;
    }
}
