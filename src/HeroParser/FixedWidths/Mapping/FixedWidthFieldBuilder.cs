namespace HeroParser.FixedWidths.Mapping;

/// <summary>
/// Fluent builder for configuring a single fixed-width field mapping used with inline
/// <c>Map&lt;TProperty&gt;()</c> calls on <see cref="Records.FixedWidthReaderBuilder{T}"/>.
/// </summary>
/// <remarks>
/// This builder is distinct from <see cref="FixedWidthColumnBuilder"/>, which is used with
/// <see cref="FixedWidthMap{T}"/> for both reading and writing. <see cref="FixedWidthFieldBuilder"/>
/// is read-only and focused on inline reader configuration.
/// </remarks>
public sealed class FixedWidthFieldBuilder
{
    /// <summary>Gets the configured start position, or null if not set.</summary>
    internal int? StartPosition { get; private set; }

    /// <summary>Gets the configured field length, or null if not set.</summary>
    internal int? FieldLength { get; private set; }

    /// <summary>Gets the configured end position (exclusive), or null if not set.</summary>
    internal int? EndPosition { get; private set; }

    /// <summary>Gets the configured padding character, or null for default.</summary>
    internal char? FieldPadChar { get; private set; }

    /// <summary>Gets the configured field alignment, or null for default.</summary>
    internal FieldAlignment? FieldAlignment { get; private set; }

    /// <summary>Gets the configured header name, or null to skip header validation for this field.</summary>
    internal string? HeaderName { get; private set; }

    /// <summary>
    /// Sets the 0-based start position of the field.
    /// </summary>
    /// <param name="start">The 0-based character index where the field begins.</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthFieldBuilder Start(int start)
    {
        StartPosition = start;
        return this;
    }

    /// <summary>
    /// Sets the field length in characters.
    /// </summary>
    /// <param name="length">The number of characters in the field.</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthFieldBuilder Length(int length)
    {
        FieldLength = length;
        return this;
    }

    /// <summary>
    /// Sets the exclusive end position. Length is computed as <c>End - Start</c>.
    /// When both <see cref="Length"/> and <see cref="End"/> are set, <see cref="End"/> takes precedence.
    /// </summary>
    /// <param name="end">The exclusive end character index.</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthFieldBuilder End(int end)
    {
        EndPosition = end;
        return this;
    }

    /// <summary>
    /// Sets the padding character for this field. Overrides the reader's default padding character.
    /// </summary>
    /// <param name="padChar">The character used to pad this field.</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthFieldBuilder PadChar(char padChar)
    {
        FieldPadChar = padChar;
        return this;
    }

    /// <summary>
    /// Sets the alignment for this field, determining which side to trim padding from.
    /// </summary>
    /// <param name="alignment">The alignment to use when trimming this field.</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthFieldBuilder Alignment(FieldAlignment alignment)
    {
        FieldAlignment = alignment;
        return this;
    }

    /// <summary>
    /// Sets the expected header name for this field.
    /// When a header row is present (configured via <c>WithHeader()</c>), the header row is
    /// parsed as a fixed-width record and the header value at this field's position is
    /// compared against this name. Case sensitivity is controlled by <c>CaseSensitiveHeaders()</c>
    /// on the builder.
    /// </summary>
    /// <param name="name">The expected header name at this field's position.</param>
    /// <returns>This builder for method chaining.</returns>
    public FixedWidthFieldBuilder WithHeaderName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        HeaderName = name;
        return this;
    }

    /// <summary>
    /// Resolves the effective field length, applying end-based computation when <see cref="End"/> is set.
    /// </summary>
    internal int? ResolvedFieldLength
    {
        get
        {
            if (EndPosition.HasValue && StartPosition.HasValue)
                return EndPosition.Value - StartPosition.Value;
            return FieldLength;
        }
    }
}
