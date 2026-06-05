using System;
using System.Buffers;
using System.Buffers.Text;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Rows;

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
        using var csvStream = new FileStream(csvPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        using var jsonlStream = new FileStream(jsonlPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096);
        ConvertAsync(csvStream, jsonlStream, shape, opt).AsTask().GetAwaiter().GetResult();
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

        CsvReadOptions parserReadOptions = new() { Delimiter = opt.Delimiter };

        await using var rowReader = Csv.CreateAsyncStreamReader(csvStream, parserReadOptions, leaveOpen: true);

        JsonWriterOptions writerOptions = new()
        {
            Indented = false,
            SkipValidation = false,
            Encoder = opt.JsonOptions?.Encoder ?? JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        byte[] newlineBytes = Encoding.UTF8.GetBytes(opt.NewLine);
        string[]? headers = null;
        bool isFirst = true;
        long emitted = 0;

        using var writer = new Utf8JsonWriter(jsonlStream, writerOptions);

        while (await rowReader.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            if (isFirst && opt.HasHeaderRow)
            {
                isFirst = false;
                var row = rowReader.Current;
                headers = new string[row.ColumnCount];
                for (int i = 0; i < row.ColumnCount; i++)
                    headers[i] = row[i].UnquoteToString();
                continue;
            }
            isFirst = false;

            if (emitted > 0)
            {
                await jsonlStream.WriteAsync(newlineBytes.AsMemory(), cancellationToken).ConfigureAwait(false);
            }

            {
                var row = rowReader.Current;
                EmitRecord(writer, shape, headers, row);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                writer.Reset();
            }

            emitted++;
        }
    }

    private static void ConvertCore(ReadOnlySpan<char> csvText, CsvToJsonlShape shape, CsvToJsonlOptions options, Stream output)
    {
        CsvReadOptions parser = new() { Delimiter = options.Delimiter };
        byte[] newlineBytes = Encoding.UTF8.GetBytes(options.NewLine);

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
                if (isFirst && options.HasHeaderRow)
                {
                    isFirst = false;
                    var row = rowReader.Current;
                    headers = new string[row.ColumnCount];
                    for (int i = 0; i < row.ColumnCount; i++)
                        headers[i] = row[i].UnquoteToString();
                    continue;
                }
                isFirst = false;

                if (emitted > 0)
                {
                    writer.Flush();
                    output.Write(newlineBytes);
                }

                {
                    var row = rowReader.Current;
                    EmitRecord(writer, shape, headers, row);
                    writer.Flush();
                    writer.Reset();
                }
                emitted++;
            }
        }
        finally
        {
            rowReader.Dispose();
        }
    }

    private static void EmitRecord(Utf8JsonWriter writer, CsvToJsonlShape shape, string[]? headers, CsvRow<byte> row)
    {
        switch (shape)
        {
            case CsvToJsonlShape.FlatObjectShape:
                EmitFlat(writer, headers, row);
                break;
            case CsvToJsonlShape.OpenAiChatShape openai:
                EmitOpenAi(writer, openai, headers, row);
                break;
            case CsvToJsonlShape.AnthropicMessagesShape anthropic:
                EmitAnthropic(writer, anthropic, headers, row);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape), $"Unknown shape: {shape.GetType().Name}");
        }
    }

    private static void EmitRecord(Utf8JsonWriter writer, CsvToJsonlShape shape, string[]? headers, CsvRow<char> row)
    {
        switch (shape)
        {
            case CsvToJsonlShape.FlatObjectShape:
                EmitFlat(writer, headers, row);
                break;
            case CsvToJsonlShape.OpenAiChatShape openai:
                EmitOpenAi(writer, openai, headers, row);
                break;
            case CsvToJsonlShape.AnthropicMessagesShape anthropic:
                EmitAnthropic(writer, anthropic, headers, row);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape), $"Unknown shape: {shape.GetType().Name}");
        }
    }

    private static void EmitFlat(Utf8JsonWriter writer, string[]? headers, CsvRow<byte> row)
    {
        writer.WriteStartObject();
        if (headers is null)
        {
            Span<byte> nameBytes = stackalloc byte[32];
            for (int i = 0; i < row.ColumnCount; i++)
            {
                if (Utf8Formatter.TryFormat(i + 1, nameBytes[6..], out int written))
                {
                    "column"u8.CopyTo(nameBytes);
                    var nameSpan = nameBytes[..(6 + written)];
                    WriteColumnProperty(writer, nameSpan, row[i]);
                }
                else
                {
                    writer.WriteString($"column{i + 1}", row[i].UnquoteToString());
                }
            }
        }
        else
        {
            int n = Math.Min(headers.Length, row.ColumnCount);
            for (int i = 0; i < n; i++)
            {
                WriteColumnProperty(writer, headers[i], row[i]);
            }
        }
        writer.WriteEndObject();
    }

    private static void EmitFlat(Utf8JsonWriter writer, string[]? headers, CsvRow<char> row)
    {
        writer.WriteStartObject();
        if (headers is null)
        {
            for (int i = 0; i < row.ColumnCount; i++)
            {
                writer.WriteString($"column{i + 1}", row[i].UnquoteToString());
            }
        }
        else
        {
            int n = Math.Min(headers.Length, row.ColumnCount);
            for (int i = 0; i < n; i++)
            {
                WriteColumnProperty(writer, headers[i], row[i]);
            }
        }
        writer.WriteEndObject();
    }

    private static void EmitOpenAi(Utf8JsonWriter writer, CsvToJsonlShape.OpenAiChatShape shape, string[]? headers, CsvRow<byte> row)
    {
        RequireHeaders(headers);
        writer.WriteStartObject();
        writer.WriteStartArray("messages");

        if (!string.IsNullOrEmpty(shape.SystemColumn))
        {
            WriteOptionalMessage(writer, "system", headers!, row, shape.SystemColumn);
        }

        WriteRequiredMessage(writer, "user", headers!, row, shape.UserColumn);
        WriteRequiredMessage(writer, "assistant", headers!, row, shape.AssistantColumn);

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void EmitOpenAi(Utf8JsonWriter writer, CsvToJsonlShape.OpenAiChatShape shape, string[]? headers, CsvRow<char> row)
    {
        RequireHeaders(headers);
        writer.WriteStartObject();
        writer.WriteStartArray("messages");

        if (!string.IsNullOrEmpty(shape.SystemColumn))
        {
            WriteOptionalMessage(writer, "system", headers!, row, shape.SystemColumn);
        }

        WriteRequiredMessage(writer, "user", headers!, row, shape.UserColumn);
        WriteRequiredMessage(writer, "assistant", headers!, row, shape.AssistantColumn);

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void EmitAnthropic(Utf8JsonWriter writer, CsvToJsonlShape.AnthropicMessagesShape shape, string[]? headers, CsvRow<byte> row)
    {
        RequireHeaders(headers);
        writer.WriteStartObject();
        writer.WriteStartArray("messages");
        WriteRequiredMessage(writer, "user", headers!, row, shape.UserColumn);
        WriteRequiredMessage(writer, "assistant", headers!, row, shape.AssistantColumn);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void EmitAnthropic(Utf8JsonWriter writer, CsvToJsonlShape.AnthropicMessagesShape shape, string[]? headers, CsvRow<char> row)
    {
        RequireHeaders(headers);
        writer.WriteStartObject();
        writer.WriteStartArray("messages");
        WriteRequiredMessage(writer, "user", headers!, row, shape.UserColumn);
        WriteRequiredMessage(writer, "assistant", headers!, row, shape.AssistantColumn);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteRequiredMessage(Utf8JsonWriter writer, string role, string[] headers, CsvRow<byte> row, string name)
    {
        for (int i = 0; i < headers.Length; i++)
        {
            if (string.Equals(headers[i], name, StringComparison.OrdinalIgnoreCase))
            {
                writer.WriteStartObject();
                writer.WriteString("role", role);
                if (i < row.ColumnCount)
                {
                    WriteColumnProperty(writer, "content", row[i]);
                }
                else
                {
                    writer.WriteString("content", string.Empty);
                }
                writer.WriteEndObject();
                return;
            }
        }
        throw new InvalidOperationException($"CSV does not contain a column named '{name}'.");
    }

    private static void WriteRequiredMessage(Utf8JsonWriter writer, string role, string[] headers, CsvRow<char> row, string name)
    {
        for (int i = 0; i < headers.Length; i++)
        {
            if (string.Equals(headers[i], name, StringComparison.OrdinalIgnoreCase))
            {
                writer.WriteStartObject();
                writer.WriteString("role", role);
                if (i < row.ColumnCount)
                {
                    WriteColumnProperty(writer, "content", row[i]);
                }
                else
                {
                    writer.WriteString("content", string.Empty);
                }
                writer.WriteEndObject();
                return;
            }
        }
        throw new InvalidOperationException($"CSV does not contain a column named '{name}'.");
    }

    private static void WriteOptionalMessage(Utf8JsonWriter writer, string role, string[] headers, CsvRow<byte> row, string name)
    {
        for (int i = 0; i < headers.Length; i++)
        {
            if (string.Equals(headers[i], name, StringComparison.OrdinalIgnoreCase))
            {
                if (i < row.ColumnCount)
                {
                    writer.WriteStartObject();
                    writer.WriteString("role", role);
                    WriteColumnProperty(writer, "content", row[i]);
                    writer.WriteEndObject();
                }
                return;
            }
        }
    }

    private static void WriteOptionalMessage(Utf8JsonWriter writer, string role, string[] headers, CsvRow<char> row, string name)
    {
        for (int i = 0; i < headers.Length; i++)
        {
            if (string.Equals(headers[i], name, StringComparison.OrdinalIgnoreCase))
            {
                if (i < row.ColumnCount)
                {
                    writer.WriteStartObject();
                    writer.WriteString("role", role);
                    WriteColumnProperty(writer, "content", row[i]);
                    writer.WriteEndObject();
                }
                return;
            }
        }
    }

    private static void WriteColumnProperty(Utf8JsonWriter writer, string propertyName, CsvColumn<byte> column)
    {
        var span = column.Span;
        if (span.Length >= 2 && span[0] == (byte)'"' && span[^1] == (byte)'"')
        {
            var inner = span[1..^1];
            if (inner.Contains((byte)'"'))
            {
                writer.WriteString(propertyName, column.UnquoteToString());
            }
            else
            {
                writer.WriteString(propertyName, inner);
            }
        }
        else
        {
            writer.WriteString(propertyName, span);
        }
    }

    private static void WriteColumnProperty(Utf8JsonWriter writer, ReadOnlySpan<byte> propertyName, CsvColumn<byte> column)
    {
        var span = column.Span;
        if (span.Length >= 2 && span[0] == (byte)'"' && span[^1] == (byte)'"')
        {
            var inner = span[1..^1];
            if (inner.Contains((byte)'"'))
            {
                writer.WriteString(propertyName, column.UnquoteToString());
            }
            else
            {
                writer.WriteString(propertyName, inner);
            }
        }
        else
        {
            writer.WriteString(propertyName, span);
        }
    }

    private static void WriteColumnProperty(Utf8JsonWriter writer, string propertyName, CsvColumn<char> column)
    {
        var span = column.Span;
        if (span.Length >= 2 && span[0] == '"' && span[^1] == '"')
        {
            var inner = span[1..^1];
            if (inner.Contains('"'))
            {
                writer.WriteString(propertyName, column.UnquoteToString());
            }
            else
            {
                writer.WriteString(propertyName, inner);
            }
        }
        else
        {
            writer.WriteString(propertyName, span);
        }
    }

    private static void RequireHeaders(string[]? headers)
    {
        if (headers is null)
            throw new InvalidOperationException("Shape requires a CSV header row (set HasHeaderRow = true).");
    }
}
