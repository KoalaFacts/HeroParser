using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HeroParser.AI;
using HeroParser.Conversion;
using HeroParser.Excels.Reading.Data;
using HeroParser.Excels.Xlsx;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Writing;
using HeroParser.Htbs.Records;
using HeroParser.JsonLines.Reading;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Detection;
using HeroParser.SeparatedValues.Validation;
using HeroParser.SeparatedValues.Writing;
using HeroParser.Cli.AI;
using AnsiConsole = HeroParser.Console.AnsiConsole;
using Color = HeroParser.Console.Color;
using Markup = HeroParser.Console.Markup;
using Panel = HeroParser.Console.Panel;
using Rule = HeroParser.Console.Rule;
using Style = HeroParser.Console.Style;
using Table = HeroParser.Console.Table;
using Text = HeroParser.Console.Text;
using ProgressBarColumn = HeroParser.Console.ProgressBarColumn;
using TaskDescriptionColumn = HeroParser.Console.TaskDescriptionColumn;
using PercentageColumn = HeroParser.Console.PercentageColumn;
using RemainingTimeColumn = HeroParser.Console.RemainingTimeColumn;
using SpinnerColumn = HeroParser.Console.SpinnerColumn;
using TableBorder = HeroParser.Console.TableBorder;
using BoxBorder = HeroParser.Console.BoxBorder;
using PanelHeader = HeroParser.Console.PanelHeader;
using SysConsole = System.Console;

namespace HeroParser.Cli;

internal sealed partial class CliCommands
{
    private const int EXCEL_MAX_COLUMNS = 16_384;
    private static readonly UTF8Encoding utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly CliConsole console = new();

    public bool Detect(string path)
    {
        if (!File.Exists(path))
        {
            console.Error($"File not found: {path}");
            return false;
        }

        console.Info($"Analyzing delimiter and encoding for: {path}");

        try
        {
            var (bytes, length) = ReadCsvSample(path);
            bool utf32LittleEndian = IsUtf32LittleEndian(bytes, length);
            bool utf32BigEndian = IsUtf32BigEndian(bytes, length);
            bool utf16LittleEndian = IsUtf16LittleEndian(bytes, length);
            bool utf16BigEndian = IsUtf16BigEndian(bytes, length);
            bool hasBom = length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            string content;
            if (utf32LittleEndian || utf32BigEndian)
            {
                int payloadBytes = length - 4;
                int alignedBytes = payloadBytes - (payloadBytes % 4);
                Encoding utf32 = utf32LittleEndian
                    ? Encoding.UTF32
                    : new UTF32Encoding(bigEndian: true, byteOrderMark: true);
                content = utf32.GetString(bytes, 4, alignedBytes);
            }
            else if (utf16LittleEndian || utf16BigEndian)
            {
                int payloadBytes = length - 2;
                int alignedBytes = payloadBytes - (payloadBytes % 2);
                content = utf16LittleEndian
                    ? Encoding.Unicode.GetString(bytes, 2, alignedBytes)
                    : Encoding.BigEndianUnicode.GetString(bytes, 2, alignedBytes);
            }
            else
            {
                content = Encoding.UTF8.GetString(bytes, hasBom ? 3 : 0, length - (hasBom ? 3 : 0));
            }

            // Delimiter detection
            var detection = CsvDelimiterDetector.Detect(content);
            string readableDelimiter = detection.DetectedDelimiter switch
            {
                ',' => "Comma (,)",
                ';' => "Semicolon (;)",
                '|' => "Pipe (|)",
                '\t' => "Tab (\\t)",
                _ => $"Character '{detection.DetectedDelimiter}'"
            };

            // Display results in a table
            var table = new Table();
            table.Border(TableBorder.Rounded);
            table.AddColumn("[blue bold]Metadata Property[/]");
            table.AddColumn("[blue bold]Value[/]");

            table.AddRow("File Path", path);
            table.AddRow("Encoding", utf32LittleEndian ? "UTF-32 LE with BOM"
                : utf32BigEndian ? "UTF-32 BE with BOM"
                : utf16LittleEndian ? "UTF-16 LE with BOM"
                : utf16BigEndian ? "UTF-16 BE with BOM"
                : hasBom ? "UTF-8 with BOM" : "UTF-8 or ASCII (assumed)");
            table.AddRow("Detected Delimiter", readableDelimiter);

            string confColor = detection.Confidence >= 80 ? "green" : (detection.Confidence >= 50 ? "yellow" : "red");
            table.AddRow("Confidence Score", $"[{confColor}]{detection.Confidence}/100[/]");
            table.AddRow("Sampled Rows", detection.SampledRows.ToString());
            table.AddRow("Avg Columns/Row", detection.AverageDelimiterCount.ToString("F2"));

            SysConsole.WriteLine();
            console.Header("Detection Results");
            AnsiConsole.Write(table);

            var candTable = new Table();
            candTable.Border(TableBorder.Rounded);
            candTable.AddColumn("[blue bold]Delimiter Candidate[/]");
            candTable.AddColumn("[blue bold]Occurrences Across Sampled Lines[/]");

            foreach (var cand in detection.CandidateCounts)
            {
                string label = cand.Key switch
                {
                    ',' => "comma (,)",
                    ';' => "semicolon (;)",
                    '|' => "pipe (|)",
                    '\t' => "tab (\\t)",
                    _ => $"'{cand.Key}'"
                };
                candTable.AddRow(label, cand.Value.ToString());
            }

            SysConsole.WriteLine();
            console.Header("Delimiter Occurrence Counts");
            AnsiConsole.Write(candTable);
            SysConsole.WriteLine();

            if (detection.Confidence >= 80)
            {
                console.Success("Delimiter detected with high confidence.");
            }
            else
            {
                console.Warning("Low delimiter confidence. Please manually verify using --delimiter option.");
            }
            return true;
        }
        catch (Exception ex)
        {
            console.Error($"Detection failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> ValidateAsync(string path, char? delimiter, CsvImportPlan? plan = null, string? reportPath = null)
    {
        if (!File.Exists(path))
        {
            console.Error($"File not found: {path}");
            return false;
        }

        console.Info($"Running structural validation on: {path}");

        try
        {
            if (reportPath is not null && Path.GetFullPath(path).Equals(Path.GetFullPath(reportPath),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                console.Error("Validation report path must differ from the CSV input path.");
                return false;
            }

            if (plan is not null && !IsCsvInput(path))
            {
                console.Error("CSV import plans support .csv and .tsv UTF-8 input only.");
                return false;
            }

            var options = new CsvValidationOptions { MaxRows = 0 };
            if (delimiter.HasValue)
            {
                options = options with { Delimiter = delimiter.Value };
            }
            if (plan is not null)
            {
                options = plan.ToValidationOptions();
                var (sample, sampleLength) = ReadCsvSample(path);
                if (IsUtf32LittleEndian(sample, sampleLength) || IsUtf32BigEndian(sample, sampleLength))
                {
                    console.Error("CSV import plans support UTF-8 only; this file has a UTF-32 BOM.");
                    return false;
                }
                if (IsUtf16LittleEndian(sample, sampleLength) || IsUtf16BigEndian(sample, sampleLength))
                {
                    console.Error("CSV import plans support UTF-8 only; this file has a UTF-16 BOM.");
                    return false;
                }
            }
            var result = await Csv.ValidateFileAsync(path, options).ConfigureAwait(false);

            if (reportPath is not null)
            {
                new CsvValidationReport(result).Save(reportPath);
                console.Info($"Saved validation report: {reportPath}");
            }

            if (result.IsValid)
            {
                var panel = new Panel(new Markup("[green]File structure is completely valid! No inconsistencies detected.[/]"))
                {
                    Header = new PanelHeader("[bold green] Validation Success [/]"),
                    Border = BoxBorder.Rounded,
                    BorderStyle = new Style(Color.Green)
                };
                AnsiConsole.Write(panel);
            }
            else
            {
                var errTable = new Table();
                errTable.Border(TableBorder.Rounded);
                errTable.AddColumn("[red bold]Row[/]");
                errTable.AddColumn("[red bold]Validation Error Description[/]");

                int displayLimit = Math.Min(10, result.Errors.Count);
                for (int i = 0; i < displayLimit; i++)
                {
                    var err = result.Errors[i];
                    errTable.AddRow($"[red]{err.RowNumber}[/]", Markup.Escape(err.Message));
                }

                var panel = new Panel(errTable)
                {
                    Header = new PanelHeader($"[bold red] Validation Failed ({result.Errors.Count} Errors) [/]"),
                    Border = BoxBorder.Rounded,
                    BorderStyle = new Style(Color.Red)
                };
                AnsiConsole.Write(panel);

                if (result.Errors.Count > displayLimit)
                {
                    console.Info($"... and {result.Errors.Count - displayLimit} more errors.");
                }
                if (result.StoppedEarly)
                    console.Warning("Validation stopped after reaching the error limit; later rows were not checked.");
            }
            return result.IsValid;
        }
        catch (Exception ex)
        {
            console.Error($"Validation failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> ProfileAsync(string path, char? delimiter, string? sheet)
    {
        if (!File.Exists(path))
        {
            console.Error($"File not found: {path}");
            return false;
        }

        console.Info($"Profiling dataset: {path}");

        try
        {
            List<DynamicColumnStats> statsList;
            int totalRows;
            if (IsExcelInput(path))
            {
                (_, statsList, totalRows, _) = ProfileExcel(path, sheet);
            }
            else
            {
                (_, statsList, totalRows) = await ProfileCsvAsync(path, delimiter).ConfigureAwait(false);
            }
            var filename = Path.GetFileName(path);

            var table = new Table();
            table.Border(TableBorder.Rounded);
            table.AddColumn("[blue bold]Column Name[/]");
            table.AddColumn("[blue bold]Inferred Type[/]");
            table.AddColumn("[blue bold]Null Density[/]");
            table.AddColumn("[blue bold]Statistics / Frequency Details[/]");

            foreach (var stats in statsList)
            {
                double nullPct = totalRows > 0 ? (double)stats.NullCount / totalRows * 100 : 0;
                string nullStr = stats.NullCount > 0 ? $"[yellow]{stats.NullCount} ({nullPct:F1}%)[/]" : "[green]0 (0%)[/]";
                string typeName = stats.InferTypeName();

                string detail = "";
                if (totalRows == 0)
                {
                    detail = "[grey]No data[/]";
                }
                else if (typeName == "Integer" || typeName == "Decimal")
                {
                    double minVal = stats.Min == double.MaxValue ? 0 : stats.Min;
                    double maxVal = stats.Max == double.MinValue ? 0 : stats.Max;
                    double avg = stats.NonNullCount > 0 ? stats.Sum / stats.NonNullCount : 0;
                    detail = $"Range: [[{minVal:N2} to {maxVal:N2}]], Avg: {avg:N2}";
                }
                else if (typeName == "Boolean")
                {
                    double truePct = totalRows > 0 ? (double)stats.TrueCount / totalRows * 100 : 0;
                    detail = $"True: {stats.TrueCount} ({truePct:F1}%), False: {stats.FalseCount} ({100 - truePct:F1}%)";
                }
                else
                {
                    int distinctCount = stats.ValueCounts.Count;
                    var sb = new StringBuilder();
                    sb.Append(stats.CategoriesTruncated
                        ? $"At least {distinctCount} distinct categories tracked. Top tracked: "
                        : $"{distinctCount} distinct categories. Top: ");
                    var topValues = stats.ValueCounts.OrderByDescending(v => v.Value).Take(3).ToList();
                    for (int j = 0; j < topValues.Count; j++)
                    {
                        double pct = (double)topValues[j].Value / totalRows * 100;
                        sb.Append($"\"{Markup.Escape(topValues[j].Key)}\" ({pct:F1}%)");
                        if (j < topValues.Count - 1) sb.Append(", ");
                    }
                    detail = sb.ToString();
                }

                table.AddRow(stats.Name, typeName, nullStr, detail);
            }

            SysConsole.WriteLine();
            console.Header($"Dataset Profile: {filename} ({totalRows:N0} rows)");
            AnsiConsole.Write(table);
            SysConsole.WriteLine();
            return true;
        }
        catch (Exception ex)
        {
            console.Error($"Profiling failed: {ex.Message}");
            return false;
        }
    }

    public bool Convert(string inputPath, string outputPath, char? delimiter, string? shapeName, string? sheet, CsvImportPlan? plan = null)
    {
        if (!File.Exists(inputPath))
        {
            console.Error($"Input file not found: {inputPath}");
            return false;
        }

        console.Info($"Converting: {inputPath} -> {outputPath}");

        try
        {
            var inExt = GetFormatExtension(inputPath);
            var outExt = GetFormatExtension(outputPath);
            if ((inExt == ".xlsx" && DetectCompressionFormat(inputPath) != CliCompressionFormat.None)
                || (outExt == ".xlsx" && DetectCompressionFormat(outputPath) != CliCompressionFormat.None))
            {
                console.Error("Compressed Excel (.xlsx.gz/.br/.zst) paths are not supported; .xlsx files are already ZIP-compressed.");
                return false;
            }
            if (plan is not null)
            {
                if (!IsCsvInput(inputPath) || outExt != ".jsonl")
                {
                    console.Error("CSV import plans currently support CSV/TSV to JSONL conversion only.");
                    return false;
                }

                var (sample, sampleLength) = ReadCsvSample(inputPath);
                if (IsUtf32LittleEndian(sample, sampleLength) || IsUtf32BigEndian(sample, sampleLength))
                {
                    console.Error("CSV import plans support UTF-8 only; this file has a UTF-32 BOM.");
                    return false;
                }
                if (IsUtf16LittleEndian(sample, sampleLength) || IsUtf16BigEndian(sample, sampleLength))
                {
                    console.Error("CSV import plans support UTF-8 only; this file has a UTF-16 BOM.");
                    return false;
                }

                var validation = Csv.ValidateFileAsync(inputPath, plan.ToValidationOptions())
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                if (!validation.IsValid)
                {
                    console.Error($"Conversion stopped: CSV does not match the import plan ({validation.Errors[0].Message}). Run validate --plan for details.");
                    return false;
                }
            }

            char inputDelimiter = delimiter ?? (inExt == ".tsv" ? '\t' : ',');

            if (IsCsvInput(inputPath) && outExt == ".jsonl")
            {
                CsvToJsonlShape shape = CsvToJsonlShape.FlatObject();
                if (!string.IsNullOrWhiteSpace(shapeName))
                {
                    if (shapeName.Equals("openai", StringComparison.OrdinalIgnoreCase))
                        shape = CsvToJsonlShape.OpenAiChat("System", "Question", "Answer");
                    else if (shapeName.Equals("anthropic", StringComparison.OrdinalIgnoreCase))
                        shape = CsvToJsonlShape.AnthropicMessages("Question", "Answer");
                }

                var options = new CsvToJsonlOptions
                {
                    Delimiter = inputDelimiter,
                    AllowNewlinesInsideQuotes = plan is not null,
                    MaxColumnCount = plan is null ? 100 : 1000,
                    MaxRowCount = plan is null ? 100_000 : int.MaxValue
                };
                CsvToJsonlConverter.Convert(inputPath, outputPath, shape, options);
                console.Success($"Converted CSV to JSONL successfully.");
            }
            else if (inExt == ".jsonl" && outExt is ".csv" or ".tsv")
            {
                var options = new JsonlToCsvOptions
                {
                    Delimiter = delimiter ?? (outExt == ".tsv" ? '\t' : ',')
                };
                JsonlToCsvConverter.Convert(inputPath, outputPath, options);
                console.Success($"Converted JSONL to CSV successfully.");
            }
            else if (IsCsvInput(inputPath) && outExt == ".xlsx")
            {
                ConvertCsvToExcel(inputPath, outputPath, inputDelimiter, sheet ?? "Sheet1");
                console.Success("Converted CSV to Excel successfully.");
            }
            else if (inExt == ".jsonl" && outExt == ".xlsx")
            {
                ConvertJsonlToExcel(inputPath, outputPath, sheet ?? "Sheet1");
                console.Success("Converted JSONL to Excel successfully.");
            }
            else if (IsCsvInput(inputPath) && outExt == ".htb")
            {
                ConvertCsvToHtb(inputPath, outputPath, inputDelimiter);
                console.Success("Converted CSV to HTB successfully.");
            }
            else if (inExt == ".htb" && outExt is ".csv" or ".tsv")
            {
                var options = new HtbToCsvOptions
                {
                    Delimiter = delimiter ?? (outExt == ".tsv" ? '\t' : ','),
                    MaxRowCount = int.MaxValue
                };
                ConvertHtbToCsv(inputPath, outputPath, options);
                console.Success("Converted HTB to CSV successfully.");
            }
            else if (IsCsvInput(inputPath) && outExt == ".txt") // CSV -> FixedWidth
            {
                ConvertCsvToFixedWidth(inputPath, outputPath, inputDelimiter);
                console.Success($"Converted CSV to Fixed-Width successfully.");
            }
            else if (inExt == ".txt" && outExt == ".csv") // FixedWidth -> CSV
            {
                console.Error("Conversion from Fixed-Width requires column widths. This CLI uses CSV/Excel as first class. Use the core API directly for manual layouts.");
                return false;
            }
            else if (inExt == ".xlsx") // Excel -> CSV or JSONL
            {
                if (outExt is not (".csv" or ".jsonl"))
                {
                    console.Error($"Unsupported output extension from Excel: {outExt}");
                    return false;
                }
                var allRows = Excel.Read().WithoutHeader().FromSheet(sheet ?? "Sheet1").FromFile(inputPath);
                if (allRows.Count == 0)
                {
                    WriteEmptyOutputFile(outputPath);
                    console.Warning("Excel sheet was empty.");
                    return true;
                }

                var headers = allRows[0];
                var dataRows = allRows.Skip(1).ToList();

                if (outExt == ".csv")
                {
                    using var csvWriter = Csv.CreateFileWriter(outputPath, new CsvWriteOptions { Delimiter = delimiter ?? ',' });
                    foreach (var row in allRows)
                    {
                        csvWriter.WriteRow(row);
                    }
                    console.Success("Converted Excel sheet to CSV successfully.");
                }
                else if (outExt == ".jsonl")
                {
                    if (dataRows.Count == 0)
                    {
                        WriteEmptyOutputFile(outputPath);
                    }
                    else
                    {
                        using var fileStream = OpenWriteStream(outputPath);
                        using var writer = new System.Text.Json.Utf8JsonWriter(fileStream);
                        foreach (var row in dataRows)
                        {
                            writer.WriteStartObject();
                            for (int i = 0; i < Math.Min(headers.Length, row.Length); i++)
                            {
                                writer.WriteString(headers[i], row[i]);
                            }
                            writer.WriteEndObject();
                            writer.Flush();
                            writer.Reset();
                            fileStream.WriteByte((byte)'\n');
                        }
                    }
                    console.Success("Converted Excel sheet to JSONL successfully.");
                }
                else
                {
                    console.Error($"Unsupported output extension from Excel: {outExt}");
                    return false;
                }
            }
            else
            {
                console.Error($"Unsupported conversion direction from {inExt} to {outExt}");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            console.Error($"Conversion failed: {ex.Message}");
            return false;
        }
    }

    public bool Repair(string inputPath, string outputPath)
    {
        if (!File.Exists(inputPath))
        {
            console.Error($"Input file not found: {inputPath}");
            return false;
        }

        console.Info($"Repairing: {inputPath} -> {outputPath}");

        try
        {
            var text = File.ReadAllText(inputPath);
            var repaired = LlmRepair.RepairText(text);
            File.WriteAllText(outputPath, repaired);
            console.Success("Text repaired successfully (stripped markdown blocks & resolved unbalanced quotes).");
            return true;
        }
        catch (Exception ex)
        {
            console.Error($"Repair failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> SchemaAsync(string path, char? delimiter, bool useAi, string? providerName, string? apiKey, string? model,
        LlmClient? aiClientOverride = null, CsvImportPlan? plan = null)
    {
        if (!File.Exists(path))
        {
            console.Error($"File not found: {path}");
            return false;
        }

        console.Info($"Generating schema for: {path}");

        try
        {
            if (plan is not null && !IsCsvInput(path))
            {
                console.Error("CSV import plans support .csv and .tsv UTF-8 input only.");
                return false;
            }

            var (sample, sampleLength) = ReadCsvSample(path);
            bool utf32 = IsUtf32LittleEndian(sample, sampleLength) || IsUtf32BigEndian(sample, sampleLength);
            bool utf16 = IsUtf16LittleEndian(sample, sampleLength) || IsUtf16BigEndian(sample, sampleLength);
            if (plan is not null && utf32)
            {
                console.Error("CSV import plans support UTF-8 only; this file has a UTF-32 BOM.");
                return false;
            }
            if (plan is not null && utf16)
            {
                console.Error("CSV import plans support UTF-8 only; this file has a UTF-16 BOM.");
                return false;
            }

            char effectiveDelimiter = delimiter ?? DetectSchemaDelimiter(path, sample, sampleLength, utf16);
            var schemaResult = await Csv.InferSchemaFileAsync(path, new CsvSchemaInferenceOptions
            {
                Delimiter = effectiveDelimiter,
                MaxColumnCount = plan is null ? 100 : 1000
            }).ConfigureAwait(false);
            if (plan is not null && schemaResult.Columns.Count != plan.ExpectedColumnCount)
            {
                console.Error($"CSV header has {schemaResult.Columns.Count} columns, but the import plan expects {plan.ExpectedColumnCount}.");
                return false;
            }

            console.Info($"Types inferred from {schemaResult.SampledRowCount} data rows (limit 100); this is not full-file validation.");

            var className = Path.GetFileNameWithoutExtension(path);
            // Replace non-alphanumeric for class name
            className = string.Concat(className.Where(char.IsLetterOrDigit));
            if (string.IsNullOrWhiteSpace(className)) className = "RecordModel";

            // Pascal case class name
            className = char.ToUpper(className[0]) + className[1..];

            var codeBuilder = new StringBuilder();
            codeBuilder.AppendLine("using System;");
            codeBuilder.AppendLine("using HeroParser.Attributes;");
            codeBuilder.AppendLine();
            codeBuilder.AppendLine("namespace HeroParser.Cli.Generated;");
            codeBuilder.AppendLine();
            codeBuilder.AppendLine("[GenerateBinder]");
            codeBuilder.AppendLine($"public sealed class {className}");
            codeBuilder.AppendLine("{");

            // Build a preview table for columns
            var schemaTable = new Table();
            schemaTable.Border(TableBorder.Rounded);
            schemaTable.AddColumn("[blue bold]Column Name[/]");
            schemaTable.AddColumn("[blue bold]Inferred Type[/]");
            schemaTable.AddColumn("[blue bold]Nullable[/]");
            schemaTable.AddColumn("[blue bold]Property Name[/]");

            foreach (var col in schemaResult.Columns)
            {
                var cSharpName = string.Concat(col.Name.Where(char.IsLetterOrDigit));
                if (string.IsNullOrEmpty(cSharpName)) cSharpName = "Property";
                cSharpName = char.ToUpper(cSharpName[0]) + cSharpName[1..];

                var propType = col.InferredType switch
                {
                    CsvInferredType.Integer => "int",
                    CsvInferredType.Long => "long",
                    CsvInferredType.Decimal => "double",
                    CsvInferredType.Boolean => "bool",
                    CsvInferredType.DateTime => "DateTime",
                    CsvInferredType.DateOnly => "DateOnly",
                    CsvInferredType.TimeOnly => "TimeOnly",
                    CsvInferredType.DateTimeOffset => "DateTimeOffset",
                    CsvInferredType.TimeSpan => "TimeSpan",
                    CsvInferredType.Guid => "Guid",
                    _ => "string"
                };

                schemaTable.AddRow(col.Name, propType, col.IsNullable ? "Yes" : "No", cSharpName);

                if (col.IsNullable && propType != "string")
                {
                    propType += "?";
                }

                codeBuilder.AppendLine($"    [TabularMap(Name = \"{col.Name.Replace("\"", "\\\"")}\")]");
                codeBuilder.AppendLine($"    public {propType} {cSharpName} {{ get; set; }}");
                codeBuilder.AppendLine();
            }

            codeBuilder.AppendLine("}");

            var localSchema = codeBuilder.ToString();

            SysConsole.WriteLine();
            console.Header("Schema Preview");
            AnsiConsole.Write(schemaTable);
            SysConsole.WriteLine();

            if (!useAi)
            {
                console.Header("Generated C# Class (Local Inference)");
                SysConsole.WriteLine(localSchema);
                console.Header("End of Class");
                return true;
            }

            // AI Schema Generation
            var filename = Path.GetFileName(path);
            var (headers, rows) = await ReadSchemaContextAsync(path, effectiveDelimiter, plan is null ? 100 : 1000).ConfigureAwait(false);
            var contextProfiler = new DynamicProfiler(headers);
            contextProfiler.ObserveRows(rows.Take(10));
            var contextCard = contextProfiler.GenerateContextCard(filename);

            var aiClient = aiClientOverride ?? LlmClient.CreateFromEnvironment(providerName, apiKey, model);

            string prompt = $"""
You are an expert compiler engineer and software architect.
Here is the dataset profile and sample rows:
{contextCard}

This profile covers at most the first 10 data rows, not the whole file.

Here is a basic inferred C# record class:
```csharp
{localSchema}
```

Task: Rewrite the C# record class to make it semantically rich and ready for production parsing in HeroParser:
1. Ensure property names are clean PascalCase matching the column meanings, mapped via `[TabularMap(Name = "...")]`.
2. Add bidirectional validators using the `[Validate(...)]` attribute where applicable. Validations support:
   - `NotNull = true` for non-nullable values
   - `NotEmpty = true` for string columns that should not be blank
   - `MinLength = X`, `MaxLength = Y`
   - `RangeMin = X`, `RangeMax = Y` for numeric columns
   - `Pattern = @"..."` for string regex matching (e.g. Email format, phone format, zip code)
3. Use specific types if observed data warrants it (e.g. use enums if a column has limited distinct categorical values, and define that enum).
4. Add XML docs (`/// <summary>...`) explaining each property's role.
5. Retain the `[GenerateBinder]` attribute on the class.

Output ONLY the complete C# code, wrapped inside a single C# markdown code block. Do not provide conversational text.
""";

            var response = await console.RunWithSpinnerAsync(
                "Querying LLM for schema optimizations",
                token => aiClient.AskAsync(prompt, token)
            );

            var repairedCode = LlmRepair.RepairText(response);
            SysConsole.WriteLine();
            console.Header("Generated AI-Optimized C# Class");
            SysConsole.WriteLine(repairedCode);
            console.Header("End of Class");
            return true;
        }
        catch (Exception ex)
        {
            console.Error($"Schema generation failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> QueryAsync(string path, char? delimiter, string? sheet, string userQuery, string? providerName, string? apiKey, string? model, LlmClient? aiClientOverride = null)
    {
        if (!File.Exists(path))
        {
            console.Error($"File not found: {path}");
            return false;
        }

        console.Info($"Loading dataset for AI Q&A...");

        try
        {
            string[] headers;
            List<string[]> sampleRows;
            var filename = Path.GetFileName(path);
            string card;
            if (!IsExcelInput(path))
            {
                var (sample, sampleLength) = ReadCsvSample(path);
                bool utf16 = IsUtf16LittleEndian(sample, sampleLength) || IsUtf16BigEndian(sample, sampleLength);
                char effectiveDelimiter = delimiter ?? DetectSchemaDelimiter(path, sample, sampleLength, utf16);
                var (streamHeaders, stats, totalRows) = await ProfileCsvAsync(path, effectiveDelimiter).ConfigureAwait(false);
                headers = streamHeaders;
                (_, sampleRows) = await ReadSchemaContextAsync(path, effectiveDelimiter, 100).ConfigureAwait(false);
                card = new DynamicProfiler(stats, totalRows).GenerateContextCard(filename);
            }
            else
            {
                (headers, var stats, var totalRows, sampleRows) = ProfileExcel(path, sheet);
                card = new DynamicProfiler(stats, totalRows).GenerateContextCard(filename);
            }

            // Format sample rows
            var sampleSb = new StringBuilder();
            sampleSb.AppendLine("| " + string.Join(" | ", headers) + " |");
            sampleSb.AppendLine("| " + string.Join(" | ", headers.Select(_ => "---")) + " |");
            foreach (var r in sampleRows.Take(10))
            {
                sampleSb.AppendLine("| " + string.Join(" | ", r.Select(c => c?.Replace("|", "\\|") ?? "")) + " |");
            }

            var aiClient = aiClientOverride ?? LlmClient.CreateFromEnvironment(providerName, apiKey, model);

            string prompt = $"""
You are a helpful data analyst querying a tabular dataset.
Here is the dataset profile card:
{card}

Here are the first 10 sample rows:
{sampleSb}

User Query: {userQuery}

Answer the query clearly and concisely based on the schema, stats, and sample rows. If you need to make numeric inferences or math calculations, show your step-by-step logic.
""";

            var response = await console.RunWithSpinnerAsync(
                "Analyzing dataset and generating answer",
                token => aiClient.AskAsync(prompt, token)
            );

            SysConsole.WriteLine();
            var panel = new Panel(new Text(response))
            {
                Header = new PanelHeader("[bold cyan] AI Query Response [/]"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.FromConsoleColor(ConsoleColor.Cyan))
            };
            AnsiConsole.Write(panel);
            SysConsole.WriteLine();
            return true;
        }
        catch (Exception ex)
        {
            console.Error($"Query failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> TranslateAsync(string path, char? delimiter, string? sheet, string transformPrompt, string outputPath, int batchSize, string? providerName, string? apiKey, string? model, LlmClient? aiClientOverride = null)
    {
        if (!File.Exists(path))
        {
            console.Error($"File not found: {path}");
            return false;
        }

        if (batchSize <= 0)
        {
            console.Error("Batch size must be positive.");
            return false;
        }
        if (Path.GetFullPath(path).Equals(Path.GetFullPath(outputPath),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            console.Error("Translation output path must differ from the input path.");
            return false;
        }

        console.Info($"Starting batch translation / transformation pipeline...");
        console.Info($"Output will be written to: {outputPath}");

        string? stagingPath = null;
        try
        {
            bool streamCsv = !IsExcelInput(path);
            char inputDelimiter = ',';
            if (streamCsv)
            {
                var (sample, sampleLength) = ReadCsvSample(path);
                bool utf16 = IsUtf16LittleEndian(sample, sampleLength) || IsUtf16BigEndian(sample, sampleLength);
                inputDelimiter = delimiter ?? DetectSchemaDelimiter(path, sample, sampleLength, utf16);
            }
            string[] headers;
            long totalRows;
            await using (IRowBatchSource counting = streamCsv
                ? await CsvRowBatchSource.OpenAsync(path, inputDelimiter).ConfigureAwait(false)
                : ExcelRowBatchSource.Open(path, sheet))
            {
                headers = counting.Headers;
                totalRows = await counting.CountRemainingRowsAsync().ConfigureAwait(false);
            }
            await using IRowBatchSource source = streamCsv
                ? await CsvRowBatchSource.OpenAsync(path, inputDelimiter).ConfigureAwait(false)
                : ExcelRowBatchSource.Open(path, sheet);
            var aiClient = aiClientOverride ?? LlmClient.CreateFromEnvironment(providerName, apiKey, model);

            var headersJoined = string.Join(",", headers);
            var duplicateInputColumns = new HashSet<string>(headers, StringComparer.Ordinal).Count == headers.Length
                ? null
                : headers.Select((name, index) => (name, index))
                    .GroupBy(column => column.name, StringComparer.Ordinal)
                    .Select(group => (Name: group.Key, Indexes: group.Select(column => column.index).ToArray()))
                    .ToArray();

            stagingPath = $"{Path.GetFullPath(outputPath)}.{Guid.NewGuid():N}.tmp";
            var stagingOptions = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None
            };
            if (!OperatingSystem.IsWindows())
                stagingOptions.UnixCreateMode = File.Exists(outputPath)
                    ? File.GetUnixFileMode(outputPath)
                    : UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stagingStream = new FileStream(stagingPath, stagingOptions))
            using (var fileWriter = Csv.CreateStreamWriter(stagingStream,
                new CsvWriteOptions { Delimiter = delimiter ?? ',' }, leaveOpen: false))
            {

                bool isFirstBatch = true;
                string[] outputHeaders = headers;

                char openBrace = '{';
                char closeBrace = '}';
                var batchJsonBuffer = new ArrayBufferWriter<byte>();
                var responsePropertyNames = new HashSet<string>(StringComparer.Ordinal);

                await AnsiConsole.Progress()
                    .Columns([
                        new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new RemainingTimeColumn(),
                    new SpinnerColumn()
                    ])
                    .StartAsync(async ctx =>
                    {
                        var progressTask = ctx.AddTask("[green]Transforming rows[/]", maxValue: totalRows);

                        for (long i = 0; i < totalRows; i += batchSize)
                        {
                            List<string[]> batch = await source.ReadBatchAsync(batchSize).ConfigureAwait(false);
                            if (batch.Count == 0)
                                throw new IOException("Input changed while translating; expected more rows.");
                            progressTask.Description = $"[green]Transforming rows {i + 1} to {Math.Min(i + batchSize, totalRows)} of {totalRows}[/]";

                            batchJsonBuffer.Clear();
                            using (var jsonWriter = new Utf8JsonWriter(batchJsonBuffer))
                            {
                                jsonWriter.WriteStartArray();
                                foreach (var r in batch)
                                {
                                    jsonWriter.WriteStartObject();
                                    if (duplicateInputColumns is null)
                                    {
                                        for (int c = 0; c < Math.Min(headers.Length, r.Length); c++)
                                            jsonWriter.WriteString(headers[c], r[c]);
                                    }
                                    else
                                    {
                                        foreach (var (name, indexes) in duplicateInputColumns)
                                        {
                                            for (int index = indexes.Length - 1; index >= 0; index--)
                                            {
                                                if (indexes[index] >= r.Length) continue;
                                                jsonWriter.WriteString(name, r[indexes[index]]);
                                                break;
                                            }
                                        }
                                    }
                                    jsonWriter.WriteEndObject();
                                }
                                jsonWriter.WriteEndArray();
                            }
                            string batchJson = Encoding.UTF8.GetString(batchJsonBuffer.WrittenSpan);

                            string prompt = $"""
You are a high-performance tabular data mapping agent. 
Task: Transform the input rows according to this prompt: "{transformPrompt}"

Input Columns: {headersJoined}
Input Rows (JSON):
{batchJson}

Instructions:
1. Process each row.
2. Return exactly one JSONL line per processed row. Each line must be a flat JSON object representing the columns of the row.
3. Keep the keys matching the columns (PascalCase or original header names) unless the instruction explicitly renames or adds fields.
4. Output ONLY the JSONL records. Do not output markdown code blocks (no ```json or ```), conversational text, or explanations. Each line must start with '{openBrace}' and end with '{closeBrace}'.
""";

                            // Call AI without inner status spinner, letting progress bar own rendering
                            var responseText = await aiClient.AskAsync(prompt).ConfigureAwait(false);

                            var cleaned = LlmRepair.RepairText(responseText);
                            var lines = cleaned.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                            int acceptedRows = 0;
                            foreach (var line in lines)
                            {
                                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("{")) continue;

                                try
                                {
                                    using var parsed = JsonDocument.Parse(line);
                                    JsonElement parsedObj = parsed.RootElement;
                                    if (parsedObj.ValueKind != JsonValueKind.Object) continue;

                                    if (isFirstBatch)
                                    {
                                        responsePropertyNames.Clear();
                                        var firstHeaders = new List<string>();
                                        foreach (JsonProperty property in parsedObj.EnumerateObject())
                                        {
                                            string name = property.Name;
                                            if (!responsePropertyNames.Add(name))
                                                throw new JsonException($"Duplicate property '{name}' in model output.");
                                            firstHeaders.Add(name);
                                        }
                                        isFirstBatch = false;
                                        outputHeaders = [.. firstHeaders];
                                        fileWriter.WriteRow(outputHeaders);
                                    }
                                    else
                                    {
                                        // Stable model schemas can be checked without allocating property-name strings per row.
                                        int propertyIndex = 0;
                                        bool sameOrder = true;
                                        foreach (JsonProperty property in parsedObj.EnumerateObject())
                                        {
                                            if (propertyIndex >= outputHeaders.Length || !property.NameEquals(outputHeaders[propertyIndex++]))
                                            {
                                                sameOrder = false;
                                                break;
                                            }
                                        }
                                        if (!sameOrder || propertyIndex != outputHeaders.Length)
                                        {
                                            responsePropertyNames.Clear();
                                            foreach (JsonProperty property in parsedObj.EnumerateObject())
                                            {
                                                if (!responsePropertyNames.Add(property.Name))
                                                    throw new JsonException($"Duplicate property '{property.Name}' in model output.");
                                            }
                                        }
                                    }

                                    var rowValues = new string[outputHeaders.Length];
                                    for (int colIndex = 0; colIndex < outputHeaders.Length; colIndex++)
                                    {
                                        rowValues[colIndex] = parsedObj.TryGetProperty(outputHeaders[colIndex], out JsonElement value)
                                            ? value.ValueKind switch
                                            {
                                                JsonValueKind.True => "true",
                                                JsonValueKind.False => "false",
                                                _ => value.ToString()
                                            }
                                            : "";
                                    }
                                    fileWriter.WriteRow(rowValues);
                                    acceptedRows++;
                                }
                                catch (Exception lineEx)
                                {
                                    AnsiConsole.MarkupLine($"[yellow]⚠ Failed to parse output line: {Markup.Escape(line)}. Error: {Markup.Escape(lineEx.Message)}[/]");
                                }
                            }

                            if (acceptedRows != batch.Count)
                                throw new InvalidDataException($"Model returned {acceptedRows} valid records for a batch of {batch.Count} rows.");

                            progressTask.Increment(batch.Count);
                        }

                        if ((await source.ReadBatchAsync(1).ConfigureAwait(false)).Count > 0)
                            throw new IOException("Input changed while translating; additional rows appeared.");

                        progressTask.Description = $"[green]Processed all {totalRows} rows[/]";
                    });
            }

            if (File.Exists(outputPath))
                File.Replace(stagingPath, outputPath, destinationBackupFileName: null);
            else
                File.Move(stagingPath, outputPath);
            stagingPath = null;
            console.Success($"Translation/transformation completed successfully. Saved to: {outputPath}");
            return true;
        }
        catch (Exception ex)
        {
            console.Error($"Translation pipeline failed: {ex.Message}");
            return false;
        }
        finally
        {
            if (stagingPath is not null)
                File.Delete(stagingPath);
        }
    }

    private static char DetectSchemaDelimiter(string path, byte[] sample, int length, bool utf16)
    {
        try
        {
            if (IsUtf32LittleEndian(sample, length) || IsUtf32BigEndian(sample, length))
            {
                int payloadBytes = length - 4;
                int alignedBytes = payloadBytes - (payloadBytes % 4);
                Encoding utf32 = IsUtf32LittleEndian(sample, length)
                    ? Encoding.UTF32
                    : new UTF32Encoding(bigEndian: true, byteOrderMark: true);
                return CsvDelimiterDetector.DetectDelimiter(utf32.GetString(sample, 4, alignedBytes));
            }
            if (utf16)
            {
                int payloadBytes = length - 2;
                int alignedBytes = payloadBytes - (payloadBytes % 2);
                string text = IsUtf16LittleEndian(sample, length)
                    ? Encoding.Unicode.GetString(sample, 2, alignedBytes)
                    : Encoding.BigEndianUnicode.GetString(sample, 2, alignedBytes);
                return CsvDelimiterDetector.DetectDelimiter(text);
            }
            return CsvDelimiterDetector.DetectDelimiter(sample.AsSpan(0, length));
        }
        catch (InvalidOperationException)
        {
            if (new FileInfo(path).Length > length)
                throw new InvalidOperationException("Cannot detect delimiter from the bounded sample; specify --delimiter explicitly.");
            return ',';
        }
    }

    private static async Task<(string[] Headers, List<string[]> Rows)> ReadSchemaContextAsync(
        string path, char delimiter, int maxColumns)
    {
        await using var source = await CsvRowBatchSource.OpenAsync(path, delimiter, maxColumns).ConfigureAwait(false);
        var rows = await source.ReadBatchAsync(10).ConfigureAwait(false);
        return (source.Headers, rows);
    }

    private static (string[] Headers, List<DynamicColumnStats> Stats, int RowCount, List<string[]> SampleRows)
        ProfileExcel(string path, string? sheet)
    {
        using var reader = Excel.CreateDataReader(path,
            new ExcelDataReaderOptions { AllowMissingColumns = true }, sheet ?? "Sheet1");
        var headers = new string[reader.FieldCount];
        for (int i = 0; i < headers.Length; i++)
            headers[i] = reader.GetName(i);

        var profiler = new DynamicProfiler(headers);
        var sampleRows = new List<string[]>(10);
        int rowCount = 0;
        while (reader.Read())
        {
            rowCount++;
            string[]? sample = sampleRows.Count < 10 ? new string[headers.Length] : null;
            for (int i = 0; i < profiler.Columns.Count; i++)
            {
                string? value = reader.GetValue(i) as string;
                profiler.ObserveCell(i, value);
                sample?[i] = value ?? "";
            }
            if (sample is not null)
                sampleRows.Add(sample);
        }

        return (headers, profiler.Columns, rowCount, sampleRows);
    }

    private static async Task<(string[] Headers, List<DynamicColumnStats> Stats, int RowCount)> ProfileCsvAsync(
        string path, char? delimiter)
    {
        var (sample, sampleLength) = ReadCsvSample(path);
        bool utf16 = IsUtf16LittleEndian(sample, sampleLength) || IsUtf16BigEndian(sample, sampleLength);
        char detectedDelimiter = delimiter ?? DetectSchemaDelimiter(path, sample, sampleLength, utf16);
        await using var reader = Csv.Read()
            .WithDelimiter(detectedDelimiter)
            .WithMaxRows(int.MaxValue)
            .WithMaxRowSize(null)
            .AllowNewlinesInQuotes()
            .FromTextFileAsync(path);
        if (!await reader.MoveNextAsync().ConfigureAwait(false))
            return ([], [], 0);

        var header = reader.Current;
        var columnNames = new string[header.ColumnCount];
        for (int i = 0; i < columnNames.Length; i++)
            columnNames[i] = header[i].UnquoteToString();

        var profiler = new DynamicProfiler(columnNames);
        int rowCount = 0;
        while (await reader.MoveNextAsync().ConfigureAwait(false))
        {
            rowCount++;
            var row = reader.Current;
            for (int i = 0; i < profiler.Columns.Count; i++)
            {
                if (i < row.ColumnCount)
                {
                    var cell = row[i];
                    // A quoted field carries its quotes and doubled quotes in the raw span;
                    // profile the field's value, not its CSV encoding.
                    if (cell.Span is [(byte)'"', ..])
                        profiler.ObserveCell(i, cell.UnquoteToString());
                    else
                        profiler.ObserveCellUtf8(i, cell.Span);
                }
                else
                    profiler.ObserveCell(i, null);
            }
        }

        return (columnNames, profiler.Columns, rowCount);
    }

    private static void ConvertCsvToExcel(string inputPath, string outputPath, char delimiter, string sheetName)
    {
        WriteAtomically(outputPath, stagingPath =>
        {
            using var outStream = new FileStream(stagingPath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var xlsxWriter = XlsxWriter.CreateStreaming(outStream, leaveOpen: false);
            using var sheetWriter = xlsxWriter.StartSheet(sheetName);

            var reader = Csv.Read()
                .WithDelimiter(delimiter)
                .WithMaxColumns(EXCEL_MAX_COLUMNS)
                .WithMaxRows(int.MaxValue)
                .WithMaxRowSize(null)
                .AllowNewlinesInQuotes()
                .FromTextFileAsync(inputPath);
            try
            {
                int rowNum = 0;
                while (reader.MoveNextAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult())
                {
                    rowNum++;
                    var row = reader.Current;
                    if (rowNum == 1)
                    {
                        var headers = new string[row.ColumnCount];
                        for (int i = 0; i < headers.Length; i++)
                            headers[i] = row[i].UnquoteToString();
                        sheetWriter.WriteHeaderRow(headers);
                    }
                    else
                    {
                        sheetWriter.StartRow(rowNum);
                        for (int i = 0; i < row.ColumnCount; i++)
                            sheetWriter.WriteCellString(i + 1, row[i].UnquoteToString());
                        sheetWriter.EndRow();
                    }
                }
            }
            finally
            {
                reader.DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
            }

            sheetWriter.Close();
        });
    }

    private static void ConvertCsvToFixedWidth(string inputPath, string outputPath, char delimiter)
    {
        string[] headers = [];
        int[] widths = [];

        var measureReader = Csv.Read()
            .WithDelimiter(delimiter)
            .WithMaxColumns(EXCEL_MAX_COLUMNS)
            .WithMaxRows(int.MaxValue)
            .WithMaxRowSize(null)
            .AllowNewlinesInQuotes()
            .FromTextFileAsync(inputPath);
        try
        {
            if (measureReader.MoveNextAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult())
            {
                var headerRow = measureReader.Current;
                headers = new string[headerRow.ColumnCount];
                int[] maxLengths = new int[headerRow.ColumnCount];
                for (int i = 0; i < headers.Length; i++)
                {
                    string headerName = headerRow[i].UnquoteToString();
                    if (headerName.AsSpan().IndexOfAny('\r', '\n') >= 0)
                    {
                        throw new InvalidDataException(
                            $"Header column {i + 1} ('{headerName}') contains an embedded newline, which cannot be represented in line-delimited fixed-width output.");
                    }
                    headers[i] = headerName;
                    maxLengths[i] = headerName.Length;
                }

                long dataRowNumber = 0;
                while (measureReader.MoveNextAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult())
                {
                    dataRowNumber++;
                    var row = measureReader.Current;
                    for (int i = 0; i < headers.Length; i++)
                    {
                        string value = i < row.ColumnCount ? row[i].UnquoteToString() : "";
                        if (value.AsSpan().IndexOfAny('\r', '\n') >= 0)
                        {
                            throw new InvalidDataException(
                                $"Row {dataRowNumber}, column {i + 1} ('{headers[i]}') contains an embedded newline, which cannot be represented in line-delimited fixed-width output.");
                        }
                        if (value.Length > maxLengths[i])
                            maxLengths[i] = value.Length;
                    }
                }

                widths = new int[headers.Length];
                for (int i = 0; i < widths.Length; i++)
                    widths[i] = Math.Max(5, maxLengths[i] + 2);
            }
        }
        finally
        {
            measureReader.DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
        }

        var outputFormat = DetectCompressionFormat(outputPath);
        WriteAtomically(outputPath, stagingPath =>
        {
            using var outStream = OpenWriteStream(stagingPath, outputFormat);
            using var sw = new StreamWriter(outStream, utf8NoBom);
            using var fwWriter = new FixedWidthStreamWriter(sw, leaveOpen: true);

            if (headers.Length > 0)
            {
                var reader = Csv.Read()
                    .WithDelimiter(delimiter)
                    .WithMaxColumns(EXCEL_MAX_COLUMNS)
                    .WithMaxRows(int.MaxValue)
                    .WithMaxRowSize(null)
                    .AllowNewlinesInQuotes()
                    .FromTextFileAsync(inputPath);
                try
                {
                    if (reader.MoveNextAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult())
                    {
                        var headerRow = reader.Current;
                        for (int i = 0; i < widths.Length; i++)
                        {
                            string headerName = i < headerRow.ColumnCount ? headerRow[i].UnquoteToString() : "";
                            if (headerName.Length > widths[i])
                                throw new InvalidDataException($"Header column {i + 1} ('{headers[i]}') changed during conversion and exceeds measured width {widths[i]}.");
                            fwWriter.WriteField(headerName, widths[i], FieldAlignment.Left, ' ');
                        }
                        fwWriter.EndRow();

                        long dataRowNumber = 0;
                        while (reader.MoveNextAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult())
                        {
                            dataRowNumber++;
                            var row = reader.Current;
                            for (int i = 0; i < widths.Length; i++)
                            {
                                string value = i < row.ColumnCount ? row[i].UnquoteToString() : "";
                                if (value.AsSpan().IndexOfAny('\r', '\n') >= 0)
                                {
                                    throw new InvalidDataException(
                                        $"Row {dataRowNumber}, column {i + 1} ('{headers[i]}') contains an embedded newline, which cannot be represented in line-delimited fixed-width output.");
                                }
                                if (value.Length > widths[i])
                                    throw new InvalidDataException($"Row {dataRowNumber}, column {i + 1} ('{headers[i]}') exceeds measured width {widths[i]}.");
                                fwWriter.WriteField(value, widths[i], FieldAlignment.Left, ' ');
                            }
                            fwWriter.EndRow();
                        }
                    }
                }
                finally
                {
                    reader.DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
                }
            }

            fwWriter.Flush();
        });
    }

    private static void ConvertHtbToCsv(string inputPath, string outputPath, HtbToCsvOptions options)
    {
        CliCompressionFormat outputFormat = DetectCompressionFormat(outputPath);

        WriteAtomically(outputPath, stagingPath =>
        {
            using var inStream = OpenReadStream(inputPath, bufferSize: 4096, FileOptions.SequentialScan);
            using var outStream = OpenWriteStream(stagingPath, outputFormat);
            using var writer = new StreamWriter(outStream, utf8NoBom);
            HtbToCsvConverter.Convert(inStream, writer, options);
        });
    }

    private static void ConvertJsonlToExcel(string inputPath, string outputPath, string sheetName)
    {
        var (headers, recordCount) = CollectJsonlHeaders(inputPath);
        if (recordCount > 0 && headers.Count == 0)
            throw new InvalidDataException("JSONL contains object records with no properties; cannot create a zero-column Excel worksheet.");

        WriteAtomically(outputPath, stagingPath =>
        {
            using var outStream = new FileStream(stagingPath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var xlsxWriter = XlsxWriter.CreateStreaming(outStream, leaveOpen: false);
            using var sheetWriter = xlsxWriter.StartSheet(sheetName);

            if (headers.Count > 0)
            {
                sheetWriter.WriteHeaderRow([.. headers]);
                Dictionary<string, int> headerColumns = new(headers.Count, StringComparer.Ordinal);
                for (int i = 0; i < headers.Count; i++)
                    headerColumns[headers[i]] = i + 1;

                List<(int ColIndex, int Order, JsonElement Value)> presentCells = [];
                int rowNum = 2;

                using var inStream = OpenReadStream(inputPath, bufferSize: 4096, FileOptions.SequentialScan);
                using var reader = OpenJsonlElementReader(inStream);
                foreach (JsonElement root in reader)
                {
                    if (root.ValueKind != JsonValueKind.Object)
                        throw new InvalidDataException("JSONL line must be a JSON object.");

                    presentCells.Clear();
                    int lastColIndex = 0;
                    int order = 0;
                    bool needsSort = false;
                    foreach (JsonProperty prop in root.EnumerateObject())
                    {
                        int colIndex = headerColumns[prop.Name];
                        if (colIndex <= lastColIndex)
                            needsSort = true;
                        lastColIndex = colIndex;
                        presentCells.Add((colIndex, order++, prop.Value));
                    }

                    if (needsSort && presentCells.Count > 1)
                    {
                        presentCells.Sort(static (a, b) =>
                        {
                            int cmp = a.ColIndex.CompareTo(b.ColIndex);
                            return cmp != 0 ? cmp : a.Order.CompareTo(b.Order);
                        });
                    }

                    sheetWriter.StartRow(rowNum++);
                    for (int i = 0; i < presentCells.Count; i++)
                    {
                        // Duplicate keys within a single JSON object collapse to the last occurrence so each cell reference is emitted at most once.
                        if (i + 1 < presentCells.Count && presentCells[i].ColIndex == presentCells[i + 1].ColIndex)
                            continue;

                        WriteExcelCellFromJson(sheetWriter, presentCells[i].ColIndex, presentCells[i].Value);
                    }
                    sheetWriter.EndRow();
                }
            }

            sheetWriter.Close();
        });
    }

    private static (List<string> Headers, int RecordCount) CollectJsonlHeaders(string inputPath)
    {
        List<string> headers = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        int recordCount = 0;

        using var inStream = OpenReadStream(inputPath, bufferSize: 4096, FileOptions.SequentialScan);
        using var reader = OpenJsonlElementReader(inStream);
        foreach (JsonElement root in reader)
        {
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("JSONL line must be a JSON object.");

            recordCount++;
            foreach (JsonProperty prop in root.EnumerateObject())
            {
                if (seen.Add(prop.Name))
                {
                    if (headers.Count >= EXCEL_MAX_COLUMNS)
                    {
                        throw new InvalidDataException(
                            $"JSONL contains more than {EXCEL_MAX_COLUMNS} distinct properties; Excel worksheets support at most {EXCEL_MAX_COLUMNS} columns.");
                    }

                    headers.Add(prop.Name);
                }
            }
        }

        return (headers, recordCount);
    }

    private static JsonlRecordReader<JsonElement> OpenJsonlElementReader(Stream stream)
        => new(
            stream,
            CliJsonElementContext.Default.JsonElement,
            new JsonlReadOptions
            {
                MaxLineSizeBytes = JsonlReadOptions.Default.MaxLineSizeBytes,
                MaxRowCount = int.MaxValue,
                SkipEmptyLines = true
            },
            leaveOpen: true);

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

    private static void ConvertCsvToHtb(string inputPath, string outputPath, char delimiter)
    {
        var inferred = Csv.InferSchemaFileAsync(inputPath, new CsvSchemaInferenceOptions
        {
            Delimiter = delimiter,
            MaxColumnCount = 2_048,
            DistinguishTemporalTypes = true,
            RequireLosslessNumericFormatting = true
        }).ConfigureAwait(false).GetAwaiter().GetResult();

        var columns = inferred.Columns.Select((c, i) => new HtbColumn(
            string.IsNullOrWhiteSpace(c.Name) ? $"Col{i + 1}" : c.Name,
            c.InferredType switch
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
                _ => HtbDataType.String
            },
            isNullable: true)).ToList();

        var schema = new HtbSchema(columns);
        CliCompressionFormat outputFormat = DetectCompressionFormat(outputPath);

        WriteAtomically(outputPath, stagingPath =>
        {
            using var inStream = OpenReadStream(inputPath, bufferSize: 4096,
                FileOptions.SequentialScan | FileOptions.Asynchronous);
            using var outStream = OpenWriteStream(stagingPath, outputFormat, bufferSize: 4096, FileOptions.Asynchronous);

            CsvToHtbConverter.ConvertAsync(inStream, outStream, schema, new CsvToHtbOptions
            {
                Delimiter = delimiter,
                AllowNewlinesInsideQuotes = true,
                MapByOrdinal = true,
                RejectExtraColumns = true,
                RejectLossyNumericValues = true,
                MaxInputRowCount = int.MaxValue,
                MaxInputRowSize = null
            }).ConfigureAwait(false).GetAwaiter().GetResult();
        });
    }

    private enum CliCompressionFormat
    {
        None,
        GZip,
        Brotli,
        Zstandard
    }

    private static string GetFormatExtension(string path)
    {
        string ext = Path.GetExtension(path);
        if (ext.Equals(".gz", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".br", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".zst", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".zstd", StringComparison.OrdinalIgnoreCase))
        {
            ext = Path.GetExtension(path[..^ext.Length]);
        }
        return ext.ToLowerInvariant();
    }

    private static bool IsCsvInput(string path)
        => GetFormatExtension(path) is ".csv" or ".tsv";

    private static bool IsExcelInput(string path)
        => Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase);

    private static CliCompressionFormat DetectCompressionFormat(string path)
    {
        if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
            return CliCompressionFormat.GZip;
        if (path.EndsWith(".br", StringComparison.OrdinalIgnoreCase))
            return CliCompressionFormat.Brotli;
        if (path.EndsWith(".zst", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".zstd", StringComparison.OrdinalIgnoreCase))
            return CliCompressionFormat.Zstandard;
        return CliCompressionFormat.None;
    }

    private static void EnsureCompressionSupported(CliCompressionFormat format)
    {
        if (format != CliCompressionFormat.Zstandard)
            return;

#if NET11_0_OR_GREATER
        if (OperatingSystem.IsBrowser() || OperatingSystem.IsWasi())
            throw new PlatformNotSupportedException("Transparent Zstandard (.zst) compression is not supported on browser or WASI platforms.");
#else
        throw new PlatformNotSupportedException("Transparent Zstandard (.zst) compression requires .NET 11 or later.");
#endif
    }

    private static Stream OpenReadStream(string path, int bufferSize = 4096, FileOptions options = FileOptions.SequentialScan)
    {
        CliCompressionFormat format = DetectCompressionFormat(path);
        EnsureCompressionSupported(format);

        var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, options);
        if (format == CliCompressionFormat.None)
            return fileStream;

        try
        {
            return format switch
            {
                CliCompressionFormat.GZip => new GZipStream(fileStream, CompressionMode.Decompress, leaveOpen: false),
                CliCompressionFormat.Brotli => new BrotliStream(fileStream, CompressionMode.Decompress, leaveOpen: false),
#if NET11_0_OR_GREATER
                CliCompressionFormat.Zstandard => (OperatingSystem.IsBrowser() || OperatingSystem.IsWasi())
                    ? throw new PlatformNotSupportedException("Transparent Zstandard (.zst) compression is not supported on browser or WASI platforms.")
                    : new ZstandardStream(fileStream, CompressionMode.Decompress, leaveOpen: false),
#endif
                _ => throw new NotSupportedException($"Compression format '{format}' is not supported.")
            };
        }
        catch
        {
            fileStream.Dispose();
            throw;
        }
    }

    private static void WriteEmptyOutputFile(string outputPath)
    {
        CliCompressionFormat format = DetectCompressionFormat(outputPath);
        EnsureCompressionSupported(format);

        WriteAtomically(outputPath, stagingPath =>
        {
            using (OpenWriteStream(stagingPath, format)) { }
        });
    }

    private static void WriteAtomically(string outputPath, Action<string> writeToStaging)
    {
        string fullOutputPath = Path.GetFullPath(outputPath);
        string dir = Path.GetDirectoryName(fullOutputPath) ?? Directory.GetCurrentDirectory();
        if (dir.Length > 0)
            Directory.CreateDirectory(dir);

        UnixFileMode? existingMode = null;
        if (!OperatingSystem.IsWindows() && File.Exists(fullOutputPath))
        {
            try
            {
                existingMode = File.GetUnixFileMode(fullOutputPath);
            }
            catch (FileNotFoundException)
            {
            }
        }

        string? stagingPath = Path.Combine(dir, $".hp-{Guid.NewGuid():N}.tmp");
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                using var restrictedStaging = new FileStream(stagingPath, new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
                });
            }

            writeToStaging(stagingPath);

            if (!OperatingSystem.IsWindows() && existingMode is { } mode)
            {
                File.SetUnixFileMode(stagingPath, mode);
            }

            if (File.Exists(fullOutputPath))
                File.Replace(stagingPath, fullOutputPath, destinationBackupFileName: null);
            else
                File.Move(stagingPath, fullOutputPath);

            stagingPath = null;
        }
        finally
        {
            if (stagingPath is not null && File.Exists(stagingPath))
            {
                try
                {
                    File.Delete(stagingPath);
                }
                catch
                {
                    // Best-effort cleanup of temp file
                }
            }
        }
    }

    private static Stream OpenWriteStream(string path, int bufferSize = 4096, FileOptions options = FileOptions.None)
        => OpenWriteStream(path, DetectCompressionFormat(path), bufferSize, options);

    private static Stream OpenWriteStream(string path, CliCompressionFormat format, int bufferSize = 4096, FileOptions options = FileOptions.None)
    {
        EnsureCompressionSupported(format);

        var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, options);
        if (format == CliCompressionFormat.None)
            return fileStream;

        try
        {
            return format switch
            {
                CliCompressionFormat.GZip => new NonEmptyGZipWriteStream(fileStream, CompressionLevel.Fastest),
                CliCompressionFormat.Brotli => new BrotliStream(fileStream, CompressionLevel.Fastest, leaveOpen: false),
#if NET11_0_OR_GREATER
                CliCompressionFormat.Zstandard => (OperatingSystem.IsBrowser() || OperatingSystem.IsWasi())
                    ? throw new PlatformNotSupportedException("Transparent Zstandard (.zst) compression is not supported on browser or WASI platforms.")
                    : new ZstandardStream(fileStream, CompressionLevel.Fastest, leaveOpen: false),
#endif
                _ => throw new NotSupportedException($"Compression format '{format}' is not supported.")
            };
        }
        catch
        {
            fileStream.Dispose();
            throw;
        }
    }

    private static (byte[] Bytes, int Length) ReadCsvSample(string path)
    {
        var sample = new byte[64 * 1024];
        using var stream = OpenReadStream(path, bufferSize: 4096, FileOptions.SequentialScan);
        int length = 0;
        while (length < sample.Length)
        {
            int read = stream.Read(sample, length, sample.Length - length);
            if (read == 0)
                break;
            length += read;
        }
        return (sample, length);
    }

    private static bool IsUtf32LittleEndian(byte[] data, int length)
        => length >= 4 && data[0] == 0xFF && data[1] == 0xFE && data[2] == 0x00 && data[3] == 0x00;

    private static bool IsUtf32BigEndian(byte[] data, int length)
        => length >= 4 && data[0] == 0x00 && data[1] == 0x00 && data[2] == 0xFE && data[3] == 0xFF;

    private static bool IsUtf16LittleEndian(byte[] data, int length)
        => !IsUtf32LittleEndian(data, length) && length >= 2 && data[0] == 0xFF && data[1] == 0xFE;

    private static bool IsUtf16BigEndian(byte[] data, int length)
        => length >= 2 && data[0] == 0xFE && data[1] == 0xFF;

    private sealed class NonEmptyGZipWriteStream(FileStream inner, CompressionLevel level) : Stream
    {
        private static readonly byte[] emptyGzipMember =
            [0x1F, 0x8B, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

        private GZipStream? gzip;
        private bool disposed;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !disposed && inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        private GZipStream EnsureGzip() => gzip ??= new GZipStream(inner, level, leaveOpen: true);

        public override void Flush()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            gzip?.Flush();
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled(cancellationToken);

            return gzip is not null ? gzip.FlushAsync(cancellationToken) : Task.CompletedTask;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            ObjectDisposedException.ThrowIf(disposed, this);
            if (count == 0)
                return;

            EnsureGzip().Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (buffer.IsEmpty)
                return;

            EnsureGzip().Write(buffer);
        }

        public override void WriteByte(byte value)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            EnsureGzip().WriteByte(value);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ValidateBufferArguments(buffer, offset, count);
            ObjectDisposedException.ThrowIf(disposed, this);
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled(cancellationToken);
            if (count == 0)
                return Task.CompletedTask;

            return EnsureGzip().WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (cancellationToken.IsCancellationRequested)
                return ValueTask.FromCanceled(cancellationToken);
            if (buffer.IsEmpty)
                return ValueTask.CompletedTask;

            return EnsureGzip().WriteAsync(buffer, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed = true;
                try
                {
                    if (gzip is not null)
                        gzip.Dispose();
                    else
                        inner.Write(emptyGzipMember);
                }
                finally
                {
                    inner.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (disposed)
                return;

            disposed = true;
            try
            {
                if (gzip is not null)
                    await gzip.DisposeAsync().ConfigureAwait(false);
                else
                    await inner.WriteAsync(emptyGzipMember).ConfigureAwait(false);
            }
            finally
            {
                await inner.DisposeAsync().ConfigureAwait(false);
            }

            GC.SuppressFinalize(this);
        }
    }
}
