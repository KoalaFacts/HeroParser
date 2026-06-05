namespace HeroParser.JsonLines.Reading.Binders;

/// <summary>
/// Interface for JSONL binders that bind JSON spans to strongly-typed records.
/// </summary>
/// <typeparam name="TElement">The character/byte type of the span (char or byte).</typeparam>
/// <typeparam name="T">The record type to bind to.</typeparam>
public interface IJsonlSourceBinder<TElement, out T>
{
    /// <summary>
    /// Binds a JSON line span to a record instance.
    /// </summary>
    /// <param name="line">The encoded JSON line span.</param>
    /// <returns>The bound record instance.</returns>
    T Bind(ReadOnlySpan<TElement> line);
}
