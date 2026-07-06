using System;

namespace HeroParser.Console.Widgets;

/// <summary>
/// A horizontal line console widget, optionally displaying a centered label.
/// </summary>
public class RuleWidget : IConsoleWidget
{
    private readonly string label;
    private readonly char borderChar;
    private readonly Style style;

    /// <summary>
    /// Initializes a new instance of the <see cref="RuleWidget"/> class.
    /// </summary>
    public RuleWidget(string label = "", char borderChar = '─', Style style = default)
    {
        this.label = label ?? string.Empty;
        this.borderChar = borderChar;
        this.style = style;
    }

    /// <summary>
    /// Renders the horizontal rule widget with optional centered label.
    /// </summary>
    public void Render(ref AnsiBuffer buffer, int maxWidth)
    {
        if (maxWidth <= 0) return;

        if (string.IsNullOrEmpty(label))
        {
            // Render straight horizontal rule
            Span<char> rule = stackalloc char[Math.Min(maxWidth, 1024)];
            int remaining = maxWidth;
            while (remaining > 0)
            {
                int chunk = Math.Min(remaining, rule.Length);
                rule[..chunk].Fill(borderChar);
                buffer.WriteStyled(rule[..chunk], style);
                remaining -= chunk;
            }
            buffer.Write(Environment.NewLine);
            return;
        }

        // Render centered label e.g.: ───── Label ─────
        int padding = 2; // Spacing surrounding label text
        int textLength = AnsiConsole.GetMarkupVisualLength(label) + padding * 2;

        if (textLength >= maxWidth)
        {
            AnsiConsole.Markup(label.AsSpan(), ref buffer, style);
            buffer.Write(Environment.NewLine);
            return;
        }

        int leftWidth = (maxWidth - textLength) / 2;
        int rightWidth = maxWidth - textLength - leftWidth;

        // Draw left side
        if (leftWidth > 0)
        {
            Span<char> left = stackalloc char[leftWidth];
            left.Fill(borderChar);
            buffer.WriteStyled(left, style);
        }

        // Draw label
        buffer.Write("  ");
        AnsiConsole.Markup(label.AsSpan(), ref buffer, style);
        buffer.Write("  ");

        // Draw right side
        if (rightWidth > 0)
        {
            Span<char> right = stackalloc char[rightWidth];
            right.Fill(borderChar);
            buffer.WriteStyled(right, style);
        }

        buffer.Write(Environment.NewLine);
    }
}
