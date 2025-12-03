namespace HeroParser.SeparatedValues.Records.Binding;

/// <summary>
/// Interface for fully-typed CSV binders that work with memory-backed rows.
/// Enables zero-allocation binding for <see cref="ReadOnlyMemory{T}"/> properties.
/// </summary>
/// <typeparam name="T">The record type to bind to.</typeparam>
internal interface ICsvTypedMemoryBinder<T> where T : class, new()
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
    void BindHeader(CsvMemoryRow headerRow, int rowNumber);

    /// <summary>
    /// Binds a memory-backed CSV row to a new record instance without boxing.
    /// </summary>
    /// <param name="row">The memory-backed row to bind.</param>
    /// <param name="rowNumber">The 1-based row number for error reporting.</param>
    /// <returns>The bound record, or null if the row should be skipped.</returns>
    T? Bind(CsvMemoryRow row, int rowNumber);

    /// <summary>
    /// Binds a memory-backed CSV row into an existing record instance.
    /// </summary>
    /// <param name="instance">The existing instance to bind into.</param>
    /// <param name="row">The memory-backed row to bind.</param>
    /// <param name="rowNumber">The 1-based row number for error reporting.</param>
    /// <returns>True if binding succeeded, false if the row should be skipped.</returns>
    bool BindInto(T instance, CsvMemoryRow row, int rowNumber);
}
