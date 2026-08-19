using System.Text;
using AnsiBuf = HeroParser.Console.AnsiBuffer;
using AnsiConsoleApi = HeroParser.Console.AnsiConsole;
using AnsiStyle = HeroParser.Console.Style;
using IHeroConsole = HeroParser.Console.IAnsiConsole;

namespace HeroParser.Tests.ConsoleUi;

/// <summary>
/// An <see cref="IHeroConsole"/> that records everything written to it and replays a
/// scripted key sequence.
/// </summary>
/// <remarks>
/// The progress and status runners render from a background task while the caller's work
/// runs on another, so the capture has to be thread-safe — a plain StringWriter would tear.
/// Keys are supplied as <see cref="ConsoleKeyInfo"/> values so navigation keys that have no
/// character form can be scripted directly.
/// </remarks>
internal sealed class RecordingConsole : IHeroConsole
{
    private readonly StringBuilder sink = new();
    private readonly Queue<ConsoleKeyInfo> keys;
    private readonly Queue<string?> lines;

    public RecordingConsole(IEnumerable<ConsoleKeyInfo>? keys = null, IEnumerable<string?>? lines = null)
    {
        this.keys = new Queue<ConsoleKeyInfo>(keys ?? []);
        this.lines = new Queue<string?>(lines ?? []);
    }

    public int Width { get; init; } = 80;

    /// <summary>Returns everything written so far.</summary>
    public string Snapshot()
    {
        lock (sink) return sink.ToString();
    }

    private void Append(string text)
    {
        lock (sink) sink.Append(text);
    }

    public void Write(string text) => Append(text);

    public void WriteLine(string text) => Append(text + Environment.NewLine);

    public void Write(string text, AnsiStyle style) => Append(Styled(text, style, newLine: false));

    public void WriteLine(string text, AnsiStyle style) => Append(Styled(text, style, newLine: true));

    public void Markup(string markupText) => Append(RenderMarkup(markupText, newLine: false));

    public void MarkupLine(string markupText) => Append(RenderMarkup(markupText, newLine: true));

    public void Write(HeroParser.Console.Widgets.IConsoleWidget widget)
    {
        ArgumentNullException.ThrowIfNull(widget);
        Append(Render((ref buffer) => widget.Render(ref buffer, Width)));
    }

    public ConsoleKeyInfo ReadKey(bool intercept)
    {
        lock (sink)
        {
            return keys.Count > 0 ? keys.Dequeue() : new ConsoleKeyInfo('\0', ConsoleKey.Enter, false, false, false);
        }
    }

    public string? ReadLine()
    {
        lock (sink) return lines.Count > 0 ? lines.Dequeue() : null;
    }

    private static string Styled(string text, AnsiStyle style, bool newLine)
        => Render((ref buffer) =>
        {
            buffer.WriteStyled(text.AsSpan(), style);
            if (newLine) buffer.Write(Environment.NewLine);
        });

    private static string RenderMarkup(string markupText, bool newLine)
        => Render((ref buffer) =>
        {
            AnsiConsoleApi.Markup(markupText.AsSpan(), ref buffer);
            if (newLine) buffer.Write(Environment.NewLine);
        });

    private delegate void BufferAction(ref AnsiBuf buffer);

    private static string Render(BufferAction action)
    {
        using var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        Span<char> scratch = new char[16 * 1024];
        var buffer = new AnsiBuf(scratch, writer);
        action(ref buffer);
        buffer.Flush();
        return writer.ToString();
    }
}
