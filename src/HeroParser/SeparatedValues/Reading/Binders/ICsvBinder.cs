using HeroParser.SeparatedValues.Reading.Rows;

namespace HeroParser.SeparatedValues.Reading.Binders;

/// <summary>
/// Interface for CSV binders that bind rows to strongly-typed records.
/// </summary>
/// <typeparam name="TElement">The element type: <see cref="char"/> for UTF-16 or <see cref="byte"/> for UTF-8.</typeparam>
/// <typeparam name="T">The record type to bind to.</typeparam>
/// <remarks>
/// Implemented by both reflection-based, descriptor-based, and source-generated binders.
/// </remarks>
public interface ICsvBinder<TElement, T>
    where TElement : unmanaged, IEquatable<TElement>
    where T : class, new()
{
    /// <summary>
    /// Gets whether the binder needs header resolution before binding data rows.
    /// </summary>
    bool NeedsHeaderResolution { get; }

    /// <summary>
    /// Resolves column indices from the header row.
    /// </summary>
    /// <param name="headerRow">The header row containing column names.</param>
    /// <param name="rowNumber">The 1-based row number for error reporting.</param>
    void BindHeader(CsvRow<TElement> headerRow, int rowNumber);

    /// <summary>
    /// Binds a CSV row to a new record instance without boxing.
    /// </summary>
    /// <param name="row">The row to bind.</param>
    /// <param name="rowNumber">The 1-based row number for error reporting.</param>
    /// <returns>The bound record, or null if the row should be skipped.</returns>
    T? Bind(CsvRow<TElement> row, int rowNumber);

    /// <summary>
    /// Binds a CSV row into an existing record instance.
    /// This avoids allocating a new record object for each row.
    /// </summary>
    /// <param name="instance">The existing instance to bind into.</param>
    /// <param name="row">The row to bind.</param>
    /// <param name="rowNumber">The 1-based row number for error reporting.</param>
    /// <returns>True if binding succeeded, false if the row should be skipped.</returns>
    /// <remarks>
    /// Note: String properties still allocate new strings for each call.
    /// For true zero-allocation, use the span-based row API directly.
    /// </remarks>
    bool BindInto(T instance, CsvRow<TElement> row, int rowNumber);
}
