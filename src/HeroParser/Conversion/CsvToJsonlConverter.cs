using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using HeroParser.SeparatedValues.Core;

namespace HeroParser.Conversion;

/// <summary>
/// Converts CSV data to JSONL using a configurable shape — most usefully OpenAI/Anthropic chat-completion
/// fine-tuning records produced from a tabular Q/A dataset.
/// </summary>
/// <remarks>
/// Instance type so the shape and options can be captured once and reused (and so the converter
/// can be injected through DI in pipeline scenarios).
/// </remarks>
public sealed class CsvToJsonlConverter
{
    private readonly CsvToJsonlShape shape;
    private readonly CsvToJsonlOptions options;

    /// <summary>
    /// Initializes a new converter.
    /// </summary>
    /// <param name="shape">The JSONL row shape (flat object, OpenAI chat, Anthropic messages, …).</param>
    /// <param name="options">Optional conversion options (defaults to <see cref="CsvToJsonlOptions.Default"/>).</param>
    public CsvToJsonlConverter(CsvToJsonlShape shape, CsvToJsonlOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(shape);
        this.shape = shape;
        this.options = options ?? CsvToJsonlOptions.Default;
    }

    /// <summary>Converts a CSV string to a JSONL string.</summary>
    public string Convert(string csvData)
    {
        ArgumentNullException.ThrowIfNull(csvData);
        using var stream = new MemoryStream();
        ConvertCore(csvData.AsSpan(), stream);
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    /// <summary>Converts a CSV file to a JSONL file.</summary>
    public void Convert(string csvPath, string jsonlPath)
    {
        ArgumentNullException.ThrowIfNull(csvPath);
        ArgumentNullException.ThrowIfNull(jsonlPath);
        string csvText = File.ReadAllText(csvPath, Encoding.UTF8);
        using FileStream output = new(jsonlPath, FileMode.Create, FileAccess.Write, FileShare.None);
        ConvertCore(csvText.AsSpan(), output);
    }

    /// <summary>Asynchronously converts a CSV stream to a JSONL stream.</summary>
    public async ValueTask ConvertAsync(Stream csvStream, Stream jsonlStream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(csvStream);
        ArgumentNullException.ThrowIfNull(jsonlStream);
        using var ms = new MemoryStream();
        await csvStream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        string csvText = Encoding.UTF8.GetString(StripBom(ms.GetBuffer().AsSpan(0, (int)ms.Length)));
        ConvertCore(csvText.AsSpan(), jsonlStream);
    }

    private static ReadOnlySpan<byte> StripBom(ReadOnlySpan<byte> bytes)
        => bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? bytes[3..] : bytes;

    private void ConvertCore(ReadOnlySpan<char> csvText, Stream output)
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

                EmitRecord(writer, headers, values);
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

    private void EmitRecord(Utf8JsonWriter writer, string[]? headers, string[] values)
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
