using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeroParser;
using HeroParser.AI;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Detection;

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
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WasmCsvOptions))]
[JsonSerializable(typeof(List<WasmColumnSpec>))]
[JsonSerializable(typeof(List<Dictionary<string, string>>))]
[JsonSerializable(typeof(List<string[]>))]
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
                        headers[i] = row[i].ToString();
                    }
                    continue;
                }

                var dict = new Dictionary<string, string>();
                for (int i = 0; i < Math.Min(row.ColumnCount, headers.Length); i++)
                {
                    dict[headers[i]] = row[i].ToString();
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
                    arr[i] = row[i].ToString();
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

        var lines = text.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        var list = new List<Dictionary<string, string>>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var dict = new Dictionary<string, string>();
            foreach (var spec in specs)
            {
                if (spec.Start < line.Length)
                {
                    int len = Math.Min(spec.Length, line.Length - spec.Start);
                    dict[spec.Name] = line.Substring(spec.Start, len).Trim();
                }
                else
                {
                    dict[spec.Name] = string.Empty;
                }
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
        if (!hasHeader)
        {
            builder = builder.WithoutHeader();
        }

        var rows = builder.FromStream(stream);

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
    /// Serializes a list of dictionaries to CSV format and returns it as a string.
    /// </summary>
    /// <param name="recordsJson">JSON-serialized list of dictionaries representing the records.</param>
    /// <param name="optionsJson">JSON-serialized WasmCsvOptions configuration settings.</param>
    /// <returns>The CSV content as a string.</returns>
    [JSExport]
    public static string WriteCsv(string recordsJson, string optionsJson)
    {
        if (string.IsNullOrWhiteSpace(recordsJson)) return string.Empty;
        var list = JsonSerializer.Deserialize(recordsJson, WasmJsonContext.Default.ListDictionaryStringString);
        if (list == null || list.Count == 0) return string.Empty;

        var options = string.IsNullOrWhiteSpace(optionsJson)
            ? null
            : JsonSerializer.Deserialize(optionsJson, WasmJsonContext.Default.WasmCsvOptions);
        options ??= new WasmCsvOptions();

        char delim = (options.Delimiter != null && options.Delimiter.Length > 0) ? options.Delimiter[0] : ',';

        var headers = new List<string>(list[0].Keys);

        using var stringWriter = new StringWriter();
        using var writer = Csv.CreateWriter(stringWriter, new SeparatedValues.Writing.CsvWriteOptions { Delimiter = delim });

        if (options.HasHeader)
        {
            foreach (var header in headers)
            {
                writer.WriteField(header);
            }
            writer.EndRow();
        }

        foreach (var dict in list)
        {
            foreach (var header in headers)
            {
                writer.WriteField(dict.GetValueOrDefault(header, string.Empty));
            }
            writer.EndRow();
        }

        return stringWriter.ToString();
    }

    /// <summary>
    /// Serializes a list of dictionaries to a fixed-width formatted string using the column specifications.
    /// </summary>
    /// <param name="recordsJson">JSON-serialized list of dictionaries representing the records.</param>
    /// <param name="specsJson">JSON-serialized list of WasmColumnSpec ranges.</param>
    /// <returns>The fixed-width content as a string.</returns>
    [JSExport]
    public static string WriteFixedWidth(string recordsJson, string specsJson)
    {
        if (string.IsNullOrWhiteSpace(recordsJson) || string.IsNullOrWhiteSpace(specsJson)) return string.Empty;
        var list = JsonSerializer.Deserialize(recordsJson, WasmJsonContext.Default.ListDictionaryStringString);
        if (list == null || list.Count == 0) return string.Empty;

        var specs = JsonSerializer.Deserialize(specsJson, WasmJsonContext.Default.ListWasmColumnSpec);
        if (specs == null || specs.Count == 0) return string.Empty;

        using var stringWriter = new StringWriter();
        using var writer = FixedWidth.CreateWriter(stringWriter);

        foreach (var dict in list)
        {
            foreach (var spec in specs)
            {
                string val = dict.GetValueOrDefault(spec.Name, string.Empty);
                writer.WriteField(val, spec.Length, FieldAlignment.Left);
            }
            writer.EndRow();
        }

        return stringWriter.ToString();
    }

    /// <summary>
    /// Serializes a list of dictionaries to Excel (.xlsx) workbook bytes and returns the byte array.
    /// </summary>
    /// <param name="recordsJson">JSON-serialized list of dictionaries representing the records.</param>
    /// <param name="sheetName">The name of the worksheet to write (defaulting to "Sheet1").</param>
    /// <param name="hasHeader">A value indicating whether to write a header row.</param>
    /// <returns>The binary bytes of the generated .xlsx workbook.</returns>
    [JSExport]
    public static byte[] WriteExcel(string recordsJson, string sheetName, bool hasHeader)
    {
        if (string.IsNullOrWhiteSpace(recordsJson)) return [];
        var list = JsonSerializer.Deserialize(recordsJson, WasmJsonContext.Default.ListDictionaryStringString);
        if (list == null || list.Count == 0) return [];

        if (string.IsNullOrWhiteSpace(sheetName)) sheetName = "Sheet1";

        var headers = new List<string>(list[0].Keys).ToArray();

        using var memoryStream = new MemoryStream();
        using (var xlsxWriter = new Excels.Xlsx.XlsxWriter(memoryStream, leaveOpen: true))
        {
            using var sheetWriter = xlsxWriter.StartSheet(sheetName);

            if (hasHeader)
            {
                sheetWriter.WriteHeaderRow(headers);
            }

            int rowIndex = hasHeader ? 2 : 1;
            foreach (var dict in list)
            {
                sheetWriter.StartRow(rowIndex++);
                for (int i = 0; i < headers.Length; i++)
                {
                    string val = dict.GetValueOrDefault(headers[i], string.Empty);
                    sheetWriter.WriteCellString(i + 1, val);
                }
                sheetWriter.EndRow();
            }

            sheetWriter.Close();
        }

        return memoryStream.ToArray();
    }
}
