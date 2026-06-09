using System;

namespace HeroParser.Console.Widgets;

/// <summary>
/// Defines the rendering contract for all console widgets.
/// </summary>
public interface IConsoleWidget
{
    /// <summary>
    /// Renders the widget into the given ANSI character buffer.
    /// </summary>
    /// <param name="buffer">The destination buffer.</param>
    /// <param name="maxWidth">The maximum line width allowed for this widget.</param>
    void Render(ref AnsiBuffer buffer, int maxWidth);
}
