namespace HeroParser.Generators;

/// <summary>
/// Formats a string value as an escaped C# string literal fragment.
/// </summary>
internal readonly struct CSharpStringLiteral
{
    private readonly string value;

    internal CSharpStringLiteral(string value)
    {
        this.value = value;
    }

    /// <summary>Gets the escaped text suitable for placing inside double quotes in generated C# source.</summary>
    internal string Escaped => value
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"")
        .Replace("\n", "\\n")
        .Replace("\r", "\\r")
        .Replace("\t", "\\t")
        .Replace("\0", "\\0");

    /// <summary>Gets the complete double-quoted C# string literal.</summary>
    internal string Quoted => $"\"{Escaped}\"";

    /// <inheritdoc/>
    public override string ToString() => Escaped;
}

/// <summary>
/// Formats a character value as an escaped C# char literal fragment.
/// </summary>
internal readonly struct CSharpCharLiteral
{
    private readonly char value;

    internal CSharpCharLiteral(char value)
    {
        this.value = value;
    }

    /// <summary>Gets the escaped character suitable for placing inside single quotes in generated C# source.</summary>
    internal string Escaped => value switch
    {
        '\'' => "\\'",
        '\\' => "\\\\",
        '\0' => "\\0",
        '\n' => "\\n",
        '\r' => "\\r",
        '\t' => "\\t",
        _ => value.ToString()
    };

    /// <summary>Gets the complete single-quoted C# char literal.</summary>
    internal string Quoted => $"'{Escaped}'";

    /// <inheritdoc/>
    public override string ToString() => Escaped;
}
