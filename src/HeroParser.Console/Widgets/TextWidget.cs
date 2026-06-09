using System;

namespace HeroParser.Console.Widgets;

/// <summary>
/// A console widget that displays styled, word-wrapped text.
/// </summary>
public class TextWidget : IConsoleWidget
{
    private readonly string text;
    private readonly Style style;
    private readonly bool isMarkup;

    /// <summary>
    /// Initializes a new instance of the <see cref="TextWidget"/> class.
    /// </summary>
    public TextWidget(string text, Style style = default, bool isMarkup = false)
    {
        this.text = text ?? string.Empty;
        this.style = style;
        this.isMarkup = isMarkup;
    }

    /// <summary>
    /// Renders the styled, wrapped text widget.
    /// </summary>
    public void Render(ref AnsiBuffer buffer, int maxWidth)
    {
        if (maxWidth <= 0) return;

        var textSpan = text.AsSpan();
        int index = 0;
        while (index < textSpan.Length)
        {
            // Find next newline in text first to respect original layout
            int nextNewLine = textSpan[index..].IndexOf('\n');
            int segmentEnd = nextNewLine == -1 ? textSpan.Length : index + nextNewLine;
            var segment = textSpan[index..segmentEnd];

            // Wrap this segment to fit maxWidth
            int segmentIdx = 0;
            while (segmentIdx < segment.Length)
            {
                int remaining = segment.Length - segmentIdx;
                int take = Math.Min(remaining, maxWidth);

                if (take < remaining)
                {
                    // Attempt to wrap on word boundary (space)
                    int lastSpace = segment.Slice(segmentIdx, take).LastIndexOf(' ');
                    if (lastSpace > 0)
                    {
                        take = lastSpace;
                    }
                }

                var line = segment.Slice(segmentIdx, take);
                if (isMarkup)
                {
                    AnsiConsole.Markup(line, ref buffer);
                }
                else
                {
                    buffer.WriteStyled(line, style);
                }
                buffer.Write(Environment.NewLine);

                segmentIdx += take;
                // Skip the wrapping space if present
                if (segmentIdx < segment.Length && segment[segmentIdx] == ' ')
                {
                    segmentIdx++;
                }
            }

            index = nextNewLine == -1 ? textSpan.Length : segmentEnd + 1;
        }
    }
}
