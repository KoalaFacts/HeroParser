using System.Diagnostics.CodeAnalysis;

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

/// <summary>
/// Interface for UTF-8 byte JSONL binders that can decline binding when a JSON line contains
/// a non-string token shape (such as an object) for a custom parsable property and requires
/// <see cref="System.Text.Json.JsonSerializer"/> fallback.
/// </summary>
/// <typeparam name="T">The record type to bind to.</typeparam>
public interface IJsonlTryByteBinder<T> : IJsonlSourceBinder<byte, T>
{
    /// <summary>
    /// Attempts to bind a UTF-8 JSON line span to a record instance.
    /// </summary>
    /// <param name="line">The UTF-8 encoded JSON line span.</param>
    /// <param name="result">When this method returns <see langword="true"/>, contains the bound record instance.</param>
    /// <returns><see langword="true"/> if binding succeeded; <see langword="false"/> if caller fallback to <see cref="System.Text.Json.JsonSerializer"/> is required.</returns>
    bool TryBind(ReadOnlySpan<byte> line, [MaybeNullWhen(false)] out T result);
}

/// <summary>
/// Validates a materialized JSONL record instance against generated <c>[Validate]</c> attributes.
/// </summary>
/// <typeparam name="T">The record type to validate.</typeparam>
public interface IJsonlRecordValidator<in T>
{
    /// <summary>
    /// Validates the specified record instance and throws <see cref="Validation.ValidationException"/> if any rule fails.
    /// </summary>
    /// <param name="record">The record instance to validate.</param>
    /// <param name="rowNumber">The 1-based physical line or record number to attach to reported validation errors.</param>
    void Validate(T record, int rowNumber = 0);
}
