namespace HeroParser.SeparatedValues;

/// <summary>
/// Represents a single CSV column backed by <see cref="ReadOnlyMemory{T}"/> for zero-allocation access.
/// </summary>
/// <remarks>
/// Unlike <see cref="CsvCharSpanColumn"/>, this struct can be stored in fields and collections
/// because it uses <see cref="ReadOnlyMemory{T}"/> instead of <see cref="ReadOnlySpan{T}"/>.
/// </remarks>
public readonly struct CsvMemoryColumn
{
    private readonly ReadOnlyMemory<char> memory;

    internal CsvMemoryColumn(ReadOnlyMemory<char> memory)
    {
        this.memory = memory;
    }

    /// <summary>Gets the underlying memory.</summary>
    public ReadOnlyMemory<char> Memory => memory;

    /// <summary>Gets the underlying span for parsing operations.</summary>
    public ReadOnlySpan<char> Span => memory.Span;

    /// <summary>Gets the length of the column value.</summary>
    public int Length => memory.Length;

    /// <summary>Gets whether the column value is empty.</summary>
    public bool IsEmpty => memory.IsEmpty;

    /// <summary>Converts the column value to a string.</summary>
    public override string ToString() => new(memory.Span);

    /// <summary>Implicit conversion to ReadOnlyMemory{char}.</summary>
    public static implicit operator ReadOnlyMemory<char>(CsvMemoryColumn column)
    {
        return column.memory;
    }

    /// <summary>Implicit conversion to ReadOnlySpan{char}.</summary>
    public static implicit operator ReadOnlySpan<char>(CsvMemoryColumn column)
    {
        return column.memory.Span;
    }
}
