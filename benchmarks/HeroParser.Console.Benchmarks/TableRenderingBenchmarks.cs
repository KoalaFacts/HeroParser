using System;
using System.IO;
using System.Text;
using BenchmarkDotNet.Attributes;
using HeroParser.Console;
using HeroParser.Console.Widgets;
using Spectre.Console;

namespace HeroParser.Console.Benchmarks;

[MemoryDiagnoser]
[HtmlExporter]
public class TableRenderingBenchmarks
{
    private class DummyTextWriter : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(char value) { }
        public override void Write(string? value) { }
        public override void Write(char[] buffer, int index, int count) { }
        public override void Write(ReadOnlySpan<char> buffer) { }
    }

    private IAnsiConsole spectreConsole = null!;

    [GlobalSetup]
    public void Setup()
    {
        spectreConsole = Spectre.Console.AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.TrueColor,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(new DummyTextWriter())
        });
    }

    [Benchmark]
    public void HeroParserConsole_Table_Render()
    {
        var table = new TableWidget();
        table.AddColumn("Name");
        table.AddColumn("Value 1");
        table.AddColumn("Value 2");

        for (int i = 0; i < 50; i++)
        {
            table.AddRow($"Item {i}", $"ValA {i}", $"ValB {i}");
        }

        // Zero allocation render path using stackalloc buffer
        Span<char> charBuf = stackalloc char[16384];
        var buffer = new AnsiBuffer(charBuf);
        table.Render(ref buffer, 80);
    }

    [Benchmark]
    public void SpectreConsole_Table_Render()
    {
        var table = new Table();
        table.AddColumn("Name");
        table.AddColumn("Value 1");
        table.AddColumn("Value 2");

        for (int i = 0; i < 50; i++)
        {
            table.AddRow($"Item {i}", $"ValA {i}", $"ValB {i}");
        }

        spectreConsole.Write((Spectre.Console.Rendering.IRenderable)table);
    }
}
