using System.Collections;
using HeroParser.Validation;

namespace HeroParser.FixedWidths.Records;

/// <summary>
/// Holds the results of a fixed-width read operation, including both successfully parsed records
/// and any validation errors collected during parsing.
/// </summary>
/// <typeparam name="T">The record type.</typeparam>
/// <remarks>
/// Implements <see cref="IEnumerable{T}"/> so existing code that iterates over results continues to work.
/// Access <see cref="Errors"/> to inspect validation failures.
/// </remarks>
public sealed class FixedWidthReadResult<T> : IEnumerable<T>
{
    private readonly List<T> records;

    internal FixedWidthReadResult(List<T> records, List<ValidationError> errors)
    {
        this.records = records;
        Errors = errors;
    }

    /// <summary>Gets the successfully parsed records.</summary>
    public IReadOnlyList<T> Records => records;

    /// <summary>Gets the validation errors collected during parsing. Empty when no errors occurred.</summary>
    public IReadOnlyList<ValidationError> Errors { get; }

    /// <summary>Gets the number of successfully parsed records.</summary>
    public int Count => records.Count;

    /// <summary>Converts the result to a <see cref="List{T}"/> containing only the valid records.</summary>
    public List<T> ToList() => [.. records];

    /// <inheritdoc/>
    public IEnumerator<T> GetEnumerator() => records.GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => records.GetEnumerator();
}
