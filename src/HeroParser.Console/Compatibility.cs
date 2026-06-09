using System;
using System.Diagnostics.CodeAnalysis;

namespace HeroParser.Console;

/// <summary>
/// A drop-in compatibility class mapping Table to TableWidget.
/// </summary>
public class Table : Widgets.TableWidget
{
    /// <summary>
    /// Stubs the TableBorder setting to support Spectre.Console API compatibility.
    /// </summary>
    [SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "API compatibility with Spectre.Console")]
    public Table Border(object border)
    {
        return this;
    }
}

/// <summary>
/// Predefined table borders for compatibility.
/// </summary>
public static class TableBorder
{
    /// <summary>
    /// Gets a rounded border stub.
    /// </summary>
    public static readonly object Rounded = new();
}

/// <summary>
/// Predefined panel borders for compatibility.
/// </summary>
public static class BoxBorder
{
    /// <summary>
    /// Gets a rounded border stub.
    /// </summary>
    public static readonly object Rounded = new();

    /// <summary>
    /// Gets a none border stub.
    /// </summary>
    public static readonly object None = new();

    /// <summary>
    /// Gets a double border stub.
    /// </summary>
    public static readonly object Double = new();
}

/// <summary>
/// A drop-in compatibility class mapping Panel to PanelWidget.
/// </summary>
public class Panel : Widgets.PanelWidget
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Panel"/> class.
    /// </summary>
    public Panel(string text) : base(text)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Panel"/> class with a child widget.
    /// </summary>
    public Panel(Widgets.IConsoleWidget childWidget) : base(childWidget)
    {
    }

    /// <summary>
    /// Gets or sets the header of the panel.
    /// </summary>
    public PanelHeader? Header
    {
        get => string.IsNullOrEmpty(Title) ? null : new PanelHeader(Title);
        set => Title = value?.Text ?? string.Empty;
    }

    /// <summary>
    /// Gets or sets the border configuration for compatibility.
    /// </summary>
    public object? Border { get; set; }
}

/// <summary>
/// Represents a panel header wrapper.
/// </summary>
public class PanelHeader
{
    /// <summary>
    /// Gets the header text.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PanelHeader"/> class.
    /// </summary>
    public PanelHeader(string text)
    {
        Text = text ?? string.Empty;
    }
}

/// <summary>
/// A drop-in compatibility class mapping Rule to RuleWidget.
/// </summary>
public class Rule : Widgets.RuleWidget
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Rule"/> class.
    /// </summary>
    public Rule(string label = "") : base(label)
    {
    }

    /// <summary>
    /// Configures the rule alignment to centered (no-op stub).
    /// </summary>
    public Rule Centered() => this;

    /// <summary>
    /// Configures the rule alignment to left-justified (no-op stub).
    /// </summary>
    public Rule LeftJustified() => this;

    /// <summary>
    /// Configures the rule alignment to right-justified (no-op stub).
    /// </summary>
    public Rule RightJustified() => this;
}

/// <summary>
/// A compatibility widget representing larger banner text.
/// </summary>
public class FigletText : Widgets.IConsoleWidget
{
    private readonly string text;
    private HeroParser.Console.Color color = HeroParser.Console.Color.Default;

    /// <summary>
    /// Initializes a new instance of the <see cref="FigletText"/> class.
    /// </summary>
    public FigletText(string text)
    {
        this.text = text ?? string.Empty;
    }

    /// <summary>
    /// Configures the banner text color.
    /// </summary>
    public FigletText Color(HeroParser.Console.Color bannerColor)
    {
        color = bannerColor;
        return this;
    }

    /// <summary>
    /// Renders the FigletText directly to the buffer.
    /// </summary>
    public void Render(ref AnsiBuffer buffer, int maxWidth)
    {
        buffer.Write(Environment.NewLine);
        buffer.WriteStyled("╔═══════════════════════════════════════╗", new Style(color, default, Decoration.Bold));
        buffer.Write(Environment.NewLine);

        string centered = $"   {text}   ";
        int padding = (39 - centered.Length) / 2;
        padding = Math.Max(0, padding);
        string padStr = new(' ', padding);

        buffer.WriteStyled("║", new Style(color, default, Decoration.Bold));
        buffer.Write(padStr);
        buffer.WriteStyled(centered, new Style(color, default, Decoration.Bold));

        int rightPadding = 39 - centered.Length - padding;
        if (rightPadding > 0)
        {
            buffer.Write(new string(' ', rightPadding));
        }
        buffer.WriteStyled("║", new Style(color, default, Decoration.Bold));
        buffer.Write(Environment.NewLine);
        buffer.WriteStyled("╚═══════════════════════════════════════╝", new Style(color, default, Decoration.Bold));
        buffer.Write(Environment.NewLine);
    }
}

/// <summary>
/// A compatibility widget representing inline text.
/// </summary>
public class Text : Widgets.IConsoleWidget
{
    /// <summary>
    /// Gets the text value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Text"/> class.
    /// </summary>
    public Text(string value)
    {
        Value = value ?? string.Empty;
    }

    /// <summary>
    /// Renders the text directly to the buffer.
    /// </summary>
    public void Render(ref AnsiBuffer buffer, int maxWidth)
    {
        var textWidget = new Widgets.TextWidget(Value);
        textWidget.Render(ref buffer, maxWidth);
    }
}

/// <summary>
/// Represents inline styled markup text for compatibility.
/// </summary>
public class Markup : Widgets.IConsoleWidget
{
    /// <summary>
    /// Gets the markup text.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Markup"/> class.
    /// </summary>
    public Markup(string value)
    {
        Value = value ?? string.Empty;
    }

    /// <summary>
    /// Renders the markup directly to the buffer.
    /// </summary>
    public void Render(ref AnsiBuffer buffer, int maxWidth)
    {
        AnsiConsole.Markup(Value.AsSpan(), ref buffer);
    }

    /// <summary>
    /// Escapes square bracket characters.
    /// </summary>
    public static string Escape(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return text.Replace("[", "[[").Replace("]", "]]");
    }
}

// Dummy progress columns matching Spectre.Console
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

public class TaskDescriptionColumn
{
}

public class ProgressBarColumn
{
}

public class PercentageColumn
{
}

public class RemainingTimeColumn
{
}

public class SpinnerColumn
{
}

#pragma warning restore CS1591
