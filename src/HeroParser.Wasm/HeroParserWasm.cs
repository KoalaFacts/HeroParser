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

public class WasmCsvOptions
{
    public string? Delimiter { get; set; }
    public bool HasHeader { get; set; } = true;
}

public class WasmColumnSpec
{
    public string Name { get; set; } = string.Empty;
    public int Start { get; set; }
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

[SupportedOSPlatform("browser")]
public partial class HeroParserWasm
{
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

    [JSExport]
    public static string DetectCsvDelimiter(string sampleRows)
    {
        if (string.IsNullOrEmpty(sampleRows)) return ",";
        char result = CsvDelimiterDetector.DetectDelimiter(sampleRows);
        return result.ToString();
    }

    [JSExport]
    public static string RepairTabularOutput(string rawText)
    {
        return LlmRepair.RepairText(rawText);
    }
}
