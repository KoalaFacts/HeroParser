using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using HeroParser.FixedWidths.Writing;

namespace HeroParser.FixedWidths.Streaming;

/// <summary>
/// High-performance async fixed-width writer that writes to a <see cref="Stream"/> without blocking.
/// </summary>
/// <remarks>
/// <para>
/// Uses <see cref="ArrayPool{T}"/> for buffer management to minimize allocations.
/// Supports all <see cref="FixedWidthWriterOptions"/> including padding, alignment,
/// and overflow behavior.
/// </para>
/// <para>
/// Thread-Safety: This class is not thread-safe. Do not call methods concurrently.
/// </para>
/// </remarks>
public sealed class FixedWidthAsyncStreamWriter : IAsyncDisposable
{
    private readonly Stream stream;
    private readonly FixedWidthWriterOptions options;
    private readonly bool leaveOpen;
    private readonly Encoding encoding;

    // Cached options for hot path access
    private readonly char defaultPadChar;
    private readonly FieldAlignment defaultAlignment;
    private readonly ReadOnlyMemory<char> newLineMemory;
    private readonly CultureInfo culture;
    private readonly string nullValue;
    private readonly string? dateTimeFormat;
    private readonly string? numberFormat;
    private readonly OverflowBehavior overflowBehavior;
#if NET6_0_OR_GREATER
    private readonly string? dateOnlyFormat;
    private readonly string? timeOnlyFormat;
#endif

    // DoS protection
    private readonly long? maxOutputSize;

    // State tracking
    private char[] charBuffer;
    private int charBufferPosition;
    private byte[] byteBuffer;
    private bool disposed;
    private long totalCharsWritten;

    private const int DEFAULT_CHAR_BUFFER_SIZE = 16 * 1024; // 16KB chars
    private const int DEFAULT_BYTE_BUFFER_SIZE = 16 * 1024; // 16KB bytes
    private const int MAX_STACK_ALLOC_SIZE = 256;

    /// <summary>
    /// Creates a new async fixed-width writer that writes to the specified stream.
    /// </summary>
    /// <param name="stream">The underlying stream to write fixed-width output to.</param>
    /// <param name="options">Writer options; defaults to <see cref="FixedWidthWriterOptions.Default"/>.</param>
    /// <param name="encoding">Text encoding; defaults to UTF-8.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, the <paramref name="stream"/> is not disposed.</param>
    public FixedWidthAsyncStreamWriter(Stream stream, FixedWidthWriterOptions? options = null, Encoding? encoding = null, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);

        this.stream = stream;
        this.options = options ?? FixedWidthWriterOptions.Default;
        this.options.Validate();
        this.encoding = encoding ?? Encoding.UTF8;
        this.leaveOpen = leaveOpen;

        // Cache frequently accessed options to avoid property access overhead in hot paths
        defaultPadChar = this.options.DefaultPadChar;
        defaultAlignment = this.options.DefaultAlignment;
        newLineMemory = this.options.NewLine.AsMemory();
        culture = this.options.Culture;
        nullValue = this.options.NullValue;
        dateTimeFormat = this.options.DateTimeFormat;
        numberFormat = this.options.NumberFormat;
        overflowBehavior = this.options.OverflowBehavior;
#if NET6_0_OR_GREATER
        dateOnlyFormat = this.options.DateOnlyFormat;
        timeOnlyFormat = this.options.TimeOnlyFormat;
#endif

        // DoS protection
        maxOutputSize = this.options.MaxOutputSize;

        charBuffer = ArrayPool<char>.Shared.Rent(DEFAULT_CHAR_BUFFER_SIZE);
        byteBuffer = ArrayPool<byte>.Shared.Rent(DEFAULT_BYTE_BUFFER_SIZE);
        charBufferPosition = 0;
        totalCharsWritten = 0;
    }

    /// <summary>
    /// Asynchronously writes a single field value with the specified width.
    /// </summary>
    /// <param name="value">The field value to write.</param>
    /// <param name="width">The fixed width of the field.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    public async ValueTask WriteFieldAsync(string? value, int width, CancellationToken cancellationToken = default)
    {
        await WriteFieldAsync(value.AsMemory(), width, defaultAlignment, defaultPadChar, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously writes a single field value with the specified width and alignment.
    /// </summary>
    /// <param name="value">The field value to write.</param>
    /// <param name="width">The fixed width of the field.</param>
    /// <param name="alignment">Field alignment.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    public async ValueTask WriteFieldAsync(string? value, int width, FieldAlignment alignment, CancellationToken cancellationToken = default)
    {
        await WriteFieldAsync(value.AsMemory(), width, alignment, defaultPadChar, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously writes a single field value with the specified width, alignment, and padding character.
    /// </summary>
    /// <param name="value">The field value to write.</param>
    /// <param name="width">The fixed width of the field.</param>
    /// <param name="alignment">Field alignment.</param>
    /// <param name="padChar">Padding character.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    public async ValueTask WriteFieldAsync(ReadOnlyMemory<char> value, int width, FieldAlignment alignment, char padChar, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive");
        }

        await WriteFieldValueAsync(value, width, alignment, padChar, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously writes a formatted value with the specified width.
    /// </summary>
    /// <param name="value">The value to write.</param>
    /// <param name="width">The fixed width of the field.</param>
    /// <param name="alignment">Optional alignment override.</param>
    /// <param name="padChar">Optional padding character override.</param>
    /// <param name="format">Optional format string.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    public async ValueTask WriteFieldAsync(object? value, int width, FieldAlignment? alignment = null, char? padChar = null, string? format = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive");
        }

        var align = alignment ?? defaultAlignment;
        var pad = padChar ?? defaultPadChar;

        await WriteFormattedValueAsync(value, width, align, pad, format, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously ends the current row by writing a newline.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    public async ValueTask EndRowAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        await WriteNewLineAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously flushes the internal buffer to the underlying stream.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        await FlushCharBufferAsync(cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    #region Internal Writing Methods

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ValueTask WriteNewLineAsync(CancellationToken cancellationToken)
    {
        var newLineLen = newLineMemory.Length;
        // Sync fast path - most common case
        if (charBufferPosition + newLineLen <= charBuffer.Length)
        {
            newLineMemory.Span.CopyTo(charBuffer.AsSpan(charBufferPosition));
            charBufferPosition += newLineLen;
            return default;
        }
        return WriteNewLineSlowAsync(cancellationToken);
    }

#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    private async ValueTask WriteNewLineSlowAsync(CancellationToken cancellationToken)
    {
        var newLineLen = newLineMemory.Length;
        await FlushCharBufferAsync(cancellationToken).ConfigureAwait(false);
        if (newLineLen > charBuffer.Length)
        {
            GrowCharBuffer(newLineLen);
        }
        newLineMemory.Span.CopyTo(charBuffer.AsSpan(charBufferPosition));
        charBufferPosition += newLineLen;
    }

#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    private async ValueTask WriteFieldValueAsync(ReadOnlyMemory<char> value, int width, FieldAlignment alignment, char padChar, CancellationToken cancellationToken)
    {
        await EnsureCapacityAsync(width, cancellationToken).ConfigureAwait(false);

        var valueSpan = value.Span;
        var bufferSpan = charBuffer.AsSpan(charBufferPosition);

        if (valueSpan.Length >= width)
        {
            // Value is exactly the right size or needs truncation
            if (valueSpan.Length > width && overflowBehavior == OverflowBehavior.Throw)
            {
                throw new FixedWidthException(
                    FixedWidthErrorCode.FieldOverflow,
                    $"Field value length {valueSpan.Length} exceeds width {width}");
            }

            // Truncate based on alignment
            if (alignment == FieldAlignment.Right)
            {
                // Right-aligned: take rightmost characters
                valueSpan[(valueSpan.Length - width)..].CopyTo(bufferSpan);
            }
            else
            {
                // Left-aligned: take leftmost characters
                valueSpan[..width].CopyTo(bufferSpan);
            }
            charBufferPosition += width;
        }
        else
        {
            // Value needs padding
            int paddingNeeded = width - valueSpan.Length;

            switch (alignment)
            {
                case FieldAlignment.Right:
                    // Pad on the left
                    bufferSpan[..paddingNeeded].Fill(padChar);
                    charBufferPosition += paddingNeeded;
                    valueSpan.CopyTo(charBuffer.AsSpan(charBufferPosition));
                    charBufferPosition += valueSpan.Length;
                    break;

                case FieldAlignment.Center:
                    // Pad on both sides
                    int leftPad = paddingNeeded / 2;
                    int rightPad = paddingNeeded - leftPad;
                    bufferSpan[..leftPad].Fill(padChar);
                    charBufferPosition += leftPad;
                    valueSpan.CopyTo(charBuffer.AsSpan(charBufferPosition));
                    charBufferPosition += valueSpan.Length;
                    charBuffer.AsSpan(charBufferPosition, rightPad).Fill(padChar);
                    charBufferPosition += rightPad;
                    break;

                case FieldAlignment.Left:
                case FieldAlignment.None:
                default:
                    // Pad on the right
                    valueSpan.CopyTo(bufferSpan);
                    charBufferPosition += valueSpan.Length;
                    charBuffer.AsSpan(charBufferPosition, paddingNeeded).Fill(padChar);
                    charBufferPosition += paddingNeeded;
                    break;
            }
        }
    }

#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    private async ValueTask WriteFormattedValueAsync(object? value, int width, FieldAlignment alignment, char padChar, string? format, CancellationToken cancellationToken)
    {
        if (value is null)
        {
            await WriteFieldValueAsync(nullValue.AsMemory(), width, alignment, padChar, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Direct type handling for common types
        switch (value)
        {
            case string s:
                await WriteFieldValueAsync(s.AsMemory(), width, alignment, padChar, cancellationToken).ConfigureAwait(false);
                return;

            case int i:
                await WriteSpanFormattableAsync(i, width, alignment, padChar, format ?? numberFormat, cancellationToken).ConfigureAwait(false);
                return;

            case long l:
                await WriteSpanFormattableAsync(l, width, alignment, padChar, format ?? numberFormat, cancellationToken).ConfigureAwait(false);
                return;

            case double d:
                await WriteSpanFormattableAsync(d, width, alignment, padChar, format ?? numberFormat, cancellationToken).ConfigureAwait(false);
                return;

            case decimal dec:
                await WriteSpanFormattableAsync(dec, width, alignment, padChar, format ?? numberFormat, cancellationToken).ConfigureAwait(false);
                return;

            case bool b:
                await WriteFieldValueAsync((b ? "True" : "False").AsMemory(), width, alignment, padChar, cancellationToken).ConfigureAwait(false);
                return;

            case DateTime dt:
                await WriteSpanFormattableAsync(dt, width, alignment, padChar, format ?? dateTimeFormat, cancellationToken).ConfigureAwait(false);
                return;

            case DateTimeOffset dto:
                await WriteSpanFormattableAsync(dto, width, alignment, padChar, format ?? dateTimeFormat, cancellationToken).ConfigureAwait(false);
                return;

#if NET6_0_OR_GREATER
            case DateOnly dateOnly:
                await WriteSpanFormattableAsync(dateOnly, width, alignment, padChar, format ?? dateOnlyFormat, cancellationToken).ConfigureAwait(false);
                return;

            case TimeOnly timeOnly:
                await WriteSpanFormattableAsync(timeOnly, width, alignment, padChar, format ?? timeOnlyFormat, cancellationToken).ConfigureAwait(false);
                return;
#endif

            case float f:
                await WriteSpanFormattableAsync(f, width, alignment, padChar, format ?? numberFormat, cancellationToken).ConfigureAwait(false);
                return;

            case byte by:
                await WriteSpanFormattableAsync(by, width, alignment, padChar, format ?? numberFormat, cancellationToken).ConfigureAwait(false);
                return;

            case short sh:
                await WriteSpanFormattableAsync(sh, width, alignment, padChar, format ?? numberFormat, cancellationToken).ConfigureAwait(false);
                return;

            case uint ui:
                await WriteSpanFormattableAsync(ui, width, alignment, padChar, format ?? numberFormat, cancellationToken).ConfigureAwait(false);
                return;

            case ulong ul:
                await WriteSpanFormattableAsync(ul, width, alignment, padChar, format ?? numberFormat, cancellationToken).ConfigureAwait(false);
                return;

            case Guid g:
                await WriteSpanFormattableAsync(g, width, alignment, padChar, format, cancellationToken).ConfigureAwait(false);
                return;

            default:
                break;
        }

        // Fallback for other ISpanFormattable types
        if (value is ISpanFormattable spanFormattable)
        {
            await WriteSpanFormattableInterfaceAsync(spanFormattable, width, alignment, padChar, format, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Final fallback for IFormattable
        if (value is IFormattable formattable)
        {
            await WriteFieldValueAsync(formattable.ToString(format, culture).AsMemory(), width, alignment, padChar, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Last resort: ToString()
        await WriteFieldValueAsync((value.ToString() ?? string.Empty).AsMemory(), width, alignment, padChar, cancellationToken).ConfigureAwait(false);
    }

#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    private async ValueTask WriteSpanFormattableAsync<T>(T value, int width, FieldAlignment alignment, char padChar, string? format, CancellationToken cancellationToken)
        where T : ISpanFormattable
    {
        Span<char> stackBuffer = stackalloc char[MAX_STACK_ALLOC_SIZE];

        if (value.TryFormat(stackBuffer, out int charsWritten, format, culture))
        {
            await WriteFieldValueAsync(stackBuffer[..charsWritten].ToArray().AsMemory(), width, alignment, padChar, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await WriteFieldValueAsync(value.ToString(format, culture).AsMemory(), width, alignment, padChar, cancellationToken).ConfigureAwait(false);
        }
    }

#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    private async ValueTask WriteSpanFormattableInterfaceAsync(ISpanFormattable value, int width, FieldAlignment alignment, char padChar, string? format, CancellationToken cancellationToken)
    {
        Span<char> stackBuffer = stackalloc char[MAX_STACK_ALLOC_SIZE];

        if (value.TryFormat(stackBuffer, out int charsWritten, format, culture))
        {
            await WriteFieldValueAsync(stackBuffer[..charsWritten].ToArray().AsMemory(), width, alignment, padChar, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await WriteFieldValueAsync(value.ToString(format, culture).AsMemory(), width, alignment, padChar, cancellationToken).ConfigureAwait(false);
        }
    }

    #endregion

    #region Buffer Management

#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async ValueTask EnsureCapacityAsync(int required, CancellationToken cancellationToken)
    {
        if (charBufferPosition + required > charBuffer.Length)
        {
            await FlushCharBufferAsync(cancellationToken).ConfigureAwait(false);

            // If still not enough capacity after flush, grow the buffer
            if (required > charBuffer.Length)
            {
                GrowCharBuffer(required);
            }
        }
    }

#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    private async ValueTask FlushCharBufferAsync(CancellationToken cancellationToken)
    {
        if (charBufferPosition > 0)
        {
            // Track total output size for DoS protection
            totalCharsWritten += charBufferPosition;
            if (maxOutputSize.HasValue && totalCharsWritten > maxOutputSize.Value)
            {
                throw new FixedWidthException(
                    FixedWidthErrorCode.OutputSizeExceeded,
                    $"Output size {totalCharsWritten} exceeds maximum of {maxOutputSize.Value}");
            }

            // Convert chars to bytes
            int byteCount = encoding.GetByteCount(charBuffer.AsSpan(0, charBufferPosition));

            // Ensure byte buffer is large enough
            if (byteCount > byteBuffer.Length)
            {
                var oldByteBuffer = byteBuffer;
                byteBuffer = ArrayPool<byte>.Shared.Rent(byteCount);
                ArrayPool<byte>.Shared.Return(oldByteBuffer);
            }

            int bytesWritten = encoding.GetBytes(charBuffer.AsSpan(0, charBufferPosition), byteBuffer);

            // Write bytes to stream
            await stream.WriteAsync(byteBuffer.AsMemory(0, bytesWritten), cancellationToken).ConfigureAwait(false);

            charBufferPosition = 0;
        }
    }

    private void GrowCharBuffer(int minimumRequired)
    {
        int newSize = Math.Max(charBuffer.Length * 2, minimumRequired);
        var oldBuffer = charBuffer;
        charBuffer = ArrayPool<char>.Shared.Rent(newSize);
        ArrayPool<char>.Shared.Return(oldBuffer);
    }

    #endregion

    #region Disposal

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(FixedWidthAsyncStreamWriter));
        }
    }

    /// <summary>
    /// Asynchronously flushes the buffer and releases resources.
    /// </summary>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous dispose operation.</returns>
#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;

        try
        {
            await FlushCharBufferAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(charBuffer);
            ArrayPool<byte>.Shared.Return(byteBuffer);
            charBuffer = null!;
            byteBuffer = null!;

            if (!leaveOpen)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    #endregion
}
