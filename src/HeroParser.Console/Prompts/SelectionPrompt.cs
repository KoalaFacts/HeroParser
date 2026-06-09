using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace HeroParser.Console.Prompts;

/// <summary>
/// An interactive prompt that allows the user to select from a list of choices using the keyboard.
/// </summary>
/// <typeparam name="T">The type of the choices.</typeparam>
public class SelectionPrompt<T> where T : notnull
{
    private string title = string.Empty;
    private readonly List<T> choices = [];
    private Style highlightStyle = new(Color.Cyan, default, Decoration.Bold);

    /// <summary>
    /// Initializes a new instance of the <see cref="SelectionPrompt{T}"/> class.
    /// </summary>
    public SelectionPrompt()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SelectionPrompt{T}"/> class with a title.
    /// </summary>
    public SelectionPrompt(string title)
    {
        this.title = title ?? string.Empty;
    }

    /// <summary>
    /// Configures the title of the selection prompt.
    /// </summary>
    public SelectionPrompt<T> Title(string promptTitle)
    {
        title = promptTitle ?? string.Empty;
        return this;
    }

    /// <summary>
    /// Configures the page size of choices (no-op stub).
    /// </summary>
    [SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "API compatibility with Spectre.Console")]
    public SelectionPrompt<T> PageSize(int size)
    {
        return this;
    }

    /// <summary>
    /// Configures the more choices text (no-op stub).
    /// </summary>
    [SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "API compatibility with Spectre.Console")]
    public SelectionPrompt<T> MoreChoicesText(string text)
    {
        return this;
    }

    /// <summary>
    /// Adds choices to the selection menu.
    /// </summary>
    public SelectionPrompt<T> AddChoices(IEnumerable<T> items)
    {
        choices.AddRange(items);
        return this;
    }

    /// <summary>
    /// Adds a single choice to the selection menu.
    /// </summary>
    public SelectionPrompt<T> AddChoice(T item)
    {
        choices.Add(item);
        return this;
    }

    /// <summary>
    /// Configures the highlighting style of the selected option.
    /// </summary>
    public SelectionPrompt<T> HighlightStyle(Style style)
    {
        highlightStyle = style;
        return this;
    }

    /// <summary>
    /// Renders the prompt and waits for the user to make a selection.
    /// </summary>
    public T Show()
    {
        if (choices.Count == 0)
        {
            throw new InvalidOperationException("Cannot show SelectionPrompt with 0 choices.");
        }

        int selectedIndex = 0;
        bool isFirstRender = true;

        // Hide terminal cursor
        System.Console.Write("\x1b[?25l");

        try
        {
            while (true)
            {
                if (!isFirstRender)
                {
                    // Move cursor up choices.Count lines to redraw in place
                    System.Console.Write($"\x1b[{choices.Count}A");
                }
                else
                {
                    if (!string.IsNullOrEmpty(title))
                    {
                        AnsiConsole.MarkupLine(title);
                    }
                    isFirstRender = false;
                }

                // Render each option
                for (int i = 0; i < choices.Count; i++)
                {
                    // Clear the line to prevent trailing text overflow from previous frames
                    System.Console.Write("\x1b[2K\r");

                    if (i == selectedIndex)
                    {
                        AnsiConsole.Write("> ", highlightStyle);
                        AnsiConsole.WriteLine(choices[i].ToString() ?? string.Empty, highlightStyle);
                    }
                    else
                    {
                        System.Console.Write("  ");
                        System.Console.WriteLine(choices[i].ToString() ?? string.Empty);
                    }
                }

                // Wait for keystroke
                var key = System.Console.ReadKey(intercept: true).Key;
                if (key == ConsoleKey.UpArrow)
                {
                    selectedIndex = selectedIndex == 0 ? choices.Count - 1 : selectedIndex - 1;
                }
                else if (key == ConsoleKey.DownArrow)
                {
                    selectedIndex = selectedIndex == choices.Count - 1 ? 0 : selectedIndex + 1;
                }
                else if (key == ConsoleKey.Enter)
                {
                    // Print selected option in place and restore standard line
                    System.Console.Write($"\x1b[{choices.Count}A");
                    for (int i = 0; i < choices.Count; i++)
                    {
                        System.Console.Write("\x1b[2K\r");
                    }
                    AnsiConsole.Write("> Selected: ", Style.Default.WithDim());
                    AnsiConsole.WriteLine(choices[selectedIndex].ToString() ?? string.Empty, highlightStyle);
                    return choices[selectedIndex];
                }
            }
        }
        finally
        {
            // Always restore terminal cursor visibility
            System.Console.Write("\x1b[?25h");
        }
    }
}
