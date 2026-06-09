using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using HeroParser.AI;
using HeroParser.Conversion;
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

internal static class CliCommands
{
    public static void Detect(string path)
    {
        if (!File.Exists(path))
        {
            ConsoleUtils.Error($"File not found: {path}");
            return;
        }

        ConsoleUtils.Info($"Analyzing delimiter and encoding for: {path}");

        try
        {
            var bytes = File.ReadAllBytes(path);
            var content = Encoding.UTF8.GetString(bytes);

            // Detect encoding (UTF-8 BOM check)
            bool hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

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
            table.AddRow("Encoding", hasBom ? "UTF-8 with BOM" : "UTF-8 or ASCII");
            table.AddRow("Detected Delimiter", readableDelimiter);

            string confColor = detection.Confidence >= 80 ? "green" : (detection.Confidence >= 50 ? "yellow" : "red");
            table.AddRow("Confidence Score", $"[{confColor}]{detection.Confidence}/100[/]");
            table.AddRow("Sampled Rows", detection.SampledRows.ToString());
            table.AddRow("Avg Columns/Row", detection.AverageDelimiterCount.ToString("F2"));

            SysConsole.WriteLine();
            ConsoleUtils.Header("Detection Results");
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
            ConsoleUtils.Header("Delimiter Occurrence Counts");
            AnsiConsole.Write(candTable);
            SysConsole.WriteLine();

            if (detection.Confidence >= 80)
            {
                ConsoleUtils.Success("Delimiter detected with high confidence.");
            }
            else
            {
                ConsoleUtils.Warning("Low delimiter confidence. Please manually verify using --delimiter option.");
            }
        }
        catch (Exception ex)
        {
            ConsoleUtils.Error($"Detection failed: {ex.Message}");
        }
    }

    public static void Validate(string path, char? delimiter)
    {
        if (!File.Exists(path))
        {
            ConsoleUtils.Error($"File not found: {path}");
            return;
        }

        ConsoleUtils.Info($"Running structural validation on: {path}");

        try
        {
            var content = File.ReadAllText(path);
            var options = new CsvValidationOptions();
            if (delimiter.HasValue)
            {
                options = options with { Delimiter = delimiter.Value };
            }

            var result = Csv.Validate(content, options);

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
                    ConsoleUtils.Info($"... and {result.Errors.Count - displayLimit} more errors.");
                }
            }
        }
        catch (Exception ex)
        {
            ConsoleUtils.Error($"Validation failed: {ex.Message}");
        }
    }

    public static void Profile(string path, char? delimiter, string? sheet)
    {
        if (!File.Exists(path))
        {
            ConsoleUtils.Error($"File not found: {path}");
            return;
        }

        ConsoleUtils.Info($"Profiling dataset: {path}");

        try
        {
            var (headers, rows) = ReadTabularData(path, delimiter, sheet);
            var filename = Path.GetFileName(path);
            var statsList = DynamicProfiler.Analyze(headers, rows);

            var table = new Table();
            table.Border(TableBorder.Rounded);
            table.AddColumn("[blue bold]Column Name[/]");
            table.AddColumn("[blue bold]Inferred Type[/]");
            table.AddColumn("[blue bold]Null Density[/]");
            table.AddColumn("[blue bold]Statistics / Frequency Details[/]");

            int totalRows = rows.Count;

            foreach (var stats in statsList)
            {
                double nullPct = totalRows > 0 ? (double)stats.NullCount / totalRows * 100 : 0;
                string nullStr = stats.NullCount > 0 ? $"[yellow]{stats.NullCount} ({nullPct:F1}%)[/]" : "[green]0 (0%)[/]";
                string typeName = DynamicProfiler.InferTypeName(stats);

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
                    sb.Append($"{distinctCount} distinct categories. Top: ");
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
            ConsoleUtils.Header($"Dataset Profile: {filename} ({totalRows:N0} rows)");
            AnsiConsole.Write(table);
            SysConsole.WriteLine();
        }
        catch (Exception ex)
        {
            ConsoleUtils.Error($"Profiling failed: {ex.Message}");
        }
    }

    public static void Convert(string inputPath, string outputPath, char? delimiter, string? shapeName, string? sheet)
    {
        if (!File.Exists(inputPath))
        {
            ConsoleUtils.Error($"Input file not found: {inputPath}");
            return;
        }

        ConsoleUtils.Info($"Converting: {inputPath} -> {outputPath}");

        try
        {
            var inExt = Path.GetExtension(inputPath).ToLowerInvariant();
            var outExt = Path.GetExtension(outputPath).ToLowerInvariant();

            if (inExt == ".csv" && outExt == ".jsonl")
            {
                CsvToJsonlShape shape = CsvToJsonlShape.FlatObject();
                if (!string.IsNullOrWhiteSpace(shapeName))
                {
                    if (shapeName.Equals("openai", StringComparison.OrdinalIgnoreCase))
                        shape = CsvToJsonlShape.OpenAiChat("System", "Question", "Answer");
                    else if (shapeName.Equals("anthropic", StringComparison.OrdinalIgnoreCase))
                        shape = CsvToJsonlShape.AnthropicMessages("Question", "Answer");
                }

                var options = new CsvToJsonlOptions { Delimiter = delimiter ?? ',' };
                CsvToJsonlConverter.Convert(inputPath, outputPath, shape, options);
                ConsoleUtils.Success($"Converted CSV to JSONL successfully.");
            }
            else if (inExt == ".jsonl" && outExt == ".csv")
            {
                var options = new JsonlToCsvOptions();
                if (delimiter.HasValue)
                {
                    options = options with { Delimiter = delimiter.Value };
                }
                JsonlToCsvConverter.Convert(inputPath, outputPath, options);
                ConsoleUtils.Success($"Converted JSONL to CSV successfully.");
            }
            else if (inExt == ".csv" && outExt == ".txt") // CSV -> FixedWidth
            {
                var csvData = File.ReadAllText(inputPath);
                var schema = Csv.InferSchema(csvData, new CsvSchemaInferenceOptions { Delimiter = delimiter });

                // Map columns to widths matching their observed max lengths
                var fields = schema.Columns.Select(c => new FixedWidthFieldDefinition(c.Name, Math.Max(5, c.MaxLength + 2))).ToList();

                var options = new CsvToFixedWidthOptions { Delimiter = delimiter ?? ',', IncludeHeader = true };
                var fixedWidthData = CsvToFixedWidthConverter.Convert(csvData, fields, options);

                File.WriteAllText(outputPath, fixedWidthData);
                ConsoleUtils.Success($"Converted CSV to Fixed-Width successfully.");
            }
            else if (inExt == ".txt" && outExt == ".csv") // FixedWidth -> CSV
            {
                ConsoleUtils.Error("Conversion from Fixed-Width requires column widths. This CLI uses CSV/Excel as first class. Use the core API directly for manual layouts.");
            }
            else if (inExt == ".xlsx") // Excel -> CSV or JSONL
            {
                var allRows = Excel.Read().WithoutHeader().FromSheet(sheet ?? "Sheet1").FromFile(inputPath);
                if (allRows.Count == 0)
                {
                    File.WriteAllText(outputPath, "");
                    ConsoleUtils.Warning("Excel sheet was empty.");
                    return;
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
                    ConsoleUtils.Success("Converted Excel sheet to CSV successfully.");
                }
                else if (outExt == ".jsonl")
                {
                    using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
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
                    ConsoleUtils.Success("Converted Excel sheet to JSONL successfully.");
                }
                else
                {
                    ConsoleUtils.Error($"Unsupported output extension from Excel: {outExt}");
                }
            }
            else
            {
                ConsoleUtils.Error($"Unsupported conversion direction from {inExt} to {outExt}");
            }
        }
        catch (Exception ex)
        {
            ConsoleUtils.Error($"Conversion failed: {ex.Message}");
        }
    }

    public static void Repair(string inputPath, string outputPath)
    {
        if (!File.Exists(inputPath))
        {
            ConsoleUtils.Error($"Input file not found: {inputPath}");
            return;
        }

        ConsoleUtils.Info($"Repairing: {inputPath} -> {outputPath}");

        try
        {
            var text = File.ReadAllText(inputPath);
            var repaired = LlmRepair.RepairText(text);
            File.WriteAllText(outputPath, repaired);
            ConsoleUtils.Success("Text repaired successfully (stripped markdown blocks & resolved unbalanced quotes).");
        }
        catch (Exception ex)
        {
            ConsoleUtils.Error($"Repair failed: {ex.Message}");
        }
    }

    public static async Task SchemaAsync(string path, char? delimiter, bool useAi, string? providerName, string? apiKey, string? model)
    {
        if (!File.Exists(path))
        {
            ConsoleUtils.Error($"File not found: {path}");
            return;
        }

        ConsoleUtils.Info($"Generating schema for: {path}");

        try
        {
            var csvData = File.ReadAllText(path);
            var schemaResult = Csv.InferSchema(csvData, new CsvSchemaInferenceOptions { Delimiter = delimiter });

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
            ConsoleUtils.Header("Schema Preview");
            AnsiConsole.Write(schemaTable);
            SysConsole.WriteLine();

            if (!useAi)
            {
                ConsoleUtils.Header("Generated C# Class (Local Inference)");
                SysConsole.WriteLine(localSchema);
                ConsoleUtils.Header("End of Class");
                return;
            }

            // AI Schema Generation
            var filename = Path.GetFileName(path);
            var (headers, rows) = ReadTabularData(path, delimiter, null);
            var contextCard = DynamicProfiler.GenerateContextCard(filename, headers, [.. rows.Take(10)]);

            var aiClient = LlmClient.CreateFromEnvironment(providerName, apiKey, model);

            string prompt = $"""
You are an expert compiler engineer and software architect.
Here is the dataset profile and sample rows:
{contextCard}

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

            var response = await ConsoleUtils.RunWithSpinnerAsync(
                "Querying LLM for schema optimizations",
                token => aiClient.AskAsync(prompt, token)
            );

            var repairedCode = LlmRepair.RepairText(response);
            SysConsole.WriteLine();
            ConsoleUtils.Header("Generated AI-Optimized C# Class");
            SysConsole.WriteLine(repairedCode);
            ConsoleUtils.Header("End of Class");
        }
        catch (Exception ex)
        {
            ConsoleUtils.Error($"Schema generation failed: {ex.Message}");
        }
    }

    public static async Task QueryAsync(string path, char? delimiter, string? sheet, string userQuery, string? providerName, string? apiKey, string? model)
    {
        if (!File.Exists(path))
        {
            ConsoleUtils.Error($"File not found: {path}");
            return;
        }

        ConsoleUtils.Info($"Loading dataset for AI Q&A...");

        try
        {
            var (headers, rows) = ReadTabularData(path, delimiter, sheet);
            var filename = Path.GetFileName(path);
            var card = DynamicProfiler.GenerateContextCard(filename, headers, rows);

            // Format sample rows
            var sampleSb = new StringBuilder();
            sampleSb.AppendLine("| " + string.Join(" | ", headers) + " |");
            sampleSb.AppendLine("| " + string.Join(" | ", headers.Select(_ => "---")) + " |");
            foreach (var r in rows.Take(10))
            {
                sampleSb.AppendLine("| " + string.Join(" | ", r.Select(c => c?.Replace("|", "\\|") ?? "")) + " |");
            }

            var aiClient = LlmClient.CreateFromEnvironment(providerName, apiKey, model);

            string prompt = $"""
You are a helpful data analyst querying a tabular dataset.
Here is the dataset profile card:
{card}

Here are the first 10 sample rows:
{sampleSb}

User Query: {userQuery}

Answer the query clearly and concisely based on the schema, stats, and sample rows. If you need to make numeric inferences or math calculations, show your step-by-step logic.
""";

            var response = await ConsoleUtils.RunWithSpinnerAsync(
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
        }
        catch (Exception ex)
        {
            ConsoleUtils.Error($"Query failed: {ex.Message}");
        }
    }

    public static async Task TranslateAsync(string path, char? delimiter, string? sheet, string transformPrompt, string outputPath, int batchSize, string? providerName, string? apiKey, string? model)
    {
        if (!File.Exists(path))
        {
            ConsoleUtils.Error($"File not found: {path}");
            return;
        }

        ConsoleUtils.Info($"Starting batch translation / transformation pipeline...");
        ConsoleUtils.Info($"Output will be written to: {outputPath}");

        try
        {
            var (headers, rows) = ReadTabularData(path, delimiter, sheet);
            var aiClient = LlmClient.CreateFromEnvironment(providerName, apiKey, model);

            var headersJoined = string.Join(",", headers);

            // Setup output file and write header
            using var fileWriter = Csv.CreateFileWriter(outputPath, new CsvWriteOptions { Delimiter = delimiter ?? ',' });

            bool isFirstBatch = true;
            string[] outputHeaders = headers;

            int totalRows = rows.Count;
            int processed = 0;

            char openBrace = '{';
            char closeBrace = '}';

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

                    for (int i = 0; i < totalRows; i += batchSize)
                    {
                        List<string[]> batch = [.. rows.Skip(i).Take(batchSize)];
                        progressTask.Description = $"[green]Transforming rows {i + 1} to {Math.Min(i + batchSize, totalRows)} of {totalRows}[/]";

                        // Format batch as JSON array of key-value maps
                        var batchArray = new JsonArray();
                        foreach (var r in batch)
                        {
                            var obj = new JsonObject();
                            for (int c = 0; c < Math.Min(headers.Length, r.Length); c++)
                            {
                                obj[headers[c]] = r[c];
                            }
                            batchArray.Add((JsonNode)obj);
                        }

                        string prompt = $"""
You are a high-performance tabular data mapping agent. 
Task: Transform the input rows according to this prompt: "{transformPrompt}"

Input Columns: {headersJoined}
Input Rows (JSON):
{batchArray.ToJsonString()}

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

                        foreach (var line in lines)
                        {
                            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("{")) continue;

                            try
                            {
                                if (JsonNode.Parse(line) is not JsonObject parsedObj) continue;

                                if (isFirstBatch)
                                {
                                    isFirstBatch = false;
                                    outputHeaders = [.. parsedObj.Select(k => k.Key)];
                                    fileWriter.WriteRow(outputHeaders);
                                }

                                var rowValues = new string[outputHeaders.Length];
                                for (int colIndex = 0; colIndex < outputHeaders.Length; colIndex++)
                                {
                                    rowValues[colIndex] = parsedObj[outputHeaders[colIndex]]?.ToString() ?? "";
                                }
                                fileWriter.WriteRow(rowValues);
                            }
                            catch (Exception lineEx)
                            {
                                AnsiConsole.MarkupLine($"[yellow]⚠ Failed to parse output line: {Markup.Escape(line)}. Error: {Markup.Escape(lineEx.Message)}[/]");
                            }
                        }

                        processed += batch.Count;
                        progressTask.Increment(batch.Count);
                    }

                    progressTask.Description = $"[green]Processed all {totalRows} rows[/]";
                });

            ConsoleUtils.Success($"Translation/transformation completed successfully. Saved to: {outputPath}");
        }
        catch (Exception ex)
        {
            ConsoleUtils.Error($"Translation pipeline failed: {ex.Message}");
        }
    }

    private static (string[] Headers, List<string[]> Rows) ReadTabularData(string path, char? delimiter, string? sheet)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();

        if (ext == ".xlsx")
        {
            var allRows = Excel.Read().WithoutHeader().FromSheet(sheet ?? "Sheet1").FromFile(path);
            if (allRows.Count == 0)
            {
                return ([], []);
            }
            var headers = allRows[0];
            var rows = allRows.Skip(1).ToList();
            return (headers, rows);
        }
        else
        {
            // Assume CSV/TSV
            var content = File.ReadAllText(path);
            var detectDelim = delimiter ?? CsvDelimiterDetector.DetectDelimiter(content);

            var readOptions = new CsvReadOptions { Delimiter = detectDelim };
            using var reader = Csv.ReadFromText(content, readOptions);

            if (!reader.MoveNext())
            {
                return ([], []);
            }

            var headerRow = reader.Current;
            var headers = new string[headerRow.ColumnCount];
            for (int i = 0; i < headerRow.ColumnCount; i++)
            {
                headers[i] = headerRow[i].ToString();
            }

            var rows = new List<string[]>();
            while (reader.MoveNext())
            {
                var r = reader.Current;
                var rowData = new string[headers.Length];
                for (int i = 0; i < headers.Length; i++)
                {
                    rowData[i] = i < r.ColumnCount ? r[i].ToString() : "";
                }
                rows.Add(rowData);
            }

            return (headers, rows);
        }
    }
}
