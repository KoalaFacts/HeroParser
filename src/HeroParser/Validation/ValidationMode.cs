namespace HeroParser.Validation;

/// <summary>
/// Controls how validation errors are surfaced during record reading.
/// </summary>
public enum ValidationMode
{
    /// <summary>
    /// Validation errors cause terminal methods (<c>ToList</c>, <c>ToArray</c>, etc.) to throw
    /// a <see cref="ValidationException"/>. This is the default.
    /// </summary>
    Strict,

    /// <summary>
    /// Validation errors are collected silently. Invalid rows are excluded from results
    /// but no exception is thrown. Errors can be inspected via <see cref="SeparatedValues.Reading.Records.CsvRecordReader{TElement, T}.Errors"/>.
    /// </summary>
    Lenient
}
