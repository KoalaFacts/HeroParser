namespace HeroParser.FixedWidths.Records.Binding;

/// <summary>
/// Interface for fully-typed binders that avoid boxing during parsing.
/// Implemented by source-generated binders for maximum performance.
/// </summary>
/// <typeparam name="T">The record type to bind to.</typeparam>
internal interface IFixedWidthTypedBinder<T> where T : class, new()
{
    /// <summary>
    /// Binds a fixed-width row to a record instance without boxing.
    /// </summary>
    /// <param name="row">The row to bind.</param>
    /// <returns>The bound record, or null if the row should be skipped.</returns>
    T? Bind(FixedWidthCharSpanRow row);

    /// <summary>
    /// Binds a fixed-width row into an existing record instance.
    /// This avoids allocating a new record object for each row.
    /// </summary>
    /// <param name="instance">The existing instance to bind into.</param>
    /// <param name="row">The row to bind.</param>
    /// <returns>True if binding succeeded, false if the row should be skipped.</returns>
    /// <remarks>
    /// Note: String properties still allocate new strings for each call.
    /// For true zero-allocation, use the span-based row API directly.
    /// </remarks>
    bool BindInto(T instance, FixedWidthCharSpanRow row);
}
