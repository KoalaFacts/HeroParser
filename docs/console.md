# Console API Reference

[Back to README](../README.md)

`HeroParser.Console` is a lightweight, zero-allocation, reflection-free, and 100% Native AOT compatible terminal rendering and widget library. It is designed to act as a drop-in replacement or high-performance alternative to `Spectre.Console` for performance-critical and Native AOT-targeted application environments.

By avoiding reflection, heavy styling trees, and runtime allocation cycles, it maintains a near-zero heap memory footprint when rendering tables, panels, rules, progress indicators, status spinners, and interactive selection prompts.

---

## Table of Contents

1. [Standard Rendering](#1-standard-rendering)
   - [Writing Text](#11-writing-text)
   - [Markup Syntax](#12-markup-syntax)
   - [Colors and Styles](#13-colors-and-styles)
2. [Interactive Prompts](#2-interactive-prompts)
   - [Text Prompt](#21-text-prompt)
   - [Selection Prompt](#22-selection-prompt)
3. [Widgets Reference](#3-widgets-reference)
   - [Table](#31-table)
   - [Panel](#32-panel)
   - [Rule](#33-rule)
   - [FigletText](#34-figlettext)
4. [Live & Progress Indicators](#4-live--progress-indicators)
   - [Live Status Spinner](#41-live-status-spinner)
   - [Progress Bars](#42-progress-bars)
5. [Spectre.Console Compatibility](#5-spectreconsole-compatibility)

---

## 1. Standard Rendering

The primary entry point is the static `AnsiConsole` class. It manages low-level virtual terminal processing and writes formatted output directly to standard output.

### 1.1 Writing Text

```csharp
using HeroParser.Console;

// Direct console outputs
AnsiConsole.Write("Hello World");
AnsiConsole.WriteLine("With Newline");

// Styled console outputs
Style highlight = new Style(Color.Yellow, Color.Blue, Decoration.Bold);
AnsiConsole.Write("Yellow on Blue", highlight);
AnsiConsole.WriteLine("Styled line", highlight);
```

### 1.2 Markup Syntax

`AnsiConsole` supports a bbcode-like markup parser that runs entirely on stack-allocated spans (`stackalloc` char buffers), eliminating string manipulation overhead:

```csharp
// Single markup
AnsiConsole.MarkupLine("[bold red]Error:[/] Something went wrong.");

// Nested styles
AnsiConsole.MarkupLine("[blue]This is [yellow]yellow text[/] inside blue.[/]");

// Background and decorations
AnsiConsole.MarkupLine("[underline white on green]Success![/]");
```

### 1.3 Colors and Styles

- **`Color`**: Supports standard console colors, 8-bit palette index colors, and 24-bit TrueColor (RGB).
- **`Style`**: Combines a foreground color, background color, and a bitmask of `Decoration` flags.
- **`Decoration`**: Flags include `Bold`, `Dim`, `Italic`, `Underline`, `Invert`, `Conceal`, `SlowBlink`, `RapidBlink`, and `Strikethrough`.

```csharp
var customColor = Color.FromRgb(24, 144, 255); // TrueColor (RGB)
var style = new Style(customColor, Color.Default, Decoration.Bold | Decoration.Underline);
```

---

## 2. Interactive Prompts

Interactive keyboard prompts let you capture verified console input.

### 2.1 Text Prompt

`TextPrompt<T>` prompts the user for text input and supports automatic type parsing, default fallbacks, and validation functions:

```csharp
using HeroParser.Console.Prompts;

int age = new TextPrompt<int>("How old are you?")
    .DefaultValue(18)
    .Validate(val => val >= 0 && val <= 120 
        ? ValidationResult.Success() 
        : ValidationResult.Error("[red]Please enter a valid age between 0 and 120[/]"))
    .Show();
```

### 2.2 Selection Prompt

`SelectionPrompt<T>` allows terminal users to navigate and select from a list of choices using their arrow keys and `Enter`:

```csharp
using HeroParser.Console.Prompts;

string choice = new SelectionPrompt<string>("Select a format to export:")
    .AddChoice("CSV")
    .AddChoice("JSONL")
    .AddChoice("Excel")
    .AddChoice("Fixed-Width")
    .HighlightStyle(new Style(Color.Cyan, Color.Default, Decoration.Bold))
    .Show();
```

---

## 3. Widgets Reference

Widgets implement the `IConsoleWidget` interface and are rendered inside standard console layouts.

### 3.1 Table

`Table` (or `TableWidget`) handles tabular rendering with auto-adjusting column widths:

```csharp
using HeroParser.Console;

var table = new Table()
    .Border(TableBorder.Rounded);

table.AddColumn("ID");
table.AddColumn("Name");
table.AddColumn("Price");

table.AddRow("1", "Product A", "$12.50");
table.AddRow("2", "Product B", "$99.00");

AnsiConsole.Write(table);
```

### 3.2 Panel

`Panel` (or `PanelWidget`) renders a border box container around a text message or a child widget:

```csharp
using HeroParser.Console;

var panel = new Panel("This is inside a rounded panel!")
{
    Header = new PanelHeader("System Log"),
    Border = BoxBorder.Rounded
};

AnsiConsole.Write(panel);
```

### 3.3 Rule

`Rule` (or `RuleWidget`) renders a horizontal line with an optional text label:

```csharp
using HeroParser.Console;

var rule = new Rule("Database Status")
    .LeftJustified();

AnsiConsole.Write(rule);
```

### 3.4 FigletText

`FigletText` prints a large ASCII banner box:

```csharp
using HeroParser.Console;

var banner = new FigletText("HeroParser")
    .Color(Color.Aqua);

AnsiConsole.Write(banner);
```

---

## 4. Live & Progress Indicators

Live indicators display ongoing execution without spamming the terminal history, using inline console carriage return overrides.

### 4.1 Live Status Spinner

Displays a loading spinner next to a status text message:

```csharp
using HeroParser.Console;

await AnsiConsole.Status()
    .Spinner(Spinner.Known.Dots)
    .StartAsync("Loading datasets...", async ctx =>
    {
        await Task.Delay(2000); // Do work
        ctx.Status("Translating rows...");
        await Task.Delay(2000); // Do more work
    });
```

### 4.2 Progress Bars

Displays live progress bars indicating execution ticks across async operations:

```csharp
using HeroParser.Console;

await AnsiConsole.Progress()
    .StartAsync(async ctx =>
    {
        var task = ctx.AddTask("[green]Converting records[/]", maxValue: 100);
        
        while (!task.IsFinished)
        {
            await Task.Delay(100);
            task.Increment(5);
        }
    });
```

---

## 5. Spectre.Console Compatibility

If you are migrating an existing application from `Spectre.Console` to `HeroParser.Console`, you can use the built-in compatibility stubs to minimize code changes.

Simply update your imports to use `HeroParser.Console` names:

```csharp
// Replace: using Spectre.Console;
// With:
using HeroParser.Console;
using HeroParser.Console.Prompts;

// Pre-existing Spectre.Console code remains compatible:
AnsiConsole.MarkupLine("[bold yellow]Status:[/] Initiating transfer...");

var table = new Table().Border(TableBorder.Rounded);
table.AddColumn("[blue]Filename[/]");
table.AddColumn("[blue]Size[/]");

table.AddRow("data.csv", "4.2 MB");
table.AddRow("data.htb", "1.1 MB");

AnsiConsole.Write(table);
```

---

[Back to README](../README.md)
