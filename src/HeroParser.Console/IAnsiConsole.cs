using System;

namespace HeroParser.Console;

/// <summary>
/// Abstracts the terminal a console UI writes to and reads from.
/// </summary>
/// <remarks>
/// Every rendering and prompting entry point in this library ultimately goes through
/// this interface, so a host can redirect console UI wholesale — into a string for a
/// test, a log, or an embedded pane — by supplying its own implementation. The default
/// is <see cref="SystemAnsiConsole"/>, which targets the process's real console.
/// </remarks>
public interface IAnsiConsole
{
    /// <summary>
    /// Gets the usable width in characters that widgets should render within.
    /// </summary>
    int Width { get; }

    /// <summary>Writes text with no styling.</summary>
    void Write(string text);

    /// <summary>Writes styled text.</summary>
    void Write(string text, Style style);

    /// <summary>Writes text followed by a line terminator.</summary>
    void WriteLine(string text);

    /// <summary>Writes styled text followed by a line terminator.</summary>
    void WriteLine(string text, Style style);

    /// <summary>Renders markup such as <c>[bold red]text[/]</c>.</summary>
    void Markup(string markupText);

    /// <summary>Renders markup followed by a line terminator.</summary>
    void MarkupLine(string markupText);

    /// <summary>Renders a widget at this console's width.</summary>
    void Write(Widgets.IConsoleWidget widget);

    /// <summary>
    /// Reads a single key press.
    /// </summary>
    /// <param name="intercept">When <see langword="true"/>, the key is not echoed.</param>
    ConsoleKeyInfo ReadKey(bool intercept);

    /// <summary>
    /// Reads a line of input, or <see langword="null"/> when input is exhausted.
    /// </summary>
    string? ReadLine();
}
