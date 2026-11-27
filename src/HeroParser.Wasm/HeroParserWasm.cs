using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text;
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
using HeroParser.SeparatedValues.Writing;

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

    /// <summary>
    /// Gets or sets a value indicating whether newlines are allowed inside quoted fields (defaults to <see langword="false"/>).
    /// </summary>
    public bool AllowNewlinesInsideQuotes { get; set; }
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
    /// Gets or sets the optional starting index of the column range. When omitted, the column follows sequentially from the previous column.
    /// </summary>
    public int? Start { get; set; }

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
        using var rows = Csv.ReadFromText(csvText, new CsvReadOptions
        {
            Delimiter = delim,
            AllowNewlinesInsideQuotes = options.AllowNewlinesInsideQuotes
        });

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

        ResolvedFixedWidthSpec[] resolved = ResolveFixedWidthSpecs(specs, rejectOverlaps: false, out _, out _);
        bool skipWhitespaceOnly = !HasAnyExplicitReadTrim(resolved);
        var reader = FixedWidth.ReadFromText(text ?? string.Empty, new FixedWidthReadOptions { AllowShortRows = true });
        var list = new List<Dictionary<string, string>>();
        foreach (var row in reader)
        {
            if (skipWhitespaceOnly ? row.RawRecord.IsWhiteSpace() : row.Length == 0) continue;

            var dict = new Dictionary<string, string>(resolved.Length, StringComparer.Ordinal);
            foreach (var spec in resolved)
            {
                dict[spec.Name] = ReadFixedWidthField(row, spec);
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

        bool isObjectRows = ValidateUniformRowShape(doc.RootElement);
        if (isObjectRows)
        {
            List<string> headers = CollectRecordHeaders(doc.RootElement);
            if (headers.Count == 0)
            {
                throw new ArgumentException("Object record rows must contain at least one property.", nameof(recordsJson));
            }

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

        ValidateUniformRowShape(doc.RootElement, requireObjects: true);
        _ = ResolveFixedWidthSpecs(specs, rejectOverlaps: true, out ResolvedFixedWidthSpec[] sortedSpecs, out int rowWidth);

        var sb = new StringBuilder();
        char[] rowBuffer = new char[rowWidth];
        string[] values = new string[sortedSpecs.Length];

        foreach (JsonElement item in doc.RootElement.EnumerateArray())
        {
            for (int i = 0; i < sortedSpecs.Length; i++)
            {
                values[i] = item.TryGetProperty(sortedSpecs[i].Name, out JsonElement propVal)
                    ? JsonElementToString(propVal)
                    : string.Empty;
            }
            WritePositionedFixedWidthRow(sb, sortedSpecs, values, rowBuffer);
        }

        return sb.ToString();
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

        _ = ResolveFixedWidthSpecs(specs, rejectOverlaps: true, out ResolvedFixedWidthSpec[] sortedSpecs, out int rowWidth);
        char delim = string.IsNullOrEmpty(delimiter) ? ',' : delimiter[0];
        using var reader = Csv.ReadFromText(csvText, new CsvReadOptions { Delimiter = delim });
        var sb = new StringBuilder();
        char[] rowBuffer = new char[rowWidth];
        string[] values = new string[sortedSpecs.Length];

        if (!reader.MoveNext())
        {
            if (includeHeader)
            {
                for (int i = 0; i < sortedSpecs.Length; i++)
                    values[i] = sortedSpecs[i].Name;
                WritePositionedFixedWidthRow(sb, sortedSpecs, values, rowBuffer);
                return sb.ToString();
            }
            return string.Empty;
        }

        var headerRow = reader.Current;
        var headerNames = new string[headerRow.ColumnCount];
        for (int i = 0; i < headerRow.ColumnCount; i++)
            headerNames[i] = headerRow[i].UnquoteToString().Trim();

        var columnMap = new int[sortedSpecs.Length];
        for (int i = 0; i < sortedSpecs.Length; i++)
        {
            string targetName = sortedSpecs[i].Name.Trim();
            columnMap[i] = Array.FindIndex(headerNames, h => string.Equals(h, targetName, StringComparison.OrdinalIgnoreCase));
        }

        if (includeHeader)
        {
            for (int i = 0; i < sortedSpecs.Length; i++)
                values[i] = sortedSpecs[i].Name;
            WritePositionedFixedWidthRow(sb, sortedSpecs, values, rowBuffer);
        }

        bool hasData = false;
        while (reader.MoveNext())
        {
            hasData = true;
            var row = reader.Current;
            for (int i = 0; i < sortedSpecs.Length; i++)
            {
                int csvIndex = columnMap[i];
                values[i] = (csvIndex >= 0 && csvIndex < row.ColumnCount)
                    ? row[csvIndex].UnquoteToString()
                    : string.Empty;
            }
            WritePositionedFixedWidthRow(sb, sortedSpecs, values, rowBuffer);
        }

        return (hasData || includeHeader) ? sb.ToString() : string.Empty;
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
        if (string.IsNullOrEmpty(fixedWidthText) || string.IsNullOrWhiteSpace(specsJson)) return string.Empty;
        var specs = JsonSerializer.Deserialize(specsJson, WasmJsonContext.Default.ListWasmColumnSpec);
        if (specs == null || specs.Count == 0) return string.Empty;

        ResolvedFixedWidthSpec[] resolved = ResolveFixedWidthSpecs(specs, rejectOverlaps: false, out _, out _);
        bool skipWhitespaceOnly = !HasAnyExplicitReadTrim(resolved);
        if (skipWhitespaceOnly && string.IsNullOrWhiteSpace(fixedWidthText)) return string.Empty;

        char delim = string.IsNullOrEmpty(delimiter) ? ',' : delimiter[0];
        using var sw = new StringWriter();
        using var csvWriter = new CsvStreamWriter(sw, new CsvWriteOptions
        {
            Delimiter = delim,
            NewLine = "\r\n",
            QuoteStyle = QuoteStyle.WhenNeeded,
            InjectionProtection = CsvInjectionProtection.EscapeWithQuote
        }, leaveOpen: true);

        if (includeHeader)
        {
            foreach (var spec in resolved)
                csvWriter.WriteField(spec.Name);
            csvWriter.EndRow();
        }

        var reader = FixedWidth.ReadFromText(fixedWidthText, new FixedWidthReadOptions { AllowShortRows = true });
        foreach (var row in reader)
        {
            if (skipWhitespaceOnly ? row.RawRecord.IsWhiteSpace() : row.Length == 0)
                continue;

            foreach (var spec in resolved)
            {
                csvWriter.WriteField(ReadFixedWidthField(row, spec));
            }
            csvWriter.EndRow();
        }

        csvWriter.Flush();
        return sw.ToString();
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

        var usedSheetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement sheetEl in doc.RootElement.EnumerateArray())
        {
            if (sheetEl.ValueKind != JsonValueKind.Object) continue;
            if (!sheetEl.TryGetProperty("records", out JsonElement recordsEl) || recordsEl.ValueKind != JsonValueKind.Array)
            {
                continue;
            }
            if (sheetEl.TryGetProperty("name", out JsonElement nameEl) && nameEl.ValueKind == JsonValueKind.String)
            {
                string? explicitName = nameEl.GetString();
                if (!string.IsNullOrWhiteSpace(explicitName))
                    usedSheetNames.Add(explicitName);
            }
        }

        using var memoryStream = new MemoryStream();
        int writtenSheets = 0;
        using (var xlsxWriter = new XlsxWriter(memoryStream, leaveOpen: true))
        {
            int fallbackIndex = 1;
            foreach (JsonElement sheetEl in doc.RootElement.EnumerateArray())
            {
                if (sheetEl.ValueKind != JsonValueKind.Object) continue;

                if (!sheetEl.TryGetProperty("records", out JsonElement recordsEl) || recordsEl.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                string sheetName = sheetEl.TryGetProperty("name", out JsonElement nameEl) && nameEl.ValueKind == JsonValueKind.String
                    ? (nameEl.GetString() ?? string.Empty)
                    : string.Empty;
                if (string.IsNullOrWhiteSpace(sheetName))
                {
                    do
                    {
                        sheetName = $"Sheet{fallbackIndex}";
                        fallbackIndex++;
                    }
                    while (!usedSheetNames.Add(sheetName));
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
    /// <param name="allowNewlinesInsideQuotes">Whether newlines inside quoted fields are allowed.</param>
    /// <returns>JSONL-formatted output string.</returns>
    [JSExport]
    public static string ConvertCsvToJsonl(
        string csvText,
        string? delimiter,
        bool hasHeader,
        bool allowNewlinesInsideQuotes)
    {
        var options = new CsvToJsonlOptions
        {
            Delimiter = string.IsNullOrEmpty(delimiter) ? ',' : delimiter[0],
            HasHeaderRow = hasHeader,
            AllowNewlinesInsideQuotes = allowNewlinesInsideQuotes
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
    /// Parses JSONL (JSON Lines) text directly into a formatted JSON array string, preserving exact numeric lexemes.
    /// </summary>
    /// <param name="jsonlText">The JSONL text (one JSON object or value per line).</param>
    /// <param name="indented">Whether to pretty-print the JSON array with indentation.</param>
    /// <returns>A formatted JSON array string containing the parsed elements.</returns>
    [JSExport]
    public static string ParseJsonlToFormattedJson(string jsonlText, bool indented)
    {
        if (string.IsNullOrWhiteSpace(jsonlText))
        {
            return "[]";
        }

        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = indented }))
        {
            writer.WriteStartArray();
            foreach (JsonElement element in Jsonl.DeserializeRecords(jsonlText, WasmJsonContext.Default.JsonElement))
            {
                element.WriteTo(writer);
            }
            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
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
    /// Losslessly parses JSONL lines or a JSON array of records and serializes them into canonical compact JSONL text
    /// without converting large numbers through JavaScript floating-point numbers.
    /// </summary>
    /// <param name="input">Raw JSONL text or a JSON array string.</param>
    /// <param name="inputFormat">
    /// Explicit input format selector: <c>"jsonl"</c> (default when null or whitespace) parses line-by-line JSONL,
    /// whereas <c>"json-array"</c> parses a single top-level JSON array and writes each element on its own line.
    /// </param>
    /// <returns>Canonical JSONL-formatted text.</returns>
    [JSExport]
    public static string RewriteJsonl(string input, string? inputFormat)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        string format = string.IsNullOrWhiteSpace(inputFormat) ? "jsonl" : inputFormat.Trim();
        if (format.Equals("json-array", StringComparison.OrdinalIgnoreCase))
        {
            using JsonDocument doc = JsonDocument.Parse(input);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new ArgumentException("Expected a JSON array when inputFormat is 'json-array'.", nameof(input));
            }

            if (doc.RootElement.GetArrayLength() == 0)
            {
                return string.Empty;
            }

            var arrayItems = new List<JsonElement>(doc.RootElement.GetArrayLength());
            foreach (JsonElement item in doc.RootElement.EnumerateArray())
            {
                arrayItems.Add(item);
            }

            return Jsonl.WriteToText(arrayItems, WasmJsonContext.Default.JsonElement);
        }

        if (!format.Equals("jsonl", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Unsupported JSONL input format '{inputFormat}'. Expected 'jsonl' or 'json-array'.", nameof(inputFormat));
        }

        var list = new List<JsonElement>();
        foreach (JsonElement element in Jsonl.DeserializeRecords(input, WasmJsonContext.Default.JsonElement))
        {
            list.Add(element.Clone());
        }

        return list.Count == 0
            ? string.Empty
            : Jsonl.WriteToText(list, WasmJsonContext.Default.JsonElement);
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
            ? DetectHtbDelimiter(csvText)
            : delimiter[0];

        var columns = new List<HtbColumn>();
        if (hasHeader)
        {
            CsvSchemaInferenceResult inferred = Csv.InferSchema(csvText, new CsvSchemaInferenceOptions
            {
                Delimiter = resolvedDelim,
                SampleRows = 10_000,
                MaxColumnCount = 2_048,
                DistinguishTemporalTypes = distinguishTemporalTypes,
                RequireLosslessNumericFormatting = true
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
            using var rowReader = Csv.ReadFromText(csvText, new CsvReadOptions
            {
                Delimiter = resolvedDelim,
                AllowNewlinesInsideQuotes = true,
                MaxColumnCount = 2_048
            });
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
            HasHeaderRow = hasHeader,
            AllowNewlinesInsideQuotes = true,
            MapByOrdinal = true,
            RejectExtraColumns = true,
            RejectLossyNumericValues = true
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

    private static char ParsePadChar(string? padChar)
    {
        if (string.IsNullOrEmpty(padChar))
            return ' ';

        char ch = padChar[0];
        if (ch is '\r' or '\n')
            throw new ArgumentException("Pad character cannot be a line terminator ('\\r' or '\\n').", nameof(padChar));

        return ch;
    }

    private static bool HasAnyExplicitReadTrim(ResolvedFixedWidthSpec[] specs)
    {
        for (int i = 0; i < specs.Length; i++)
        {
            if (specs[i].HasExplicitReadTrim)
                return true;
        }
        return false;
    }

    private static string ReadFixedWidthField(FixedWidthCharSpanRow row, ResolvedFixedWidthSpec spec)
    {
        if (!spec.HasExplicitReadTrim)
            return row.GetRawField(spec.Start, spec.Length).CharSpan.Trim().ToString();

        return row.GetField(spec.Start, spec.Length, spec.PadChar, spec.ReadAlignment).ToString();
    }

    private readonly record struct ResolvedFixedWidthSpec(
        string Name,
        int Start,
        int Length,
        FieldAlignment WriteAlignment,
        FieldAlignment ReadAlignment,
        char PadChar,
        bool HasExplicitReadTrim);

    private static ResolvedFixedWidthSpec[] ResolveFixedWidthSpecs(
        List<WasmColumnSpec> specs,
        bool rejectOverlaps,
        out ResolvedFixedWidthSpec[] sortedSpecs,
        out int rowWidth)
    {
        var resolved = new ResolvedFixedWidthSpec[specs.Count];
        int cursor = 0;

        for (int i = 0; i < specs.Count; i++)
        {
            WasmColumnSpec spec = specs[i];
            if (spec.Length <= 0)
                throw new ArgumentOutOfRangeException(nameof(specs), spec.Length, "Column length must be positive.");
            if (spec.Start is < 0)
                throw new ArgumentOutOfRangeException(nameof(specs), spec.Start, "Column start offset cannot be negative.");

            int start = spec.Start ?? cursor;
            if ((long)start + spec.Length > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(specs), start, "Column end offset exceeds maximum row length.");

            cursor = start + spec.Length;
            bool hasExplicitReadTrim = !string.IsNullOrWhiteSpace(spec.Alignment) || !string.IsNullOrEmpty(spec.PadChar);
            FieldAlignment alignment = ParseAlignment(spec.Alignment);
            resolved[i] = new ResolvedFixedWidthSpec(
                spec.Name ?? string.Empty,
                start,
                spec.Length,
                alignment,
                alignment,
                ParsePadChar(spec.PadChar),
                hasExplicitReadTrim);
        }

        sortedSpecs = [.. resolved];
        Array.Sort(sortedSpecs, static (a, b) => a.Start.CompareTo(b.Start));

        if (rejectOverlaps)
        {
            for (int i = 1; i < sortedSpecs.Length; i++)
            {
                ResolvedFixedWidthSpec prev = sortedSpecs[i - 1];
                ResolvedFixedWidthSpec curr = sortedSpecs[i];
                if (curr.Start < prev.Start + prev.Length)
                {
                    throw new ArgumentException(
                        $"Column '{curr.Name}' (start {curr.Start}, length {curr.Length}) overlaps with column '{prev.Name}' (start {prev.Start}, length {prev.Length}).",
                        nameof(specs));
                }
            }
        }

        rowWidth = 0;
        foreach (var col in sortedSpecs)
        {
            int end = col.Start + col.Length;
            if (end > rowWidth)
                rowWidth = end;
        }

        return resolved;
    }

    private static void WritePositionedFixedWidthRow(
        StringBuilder sb,
        ResolvedFixedWidthSpec[] sortedSpecs,
        ReadOnlySpan<string> values,
        char[] rowBuffer)
    {
        Array.Fill(rowBuffer, ' ');
        for (int i = 0; i < sortedSpecs.Length; i++)
        {
            ResolvedFixedWidthSpec col = sortedSpecs[i];
            string value = values[i];
            ReadOnlySpan<char> valueSpan = value.Length > col.Length
                ? value.AsSpan(0, col.Length)
                : value.AsSpan();
            Span<char> target = rowBuffer.AsSpan(col.Start, col.Length);
            target.Fill(col.PadChar);

            int offset = col.WriteAlignment switch
            {
                FieldAlignment.Right => col.Length - valueSpan.Length,
                FieldAlignment.Center => (col.Length - valueSpan.Length) / 2,
                _ => 0
            };
            valueSpan.CopyTo(target[offset..]);
        }

        sb.Append(rowBuffer);
        sb.Append("\r\n");
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

    private static bool ValidateUniformRowShape(JsonElement rowsArray, bool requireObjects = false)
    {
        JsonValueKind? expectedKind = null;
        foreach (JsonElement item in rowsArray.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object && item.ValueKind != JsonValueKind.Array)
            {
                throw new ArgumentException("Each record row must be a JSON object or JSON array.");
            }

            if (requireObjects && item.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("Fixed-width records must be JSON objects.");
            }

            expectedKind ??= item.ValueKind;
            if (item.ValueKind != expectedKind.Value)
            {
                throw new ArgumentException("All record rows must use the same JSON shape (either objects or arrays).");
            }
        }

        return expectedKind == JsonValueKind.Object;
    }

    private static void WriteSheetFromJsonArray(XlsxWriter.SheetWriter sheetWriter, JsonElement rowsArray, bool hasHeader)
    {
        if (rowsArray.GetArrayLength() == 0)
        {
            return;
        }

        bool isObjectRows = ValidateUniformRowShape(rowsArray);
        if (isObjectRows)
        {
            List<string> headers = CollectRecordHeaders(rowsArray);
            if (headers.Count == 0)
            {
                throw new ArgumentException("Object record rows must contain at least one property.");
            }

            if (hasHeader)
            {
                sheetWriter.WriteHeaderRow([.. headers]);
            }

            int rowIndex = hasHeader ? 2 : 1;
            foreach (JsonElement item in rowsArray.EnumerateArray())
            {
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
            case JsonValueKind.Number when TryGetLosslessExcelNumber(value, out double num):
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

    private static bool TryGetLosslessExcelNumber(JsonElement value, out double number)
    {
        const long MAX_EXCEL_EXACT_INTEGER = 999_999_999_999_999L;
        const double MIN_NORMAL_DOUBLE = 2.2250738585072014e-308;

        string raw = value.GetRawText();
        if (!raw.AsSpan().ContainsAny('.', 'e', 'E'))
        {
            if (!(raw.Length >= 2 && raw[0] == '-' && raw[1] == '0') &&
                value.TryGetInt64(out long intVal) &&
                intVal is >= -MAX_EXCEL_EXACT_INTEGER and <= MAX_EXCEL_EXACT_INTEGER)
            {
                number = intVal;
                return true;
            }

            number = 0;
            return false;
        }

        if (!value.TryGetDouble(out double d) || !double.IsFinite(d))
        {
            number = 0;
            return false;
        }

        if (d != 0d && Math.Abs(d) < MIN_NORMAL_DOUBLE)
        {
            number = 0;
            return false;
        }

        Span<char> formatted = stackalloc char[64];
        if (!d.TryFormat(formatted, out int written, provider: CultureInfo.InvariantCulture))
        {
            number = 0;
            return false;
        }

        Span<char> rawSig = stackalloc char[15];
        Span<char> fmtSig = stackalloc char[15];
        if (!TryCanonicalizeDecimalNumber(raw.AsSpan(), rawSig, out int rawSigLen, out long rawExp10, out bool rawIsZero, out bool rawIsNeg) ||
            !TryCanonicalizeDecimalNumber(formatted[..written], fmtSig, out int fmtSigLen, out long fmtExp10, out bool fmtIsZero, out bool fmtIsNeg))
        {
            number = 0;
            return false;
        }

        if (rawIsZero != fmtIsZero || rawIsNeg != fmtIsNeg)
        {
            number = 0;
            return false;
        }

        if (!rawIsZero && (rawExp10 != fmtExp10 || !rawSig[..rawSigLen].SequenceEqual(fmtSig[..fmtSigLen])))
        {
            number = 0;
            return false;
        }

        number = d;
        return true;
    }

    private static bool TryCanonicalizeDecimalNumber(
        ReadOnlySpan<char> text,
        Span<char> sigDigits,
        out int sigLen,
        out long exp10,
        out bool isZero,
        out bool isNegative)
    {
        sigLen = 0;
        exp10 = 0;
        isZero = false;
        isNegative = false;

        if (text.IsEmpty)
            return false;

        int i = 0;
        if (text[i] == '-')
        {
            isNegative = true;
            i++;
        }
        else if (text[i] == '+')
        {
            i++;
        }

        bool seenDot = false;
        bool anyDigit = false;
        int firstNonZeroIdx = -1;
        int digitsBeforeDot = 0;
        int totalDigits = 0;
        int pendingZeros = 0;

        for (; i < text.Length; i++)
        {
            char c = text[i];
            if (c is 'e' or 'E')
            {
                i++;
                break;
            }

            if (c == '.')
            {
                if (seenDot)
                    return false;
                seenDot = true;
                continue;
            }

            if (c < '0' || c > '9')
                return false;

            anyDigit = true;
            if (!seenDot)
                digitsBeforeDot++;

            if (firstNonZeroIdx < 0)
            {
                if (c != '0')
                {
                    if (sigDigits.Length == 0)
                        return false;
                    firstNonZeroIdx = totalDigits;
                    sigDigits[0] = c;
                    sigLen = 1;
                }
            }
            else if (c == '0')
            {
                pendingZeros++;
            }
            else
            {
                if (sigLen + pendingZeros + 1 > sigDigits.Length)
                    return false;
                for (int z = 0; z < pendingZeros; z++)
                    sigDigits[sigLen++] = '0';
                pendingZeros = 0;
                sigDigits[sigLen++] = c;
            }

            totalDigits++;
        }

        if (!anyDigit)
            return false;

        long explicitExp = 0;
        if (i < text.Length)
        {
            if (!long.TryParse(text[i..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out explicitExp))
                return false;
        }
        else if (text[^1] is 'e' or 'E')
        {
            return false;
        }

        if (firstNonZeroIdx < 0)
        {
            isZero = true;
            sigLen = 0;
            exp10 = 0;
            return true;
        }

        long baseExp = (long)digitsBeforeDot - 1 - firstNonZeroIdx;
        try
        {
            exp10 = checked(baseExp + explicitExp);
        }
        catch (OverflowException)
        {
            return false;
        }

        return true;
    }

    private static char DetectHtbDelimiter(string csvText)
    {
        try
        {
            return CsvDelimiterDetector.DetectDelimiter(csvText);
        }
        catch (InvalidOperationException) when (!ContainsUnquotedCandidateDelimiter(csvText))
        {
            return ',';
        }
    }

    private static bool ContainsUnquotedCandidateDelimiter(ReadOnlySpan<char> csv)
    {
        bool insideQuotes = false;
        for (int i = 0; i < csv.Length; i++)
        {
            char c = csv[i];
            if (c == '"')
            {
                if (insideQuotes && i + 1 < csv.Length && csv[i + 1] == '"')
                    i++;
                else
                    insideQuotes = !insideQuotes;
            }
            else if (!insideQuotes && c is ',' or ';' or '\t' or '|')
            {
                return true;
            }
        }
        return false;
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
