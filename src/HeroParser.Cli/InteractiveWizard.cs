using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HeroParser.Console.Prompts;
using AnsiConsole = HeroParser.Console.AnsiConsole;
using Color = HeroParser.Console.Color;
using FigletText = HeroParser.Console.FigletText;
using IAnsiConsole = HeroParser.Console.IAnsiConsole;
using Rule = HeroParser.Console.Rule;
using SysConsole = System.Console;

namespace HeroParser.Cli;

/// <summary>
/// The menu-driven mode the CLI starts in when it is run without arguments on a terminal.
/// </summary>
/// <remarks>
/// Every prompt and every line of output goes through the supplied
/// <see cref="IAnsiConsole"/> rather than the process console, so the wizard is a plain
/// object a host can drive — which is also what makes its menu paths reachable at all.
/// </remarks>
internal sealed class InteractiveWizard
{
    private readonly IAnsiConsole console;
    private readonly string? searchDirectory;

    /// <summary>
    /// Initializes a wizard that reads from and writes to <paramref name="console"/>.
    /// </summary>
    /// <param name="console">Console the wizard renders to and reads input from.</param>
    /// <param name="searchDirectory">
    /// Directory the file picker lists. Defaults to the process's working directory.
    /// </param>
    public InteractiveWizard(IAnsiConsole console, string? searchDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(console);
        this.console = console;
        this.searchDirectory = searchDirectory;
    }

    /// <summary>
    /// Runs the menu loop until the user exits.
    /// </summary>
    /// <param name="targetFile">File to start on, or <see langword="null"/> to pick one.</param>
    public async Task RunAsync(string? targetFile)
    {
        try
        {
            SysConsole.Clear();
        }
        catch (IOException)
        {
            // Ignore if console handle is not available (e.g. redirected output in tests)
        }
        console.Write(
            new FigletText("HeroParser")
                .Color(Color.Aqua));

        console.MarkupLine("[bold blue]========================================================[/]");
        console.MarkupLine("[bold white]    HeroParser CLI — High-Performance & AI-Native       [/]");
        console.MarkupLine("[bold blue]========================================================[/]");
        console.WriteLine(string.Empty);

        string file = targetFile ?? "";
        if (string.IsNullOrWhiteSpace(file))
        {
            // Scan current directory for files
            var cwd = searchDirectory ?? Directory.GetCurrentDirectory();
            var searchExtensions = new[] { "*.csv", "*.tsv", "*.xlsx", "*.jsonl", "*.txt" };
            var foundFiles = new List<string>();
            foreach (var ext in searchExtensions)
            {
                foundFiles.AddRange(Directory.GetFiles(cwd, ext));
            }

            var choices = foundFiles.Select(f => Path.GetFileName(f) ?? "").Where(name => !string.IsNullOrEmpty(name)).ToList();
            choices.Add("[Enter custom file path...]");
            choices.Add("[Exit]");

            var selectedFile = Prompt(
                new SelectionPrompt<string>()
                    .Title("Select a tabular file in the current directory:")
                    .PageSize(10)
                    .MoreChoicesText("[grey](Move up and down to reveal more files)[/]")
                    .AddChoices(choices));

            if (selectedFile == "[Exit]")
            {
                return;
            }

            file = selectedFile == "[Enter custom file path...]"
                ? Prompt(
                    new TextPrompt<string>("Enter path to the file:")
                        .Validate(f => File.Exists(f) ? ValidationResult.Success() : ValidationResult.Error("[red]File does not exist.[/]")))
                : Path.Combine(cwd, selectedFile);
        }

        bool running = true;
        while (running)
        {
            var fileName = Path.GetFileName(file);
            console.WriteLine(string.Empty);
            console.Write(new Rule($"[yellow]Managing File: {fileName}[/]").LeftJustified());
            console.WriteLine(string.Empty);

            var action = Prompt(
                new SelectionPrompt<string>()
                    .Title("Select an operation:")
                    .AddChoices([
                        "1. Detect delimiter & encoding",
                        "2. Validate structure & health",
                        "3. Profile statistics & values",
                        "4. Generate C# record schema (Local)",
                        "5. Generate C# record schema (AI)",
                        "6. Ask AI a question about dataset (Query)",
                        "7. Translate or transform columns (AI)",
                        "8. Convert file format",
                        "9. Change active file",
                        "10. Exit"
                    ]));

            try
            {
                switch (action)
                {
                    case "1. Detect delimiter & encoding":
                        CliCommands.Detect(file);
                        break;

                    case "2. Validate structure & health":
                        CliCommands.Validate(file, null);
                        break;

                    case "3. Profile statistics & values":
                        CliCommands.Profile(file, null, null);
                        break;

                    case "4. Generate C# record schema (Local)":
                        await CliCommands.SchemaAsync(file, null, useAi: false, null, null, null);
                        break;

                    case "5. Generate C# record schema (AI)":
                        await CliCommands.SchemaAsync(file, null, useAi: true, null, null, null);
                        break;

                    case "6. Ask AI a question about dataset (Query)":
                        var query = Prompt(
                            new TextPrompt<string>("Enter your question for the dataset:"));
                        await CliCommands.QueryAsync(file, null, null, query, null, null, null);
                        break;

                    case "7. Translate or transform columns (AI)":
                        var prompt = Prompt(
                            new TextPrompt<string>("Enter transform instruction (e.g. 'Translate Category to French'):"));
                        var defaultOut = Path.Combine(
                            Path.GetDirectoryName(file) ?? "",
                            Path.GetFileNameWithoutExtension(file) + "_transformed" + Path.GetExtension(file));
                        var outPath = Prompt(
                            new TextPrompt<string>("Enter output file path:")
                                .DefaultValue(defaultOut));
                        var batch = Prompt(
                            new TextPrompt<int>("Enter batch size:")
                                .DefaultValue(50));
                        await CliCommands.TranslateAsync(file, null, null, prompt, outPath, batch, null, null, null);
                        break;

                    case "8. Convert file format":
                        var targetExt = Prompt(
                            new SelectionPrompt<string>()
                                .Title("Select target format:")
                                .AddChoices([".csv", ".jsonl", ".txt (Fixed Width)"]));

                        string convertedOut = Path.Combine(
                            Path.GetDirectoryName(file) ?? "",
                            Path.GetFileNameWithoutExtension(file) + "_converted" + (targetExt == ".txt (Fixed Width)" ? ".txt" : targetExt));

                        var finalOut = Prompt(
                            new TextPrompt<string>("Enter output path:")
                                .DefaultValue(convertedOut));

                        string? shape = null;
                        if (targetExt == ".jsonl")
                        {
                            var selectedShape = Prompt(
                                new SelectionPrompt<string>()
                                    .Title("Select JSONL shape:")
                                    .AddChoices(["Flat (default)", "OpenAI Fine-Tuning Chat", "Anthropic Fine-Tuning Message"]));
                            shape = selectedShape switch
                            {
                                "OpenAI Fine-Tuning Chat" => "openai",
                                "Anthropic Fine-Tuning Message" => "anthropic",
                                _ => null
                            };
                        }

                        CliCommands.Convert(file, finalOut, null, shape, null);
                        break;

                    case "9. Change active file":
                        targetFile = null;
                        running = false;
                        await RunAsync(null);
                        return;

                    case "10. Exit":
                        running = false;
                        break;
                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                ConsoleUtils.Error($"Operation failed: {ex.Message}");
            }

            if (running)
            {
                console.WriteLine(string.Empty);
                console.MarkupLine("[grey]Press any key to return to operation menu...[/]");
                console.ReadKey(intercept: true);
            }
        }
    }

    private T Prompt<T>(SelectionPrompt<T> prompt) where T : notnull => prompt.Show(console);

    private T Prompt<T>(TextPrompt<T> prompt) => prompt.Show(console);
}
