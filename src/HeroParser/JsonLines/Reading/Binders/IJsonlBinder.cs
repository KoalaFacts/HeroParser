namespace HeroParser.JsonLines.Reading.Binders;

/// <summary>
/// Interface for JSONL binders that bind UTF-8 JSON spans to strongly-typed records.
/// </summary>
/// <typeparam name="T">The record type to bind to.</typeparam>
public interface IJsonlBinder<T>
{
    /// <summary>
    /// Binds a UTF-8 JSON line span to a record instance.
    /// </summary>
    /// <param name="line">The UTF-8 encoded JSON line.</param>
    /// <returns>The bound record instance.</returns>
    T Bind(ReadOnlySpan<byte> line);
}
