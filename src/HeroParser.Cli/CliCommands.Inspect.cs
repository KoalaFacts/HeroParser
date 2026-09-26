using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Detection;
using AnsiConsole = HeroParser.Console.AnsiConsole;
using Markup = HeroParser.Console.Markup;
using Table = HeroParser.Console.Table;
using TableBorder = HeroParser.Console.TableBorder;

namespace HeroParser.Cli;

internal static partial class CliCommands
{
    public static async Task<bool> InspectAsync(string path, char? delimiter, int sampleRows, string? savePlanPath = null)
    {
        if (!File.Exists(path))
        {
            ConsoleUtils.Error($"File not found: {path}");
            return false;
        }

        if (sampleRows is < 1 or > 10000)
        {
            ConsoleUtils.Error("--sample-rows must be an integer from 1 to 10000");
            return false;
        }

        if (savePlanPath is not null && Path.GetFullPath(path).Equals(Path.GetFullPath(savePlanPath),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            ConsoleUtils.Error("Plan path must differ from the CSV input path.");
            return false;
        }

        string extension = Path.GetExtension(path);
        if (extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jsonl", StringComparison.OrdinalIgnoreCase))
        {
            ConsoleUtils.Error("Quick inspect supports UTF-8 CSV/TSV only. Use profile for Excel or JSONL-specific tooling for JSONL.");
            return false;
        }

        try
        {
            var (sample, sampleLength) = ReadCsvSample(path);
            if (IsUtf16LittleEndian(sample, sampleLength) || IsUtf16BigEndian(sample, sampleLength))
            {
                ConsoleUtils.Error("Quick inspect supports UTF-8 only; this file has a UTF-16 BOM. Use profile or convert it to UTF-8 first.");
                return false;
            }

            bool utf8Bom = sampleLength >= 3 && sample[0] == 0xEF && sample[1] == 0xBB && sample[2] == 0xBF;
            var encodingLabel = utf8Bom ? "UTF-8 BOM observed" : "UTF-8/ASCII assumed (no BOM)";

            if (sampleLength == 0 || (utf8Bom && sampleLength == 3))
            {
                if (savePlanPath is not null)
                {
                    ConsoleUtils.Error("Cannot save a CSV import plan from an empty file.");
                    return false;
                }
                ConsoleUtils.Header($"CSV Inspection: {SanitizeTerminalText(Path.GetFileName(path))}");
                ConsoleUtils.Info("File is empty; no header or data rows were found.");
                return true;
            }

            CsvDelimiterDetectionResult? detection = null;
            if (!delimiter.HasValue)
            {
                try
                {
                    string sampleText = Encoding.UTF8.GetString(sample, utf8Bom ? 3 : 0, sampleLength - (utf8Bom ? 3 : 0));
                    detection = CsvDelimiterDetector.Detect(sampleText);
                    delimiter = detection.DetectedDelimiter;
                }
                catch (InvalidOperationException)
                {
                    ConsoleUtils.Error("Could not infer a delimiter from the first 64 KiB. Specify --delimiter explicitly (also for single-column files).");
                    return false;
                }
            }

            await using var reader = Csv.Read()
                .WithDelimiter(delimiter.Value)
                .WithMaxColumns(1000)
                .WithMaxRows(int.MaxValue)
                .AllowNewlinesInQuotes()
                .FromFileAsync(path);

            if (!await reader.MoveNextAsync().ConfigureAwait(false))
            {
                ConsoleUtils.Error("No CSV header row was found. Check the delimiter and file contents.");
                return false;
            }

            var header = reader.Current;
            var headers = new string[header.ColumnCount];
            for (int i = 0; i < headers.Length; i++)
                headers[i] = header[i].UnquoteToString();

            var stats = DynamicProfiler.CreateStats(headers);
            var preview = new Table();
            preview.Border(TableBorder.Rounded);
            preview.AddColumn("[blue bold]Row[/]");
            preview.AddColumn("[blue bold]First values[/]");

            int rows = 0;
            int raggedRows = 0;
            while (rows < sampleRows && await reader.MoveNextAsync().ConfigureAwait(false))
            {
                var row = reader.Current;
                rows++;
                if (row.ColumnCount != headers.Length)
                    raggedRows++;

                for (int i = 0; i < stats.Count; i++)
                    DynamicProfiler.ObserveCell(stats[i], i < row.ColumnCount ? row[i].UnquoteToString() : null);

                if (rows <= 3)
                {
                    var values = new string[Math.Min(row.ColumnCount, 4)];
                    for (int i = 0; i < values.Length; i++)
                        values[i] = TruncatePreview(row[i].UnquoteToString());
                    string suffix = row.ColumnCount > values.Length ? " ..." : "";
                    preview.AddRow(row.LineNumber.ToString(), Markup.Escape(string.Join(" | ", values) + suffix));
                }
            }

            ConsoleUtils.Header($"CSV Inspection: {SanitizeTerminalText(Path.GetFileName(path))}");
            var summary = new Table();
            summary.Border(TableBorder.Rounded);
            summary.AddColumn("[blue bold]Property[/]");
            summary.AddColumn("[blue bold]Observed value[/]");
            summary.AddRow("File size", $"{new FileInfo(path).Length:N0} bytes");
            summary.AddRow("Encoding", encodingLabel);
            summary.AddRow("Delimiter", delimiter == '\t' ? "Tab (\\t)" : Markup.Escape(SanitizeTerminalText(delimiter.Value.ToString())));
            summary.AddRow("Delimiter evidence", detection is null
                ? "Explicit --delimiter override"
                : $"{detection.Confidence}/100 confidence from {detection.SampledRows} sampled records (first 64 KiB)");
            summary.AddRow("Columns", headers.Length.ToString());
            summary.AddRow("Data rows inspected", $"{rows:N0} (limit {sampleRows:N0}; not a full-file count)");
            summary.AddRow("Row-width mismatches in sample", raggedRows.ToString());
            AnsiConsole.Write(summary);

            if (detection is { Confidence: < 80 })
                ConsoleUtils.Warning("Delimiter confidence is low; verify with --delimiter before relying on column results.");

            var columns = new Table();
            columns.Border(TableBorder.Rounded);
            columns.AddColumn("[blue bold]Column[/]");
            columns.AddColumn("[blue bold]Sample type[/]");
            columns.AddColumn("[blue bold]Empty in sample[/]");
            foreach (var stat in stats)
                columns.AddRow(Markup.Escape(SanitizeTerminalText(stat.Name)), DynamicProfiler.InferTypeName(stat), stat.NullCount.ToString());

            AnsiConsole.Write(columns);
            if (rows > 0)
            {
                ConsoleUtils.Header("First data rows (at most 4 values each)");
                AnsiConsole.Write(preview);
            }

            ConsoleUtils.Info("Types and anomalies describe only inspected rows. Run 'heroparser validate <file>' to check the full file.");
            ConsoleUtils.Info($"Re-use parser setting: --delimiter \"{SanitizeTerminalText(delimiter == '\t' ? "\\t" : delimiter.Value.ToString())}\"");
            if (savePlanPath is not null)
            {
                new CsvImportPlan
                {
                    Delimiter = delimiter.Value,
                    ExpectedColumnCount = headers.Length,
                    SampledDataRows = rows,
                    SampledWidthMismatches = raggedRows,
                    DelimiterConfidence = detection?.Confidence,
                    Utf8BomObserved = utf8Bom
                }.Save(savePlanPath);
                ConsoleUtils.Info($"Saved sampled CSV import plan: {SanitizeTerminalText(savePlanPath)}. Validate the full file before converting.");
            }
            return true;
        }
        catch (CsvException ex)
        {
            ConsoleUtils.Error($"Inspection failed: {ex.Message}");
            return false;
        }
        catch (IOException ex)
        {
            ConsoleUtils.Error($"Inspection failed: {ex.Message}");
            return false;
        }
        catch (InvalidOperationException ex)
        {
            ConsoleUtils.Error($"Inspection failed: {ex.Message}");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            ConsoleUtils.Error($"Inspection failed: {ex.Message}");
            return false;
        }
    }

    private static string TruncatePreview(string value)
    {
        string shortened = value.Length <= 80 ? value : value[..80];
        return SanitizeTerminalText(shortened) + (value.Length > 80 ? "..." : "");
    }

    private static string SanitizeTerminalText(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            if (character is '\r' or '\n' or '\t')
                result.Append(' ');
            else if (char.IsControl(character))
                result.Append($"\\u{(int)character:X4}");
            else
                result.Append(character);
        }
        return result.ToString();
    }
}
