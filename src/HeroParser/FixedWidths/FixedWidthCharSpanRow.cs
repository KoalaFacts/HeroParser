using System.Runtime.CompilerServices;

namespace HeroParser.FixedWidths;

/// <summary>
/// Represents a fixed-width record backed by the original character span.
/// </summary>
/// <remarks>
/// Unlike CSV rows where columns are determined by delimiters, fixed-width rows
/// require you to specify field positions using <see cref="GetField(int, int)"/>.
/// </remarks>
public readonly ref struct FixedWidthCharSpanRow
{
    private readonly ReadOnlySpan<char> line;
    private readonly int recordNumber;
    private readonly int sourceLineNumber;
    private readonly FixedWidthReadOptions options;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal FixedWidthCharSpanRow(
        ReadOnlySpan<char> line,
        int recordNumber,
        int sourceLineNumber,
        FixedWidthReadOptions options)
    {
        this.line = line;
        this.recordNumber = recordNumber;
        this.sourceLineNumber = sourceLineNumber;
        this.options = options;
    }

    /// <summary>Gets the 1-based record number.</summary>
    public int RecordNumber => recordNumber;

    /// <summary>
    /// Gets the 1-based source line number where this record started.
    /// Only populated when <see cref="FixedWidthReadOptions.TrackSourceLineNumbers"/> is <see langword="true"/>.
    /// </summary>
    public int SourceLineNumber => sourceLineNumber;

    /// <summary>Gets the length of the record in characters.</summary>
    public int Length => line.Length;

    /// <summary>Gets the raw record span.</summary>
    public ReadOnlySpan<char> RawRecord => line;

    /// <summary>
    /// Gets a field at the specified position using default padding options.
    /// </summary>
    /// <param name="start">Zero-based starting position of the field.</param>
    /// <param name="length">Length of the field in characters.</param>
    /// <returns>A <see cref="FixedWidthCharSpanColumn"/> representing the field.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="start"/> or <paramref name="length"/> is negative.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FixedWidthCharSpanColumn GetField(int start, int length)
        => GetField(start, length, options.DefaultPadChar, options.DefaultAlignment);

    /// <summary>
    /// Gets a field at the specified position with custom padding options.
    /// </summary>
    /// <param name="start">Zero-based starting position of the field.</param>
    /// <param name="length">Length of the field in characters.</param>
    /// <param name="padChar">The padding character to trim.</param>
    /// <param name="alignment">The alignment determining which side to trim.</param>
    /// <returns>A <see cref="FixedWidthCharSpanColumn"/> representing the trimmed field.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="start"/> or <paramref name="length"/> is negative.</exception>
    public FixedWidthCharSpanColumn GetField(int start, int length, char padChar, FieldAlignment alignment)
    {
        if (start < 0)
            throw new ArgumentOutOfRangeException(nameof(start), start, "Start position cannot be negative.");

        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length), length, "Field length cannot be negative.");

        // Check if field extends beyond the row
        var fieldEnd = start + length;
        if (fieldEnd > line.Length)
        {
            if (!options.AllowShortRows)
            {
                throw new FixedWidthException(
                    FixedWidthErrorCode.FieldOutOfBounds,
                    $"Field at position {start} with length {length} extends beyond the record length ({line.Length}). " +
                    $"Enable AllowShortRows to handle short records gracefully.",
                    recordNumber,
                    sourceLineNumber);
            }

            // AllowShortRows is true - handle gracefully
            if (start >= line.Length)
                return new FixedWidthCharSpanColumn([]);
        }

        // Calculate actual length available
        var actualLength = Math.Min(length, line.Length - start);
        var span = line.Slice(start, actualLength);

        // Apply trimming based on alignment
        span = alignment switch
        {
            FieldAlignment.Left => span.TrimEnd(padChar),
            FieldAlignment.Right => span.TrimStart(padChar),
            FieldAlignment.None => span,
            _ => span
        };

        return new FixedWidthCharSpanColumn(span);
    }

    /// <summary>
    /// Gets the raw field at the specified position without any trimming.
    /// </summary>
    /// <param name="start">Zero-based starting position of the field.</param>
    /// <param name="length">Length of the field in characters.</param>
    /// <returns>A <see cref="FixedWidthCharSpanColumn"/> representing the raw field.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FixedWidthCharSpanColumn GetRawField(int start, int length)
        => GetField(start, length, '\0', FieldAlignment.None);

    /// <summary>
    /// Creates an immutable copy of this row that can escape the stack.
    /// </summary>
    /// <returns>An <see cref="ImmutableFixedWidthRow"/> that owns its data.</returns>
    public ImmutableFixedWidthRow ToImmutable()
        => new(line.ToArray(), recordNumber, sourceLineNumber, options);
}

/// <summary>
/// An immutable copy of a fixed-width row that can be stored on the heap.
/// </summary>
public sealed class ImmutableFixedWidthRow
{
    private readonly char[] data;
    private readonly FixedWidthReadOptions options;

    internal ImmutableFixedWidthRow(char[] data, int recordNumber, int sourceLineNumber, FixedWidthReadOptions options)
    {
        this.data = data;
        RecordNumber = recordNumber;
        SourceLineNumber = sourceLineNumber;
        this.options = options;
    }

    /// <summary>Gets the 1-based record number.</summary>
    public int RecordNumber { get; }

    /// <summary>Gets the 1-based source line number.</summary>
    public int SourceLineNumber { get; }

    /// <summary>Gets the length of the record.</summary>
    public int Length => data.Length;

    /// <summary>Gets the raw record as a span.</summary>
    public ReadOnlySpan<char> RawRecord => data.AsSpan();

    /// <summary>
    /// Gets a field at the specified position.
    /// </summary>
    public string GetField(int start, int length)
        => GetField(start, length, options.DefaultPadChar, options.DefaultAlignment);

    /// <summary>
    /// Gets a field at the specified position with custom padding options.
    /// </summary>
    public string GetField(int start, int length, char padChar, FieldAlignment alignment)
    {
        if (start < 0)
            throw new ArgumentOutOfRangeException(nameof(start), start, "Start position cannot be negative.");

        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length), length, "Field length cannot be negative.");

        // Check if field extends beyond the row
        var fieldEnd = start + length;
        if (fieldEnd > data.Length)
        {
            if (!options.AllowShortRows)
            {
                throw new FixedWidthException(
                    FixedWidthErrorCode.FieldOutOfBounds,
                    $"Field at position {start} with length {length} extends beyond the record length ({data.Length}). " +
                    $"Enable AllowShortRows to handle short records gracefully.",
                    RecordNumber,
                    SourceLineNumber);
            }

            // AllowShortRows is true - handle gracefully
            if (start >= data.Length)
                return string.Empty;
        }

        var actualLength = Math.Min(length, data.Length - start);
        var span = data.AsSpan(start, actualLength);

        span = alignment switch
        {
            FieldAlignment.Left => span.TrimEnd(padChar),
            FieldAlignment.Right => span.TrimStart(padChar),
            _ => span
        };

        return new string(span);
    }
}

