using System;

namespace HeroParser.Console.Widgets;

/// <summary>
/// A panel widget that draws borders around styled, word-wrapped text or a child console widget.
/// </summary>
public class PanelWidget : IConsoleWidget
{
    /// <summary>
    /// Gets or sets the text content of the panel.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the nested child widget of the panel.
    /// </summary>
    public IConsoleWidget? ChildWidget { get; set; }

    /// <summary>
    /// Gets or sets the panel title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the border styling.
    /// </summary>
    public Style BorderStyle { get; set; }

    /// <summary>
    /// Gets or sets the title styling.
    /// </summary>
    public Style TitleStyle { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PanelWidget"/> class.
    /// </summary>
    public PanelWidget()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PanelWidget"/> class with text.
    /// </summary>
    public PanelWidget(string text, string title = "", Style borderStyle = default, Style titleStyle = default)
    {
        Text = text ?? string.Empty;
        Title = title ?? string.Empty;
        BorderStyle = borderStyle;
        TitleStyle = titleStyle;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PanelWidget"/> class with a child widget.
    /// </summary>
    public PanelWidget(IConsoleWidget childWidget, string title = "", Style borderStyle = default, Style titleStyle = default)
    {
        ChildWidget = childWidget;
        Title = title ?? string.Empty;
        BorderStyle = borderStyle;
        TitleStyle = titleStyle;
    }

    /// <summary>
    /// Renders the panel widget with border and title.
    /// </summary>
    public void Render(ref AnsiBuffer buffer, int maxWidth)
    {
        if (maxWidth <= 4) return;

        int innerWidth = maxWidth - 4;

        // Allocate border line buffer
        Span<char> borderLine = stackalloc char[innerWidth];

        // 1. Draw top border: ┌─── Title ───┐
        buffer.WriteStyled("┌", BorderStyle);
        if (!string.IsNullOrEmpty(Title))
        {
            buffer.WriteStyled("─ ", BorderStyle);
            AnsiConsole.Markup(Title.AsSpan(), ref buffer, TitleStyle);
            buffer.WriteStyled(" ", BorderStyle);
            int remainingTop = innerWidth - AnsiConsole.GetMarkupVisualLength(Title) - 2;
            if (remainingTop > 0)
            {
                borderLine[..remainingTop].Fill('─');
                buffer.WriteStyled(borderLine[..remainingTop], BorderStyle);
            }
        }
        else
        {
            borderLine.Fill('─');
            buffer.WriteStyled(borderLine, BorderStyle);
        }
        buffer.WriteStyled("┐", BorderStyle);
        buffer.Write(Environment.NewLine);

        // 2. Draw content (either nested widget or text)
        if (ChildWidget != null)
        {
            RenderChild(ref buffer, innerWidth);
        }
        else
        {
            RenderText(ref buffer, innerWidth);
        }

        // 3. Draw bottom border: └──────────┘
        buffer.WriteStyled("└", BorderStyle);
        borderLine.Fill('─');
        buffer.WriteStyled(borderLine, BorderStyle);
        buffer.WriteStyled("┘", BorderStyle);
        buffer.Write(Environment.NewLine);
    }

    private void RenderChild(ref AnsiBuffer buffer, int innerWidth)
    {
        using var stringWriter = new System.IO.StringWriter();
        char[] tempArray = System.Buffers.ArrayPool<char>.Shared.Rent(16384);
        try
        {
            var tempBuffer = new AnsiBuffer(tempArray, stringWriter);
            ChildWidget!.Render(ref tempBuffer, innerWidth);
            tempBuffer.Flush();

            string renderedChild = stringWriter.ToString();
            var lines = renderedChild.Split(["\r\n", "\n"], StringSplitOptions.None);
            int count = lines.Length;
            if (count > 0 && string.IsNullOrEmpty(lines[count - 1]) && (renderedChild.EndsWith("\n") || renderedChild.EndsWith("\r")))
            {
                count--;
            }

            for (int i = 0; i < count; i++)
            {
                buffer.WriteStyled("│ ", BorderStyle);

                string line = lines[i];
                int visualLen = GetVisualLength(line);
                buffer.Write(line);

                int paddingSpaces = innerWidth - visualLen;
                for (int s = 0; s < paddingSpaces; s++)
                {
                    buffer.Write(' ');
                }

                buffer.WriteStyled(" │", BorderStyle);
                buffer.Write(Environment.NewLine);
            }
        }
        finally
        {
            System.Buffers.ArrayPool<char>.Shared.Return(tempArray);
        }
    }

    private void RenderText(ref AnsiBuffer buffer, int innerWidth)
    {
        var textSpan = Text.AsSpan();
        int index = 0;
        while (index < textSpan.Length)
        {
            int nextNewLine = textSpan[index..].IndexOf('\n');
            int segmentEnd = nextNewLine == -1 ? textSpan.Length : index + nextNewLine;
            var segment = textSpan[index..segmentEnd];

            int segmentIdx = 0;
            while (segmentIdx < segment.Length || (segment.Length == 0 && segmentIdx == 0))
            {
                int remaining = segment.Length - segmentIdx;
                int take = Math.Min(remaining, innerWidth);

                if (take < remaining)
                {
                    int lastSpace = segment[segmentIdx..(segmentIdx + take)].LastIndexOf(' ');
                    if (lastSpace > 0)
                    {
                        take = lastSpace;
                    }
                }

                var line = segment[segmentIdx..(segmentIdx + take)];

                // Write left border
                buffer.WriteStyled("│ ", BorderStyle);

                // Write content
                buffer.Write(line);

                // Write padding spaces
                int paddingSpaces = innerWidth - line.Length;
                for (int s = 0; s < paddingSpaces; s++)
                {
                    buffer.Write(' ');
                }

                // Write right border
                buffer.WriteStyled(" │", BorderStyle);
                buffer.Write(Environment.NewLine);

                segmentIdx += take;
                if (segmentIdx < segment.Length && segment[segmentIdx] == ' ')
                {
                    segmentIdx++;
                }

                if (segment.Length == 0) break;
            }

            index = nextNewLine == -1 ? textSpan.Length : segmentEnd + 1;
        }
    }

    private static int GetVisualLength(string s)
    {
        int len = 0;
        int i = 0;
        while (i < s.Length)
        {
            if (s[i] == '\x1b')
            {
                i++;
                while (i < s.Length && s[i] != 'm')
                {
                    i++;
                }
                i++; // skip 'm'
            }
            else
            {
                len++;
                i++;
            }
        }
        return len;
    }
}
