using System;
using System.Text;

namespace HeroParser.Generators;

/// <summary>
/// Builds generated C# source code with tracked indentation levels.
/// </summary>
internal sealed class SourceBuilder
{
    private const string INDENT_STRING = "    ";

    private readonly StringBuilder builder;
    private int indent;

    /// <summary>
    /// Initializes a new instance with default capacity.
    /// </summary>
    internal SourceBuilder()
    {
        builder = new StringBuilder(4096);
    }

    /// <summary>
    /// Initializes a new instance with specified capacity.
    /// </summary>
    internal SourceBuilder(int capacity)
    {
        builder = new StringBuilder(capacity);
    }

    /// <summary>
    /// Increases indentation level.
    /// </summary>
    internal void Indent() => indent++;

    /// <summary>
    /// Decreases indentation level.
    /// </summary>
    internal void Unindent() => indent = Math.Max(0, indent - 1);

    /// <summary>
    /// Appends a line with current indentation.
    /// </summary>
    internal void AppendLine(string line = "")
    {
        for (int i = 0; i < indent; i++)
            builder.Append(INDENT_STRING);
        builder.AppendLine(line);
    }

    /// <summary>
    /// Appends text without indentation or newline.
    /// </summary>
    internal void Append(string text)
    {
        builder.Append(text);
    }

    /// <inheritdoc/>
    public override string ToString() => builder.ToString();

    /// <summary>
    /// Clears the builder content and resets indentation to zero, allowing the instance to be reused.
    /// </summary>
    internal void Clear()
    {
        builder.Clear();
        indent = 0;
    }
}
