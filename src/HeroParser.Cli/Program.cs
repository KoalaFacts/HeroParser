using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HeroParser.Console.Prompts;
using AnsiConsole = HeroParser.Console.AnsiConsole;
using Color = HeroParser.Console.Color;
using FigletText = HeroParser.Console.FigletText;
using Markup = HeroParser.Console.Markup;
using Panel = HeroParser.Console.Panel;
using Rule = HeroParser.Console.Rule;
using Style = HeroParser.Console.Style;
using Table = HeroParser.Console.Table;
using Text = HeroParser.Console.Text;
using SysConsole = System.Console;

namespace HeroParser.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            if (SysConsole.IsOutputRedirected || SysConsole.IsInputRedirected)
            {
                PrintHelp();
                return 0;
            }
            await new InteractiveWizard(AnsiConsole.Current).RunAsync(null);
            return 0;
        }

        if (args.Length == 1 && (args[0] == "-h" || args[0] == "--help" || args[0] == "help"))
        {
            PrintHelp();
            return 0;
        }

        if (args.Length == 1 && !args[0].StartsWith("-") && File.Exists(args[0]))
        {
            if (SysConsole.IsOutputRedirected || SysConsole.IsInputRedirected)
            {
                return await CliCommands.ProfileAsync(args[0], null, null) ? 0 : 1;
            }
            await new InteractiveWizard(AnsiConsole.Current).RunAsync(args[0]);
            return 0;
        }

        string command = args[0].ToLowerInvariant();
        char? delimiter = null;
        string? sheet = null;
        string? shape = null;
        string? provider = null;
        string? key = null;
        string? model = null;
        string? output = null;
        string? planPath = null;
        string? savePlanPath = null;
        string? reportPath = null;
        int batchSize = 50;
        int sampleRows = 1000;
        bool useAi = false;
        var positionalArgs = new List<string>();

        // Parse CLI options
        for (int i = 1; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg == "-d" || arg == "--delimiter")
            {
                if (i + 1 < args.Length)
                {
                    var val = args[++i];
                    if (val == "\\t") delimiter = '\t';
                    else if (val.Length > 0) delimiter = val[0];
                }
                else
                {
                    ConsoleUtils.Error("Missing value for option: " + arg);
                    return 1;
                }
            }
            else if (arg == "-s" || arg == "--sheet")
            {
                if (i + 1 < args.Length) sheet = args[++i];
                else
                {
                    ConsoleUtils.Error("Missing value for option: " + arg);
                    return 1;
                }
            }
            else if (arg == "-sh" || arg == "--shape")
            {
                if (i + 1 < args.Length) shape = args[++i];
                else
                {
                    ConsoleUtils.Error("Missing value for option: " + arg);
                    return 1;
                }
            }
            else if (arg == "-p" || arg == "--ai-provider")
            {
                if (i + 1 < args.Length) provider = args[++i];
                else
                {
                    ConsoleUtils.Error("Missing value for option: " + arg);
                    return 1;
                }
            }
            else if (arg == "-k" || arg == "--ai-key")
            {
                if (i + 1 < args.Length) key = args[++i];
                else
                {
                    ConsoleUtils.Error("Missing value for option: " + arg);
                    return 1;
                }
            }
            else if (arg == "-m" || arg == "--model")
            {
                if (i + 1 < args.Length) model = args[++i];
                else
                {
                    ConsoleUtils.Error("Missing value for option: " + arg);
                    return 1;
                }
            }
            else if (arg == "-o" || arg == "--output")
            {
                if (i + 1 < args.Length) output = args[++i];
                else
                {
                    ConsoleUtils.Error("Missing value for option: " + arg);
                    return 1;
                }
            }
            else if (arg == "-b" || arg == "--batch-size")
            {
                if (i + 1 < args.Length)
                {
                    if (int.TryParse(args[++i], out var bs) && bs > 0) batchSize = bs;
                    else
                    {
                        ConsoleUtils.Error("--batch-size must be a positive integer");
                        return 1;
                    }
                }
                else
                {
                    ConsoleUtils.Error("Missing value for option: " + arg);
                    return 1;
                }
            }
            else if (arg is "--plan" or "--save-plan" or "--report")
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith('-'))
                {
                    ConsoleUtils.Error("Missing value for option: " + arg);
                    return 1;
                }
                if (arg == "--plan") planPath = args[++i];
                else if (arg == "--save-plan") savePlanPath = args[++i];
                else reportPath = args[++i];
            }
            else if (arg == "--sample-rows")
            {
                if (command != "inspect")
                {
                    ConsoleUtils.Error("--sample-rows is only supported by inspect");
                    return 1;
                }
                if (i + 1 < args.Length && int.TryParse(args[++i], out var count) && count is > 0 and <= 10000)
                    sampleRows = count;
                else
                {
                    ConsoleUtils.Error("--sample-rows must be an integer from 1 to 10000");
                    return 1;
                }
            }
            else if (arg == "-ai" || arg == "--ai")
            {
                useAi = true;
            }
            else if (arg == "-h" || arg == "--help")
            {
                PrintCommandHelp(command);
                return 0;
            }
            else if (arg.StartsWith("-"))
            {
                ConsoleUtils.Error($"Unknown option: {arg}");
                return 1;
            }
            else
            {
                positionalArgs.Add(arg);
            }
        }

        // Route commands
        try
        {
            if (savePlanPath is not null && command != "inspect")
            {
                ConsoleUtils.Error("--save-plan is only supported by inspect");
                return 1;
            }
            if (planPath is not null && command is not ("validate" or "convert" or "schema"))
            {
                ConsoleUtils.Error("--plan is only supported by validate, convert, and schema");
                return 1;
            }
            if (reportPath is not null && command != "validate")
            {
                ConsoleUtils.Error("--report is only supported by validate");
                return 1;
            }
            if (planPath is not null && delimiter.HasValue)
            {
                ConsoleUtils.Error("--plan and --delimiter cannot be combined; edit the plan delimiter instead");
                return 1;
            }
            CsvImportPlan? plan = planPath is null ? null : CsvImportPlan.Load(planPath);
            if (plan is not null) delimiter = plan.Delimiter;

            switch (command)
            {
                case "inspect":
                    if (positionalArgs.Count != 1)
                    {
                        ConsoleUtils.Error("Usage: heroparser inspect <file> [--delimiter <char>] [--sample-rows <1-10000>] [--save-plan <file>]");
                        return 1;
                    }
                    if (!await CliCommands.InspectAsync(positionalArgs[0], delimiter, sampleRows, savePlanPath)) return 1;
                    break;

                case "detect":
                    if (positionalArgs.Count < 1)
                    {
                        ConsoleUtils.Error("Usage: heroparser detect <file>");
                        return 1;
                    }
                    if (!CliCommands.Detect(positionalArgs[0])) return 1;
                    break;

                case "validate":
                    if (positionalArgs.Count < 1)
                    {
                        ConsoleUtils.Error("Usage: heroparser validate <file> [options]");
                        return 1;
                    }
                    if (!await CliCommands.ValidateAsync(positionalArgs[0], delimiter, plan, reportPath)) return 1;
                    break;

                case "profile":
                    if (positionalArgs.Count < 1)
                    {
                        ConsoleUtils.Error("Usage: heroparser profile <file> [options]");
                        return 1;
                    }
                    if (!await CliCommands.ProfileAsync(positionalArgs[0], delimiter, sheet)) return 1;
                    break;

                case "convert":
                    if (positionalArgs.Count < 1)
                    {
                        ConsoleUtils.Error("Usage: heroparser convert <input> [output] [options]");
                        return 1;
                    }
                    string outPath = positionalArgs.Count > 1 ? positionalArgs[1] : output!;
                    if (string.IsNullOrWhiteSpace(outPath))
                    {
                        ConsoleUtils.Error("Output file path is required. Specify it as second argument or use --output flag.");
                        return 1;
                    }
                    if (planPath is not null && PathsAlias(outPath, planPath))
                    {
                        ConsoleUtils.Error("Conversion output path must differ from the import plan path.");
                        return 1;
                    }
                    if (!CliCommands.Convert(positionalArgs[0], outPath, delimiter, shape, sheet, plan)) return 1;
                    break;

                case "repair":
                    if (positionalArgs.Count < 1)
                    {
                        ConsoleUtils.Error("Usage: heroparser repair <input> [output]");
                        return 1;
                    }
                    string repairOut = positionalArgs.Count > 1 ? positionalArgs[1] : output!;
                    if (string.IsNullOrWhiteSpace(repairOut))
                    {
                        ConsoleUtils.Error("Output file path is required. Specify it as second argument or use --output flag.");
                        return 1;
                    }
                    if (!CliCommands.Repair(positionalArgs[0], repairOut)) return 1;
                    break;

                case "schema":
                    if (positionalArgs.Count < 1)
                    {
                        ConsoleUtils.Error("Usage: heroparser schema <file> [options]");
                        return 1;
                    }
                    if (!await CliCommands.SchemaAsync(positionalArgs[0], delimiter, useAi, provider, key, model, plan: plan)) return 1;
                    break;

                case "query":
                case "ask":
                    if (positionalArgs.Count < 2)
                    {
                        ConsoleUtils.Error("Usage: heroparser query <file> <prompt> [options]");
                        return 1;
                    }
                    string queryPrompt = string.Join(" ", positionalArgs.Skip(1));
                    if (!await CliCommands.QueryAsync(positionalArgs[0], delimiter, sheet, queryPrompt, provider, key, model)) return 1;
                    break;

                case "translate":
                    if (positionalArgs.Count < 2)
                    {
                        ConsoleUtils.Error("Usage: heroparser translate <file> <prompt> --output <output_file> [options]");
                        return 1;
                    }
                    if (string.IsNullOrWhiteSpace(output))
                    {
                        ConsoleUtils.Error("Output file path is required for translate. Please specify via --output.");
                        return 1;
                    }
                    string transformPrompt = string.Join(" ", positionalArgs.Skip(1));
                    if (!await CliCommands.TranslateAsync(positionalArgs[0], delimiter, sheet, transformPrompt, output, batchSize, provider, key, model)) return 1;
                    break;

                default:
                    ConsoleUtils.Error($"Unknown command: {command}");
                    PrintHelp();
                    return 1;
            }
        }
        catch (Exception ex)
        {
            ConsoleUtils.Error($"Command execution failed: {ex.Message}");
            return 1;
        }

        return 0;
    }

    private static bool PathsAlias(string outputPath, string planPath)
    {
        string output = Path.GetFullPath(outputPath);
        string plan = Path.GetFullPath(planPath);
        if (output.Equals(plan, StringComparison.Ordinal))
            return true;

        if (!File.Exists(output))
            return false;
        if (output.Equals(plan, StringComparison.OrdinalIgnoreCase) && !HasDistinctCaseSensitiveEntry(output, plan))
            return true;

        string? target = new FileInfo(output).ResolveLinkTarget(returnFinalTarget: true)?.FullName;
        return target is not null && target.Equals(plan, StringComparison.OrdinalIgnoreCase) &&
            !HasDistinctCaseSensitiveEntry(target, plan);
    }

    private static bool HasDistinctCaseSensitiveEntry(string output, string plan)
    {
        string root = Path.GetPathRoot(plan)!;
        if (!root.Equals(Path.GetPathRoot(output), StringComparison.OrdinalIgnoreCase))
            return true;

        string[] outputParts = output[root.Length..].Split(Path.DirectorySeparatorChar);
        string[] planParts = plan[root.Length..].Split(Path.DirectorySeparatorChar);
        string parent = root;
        for (int i = 0; i < planParts.Length; i++)
        {
            if (!outputParts[i].Equals(planParts[i], StringComparison.Ordinal))
            {
                bool outputEntry = false;
                bool planEntry = false;
                foreach (string? name in Directory.EnumerateFileSystemEntries(parent).Select(Path.GetFileName))
                {
                    outputEntry |= string.Equals(name, outputParts[i], StringComparison.Ordinal);
                    planEntry |= string.Equals(name, planParts[i], StringComparison.Ordinal);
                }
                if (outputEntry && planEntry)
                    return true;
                parent = Path.Join(parent, planEntry ? planParts[i] : outputParts[i]);
            }
            else
            {
                parent = Path.Join(parent, planParts[i]);
            }
        }
        return false;
    }

    private static void PrintHelp()
    {
        SysConsole.WriteLine("\n========================================================");
        SysConsole.WriteLine("    HeroParser CLI — High-Performance & AI-Native       ");
        SysConsole.WriteLine("========================================================");
        SysConsole.WriteLine("\nUsage: heroparser <command> [arguments] [options]\n");
        SysConsole.WriteLine("Commands:");
        SysConsole.WriteLine("  inspect <file>               Quickly inspect a sample of a UTF-8 CSV/TSV file");
        SysConsole.WriteLine("  detect <file>                Auto-detect delimiter and encoding of a CSV file");
        SysConsole.WriteLine("  validate <file>              Validate CSV structure and columns consistency");
        SysConsole.WriteLine("  profile <file>               Generate a Markdown statistical profile card of columns");
        SysConsole.WriteLine("  convert <input> <output>     Convert between CSV, JSONL, Fixed-Width, and Excel");
        SysConsole.WriteLine("  repair <input> <output>      Repair markdown blocks and unclosed quotes in LLM output");
        SysConsole.WriteLine("  schema <file>                Infer CSV schema and generate a C# record class model");
        SysConsole.WriteLine("  query/ask <file> <prompt>    [AI] Ask natural language questions about your dataset");
        SysConsole.WriteLine("  translate <file> <prompt>    [AI] Transform, translate, or map rows using LLM prompts");

        SysConsole.WriteLine("\nGlobal Options:");
        SysConsole.WriteLine("  -d, --delimiter <char>       Set CSV delimiter (e.g. , ; | or \\t)");
        SysConsole.WriteLine("  --sample-rows <1-10000>      Data rows to inspect (inspect only; default: 1000)");
        SysConsole.WriteLine("  --save-plan <path>           Save sampled CSV settings (inspect only)");
        SysConsole.WriteLine("  --plan <path>                Re-use CSV settings (validate, convert, or schema)");
        SysConsole.WriteLine("  --report <path>              Save bounded validation errors as JSON (validate only)");
        SysConsole.WriteLine("  -s, --sheet <name>           Sheet name to process for Excel files");
        SysConsole.WriteLine("  -o, --output <path>          Path to output file (required for convert/repair/translate)");

        SysConsole.WriteLine("\nAI-Native Options:");
        SysConsole.WriteLine("  -ai, --ai                    Enable AI optimizations (in 'schema' command)");
        SysConsole.WriteLine("  -p, --ai-provider <name>     Select local AI CLI provider: google (default), openai, anthropic, microsoft, github, ollama");
        SysConsole.WriteLine("  -k, --ai-key <key>           Not needed for local CLI providers (retained for compatibility)");
        SysConsole.WriteLine("  -m, --model <name>           Specify custom LLM model name");
        SysConsole.WriteLine("  -b, --batch-size <num>       Batch size of records sent to LLM in translation (default: 50)");

        SysConsole.WriteLine("\nTry 'heroparser <command> --help' for detailed instructions on a command.");
        SysConsole.WriteLine("========================================================\n");
    }

    private static void PrintCommandHelp(string command)
    {
        SysConsole.WriteLine($"\nHelp for command: {command}");
        SysConsole.WriteLine("=========================");

        switch (command)
        {
            case "inspect":
                SysConsole.WriteLine("Samples a UTF-8 CSV/TSV without scanning the whole file. Reports encoding evidence, delimiter confidence, columns, inferred sample types, and sampled row-width anomalies.");
                SysConsole.WriteLine("Use validate for full-file structural validation. UTF-16 and Excel are not supported by this quick command.");
                SysConsole.WriteLine("Usage: heroparser inspect <file> [--delimiter <char>] [--sample-rows <1-10000>] [--save-plan <file>]");
                break;
            case "detect":
                SysConsole.WriteLine("Auto-detects delimiter and encoding for tabular datasets.");
                SysConsole.WriteLine("Usage: heroparser detect <file>");
                break;
            case "validate":
                SysConsole.WriteLine("Validates tabular column counts, consistency, and structural anomalies.");
                SysConsole.WriteLine("Usage: heroparser validate <file> [--delimiter <char> | --plan <file>] [--report <file>]");
                break;
            case "profile":
                SysConsole.WriteLine("Generates a Markdown statistics Context Card profiling the dataset.");
                SysConsole.WriteLine("Usage: heroparser profile <file> [--delimiter <char>] [--sheet <name>]");
                break;
            case "convert":
                SysConsole.WriteLine("Converts files between CSV, JSONL, Fixed-Width, and Excel (.xlsx) formats.");
                SysConsole.WriteLine("Usage: heroparser convert <input> <output> [--shape <openai|anthropic>] [--delimiter <char> | --plan <file>] [--sheet <name>]");
                break;
            case "repair":
                SysConsole.WriteLine("Cleans up truncated, unclosed quotes, and markdown tags from LLM-generated files.");
                SysConsole.WriteLine("Usage: heroparser repair <input> <output>");
                break;
            case "schema":
                SysConsole.WriteLine("Generates a production-ready C# class matching the inferred column types.");
                SysConsole.WriteLine("Add --ai to consult LLMs for regex formats, range checks, enum resolution, and docs.");
                SysConsole.WriteLine("UTF-8 input is sampled without loading the whole file; UTF-16 still uses the in-memory path.");
                SysConsole.WriteLine("Usage: heroparser schema <file> [--plan <file>] [--ai] [--ai-provider <provider>]");
                break;
            case "query":
            case "ask":
                SysConsole.WriteLine("Queries the dataset using a full-file statistical profile and ten sample rows for UTF-8 CSV/TSV.");
                SysConsole.WriteLine("Usage: heroparser query <file> \"What are the top 3 categories by total amount?\"");
                break;
            case "translate":
                SysConsole.WriteLine("Maps or transforms UTF-8 CSV/TSV with a counting pass followed by bounded LLM batches.");
                SysConsole.WriteLine("Usage: heroparser translate <input> \"Translate the Name field to French\" --output <output>");
                break;
            default:
                ConsoleUtils.Error($"Unknown command: {command}");
                break;
        }
        SysConsole.WriteLine();
    }
}
