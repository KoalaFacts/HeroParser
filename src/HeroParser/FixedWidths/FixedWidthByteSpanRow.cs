using System.Runtime.CompilerServices;
using System.Text;

namespace HeroParser.FixedWidths;

/// <summary>
/// Represents a fixed-width record backed by the original UTF-8 byte span.
/// </summary>
/// <remarks>
/// Unlike CSV rows where columns are determined by delimiters, fixed-width rows
/// require you to specify field positions using <see cref="GetField(int, int)"/>.
/// </remarks>
public readonly ref struct FixedWidthByteSpanRow
{
    private readonly ReadOnlySpan<byte> line;
    private readonly int recordNumber;
    private readonly int sourceLineNumber;
    private readonly FixedWidthParserOptions options;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal FixedWidthByteSpanRow(
        ReadOnlySpan<byte> line,
        int recordNumber,
        int sourceLineNumber,
        FixedWidthParserOptions options)
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
    /// Only populated when <see cref="FixedWidthParserOptions.TrackSourceLineNumbers"/> is <see langword="true"/>.
    /// </summary>
    public int SourceLineNumber => sourceLineNumber;

    /// <summary>Gets the length of the record in bytes.</summary>
    public int Length => line.Length;

    /// <summary>Gets the raw record byte span.</summary>
    public ReadOnlySpan<byte> RawRecord => line;

    /// <summary>
    /// Gets a field at the specified byte position using default padding options.
    /// </summary>
    /// <param name="start">Zero-based starting position of the field in bytes.</param>
    /// <param name="length">Length of the field in bytes.</param>
    /// <returns>A <see cref="FixedWidthByteSpanColumn"/> representing the field.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="start"/> or <paramref name="length"/> is negative.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FixedWidthByteSpanColumn GetField(int start, int length)
        => GetField(start, length, (byte)options.DefaultPadChar, options.DefaultAlignment);

    /// <summary>
    /// Gets a field at the specified byte position with custom padding options.
    /// </summary>
    /// <param name="start">Zero-based starting position of the field in bytes.</param>
    /// <param name="length">Length of the field in bytes.</param>
    /// <param name="padByte">The padding byte to trim (typically ASCII space 0x20).</param>
    /// <param name="alignment">The alignment determining which side to trim.</param>
    /// <returns>A <see cref="FixedWidthByteSpanColumn"/> representing the trimmed field.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="start"/> or <paramref name="length"/> is negative.</exception>
    public FixedWidthByteSpanColumn GetField(int start, int length, byte padByte, FieldAlignment alignment)
    {
        if (start < 0)
            throw new ArgumentOutOfRangeException(nameof(start), start, "Start position cannot be negative.");

        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length), length, "Field length cannot be negative.");

        // Handle case where start is beyond the record
        if (start >= line.Length)
            return new FixedWidthByteSpanColumn([]);

        // Calculate actual length available
        var actualLength = Math.Min(length, line.Length - start);
        var span = line.Slice(start, actualLength);

        // Apply trimming based on alignment
        span = alignment switch
        {
            FieldAlignment.Left => TrimEnd(span, padByte),
            FieldAlignment.Right => TrimStart(span, padByte),
            FieldAlignment.Center => Trim(span, padByte),
            FieldAlignment.None => span,
            _ => span
        };

        return new FixedWidthByteSpanColumn(span);
    }

    /// <summary>
    /// Gets the raw field at the specified byte position without any trimming.
    /// </summary>
    /// <param name="start">Zero-based starting position of the field in bytes.</param>
    /// <param name="length">Length of the field in bytes.</param>
    /// <returns>A <see cref="FixedWidthByteSpanColumn"/> representing the raw field.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FixedWidthByteSpanColumn GetRawField(int start, int length)
        => GetField(start, length, 0, FieldAlignment.None);

    /// <summary>
    /// Converts the entire record to a UTF-8 decoded string.
    /// </summary>
    public string ToDecodedString() => Encoding.UTF8.GetString(line);

    /// <summary>
    /// Creates an immutable copy of this row that can escape the stack.
    /// </summary>
    /// <returns>An <see cref="ImmutableFixedWidthByteRow"/> that owns its data.</returns>
    public ImmutableFixedWidthByteRow ToImmutable()
        => new(line.ToArray(), recordNumber, sourceLineNumber, options);

    /// <summary>
    /// Creates an owned copy of the row data, solving the buffer ownership issue.
    /// </summary>
    public FixedWidthByteSpanRow Clone()
    {
        var newLine = line.ToArray();
        return new FixedWidthByteSpanRow(newLine, recordNumber, sourceLineNumber, options);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlySpan<byte> TrimEnd(ReadOnlySpan<byte> span, byte padByte)
    {
        int end = span.Length;
        while (end > 0 && span[end - 1] == padByte)
            end--;
        return span[..end];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlySpan<byte> TrimStart(ReadOnlySpan<byte> span, byte padByte)
    {
        int start = 0;
        while (start < span.Length && span[start] == padByte)
            start++;
        return span[start..];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlySpan<byte> Trim(ReadOnlySpan<byte> span, byte padByte)
        => TrimEnd(TrimStart(span, padByte), padByte);
}

/// <summary>
/// An immutable copy of a fixed-width byte row that can be stored on the heap.
/// </summary>
public sealed class ImmutableFixedWidthByteRow
{
    private readonly byte[] data;
    private readonly FixedWidthParserOptions options;

    internal ImmutableFixedWidthByteRow(byte[] data, int recordNumber, int sourceLineNumber, FixedWidthParserOptions options)
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

    /// <summary>Gets the length of the record in bytes.</summary>
    public int Length => data.Length;

    /// <summary>Gets the raw record as a byte span.</summary>
    public ReadOnlySpan<byte> RawRecord => data.AsSpan();

    /// <summary>
    /// Gets a field at the specified byte position.
    /// </summary>
    public string GetField(int start, int length)
        => GetField(start, length, (byte)options.DefaultPadChar, options.DefaultAlignment);

    /// <summary>
    /// Gets a field at the specified byte position with custom padding options.
    /// </summary>
    public string GetField(int start, int length, byte padByte, FieldAlignment alignment)
    {
        if (start < 0 || start >= data.Length)
            return string.Empty;

        var actualLength = Math.Min(length, data.Length - start);
        ReadOnlySpan<byte> span = data.AsSpan(start, actualLength);

        span = alignment switch
        {
            FieldAlignment.Left => TrimEnd(span, padByte),
            FieldAlignment.Right => TrimStart(span, padByte),
            FieldAlignment.Center => Trim(span, padByte),
            _ => span
        };

        return Encoding.UTF8.GetString(span);
    }

    /// <summary>
    /// Converts the entire record to a UTF-8 decoded string.
    /// </summary>
    public string ToDecodedString() => Encoding.UTF8.GetString(data);

    private static ReadOnlySpan<byte> TrimEnd(ReadOnlySpan<byte> span, byte padByte)
    {
        int end = span.Length;
        while (end > 0 && span[end - 1] == padByte)
            end--;
        return span[..end];
    }

    private static ReadOnlySpan<byte> TrimStart(ReadOnlySpan<byte> span, byte padByte)
    {
        int start = 0;
        while (start < span.Length && span[start] == padByte)
            start++;
        return span[start..];
    }

    private static ReadOnlySpan<byte> Trim(ReadOnlySpan<byte> span, byte padByte)
        => TrimEnd(TrimStart(span, padByte), padByte);
}
