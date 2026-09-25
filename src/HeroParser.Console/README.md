# HeroParser.Console

An allocation-conscious terminal UI library for .NET 8 and later. It provides ANSI styling, markup, tables, panels, prompts, progress displays, and compatibility widgets without external package dependencies.

## Install

```bash
dotnet add package HeroParser.Console
```

## Example

```csharp
using HeroParser.Console;
using HeroParser.Console.Widgets;

var table = new TableWidget()
    .AddColumn("Name")
    .AddColumn("Value")
    .AddRow("Records", "12051");

AnsiConsole.Write(table);
```

See the [repository README](https://github.com/KoalaFacts/HeroParser#readme) for the project overview and API documentation.
