namespace HeroParser.SeparatedValues.Records.Binding;

/// <summary>
/// Interface for fully-typed CSV binders that avoid boxing during parsing.
/// Implemented by source-generated binders for maximum performance.
/// </summary>
/// <typeparam name="T">The record type to bind to.</typeparam>
internal interface ICsvTypedBinder<T> where T : class, new()
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
    void BindHeader(CsvCharSpanRow headerRow, int rowNumber);

    /// <summary>
    /// Binds a CSV row to a new record instance without boxing.
    /// </summary>
    /// <param name="row">The row to bind.</param>
    /// <param name="rowNumber">The 1-based row number for error reporting.</param>
    /// <returns>The bound record, or null if the row should be skipped.</returns>
    T? Bind(CsvCharSpanRow row, int rowNumber);

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
    bool BindInto(T instance, CsvCharSpanRow row, int rowNumber);
}
