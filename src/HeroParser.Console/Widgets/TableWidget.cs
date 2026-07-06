using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroParser.Console.Widgets;

/// <summary>
/// Represents a column definition in a TableWidget.
/// </summary>
public class TableColumn
{
    /// <summary>
    /// Gets the text header of this column.
    /// </summary>
    public string Header { get; }

    /// <summary>
    /// Gets or sets the formatting style of the column header.
    /// </summary>
    public Style HeaderStyle { get; set; }

    /// <summary>
    /// Gets or sets the formatting style of the cells under this column.
    /// </summary>
    public Style ColumnStyle { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TableColumn"/> class.
    /// </summary>
    /// <param name="header">The column header text.</param>
    /// <param name="headerStyle">The style used to render the column header.</param>
    /// <param name="columnStyle">The style used to render cells in this column.</param>
    public TableColumn(string header, Style headerStyle = default, Style columnStyle = default)
    {
        Header = header ?? string.Empty;
        HeaderStyle = headerStyle;
        ColumnStyle = columnStyle;
    }
}

/// <summary>
/// A table console widget that auto-sizes columns and renders grids.
/// </summary>
public class TableWidget : IConsoleWidget
{
    private readonly List<TableColumn> columns = [];
    private readonly List<string[]> rows = [];

    /// <summary>
    /// Gets or sets the formatting style of the table border.
    /// </summary>
    public Style BorderStyle { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether column headers should be rendered.
    /// </summary>
    public bool ShowHeaders { get; set; } = true;

    /// <summary>
    /// Adds a column to the table.
    /// </summary>
    public TableWidget AddColumn(string header, Style headerStyle = default, Style columnStyle = default)
    {
        columns.Add(new TableColumn(header, headerStyle, columnStyle));
        return this;
    }

    /// <summary>
    /// Adds a row of data cells to the table.
    /// </summary>
    public TableWidget AddRow(params string[] cells)
    {
        var row = new string[columns.Count];
        for (int i = 0; i < columns.Count; i++)
        {
            row[i] = i < cells.Length ? cells[i] : string.Empty;
        }
        rows.Add(row);
        return this;
    }

    /// <summary>
    /// Renders the entire table widget with grid lines and cells.
    /// </summary>
    public void Render(ref AnsiBuffer buffer, int maxWidth)
    {
        if (columns.Count == 0 || maxWidth <= 0) return;

        // Allocate a single workspace buffer once to prevent CA2014 stackoverflow inside loops
        Span<char> spaces = stackalloc char[maxWidth];
        spaces.Fill(' ');

        // 1. Calculate column widths
        int borderOverhead = (columns.Count - 1) * 3 + 4; // e.g. "│ col1 │ col2 │" -> 4 outer borders + 3 internal separator chars
        int availableWidth = Math.Max(0, maxWidth - borderOverhead);

        int[] colWidths = new int[columns.Count];
        for (int i = 0; i < columns.Count; i++)
        {
            colWidths[i] = Math.Max(3, AnsiConsole.GetMarkupVisualLength(columns[i].Header));
        }

        int currentSum = colWidths.Sum();
        if (currentSum < availableWidth)
        {
            // Grow columns up to their max cell widths
            for (int i = 0; i < columns.Count; i++)
            {
                int maxCellLen = rows.Count > 0 ? rows.Max(r => AnsiConsole.GetMarkupVisualLength(r[i] ?? string.Empty)) : 0;
                colWidths[i] = Math.Max(colWidths[i], maxCellLen);
            }

            currentSum = colWidths.Sum();
            if (currentSum > availableWidth)
            {
                // Shrink proportionally if max content exceeds available space
                int totalMax = currentSum;
                for (int i = 0; i < columns.Count; i++)
                {
                    colWidths[i] = (colWidths[i] * availableWidth) / totalMax;
                    colWidths[i] = Math.Max(3, colWidths[i]);
                }
            }
        }
        else
        {
            // Headers already exceed space, shrink proportionally
            int totalMax = currentSum;
            for (int i = 0; i < columns.Count; i++)
            {
                colWidths[i] = (colWidths[i] * availableWidth) / totalMax;
                colWidths[i] = Math.Max(3, colWidths[i]);
            }
        }

        // 2. Draw Top Border: ┌───┬───┐
        DrawSeparator(ref buffer, colWidths, '┌', '─', '┬', '┐');

        // 3. Draw Headers
        if (ShowHeaders)
        {
            buffer.WriteStyled("│ ", BorderStyle);
            for (int i = 0; i < columns.Count; i++)
            {
                var headerText = columns[i].Header;
                AnsiConsole.Markup(headerText.AsSpan(), ref buffer, columns[i].HeaderStyle);

                int pad = colWidths[i] - AnsiConsole.GetMarkupVisualLength(headerText);
                if (pad > 0)
                {
                    buffer.Write(spaces[..pad]);
                }

                buffer.WriteStyled(i == columns.Count - 1 ? " │" : " │ ", BorderStyle);
            }
            buffer.Write(Environment.NewLine);

            // Header separator: ├───┼───┤
            DrawSeparator(ref buffer, colWidths, '├', '─', '┼', '┤');
        }

        // 4. Draw Rows
        for (int r = 0; r < rows.Count; r++)
        {
            var rowCells = rows[r];

            // Calculate max lines required for this row (wrapped cells)
            int maxLines = 1;
            for (int i = 0; i < columns.Count; i++)
            {
                maxLines = Math.Max(maxLines, GetLineCount(rowCells[i].AsSpan(), colWidths[i]));
            }

            for (int l = 0; l < maxLines; l++)
            {
                buffer.WriteStyled("│ ", BorderStyle);
                for (int i = 0; i < columns.Count; i++)
                {
                    var cellLine = GetWrappedLine(rowCells[i].AsSpan(), colWidths[i], l);
                    AnsiConsole.Markup(cellLine, ref buffer, columns[i].ColumnStyle);

                    int pad = colWidths[i] - AnsiConsole.GetMarkupVisualLength(cellLine);
                    if (pad > 0)
                    {
                        buffer.Write(spaces[..pad]);
                    }

                    buffer.WriteStyled(i == columns.Count - 1 ? " │" : " │ ", BorderStyle);
                }
                buffer.Write(Environment.NewLine);
            }
        }

        // 5. Draw Bottom Border: └───┴───┘
        DrawSeparator(ref buffer, colWidths, '└', '─', '┴', '┘');
    }

    private void DrawSeparator(ref AnsiBuffer buffer, int[] widths, char left, char mid, char cross, char right)
    {
        buffer.WriteStyled(left.ToString(), BorderStyle);
        int maxVal = widths.Length > 0 ? widths.Max() + 2 : 2;
        Span<char> divider = stackalloc char[maxVal];
        divider.Fill(mid);

        for (int i = 0; i < widths.Length; i++)
        {
            int fillLen = widths[i] + 2;
            buffer.WriteStyled(divider[..fillLen], BorderStyle);
            if (i < widths.Length - 1)
            {
                buffer.WriteStyled(cross.ToString(), BorderStyle);
            }
        }
        buffer.WriteStyled(right.ToString(), BorderStyle);
        buffer.Write(Environment.NewLine);
    }

    private static int GetLineCount(ReadOnlySpan<char> text, int width)
    {
        if (text.IsEmpty) return 1;
        int count = 0;
        int index = 0;
        while (index < text.Length)
        {
            int remaining = text.Length - index;
            int take = Math.Min(remaining, width);
            if (take < remaining)
            {
                int lastSpace = text[index..(index + take)].LastIndexOf(' ');
                if (lastSpace > 0)
                {
                    take = lastSpace;
                }
            }
            count++;
            index += take;
            if (index < text.Length && text[index] == ' ')
            {
                index++;
            }
        }
        return count;
    }

    private static ReadOnlySpan<char> GetWrappedLine(ReadOnlySpan<char> text, int width, int lineIndex)
    {
        if (text.IsEmpty) return [];

        int currentLine = 0;
        int index = 0;
        while (index < text.Length)
        {
            int remaining = text.Length - index;
            int take = Math.Min(remaining, width);
            if (take < remaining)
            {
                int lastSpace = text[index..(index + take)].LastIndexOf(' ');
                if (lastSpace > 0)
                {
                    take = lastSpace;
                }
            }

            if (currentLine == lineIndex)
            {
                return text[index..(index + take)];
            }

            index += take;
            if (index < text.Length && text[index] == ' ')
            {
                index++;
            }
            currentLine++;
        }
        return [];
    }
}
