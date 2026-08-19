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
                CliCommands.Profile(args[0], null, null);
                return 0;
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
        int batchSize = 50;
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
                    if (int.TryParse(args[++i], out var bs)) batchSize = bs;
                    else
                    {
                        ConsoleUtils.Error("Invalid integer value for --batch-size");
                        return 1;
                    }
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
            switch (command)
            {
                case "detect":
                    if (positionalArgs.Count < 1)
                    {
                        ConsoleUtils.Error("Usage: heroparser detect <file>");
                        return 1;
                    }
                    CliCommands.Detect(positionalArgs[0]);
                    break;

                case "validate":
                    if (positionalArgs.Count < 1)
                    {
                        ConsoleUtils.Error("Usage: heroparser validate <file> [options]");
                        return 1;
                    }
                    CliCommands.Validate(positionalArgs[0], delimiter);
                    break;

                case "profile":
                    if (positionalArgs.Count < 1)
                    {
                        ConsoleUtils.Error("Usage: heroparser profile <file> [options]");
                        return 1;
                    }
                    CliCommands.Profile(positionalArgs[0], delimiter, sheet);
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
                    CliCommands.Convert(positionalArgs[0], outPath, delimiter, shape, sheet);
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
                    CliCommands.Repair(positionalArgs[0], repairOut);
                    break;

                case "schema":
                    if (positionalArgs.Count < 1)
                    {
                        ConsoleUtils.Error("Usage: heroparser schema <file> [options]");
                        return 1;
                    }
                    await CliCommands.SchemaAsync(positionalArgs[0], delimiter, useAi, provider, key, model);
                    break;

                case "query":
                case "ask":
                    if (positionalArgs.Count < 2)
                    {
                        ConsoleUtils.Error("Usage: heroparser query <file> <prompt> [options]");
                        return 1;
                    }
                    string queryPrompt = string.Join(" ", positionalArgs.Skip(1));
                    await CliCommands.QueryAsync(positionalArgs[0], delimiter, sheet, queryPrompt, provider, key, model);
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
                    await CliCommands.TranslateAsync(positionalArgs[0], delimiter, sheet, transformPrompt, output, batchSize, provider, key, model);
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

    private static void PrintHelp()
    {
        SysConsole.WriteLine("\n========================================================");
        SysConsole.WriteLine("    HeroParser CLI — High-Performance & AI-Native       ");
        SysConsole.WriteLine("========================================================");
        SysConsole.WriteLine("\nUsage: heroparser <command> [arguments] [options]\n");
        SysConsole.WriteLine("Commands:");
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
            case "detect":
                SysConsole.WriteLine("Auto-detects delimiter and encoding for tabular datasets.");
                SysConsole.WriteLine("Usage: heroparser detect <file>");
                break;
            case "validate":
                SysConsole.WriteLine("Validates tabular column counts, consistency, and structural anomalies.");
                SysConsole.WriteLine("Usage: heroparser validate <file> [--delimiter <char>]");
                break;
            case "profile":
                SysConsole.WriteLine("Generates a Markdown statistics Context Card profiling the dataset.");
                SysConsole.WriteLine("Usage: heroparser profile <file> [--delimiter <char>] [--sheet <name>]");
                break;
            case "convert":
                SysConsole.WriteLine("Converts files between CSV, JSONL, Fixed-Width, and Excel (.xlsx) formats.");
                SysConsole.WriteLine("Usage: heroparser convert <input> <output> [--shape <openai|anthropic>] [--delimiter <char>] [--sheet <name>]");
                break;
            case "repair":
                SysConsole.WriteLine("Cleans up truncated, unclosed quotes, and markdown tags from LLM-generated files.");
                SysConsole.WriteLine("Usage: heroparser repair <input> <output>");
                break;
            case "schema":
                SysConsole.WriteLine("Generates a production-ready C# class matching the inferred column types.");
                SysConsole.WriteLine("Add --ai to consult LLMs for regex formats, range checks, enum resolution, and docs.");
                SysConsole.WriteLine("Usage: heroparser schema <file> [--ai] [--ai-provider <provider>]");
                break;
            case "query":
            case "ask":
                SysConsole.WriteLine("Queries the dataset using natural language based on its statistical profile and top rows.");
                SysConsole.WriteLine("Usage: heroparser query <file> \"What are the top 3 categories by total amount?\"");
                break;
            case "translate":
                SysConsole.WriteLine("Maps, translates, or transforms rows in batches utilizing LLM commands.");
                SysConsole.WriteLine("Usage: heroparser translate <input> \"Translate the Name field to French\" --output <output>");
                break;
            default:
                ConsoleUtils.Error($"Unknown command: {command}");
                break;
        }
        SysConsole.WriteLine();
    }
}
