namespace HeroParser.FixedWidths.Records.Binding;

/// <summary>
/// Interface for fixed-width binders that avoid boxing during parsing.
/// Implemented by both reflection-based and descriptor-based binders.
/// </summary>
/// <typeparam name="T">The record type to bind to.</typeparam>
public interface IFixedWidthBinder<T> where T : new()
{
    /// <summary>
    /// Binds a fixed-width row to a new record instance without boxing.
    /// </summary>
    /// <param name="row">The row to bind.</param>
    /// <param name="result">The bound record when successful.</param>
    /// <returns>True if binding succeeded; otherwise false if the row should be skipped.</returns>
    bool TryBind(FixedWidthCharSpanRow row, out T result);

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
    bool BindInto(ref T instance, FixedWidthCharSpanRow row);
}
