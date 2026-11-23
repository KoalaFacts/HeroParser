using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeroParser;
using HeroParser.AI;
using HeroParser.Conversion;
using HeroParser.Excels.Xlsx;
using HeroParser.FixedWidths;
using HeroParser.Htbs;
using HeroParser.Htbs.Reading;
using HeroParser.Htbs.Records;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Detection;
using HeroParser.SeparatedValues.Validation;

namespace HeroParser.Wasm;

/// <summary>
/// Options for parsing CSV data in the WebAssembly interop context.
/// </summary>
public class WasmCsvOptions
{
    /// <summary>
    /// Gets or sets the column delimiter character (e.g. ",", ";").
    /// </summary>
    public string? Delimiter { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the CSV has a header row.
    /// </summary>
    public bool HasHeader { get; set; } = true;
}

/// <summary>
/// Specification for mapping a single fixed-width column range.
/// </summary>
public class WasmColumnSpec
{
    /// <summary>
    /// Gets or sets the name of the column.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the starting index of the column range.
    /// </summary>
    public int Start { get; set; }

    /// <summary>
    /// Gets or sets the length of the column range.
    /// </summary>
    public int Length { get; set; }

    /// <summary>
    /// Gets or sets the optional field alignment (<c>"left"</c>, <c>"right"</c>, <c>"center"</c>, or <c>"none"</c>).
    /// </summary>
    public string? Alignment { get; set; }

    /// <summary>
    /// Gets or sets the optional single-character padding character (defaults to space).
    /// </summary>
    public string? PadChar { get; set; }
}

internal sealed record WasmCsvValidationError(
    string ErrorType,
    string Message,
    int RowNumber,
    int ColumnNumber,
    string? Expected,
    string? Actual);

internal sealed record WasmCsvValidationResult(
    bool IsValid,
    int TotalRows,
    int ColumnCount,
    string Delimiter,
    bool StoppedEarly,
    IReadOnlyList<string> Headers,
    IReadOnlyList<WasmCsvValidationError> Errors);

internal sealed record WasmCsvInferredColumn(
    string Name,
    string InferredType,
    bool IsNullable,
    int MaxLength);

internal sealed record WasmCsvSchemaResult(
    int SampledRowCount,
    IReadOnlyList<WasmCsvInferredColumn> Columns);

internal sealed record WasmHtbColumnInfo(
    string Name,
    string DataType,
    bool IsNullable);

internal sealed record WasmHtbInspectResult(
    int ByteLength,
    long RecordCount,
    IReadOnlyList<WasmHtbColumnInfo> Columns);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WasmCsvOptions))]
[JsonSerializable(typeof(List<WasmColumnSpec>))]
[JsonSerializable(typeof(List<Dictionary<string, string>>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<string[]>))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(List<JsonElement>))]
[JsonSerializable(typeof(WasmCsvValidationError))]
[JsonSerializable(typeof(WasmCsvValidationResult))]
[JsonSerializable(typeof(WasmCsvInferredColumn))]
[JsonSerializable(typeof(WasmCsvSchemaResult))]
[JsonSerializable(typeof(WasmHtbColumnInfo))]
[JsonSerializable(typeof(WasmHtbInspectResult))]
internal partial class WasmJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Provides WebAssembly interop endpoints for HeroParser using JSExport.
/// </summary>
[SupportedOSPlatform("browser")]
public partial class HeroParserWasm
{
    /// <summary>
    /// Parses CSV text data and serializes the result list as a JSON string.
    /// </summary>
    /// <param name="csvText">The raw CSV text content.</param>
    /// <param name="optionsJson">JSON-serialized WasmCsvOptions configuration settings.</param>
    /// <returns>A JSON string representing the parsed records list.</returns>
    [JSExport]
    public static string ParseCsvToJson(string csvText, string optionsJson)
    {
        var options = string.IsNullOrWhiteSpace(optionsJson)
            ? null
            : JsonSerializer.Deserialize(optionsJson, WasmJsonContext.Default.WasmCsvOptions);

        options ??= new WasmCsvOptions();

        char delim = (options.Delimiter != null && options.Delimiter.Length > 0) ? options.Delimiter[0] : ',';
        using var rows = Csv.ReadFromText(csvText, new CsvReadOptions { Delimiter = delim });

        if (options.HasHeader)
        {
            var list = new List<Dictionary<string, string>>();
            string[]? headers = null;
            foreach (var row in rows)
            {
                if (headers == null)
                {
                    headers = new string[row.ColumnCount];
                    for (int i = 0; i < row.ColumnCount; i++)
                    {
                        headers[i] = row[i].UnquoteToString();
                    }
                    continue;
                }

                var dict = new Dictionary<string, string>();
                for (int i = 0; i < Math.Min(row.ColumnCount, headers.Length); i++)
                {
                    dict[headers[i]] = row[i].UnquoteToString();
                }
                list.Add(dict);
            }
            return JsonSerializer.Serialize(list, WasmJsonContext.Default.ListDictionaryStringString);
        }
        else
        {
            var list = new List<string[]>();
            foreach (var row in rows)
            {
                var arr = new string[row.ColumnCount];
                for (int i = 0; i < row.ColumnCount; i++)
                {
                    arr[i] = row[i].UnquoteToString();
                }
                list.Add(arr);
            }
            return JsonSerializer.Serialize(list, WasmJsonContext.Default.ListStringArray);
        }
    }

    /// <summary>
    /// Parses fixed-width text data using the specified column specifications and returns a JSON string.
    /// </summary>
    /// <param name="text">The raw fixed-width text content.</param>
    /// <param name="specsJson">JSON-serialized list of WasmColumnSpec ranges.</param>
    /// <returns>A JSON string representing the parsed records list.</returns>
    [JSExport]
    public static string ParseFixedWidthToJson(string text, string specsJson)
    {
        if (string.IsNullOrWhiteSpace(specsJson)) return "[]";
        var specs = JsonSerializer.Deserialize(specsJson, WasmJsonContext.Default.ListWasmColumnSpec);
        if (specs == null || specs.Count == 0) return "[]";

        var reader = FixedWidth.ReadFromText(text ?? string.Empty, new FixedWidthReadOptions { AllowShortRows = true });
        var list = new List<Dictionary<string, string>>();
        foreach (var row in reader)
        {
            if (row.Length == 0) continue;

            var dict = new Dictionary<string, string>(specs.Count, StringComparer.Ordinal);
            foreach (var spec in specs)
            {
                FieldAlignment alignment = ParseAlignment(spec.Alignment);
                char padChar = ParsePadChar(spec.PadChar);
                dict[spec.Name] = row.GetField(spec.Start, spec.Length, padChar, alignment).ToString();
            }
            list.Add(dict);
        }
        return JsonSerializer.Serialize(list, WasmJsonContext.Default.ListDictionaryStringString);
    }

    /// <summary>
    /// Parses Excel (.xlsx) workbook bytes and returns the specified sheet data as a JSON string.
    /// </summary>
    /// <param name="excelBytes">The raw binary bytes of the Excel workbook.</param>
    /// <param name="sheetName">The name of the sheet to parse, or empty for the first sheet.</param>
    /// <param name="hasHeader">A value indicating whether the sheet has a header row.</param>
    /// <returns>A JSON string representing the parsed rows.</returns>
    [JSExport]
    public static string ParseExcelToJson(byte[] excelBytes, string sheetName, bool hasHeader)
    {
        using var stream = new MemoryStream(excelBytes);
        var builder = Excel.Read();
        if (!string.IsNullOrWhiteSpace(sheetName))
        {
            builder = builder.FromSheet(sheetName);
        }
        // The row-level reader skips the header row unless told otherwise; this method needs it
        // returned as the first row so it can key the records (hasHeader) or keep it as data.
        var rows = builder.WithoutHeader().FromStream(stream);

        if (hasHeader)
        {
            var list = new List<Dictionary<string, string>>();
            string[]? headers = null;
            foreach (var row in rows)
            {
                if (headers == null)
                {
                    headers = row;
                    continue;
                }

                var dict = new Dictionary<string, string>();
                for (int i = 0; i < Math.Min(row.Length, headers.Length); i++)
                {
                    dict[headers[i]] = row[i];
                }
                list.Add(dict);
            }
            return JsonSerializer.Serialize(list, WasmJsonContext.Default.ListDictionaryStringString);
        }
        else
        {
            return JsonSerializer.Serialize(rows, WasmJsonContext.Default.ListStringArray);
        }
    }

    /// <summary>
    /// Reads the worksheet names from an Excel (.xlsx) workbook in workbook order.
    /// </summary>
    /// <param name="excelBytes">The raw binary bytes of the Excel workbook.</param>
    /// <returns>A JSON array string of worksheet names.</returns>
    [JSExport]
    public static string GetExcelSheetNames(byte[] excelBytes)
    {
        if (excelBytes == null || excelBytes.Length == 0)
        {
            return "[]";
        }

        using var stream = new MemoryStream(excelBytes, writable: false);
        IReadOnlyList<string> names = Excel.GetSheetNames(stream);
        return JsonSerializer.Serialize([.. names], WasmJsonContext.Default.ListString);
    }

    /// <summary>
    /// Analyzes sample rows to detect and return the most probable CSV delimiter character.
    /// </summary>
    /// <param name="sampleRows">A sample string containing multiple rows of CSV data.</param>
    /// <returns>The detected delimiter character as a string (defaulting to "," if none detected).</returns>
    [JSExport]
    public static string DetectCsvDelimiter(string sampleRows)
    {
        if (string.IsNullOrEmpty(sampleRows)) return ",";
        char result = CsvDelimiterDetector.DetectDelimiter(sampleRows);
        return result.ToString();
    }

    /// <summary>
    /// Serializes a list of records (objects or arrays) to CSV format and returns it as a string.
    /// </summary>
    /// <param name="recordsJson">JSON-serialized array of record objects or cell arrays.</param>
    /// <param name="optionsJson">JSON-serialized WasmCsvOptions configuration settings.</param>
    /// <returns>The CSV content as a string.</returns>
    [JSExport]
    public static string WriteCsv(string recordsJson, string optionsJson)
    {
        if (string.IsNullOrWhiteSpace(recordsJson)) return string.Empty;
        using JsonDocument doc = JsonDocument.Parse(recordsJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
        {
            return string.Empty;
        }

        var options = string.IsNullOrWhiteSpace(optionsJson)
            ? null
            : JsonSerializer.Deserialize(optionsJson, WasmJsonContext.Default.WasmCsvOptions);
        options ??= new WasmCsvOptions();

        char delim = (options.Delimiter != null && options.Delimiter.Length > 0) ? options.Delimiter[0] : ',';

        using var stringWriter = new StringWriter();
        using var writer = Csv.CreateWriter(stringWriter, new SeparatedValues.Writing.CsvWriteOptions { Delimiter = delim });

        List<string> headers = CollectRecordHeaders(doc.RootElement);
        if (headers.Count > 0)
        {
            if (options.HasHeader)
            {
                foreach (string header in headers)
                {
                    writer.WriteField(header);
                }
                writer.EndRow();
            }

            foreach (JsonElement item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;

                foreach (string header in headers)
                {
                    string value = item.TryGetProperty(header, out JsonElement propVal)
                        ? JsonElementToString(propVal)
                        : string.Empty;
                    writer.WriteField(value);
                }
                writer.EndRow();
            }
        }
        else
        {
            foreach (JsonElement row in doc.RootElement.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array) continue;

                foreach (JsonElement cell in row.EnumerateArray())
                {
                    writer.WriteField(JsonElementToString(cell));
                }
                writer.EndRow();
            }
        }

        // The writer buffers; flush it into the StringWriter before reading the text.
        writer.Flush();
        return stringWriter.ToString();
    }

    /// <summary>
    /// Serializes a list of records to a fixed-width formatted string using the column specifications.
    /// </summary>
    /// <param name="recordsJson">JSON-serialized array of record objects.</param>
    /// <param name="specsJson">JSON-serialized list of WasmColumnSpec ranges.</param>
    /// <returns>The fixed-width content as a string.</returns>
    [JSExport]
    public static string WriteFixedWidth(string recordsJson, string specsJson)
    {
        if (string.IsNullOrWhiteSpace(recordsJson) || string.IsNullOrWhiteSpace(specsJson)) return string.Empty;
        using JsonDocument doc = JsonDocument.Parse(recordsJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
        {
            return string.Empty;
        }

        var specs = JsonSerializer.Deserialize(specsJson, WasmJsonContext.Default.ListWasmColumnSpec);
        if (specs == null || specs.Count == 0) return string.Empty;

        using var stringWriter = new StringWriter();
        using var writer = FixedWidth.CreateWriter(stringWriter);

        foreach (JsonElement item in doc.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;

            foreach (var spec in specs)
            {
                string val = item.TryGetProperty(spec.Name, out JsonElement propVal)
                    ? JsonElementToString(propVal)
                    : string.Empty;
                writer.WriteField(val, spec.Length, ParseAlignment(spec.Alignment), ParsePadChar(spec.PadChar));
            }
            writer.EndRow();
        }

        // The writer buffers; flush it into the StringWriter before reading the text.
        writer.Flush();
        return stringWriter.ToString();
    }

    /// <summary>
    /// Converts CSV text directly to fixed-width format using the supplied column specifications.
    /// </summary>
    /// <param name="csvText">The CSV input text (header row mapped by column name).</param>
    /// <param name="specsJson">JSON-serialized list of WasmColumnSpec definitions.</param>
    /// <param name="delimiter">Optional CSV delimiter (defaults to comma when null or empty).</param>
    /// <param name="includeHeader">Whether to emit a fixed-width header row.</param>
    /// <returns>The fixed-width formatted text.</returns>
    [JSExport]
    public static string ConvertCsvToFixedWidth(string csvText, string specsJson, string? delimiter, bool includeHeader)
    {
        if (string.IsNullOrWhiteSpace(csvText) || string.IsNullOrWhiteSpace(specsJson)) return string.Empty;
        var specs = JsonSerializer.Deserialize(specsJson, WasmJsonContext.Default.ListWasmColumnSpec);
        if (specs == null || specs.Count == 0) return string.Empty;

        FixedWidthFieldDefinition[] columns = ToFixedWidthDefinitions(specs);
        char delim = string.IsNullOrEmpty(delimiter) ? ',' : delimiter[0];
        var options = new CsvToFixedWidthOptions
        {
            Delimiter = delim,
            IncludeHeader = includeHeader
        };
        return CsvToFixedWidthConverter.Convert(csvText, columns, options);
    }

    /// <summary>
    /// Converts fixed-width text directly to CSV format using the supplied column specifications.
    /// </summary>
    /// <param name="fixedWidthText">The fixed-width input text.</param>
    /// <param name="specsJson">JSON-serialized list of WasmColumnSpec definitions.</param>
    /// <param name="delimiter">Optional CSV delimiter (defaults to comma when null or empty).</param>
    /// <param name="includeHeader">Whether to emit a CSV header row.</param>
    /// <returns>The CSV formatted text.</returns>
    [JSExport]
    public static string ConvertFixedWidthToCsv(string fixedWidthText, string specsJson, string? delimiter, bool includeHeader)
    {
        if (string.IsNullOrWhiteSpace(fixedWidthText) || string.IsNullOrWhiteSpace(specsJson)) return string.Empty;
        var specs = JsonSerializer.Deserialize(specsJson, WasmJsonContext.Default.ListWasmColumnSpec);
        if (specs == null || specs.Count == 0) return string.Empty;

        FixedWidthFieldDefinition[] columns = ToFixedWidthDefinitions(specs);
        char delim = string.IsNullOrEmpty(delimiter) ? ',' : delimiter[0];
        var options = new FixedWidthToCsvOptions
        {
            Delimiter = delim,
            IncludeHeader = includeHeader
        };
        return FixedWidthToCsvConverter.Convert(fixedWidthText, columns, options);
    }

    /// <summary>
    /// Serializes a list of records (supporting strings, numbers, booleans, and nulls) to Excel (.xlsx) workbook bytes.
    /// </summary>
    /// <param name="recordsJson">JSON-serialized array of record objects or cell arrays.</param>
    /// <param name="sheetName">The name of the worksheet to write (defaulting to "Sheet1").</param>
    /// <param name="hasHeader">A value indicating whether to write a header row.</param>
    /// <returns>The binary bytes of the generated .xlsx workbook.</returns>
    [JSExport]
    public static byte[] WriteExcel(string recordsJson, string sheetName, bool hasHeader)
    {
        if (string.IsNullOrWhiteSpace(recordsJson)) return [];
        using JsonDocument doc = JsonDocument.Parse(recordsJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(sheetName)) sheetName = "Sheet1";

        using var memoryStream = new MemoryStream();
        using (var xlsxWriter = new XlsxWriter(memoryStream, leaveOpen: true))
        {
            using var sheetWriter = xlsxWriter.StartSheet(sheetName);
            WriteSheetFromJsonArray(sheetWriter, doc.RootElement, hasHeader);
            sheetWriter.Close();
        }

        return memoryStream.ToArray();
    }

    /// <summary>
    /// Serializes multiple named sheets to a single Excel (.xlsx) workbook.
    /// </summary>
    /// <param name="sheetsJson">JSON array of <c>{ name: string, records: any[] }</c> sheet definitions.</param>
    /// <param name="hasHeader">Whether each object-based sheet emits a header row.</param>
    /// <returns>The binary bytes of the generated multi-sheet .xlsx workbook.</returns>
    [JSExport]
    public static byte[] WriteExcelMultiSheet(string sheetsJson, bool hasHeader)
    {
        if (string.IsNullOrWhiteSpace(sheetsJson)) return [];
        using JsonDocument doc = JsonDocument.Parse(sheetsJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
        {
            return [];
        }

        using var memoryStream = new MemoryStream();
        int writtenSheets = 0;
        using (var xlsxWriter = new XlsxWriter(memoryStream, leaveOpen: true))
        {
            int fallbackIndex = 1;
            foreach (JsonElement sheetEl in doc.RootElement.EnumerateArray())
            {
                if (sheetEl.ValueKind != JsonValueKind.Object) continue;

                string sheetName = sheetEl.TryGetProperty("name", out JsonElement nameEl) && nameEl.ValueKind == JsonValueKind.String
                    ? (nameEl.GetString() ?? string.Empty)
                    : string.Empty;
                if (string.IsNullOrWhiteSpace(sheetName))
                {
                    sheetName = $"Sheet{fallbackIndex}";
                }
                fallbackIndex++;

                if (!sheetEl.TryGetProperty("records", out JsonElement recordsEl) || recordsEl.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                using var sheetWriter = xlsxWriter.StartSheet(sheetName);
                WriteSheetFromJsonArray(sheetWriter, recordsEl, hasHeader);
                sheetWriter.Close();
                writtenSheets++;
            }
        }

        return writtenSheets > 0 ? memoryStream.ToArray() : [];
    }

    /// <summary>
    /// Validates CSV text structure and returns a JSON-serialized validation result.
    /// </summary>
    /// <param name="csvText">Raw CSV text to validate.</param>
    /// <param name="delimiter">Optional delimiter character, or <see langword="null"/> to auto-detect.</param>
    /// <param name="hasHeader">Whether the CSV includes a header row.</param>
    /// <param name="allowMissingColumns">Whether rows with fewer or more columns than expected are permitted.</param>
    /// <param name="maxErrors">Maximum number of errors to collect before stopping early.</param>
    /// <returns>A camelCase JSON string representing the validation result.</returns>
    [JSExport]
    public static string ValidateCsvToJson(
        string csvText,
        string? delimiter,
        bool hasHeader,
        bool allowMissingColumns,
        int maxErrors)
    {
        var options = new CsvValidationOptions
        {
            Delimiter = string.IsNullOrEmpty(delimiter) ? null : delimiter[0],
            HasHeaderRow = hasHeader,
            CheckConsistentColumnCount = !allowMissingColumns,
            MaxErrors = maxErrors > 0 ? maxErrors : 100
        };

        CsvValidationResult result = Csv.Validate(csvText ?? string.Empty, options);
        var errors = new WasmCsvValidationError[result.Errors.Count];
        for (int i = 0; i < result.Errors.Count; i++)
        {
            CsvValidationError e = result.Errors[i];
            errors[i] = new WasmCsvValidationError(
                e.ErrorType.ToString(),
                e.Message,
                e.RowNumber,
                e.ColumnNumber,
                e.Expected,
                e.Actual);
        }

        var dto = new WasmCsvValidationResult(
            result.IsValid,
            result.TotalRows,
            result.ColumnCount,
            result.Delimiter.ToString(),
            result.StoppedEarly,
            result.Headers,
            errors);

        return JsonSerializer.Serialize(dto, WasmJsonContext.Default.WasmCsvValidationResult);
    }

    /// <summary>
    /// Infers column names, data types, and nullability from CSV text and returns a JSON-serialized schema result.
    /// </summary>
    /// <param name="csvText">Raw CSV text to inspect.</param>
    /// <param name="delimiter">Optional delimiter character, or <see langword="null"/> to auto-detect.</param>
    /// <param name="sampleRows">Maximum number of rows to sample when inferring types.</param>
    /// <param name="distinguishTemporalTypes">Whether to distinguish DateOnly, TimeOnly, DateTimeOffset, and TimeSpan.</param>
    /// <returns>A camelCase JSON string representing the inferred schema.</returns>
    [JSExport]
    public static string InferCsvSchemaToJson(
        string csvText,
        string? delimiter,
        int sampleRows,
        bool distinguishTemporalTypes)
    {
        if (string.IsNullOrWhiteSpace(csvText))
        {
            return JsonSerializer.Serialize(new WasmCsvSchemaResult(0, []), WasmJsonContext.Default.WasmCsvSchemaResult);
        }

        var options = new CsvSchemaInferenceOptions
        {
            Delimiter = string.IsNullOrEmpty(delimiter) ? null : delimiter[0],
            SampleRows = sampleRows > 0 ? sampleRows : 100,
            DistinguishTemporalTypes = distinguishTemporalTypes
        };

        CsvSchemaInferenceResult result = Csv.InferSchema(csvText, options);
        var cols = new WasmCsvInferredColumn[result.Columns.Count];
        for (int i = 0; i < result.Columns.Count; i++)
        {
            CsvInferredColumn c = result.Columns[i];
            cols[i] = new WasmCsvInferredColumn(c.Name, c.InferredType.ToString(), c.IsNullable, c.MaxLength);
        }

        var dto = new WasmCsvSchemaResult(result.SampledRowCount, cols);
        return JsonSerializer.Serialize(dto, WasmJsonContext.Default.WasmCsvSchemaResult);
    }

    /// <summary>
    /// Cleans markdown fences and repairs unclosed trailing quotes in LLM-generated CSV text.
    /// </summary>
    /// <param name="text">Raw text produced by an LLM.</param>
    /// <param name="quote">Optional quote character (defaults to double quote when null or empty).</param>
    /// <param name="escape">Optional escape character; pass an empty string to disable escaping or <see langword="null"/> to use backslash.</param>
    /// <returns>Repaired CSV text.</returns>
    [JSExport]
    public static string RepairLlmCsv(string text, string? quote, string? escape)
    {
        char quoteChar = string.IsNullOrEmpty(quote) ? '"' : quote[0];
        char? escapeChar = escape is null ? '\\' : (escape.Length == 0 ? null : escape[0]);
        return LlmRepair.RepairText(text ?? string.Empty, quoteChar, escapeChar);
    }

    /// <summary>
    /// Converts CSV text into flat JSONL objects.
    /// </summary>
    /// <param name="csvText">Raw CSV text to convert.</param>
    /// <param name="delimiter">Optional delimiter character (defaults to comma when null or empty).</param>
    /// <param name="hasHeader">Whether the CSV includes a header row.</param>
    /// <returns>JSONL-formatted output string.</returns>
    [JSExport]
    public static string ConvertCsvToJsonl(string csvText, string? delimiter, bool hasHeader)
    {
        var options = new CsvToJsonlOptions
        {
            Delimiter = string.IsNullOrEmpty(delimiter) ? ',' : delimiter[0],
            HasHeaderRow = hasHeader
        };

        return CsvToJsonlConverter.Convert(csvText ?? string.Empty, CsvToJsonlShape.FlatObject(), options);
    }

    /// <summary>
    /// Parses JSONL (JSON Lines) text into a JSON array string.
    /// </summary>
    /// <param name="jsonlText">The JSONL text (one JSON object or value per line).</param>
    /// <returns>A JSON array string containing the parsed elements.</returns>
    [JSExport]
    public static string ParseJsonlToJson(string jsonlText)
    {
        if (string.IsNullOrWhiteSpace(jsonlText))
        {
            return "[]";
        }

        var list = new List<JsonElement>();
        foreach (JsonElement element in Jsonl.DeserializeRecords(jsonlText, WasmJsonContext.Default.JsonElement))
        {
            list.Add(element.Clone());
        }

        return JsonSerializer.Serialize(list, WasmJsonContext.Default.ListJsonElement);
    }

    /// <summary>
    /// Serializes a JSON array of records into JSONL (one compact JSON object per line).
    /// </summary>
    /// <param name="recordsJson">JSON array string containing objects or values.</param>
    /// <returns>JSONL-formatted text.</returns>
    [JSExport]
    public static string WriteJsonl(string recordsJson)
    {
        if (string.IsNullOrWhiteSpace(recordsJson))
        {
            return string.Empty;
        }

        using JsonDocument doc = JsonDocument.Parse(recordsJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
        {
            return string.Empty;
        }

        var list = new List<JsonElement>(doc.RootElement.GetArrayLength());
        foreach (JsonElement item in doc.RootElement.EnumerateArray())
        {
            list.Add(item);
        }

        return Jsonl.WriteToText(list, WasmJsonContext.Default.JsonElement);
    }

    /// <summary>
    /// Converts JSONL text into CSV by inferring columns across the leading records.
    /// </summary>
    /// <param name="jsonlText">Raw JSONL text to convert.</param>
    /// <param name="delimiter">Optional CSV delimiter (defaults to comma when null or empty).</param>
    /// <returns>The CSV formatted text.</returns>
    [JSExport]
    public static string ConvertJsonlToCsv(string jsonlText, string? delimiter)
    {
        if (string.IsNullOrWhiteSpace(jsonlText))
        {
            return string.Empty;
        }

        char delim = string.IsNullOrEmpty(delimiter) ? ',' : delimiter[0];
        return JsonlToCsvConverter.Convert(jsonlText, new JsonlToCsvOptions { Delimiter = delim });
    }

    /// <summary>
    /// Encodes CSV text into the compact HeroParser Tabular Binary (HTB) format using inferred column types.
    /// </summary>
    /// <param name="csvText">Raw CSV text to encode.</param>
    /// <param name="delimiter">Optional CSV delimiter, or <see langword="null"/> to auto-detect.</param>
    /// <param name="hasHeader">Whether the CSV includes a header row.</param>
    /// <param name="distinguishTemporalTypes">Whether temporal inference distinguishes DateOnly, TimeOnly, DateTimeOffset, and TimeSpan.</param>
    /// <returns>The encoded HTB binary payload.</returns>
    [JSExport]
    public static byte[] ConvertCsvToHtb(
        string csvText,
        string? delimiter,
        bool hasHeader,
        bool distinguishTemporalTypes)
    {
        if (string.IsNullOrWhiteSpace(csvText))
        {
            return [];
        }

        char resolvedDelim = string.IsNullOrEmpty(delimiter)
            ? CsvDelimiterDetector.DetectDelimiter(csvText)
            : delimiter[0];

        var columns = new List<HtbColumn>();
        if (hasHeader)
        {
            CsvSchemaInferenceResult inferred = Csv.InferSchema(csvText, new CsvSchemaInferenceOptions
            {
                Delimiter = resolvedDelim,
                SampleRows = 10_000,
                DistinguishTemporalTypes = distinguishTemporalTypes
            });

            for (int i = 0; i < inferred.Columns.Count; i++)
            {
                CsvInferredColumn c = inferred.Columns[i];
                string colName = string.IsNullOrWhiteSpace(c.Name) ? $"Col{i + 1}" : c.Name;
                columns.Add(new HtbColumn(colName, MapInferredTypeToHtb(c.InferredType), isNullable: true));
            }
        }
        else
        {
            using var rowReader = Csv.ReadFromText(csvText, new CsvReadOptions { Delimiter = resolvedDelim });
            if (!rowReader.MoveNext())
            {
                return [];
            }

            int count = rowReader.Current.ColumnCount;
            for (int i = 0; i < count; i++)
            {
                columns.Add(new HtbColumn($"Col{i + 1}", HtbDataType.String, isNullable: true));
            }
        }

        if (columns.Count == 0)
        {
            return [];
        }

        var schema = new HtbSchema(columns);
        using var output = new MemoryStream();
        CsvToHtbConverter.Convert(csvText, output, schema, new CsvToHtbOptions
        {
            Delimiter = resolvedDelim,
            HasHeaderRow = hasHeader
        });

        return output.ToArray();
    }

    /// <summary>
    /// Decodes a binary HTB payload back into CSV text.
    /// </summary>
    /// <param name="htbBytes">The raw HTB binary bytes.</param>
    /// <param name="delimiter">Optional CSV delimiter (defaults to comma when null or empty).</param>
    /// <param name="includeHeader">Whether to include the header row in the CSV output.</param>
    /// <returns>The decoded CSV text.</returns>
    [JSExport]
    public static string ConvertHtbToCsv(byte[] htbBytes, string? delimiter, bool includeHeader)
    {
        if (htbBytes == null || htbBytes.Length == 0)
        {
            return string.Empty;
        }

        char delim = string.IsNullOrEmpty(delimiter) ? ',' : delimiter[0];
        using var input = new MemoryStream(htbBytes, writable: false);
        using var output = new StringWriter();
        HtbToCsvConverter.Convert(input, output, new HtbToCsvOptions
        {
            Delimiter = delim,
            IncludeHeaderRow = includeHeader
        });
        return output.ToString();
    }

    /// <summary>
    /// Inspects the binary schema header and record count of an HTB payload without decoding values into text.
    /// </summary>
    /// <param name="htbBytes">The raw HTB binary bytes.</param>
    /// <returns>A camelCase JSON string containing byte length, record count, and column schema metadata.</returns>
    [JSExport]
    public static string InspectHtbSchemaToJson(byte[] htbBytes)
    {
        if (htbBytes == null || htbBytes.Length == 0)
        {
            return JsonSerializer.Serialize(new WasmHtbInspectResult(0, 0, []), WasmJsonContext.Default.WasmHtbInspectResult);
        }

        using var stream = new MemoryStream(htbBytes, writable: false);
        using var reader = new HtbStreamReader(stream, HtbReadOptions.Default, leaveOpen: true);
        HtbSchema schema = reader.ParseHeader();

        int colCount = schema.Columns.Count;
        int maskLen = (colCount + 7) / 8;
        byte[] rented = ArrayPool<byte>.Shared.Rent(maskLen);
        try
        {
            Span<byte> mask = rented.AsSpan(0, maskLen);
            while (!reader.IsEndOfStream())
            {
                reader.IncrementRecordCount();
                reader.ReadMask(mask);
                for (int i = 0; i < colCount; i++)
                {
                    if ((mask[i / 8] & (1 << (i % 8))) != 0)
                    {
                        continue;
                    }
                    reader.SkipColumnValue(schema.Columns[i].DataType);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }

        var cols = new WasmHtbColumnInfo[colCount];
        for (int i = 0; i < colCount; i++)
        {
            HtbColumn col = schema.Columns[i];
            cols[i] = new WasmHtbColumnInfo(col.Name, col.DataType.ToString(), col.IsNullable);
        }

        var dto = new WasmHtbInspectResult(htbBytes.Length, reader.RecordsRead, cols);
        return JsonSerializer.Serialize(dto, WasmJsonContext.Default.WasmHtbInspectResult);
    }

    private static string JsonElementToString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.Number => value.GetRawText(),
        _ => value.GetRawText(),
    };

    private static FieldAlignment ParseAlignment(string? alignment) => alignment?.Trim().ToLowerInvariant() switch
    {
        "right" => FieldAlignment.Right,
        "center" => FieldAlignment.Center,
        "none" => FieldAlignment.None,
        _ => FieldAlignment.Left,
    };

    private static char ParsePadChar(string? padChar) => string.IsNullOrEmpty(padChar) ? ' ' : padChar[0];

    private static FixedWidthFieldDefinition[] ToFixedWidthDefinitions(List<WasmColumnSpec> specs)
    {
        var columns = new FixedWidthFieldDefinition[specs.Count];
        for (int i = 0; i < specs.Count; i++)
        {
            WasmColumnSpec spec = specs[i];
            columns[i] = new FixedWidthFieldDefinition(
                spec.Name,
                spec.Length,
                ParseAlignment(spec.Alignment),
                ParsePadChar(spec.PadChar));
        }
        return columns;
    }

    private static List<string> CollectRecordHeaders(JsonElement rootArray)
    {
        var headers = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement item in rootArray.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;

            foreach (JsonProperty prop in item.EnumerateObject())
            {
                if (seen.Add(prop.Name))
                {
                    headers.Add(prop.Name);
                }
            }
        }
        return headers;
    }

    private static void WriteSheetFromJsonArray(XlsxWriter.SheetWriter sheetWriter, JsonElement rowsArray, bool hasHeader)
    {
        if (rowsArray.GetArrayLength() == 0)
        {
            return;
        }

        List<string> headers = CollectRecordHeaders(rowsArray);
        if (headers.Count > 0)
        {
            if (hasHeader)
            {
                sheetWriter.WriteHeaderRow([.. headers]);
            }

            int rowIndex = hasHeader ? 2 : 1;
            foreach (JsonElement item in rowsArray.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;

                sheetWriter.StartRow(rowIndex++);
                for (int i = 0; i < headers.Count; i++)
                {
                    if (item.TryGetProperty(headers[i], out JsonElement propVal))
                    {
                        WriteExcelCellFromJson(sheetWriter, i + 1, propVal);
                    }
                    else
                    {
                        sheetWriter.WriteCellEmpty(i + 1);
                    }
                }
                sheetWriter.EndRow();
            }
            return;
        }

        int arrayRowIndex = 1;
        foreach (JsonElement row in rowsArray.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array) continue;

            sheetWriter.StartRow(arrayRowIndex++);
            int colIndex = 1;
            foreach (JsonElement cell in row.EnumerateArray())
            {
                WriteExcelCellFromJson(sheetWriter, colIndex++, cell);
            }
            sheetWriter.EndRow();
        }
    }

    private static void WriteExcelCellFromJson(XlsxWriter.SheetWriter sheetWriter, int columnIndex, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Number when value.TryGetDouble(out double num) && !double.IsNaN(num) && !double.IsInfinity(num):
                sheetWriter.WriteCellNumber(columnIndex, num);
                break;
            case JsonValueKind.True:
                sheetWriter.WriteCellBoolean(columnIndex, true);
                break;
            case JsonValueKind.False:
                sheetWriter.WriteCellBoolean(columnIndex, false);
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                sheetWriter.WriteCellEmpty(columnIndex);
                break;
            case JsonValueKind.String:
                sheetWriter.WriteCellString(columnIndex, value.GetString() ?? string.Empty);
                break;
            case JsonValueKind.Object:
            case JsonValueKind.Array:
            case JsonValueKind.Number:
            default:
                sheetWriter.WriteCellString(columnIndex, value.GetRawText());
                break;
        }
    }

    private static HtbDataType MapInferredTypeToHtb(CsvInferredType inferredType) => inferredType switch
    {
        CsvInferredType.Integer => HtbDataType.Int32,
        CsvInferredType.Long => HtbDataType.Int64,
        CsvInferredType.Decimal => HtbDataType.Decimal,
        CsvInferredType.Boolean => HtbDataType.Boolean,
        CsvInferredType.DateTime => HtbDataType.DateTime,
        CsvInferredType.DateOnly => HtbDataType.DateOnly,
        CsvInferredType.TimeOnly => HtbDataType.TimeOnly,
        CsvInferredType.DateTimeOffset => HtbDataType.DateTimeOffset,
        CsvInferredType.TimeSpan => HtbDataType.TimeSpan,
        CsvInferredType.Guid => HtbDataType.Guid,
        CsvInferredType.String or _ => HtbDataType.String,
    };
}
