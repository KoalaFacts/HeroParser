using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace HeroParser.FixedWidths.Writing;

/// <summary>
/// High-performance, low-allocation fixed-width writer that writes to a <see cref="TextWriter"/>.
/// </summary>
/// <remarks>
/// Uses per-instance pools for buffer management to minimize allocations and isolate pooled buffers.
/// Call <see cref="Dispose"/> or use a <c>using</c> statement to return pooled buffers.
/// </remarks>
public sealed class FixedWidthStreamWriter : IDisposable, IAsyncDisposable
{
    private readonly ArrayPool<char> charPool;
    private readonly TextWriter writer;
    private readonly FixedWidthWriterOptions options;
    private readonly bool leaveOpen;

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
    private char[] buffer;
    private int bufferPosition;
    private bool disposed;
    private long totalCharsWritten;

    private const int DEFAULT_BUFFER_SIZE = 16 * 1024; // 16KB
    private const int MAX_STACK_ALLOC_SIZE = 256;

    /// <summary>
    /// Creates a new fixed-width writer that writes to the specified <see cref="TextWriter"/>.
    /// </summary>
    /// <param name="writer">The underlying writer to write fixed-width output to.</param>
    /// <param name="options">Writer options; defaults to <see cref="FixedWidthWriterOptions.Default"/>.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, the <paramref name="writer"/> is not disposed.</param>
    public FixedWidthStreamWriter(TextWriter writer, FixedWidthWriterOptions? options = null, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(writer);

        this.writer = writer;
        this.options = options ?? FixedWidthWriterOptions.Default;
        this.options.Validate();
        this.leaveOpen = leaveOpen;

        // Cache frequently accessed options
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

        charPool = ArrayPool<char>.Create();
        buffer = RentBuffer(DEFAULT_BUFFER_SIZE);
        bufferPosition = 0;
        totalCharsWritten = 0;
    }

    /// <summary>
    /// Writes a single field value with the specified width.
    /// </summary>
    /// <param name="value">The field value to write.</param>
    /// <param name="width">The fixed width of the field.</param>
    /// <param name="alignment">Optional alignment override.</param>
    /// <param name="padChar">Optional padding character override.</param>
    public void WriteField(string? value, int width, FieldAlignment? alignment = null, char? padChar = null)
    {
        WriteField(value.AsSpan(), width, alignment, padChar);
    }

    /// <summary>
    /// Writes a single field value from a span with the specified width.
    /// </summary>
    /// <param name="value">The field value to write.</param>
    /// <param name="width">The fixed width of the field.</param>
    /// <param name="alignment">Optional alignment override.</param>
    /// <param name="padChar">Optional padding character override.</param>
    public void WriteField(ReadOnlySpan<char> value, int width, FieldAlignment? alignment = null, char? padChar = null)
    {
        ThrowIfDisposed();

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive");
        }

        var align = alignment ?? defaultAlignment;
        var pad = padChar ?? defaultPadChar;

        WriteFieldValue(value, width, align, pad);
    }

    /// <summary>
    /// Writes a formatted value with the specified width.
    /// </summary>
    /// <param name="value">The value to write.</param>
    /// <param name="width">The fixed width of the field.</param>
    /// <param name="alignment">Optional alignment override.</param>
    /// <param name="padChar">Optional padding character override.</param>
    /// <param name="format">Optional format string.</param>
    public void WriteField(object? value, int width, FieldAlignment? alignment = null, char? padChar = null, string? format = null)
    {
        ThrowIfDisposed();

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive");
        }

        var align = alignment ?? defaultAlignment;
        var pad = padChar ?? defaultPadChar;

        WriteFormattedValue(value, width, align, pad, format);
    }

    /// <summary>
    /// Ends the current row by writing a newline.
    /// </summary>
    public void EndRow()
    {
        ThrowIfDisposed();
        WriteNewLine();
    }

    /// <summary>
    /// Flushes the internal buffer to the underlying writer.
    /// </summary>
    public void Flush()
    {
        ThrowIfDisposed();
        FlushBuffer();
        writer.Flush();
    }

    /// <summary>
    /// Asynchronously flushes the internal buffer to the underlying writer.
    /// </summary>
    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await FlushBufferAsync(cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    #region Internal Writing Methods

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteNewLine()
    {
        var newLineSpan = newLineMemory.Span;
        EnsureCapacity(newLineSpan.Length);
        newLineSpan.CopyTo(buffer.AsSpan(bufferPosition));
        bufferPosition += newLineSpan.Length;
    }

    private void WriteFieldValue(ReadOnlySpan<char> value, int width, FieldAlignment alignment, char padChar)
    {
        EnsureCapacity(width);

        if (value.Length >= width)
        {
            // Value is exactly the right size or needs truncation
            if (value.Length > width && overflowBehavior == OverflowBehavior.Throw)
            {
                throw new FixedWidthException(
                    FixedWidthErrorCode.FieldOverflow,
                    $"Field value length {value.Length} exceeds width {width}");
            }

            // Truncate based on alignment
            if (alignment == FieldAlignment.Right)
            {
                // Right-aligned: take rightmost characters
                value[(value.Length - width)..].CopyTo(buffer.AsSpan(bufferPosition));
            }
            else
            {
                // Left-aligned: take leftmost characters
                value[..width].CopyTo(buffer.AsSpan(bufferPosition));
            }
            bufferPosition += width;
        }
        else
        {
            // Value needs padding
            int paddingNeeded = width - value.Length;

            switch (alignment)
            {
                case FieldAlignment.Right:
                    // Pad on the left
                    buffer.AsSpan(bufferPosition, paddingNeeded).Fill(padChar);
                    bufferPosition += paddingNeeded;
                    value.CopyTo(buffer.AsSpan(bufferPosition));
                    bufferPosition += value.Length;
                    break;

                case FieldAlignment.Center:
                    // Pad on both sides
                    int leftPad = paddingNeeded / 2;
                    int rightPad = paddingNeeded - leftPad;
                    buffer.AsSpan(bufferPosition, leftPad).Fill(padChar);
                    bufferPosition += leftPad;
                    value.CopyTo(buffer.AsSpan(bufferPosition));
                    bufferPosition += value.Length;
                    buffer.AsSpan(bufferPosition, rightPad).Fill(padChar);
                    bufferPosition += rightPad;
                    break;

                case FieldAlignment.Left:
                case FieldAlignment.None:
                default:
                    // Pad on the right
                    value.CopyTo(buffer.AsSpan(bufferPosition));
                    bufferPosition += value.Length;
                    buffer.AsSpan(bufferPosition, paddingNeeded).Fill(padChar);
                    bufferPosition += paddingNeeded;
                    break;
            }
        }
    }

    private void WriteFormattedValue(object? value, int width, FieldAlignment alignment, char padChar, string? format)
    {
        if (value is null)
        {
            WriteFieldValue(nullValue.AsSpan(), width, alignment, padChar);
            return;
        }

        // Direct type handling for common types
        switch (value)
        {
            case string s:
                WriteFieldValue(s.AsSpan(), width, alignment, padChar);
                return;

            case int i:
                WriteSpanFormattableDirectly(i, width, alignment, padChar, format ?? numberFormat);
                return;

            case long l:
                WriteSpanFormattableDirectly(l, width, alignment, padChar, format ?? numberFormat);
                return;

            case double d:
                WriteSpanFormattableDirectly(d, width, alignment, padChar, format ?? numberFormat);
                return;

            case decimal dec:
                WriteSpanFormattableDirectly(dec, width, alignment, padChar, format ?? numberFormat);
                return;

            case bool b:
                WriteFieldValue(b ? "True".AsSpan() : "False".AsSpan(), width, alignment, padChar);
                return;

            case DateTime dt:
                WriteSpanFormattableDirectly(dt, width, alignment, padChar, format ?? dateTimeFormat);
                return;

            case DateTimeOffset dto:
                WriteSpanFormattableDirectly(dto, width, alignment, padChar, format ?? dateTimeFormat);
                return;

#if NET6_0_OR_GREATER
            case DateOnly dateOnly:
                WriteSpanFormattableDirectly(dateOnly, width, alignment, padChar, format ?? dateOnlyFormat);
                return;

            case TimeOnly timeOnly:
                WriteSpanFormattableDirectly(timeOnly, width, alignment, padChar, format ?? timeOnlyFormat);
                return;
#endif

            case float f:
                WriteSpanFormattableDirectly(f, width, alignment, padChar, format ?? numberFormat);
                return;

            case byte by:
                WriteSpanFormattableDirectly(by, width, alignment, padChar, format ?? numberFormat);
                return;

            case short sh:
                WriteSpanFormattableDirectly(sh, width, alignment, padChar, format ?? numberFormat);
                return;

            case uint ui:
                WriteSpanFormattableDirectly(ui, width, alignment, padChar, format ?? numberFormat);
                return;

            case ulong ul:
                WriteSpanFormattableDirectly(ul, width, alignment, padChar, format ?? numberFormat);
                return;

            case Guid g:
                WriteSpanFormattableDirectly(g, width, alignment, padChar, format);
                return;

            default:
                break;
        }

        // Fallback for other ISpanFormattable types
        if (value is ISpanFormattable spanFormattable)
        {
            WriteSpanFormattable(spanFormattable, width, alignment, padChar, format);
            return;
        }

        // Final fallback for IFormattable
        if (value is IFormattable formattable)
        {
            WriteFieldValue(formattable.ToString(format, culture).AsSpan(), width, alignment, padChar);
            return;
        }

        // Last resort: ToString()
        WriteFieldValue((value.ToString() ?? string.Empty).AsSpan(), width, alignment, padChar);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteSpanFormattableDirectly<T>(T value, int width, FieldAlignment alignment, char padChar, string? format)
        where T : ISpanFormattable
    {
        Span<char> stackBuffer = stackalloc char[MAX_STACK_ALLOC_SIZE];

        if (value.TryFormat(stackBuffer, out int charsWritten, format, culture))
        {
            WriteFieldValue(stackBuffer[..charsWritten], width, alignment, padChar);
        }
        else
        {
            WriteFieldValue(value.ToString(format, culture).AsSpan(), width, alignment, padChar);
        }
    }

    private void WriteSpanFormattable(ISpanFormattable value, int width, FieldAlignment alignment, char padChar, string? format)
    {
        Span<char> stackBuffer = stackalloc char[MAX_STACK_ALLOC_SIZE];

        if (value.TryFormat(stackBuffer, out int charsWritten, format, culture))
        {
            WriteFieldValue(stackBuffer[..charsWritten], width, alignment, padChar);
        }
        else
        {
            WriteFieldValue(value.ToString(format, culture).AsSpan(), width, alignment, padChar);
        }
    }

    #endregion

    #region Buffer Management

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureCapacity(int required)
    {
        if (bufferPosition + required > buffer.Length)
        {
            FlushBuffer();

            if (required > buffer.Length)
            {
                GrowBuffer(required);
            }
        }
    }

    private void FlushBuffer()
    {
        if (bufferPosition > 0)
        {
            totalCharsWritten += bufferPosition;
            if (maxOutputSize.HasValue && totalCharsWritten > maxOutputSize.Value)
            {
                throw new FixedWidthException(
                    FixedWidthErrorCode.OutputSizeExceeded,
                    $"Output size {totalCharsWritten} exceeds maximum of {maxOutputSize.Value}");
            }

            writer.Write(buffer.AsSpan(0, bufferPosition));
            bufferPosition = 0;
        }
    }

    private async ValueTask FlushBufferAsync(CancellationToken cancellationToken)
    {
        if (bufferPosition > 0)
        {
            totalCharsWritten += bufferPosition;
            if (maxOutputSize.HasValue && totalCharsWritten > maxOutputSize.Value)
            {
                throw new FixedWidthException(
                    FixedWidthErrorCode.OutputSizeExceeded,
                    $"Output size {totalCharsWritten} exceeds maximum of {maxOutputSize.Value}");
            }

#if NET6_0_OR_GREATER
            await writer.WriteAsync(buffer.AsMemory(0, bufferPosition), cancellationToken).ConfigureAwait(false);
#else
            await writer.WriteAsync(buffer, 0, bufferPosition).ConfigureAwait(false);
#endif
            bufferPosition = 0;
        }
    }

    private void GrowBuffer(int minimumRequired)
    {
        int newSize = Math.Max(buffer.Length * 2, minimumRequired);
        var oldBuffer = buffer;
        buffer = RentBuffer(newSize);
        ReturnBuffer(oldBuffer);
    }

    #endregion

    #region Disposal

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(FixedWidthStreamWriter));
        }
    }

    /// <summary>
    /// Flushes the buffer and releases resources.
    /// </summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        try
        {
            FlushBuffer();
        }
        finally
        {
            ReturnBuffer(buffer);
            buffer = null!;

            if (!leaveOpen)
            {
                writer.Dispose();
            }
        }
    }

    /// <summary>
    /// Asynchronously flushes the buffer and releases resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;

        try
        {
            await FlushBufferAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            ReturnBuffer(buffer);
            buffer = null!;

            if (!leaveOpen)
            {
                if (writer is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    writer.Dispose();
                }
            }
        }
    }

    #endregion

    #region Pool Helpers

    private char[] RentBuffer(int minimumLength)
    {
        var rented = charPool.Rent(minimumLength);
        Array.Clear(rented);
        return rented;
    }

    private void ReturnBuffer(char[] toReturn)
    {
        charPool.Return(toReturn, clearArray: true);
    }

    #endregion
}
