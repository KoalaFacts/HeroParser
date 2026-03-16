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
    private readonly ValidationMode validationMode;

    internal FixedWidthReadResult(List<T> records, List<ValidationError> errors, ValidationMode validationMode = ValidationMode.Strict)
    {
        this.records = records;
        Errors = errors;
        this.validationMode = validationMode;
    }

    /// <summary>Gets the successfully parsed records.</summary>
    public IReadOnlyList<T> Records => records;

    /// <summary>Gets the validation errors collected during parsing. Empty when no errors occurred.</summary>
    public IReadOnlyList<ValidationError> Errors { get; }

    /// <summary>Gets the number of successfully parsed records.</summary>
    public int Count => records.Count;

    /// <summary>Converts the result to a <see cref="List{T}"/> containing only the valid records.</summary>
    /// <remarks>
    /// In <see cref="ValidationMode.Strict"/> mode (default), throws a <see cref="ValidationException"/> if any errors were collected.
    /// In <see cref="ValidationMode.Lenient"/> mode, invalid rows are silently excluded and no exception is thrown.
    /// </remarks>
    /// <exception cref="ValidationException">Thrown when validation errors exist and mode is <see cref="ValidationMode.Strict"/>.</exception>
    public List<T> ToList()
    {
        ThrowOnStrictErrors();
        return [.. records];
    }

    internal void ThrowOnStrictErrors()
    {
        if (validationMode == ValidationMode.Strict && Errors.Count > 0)
            throw new ValidationException(Errors);
    }

    /// <inheritdoc/>
    public IEnumerator<T> GetEnumerator() => records.GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => records.GetEnumerator();
}
