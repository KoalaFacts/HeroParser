using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;

namespace HeroParser.SeparatedValues.Writing;

/// <summary>
/// High-performance, low-allocation CSV writer that writes to a <see cref="TextWriter"/>.
/// </summary>
/// <remarks>
/// Uses <see cref="ArrayPool{T}"/> for buffer management to minimize allocations.
/// Call <see cref="Dispose"/> or use a <c>using</c> statement to return pooled buffers.
/// </remarks>
public sealed class CsvStreamWriter : IDisposable, IAsyncDisposable
{
    private readonly TextWriter writer;
    private readonly CsvWriterOptions options;
    private readonly bool leaveOpen;

    private char[] buffer;
    private int bufferPosition;
    private bool isFirstFieldInRow;
    private bool disposed;

    private const int DEFAULT_BUFFER_SIZE = 16 * 1024; // 16KB
    private const int MAX_STACK_ALLOC_SIZE = 256;

    /// <summary>
    /// Creates a new CSV writer that writes to the specified <see cref="TextWriter"/>.
    /// </summary>
    /// <param name="writer">The underlying writer to write CSV output to.</param>
    /// <param name="options">Writer options; defaults to <see cref="CsvWriterOptions.Default"/>.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, the <paramref name="writer"/> is not disposed.</param>
    public CsvStreamWriter(TextWriter writer, CsvWriterOptions? options = null, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(writer);

        this.writer = writer;
        this.options = options ?? CsvWriterOptions.Default;
        this.options.Validate();
        this.leaveOpen = leaveOpen;

        buffer = ArrayPool<char>.Shared.Rent(DEFAULT_BUFFER_SIZE);
        bufferPosition = 0;
        isFirstFieldInRow = true;
    }

    /// <summary>
    /// Writes a single field value, adding delimiter if needed.
    /// </summary>
    /// <param name="value">The field value to write.</param>
    public void WriteField(string? value)
    {
        WriteField(value.AsSpan());
    }

    /// <summary>
    /// Writes a single field value from a span, adding delimiter if needed.
    /// </summary>
    /// <param name="value">The field value to write.</param>
    public void WriteField(ReadOnlySpan<char> value)
    {
        ThrowIfDisposed();

        if (!isFirstFieldInRow)
        {
            WriteDelimiter();
        }
        isFirstFieldInRow = false;

        WriteFieldValue(value);
    }

    /// <summary>
    /// Writes a row with multiple string values.
    /// </summary>
    /// <param name="values">The field values for the row.</param>
    public void WriteRow(params string?[] values)
    {
        ThrowIfDisposed();

        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) WriteDelimiter();
            WriteFieldValue(values[i].AsSpan());
        }
        isFirstFieldInRow = true;
        WriteNewLine();
    }

    /// <summary>
    /// Writes a row with multiple values, formatting each according to options.
    /// </summary>
    /// <param name="values">The field values for the row.</param>
    public void WriteRow(params object?[] values)
    {
        ThrowIfDisposed();

        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) WriteDelimiter();
            WriteFormattedValue(values[i]);
        }
        isFirstFieldInRow = true;
        WriteNewLine();
    }

    /// <summary>
    /// Writes a row with values from a span, formatting each according to options.
    /// </summary>
    /// <param name="values">The field values for the row.</param>
    public void WriteRow(ReadOnlySpan<object?> values)
    {
        ThrowIfDisposed();

        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) WriteDelimiter();
            WriteFormattedValue(values[i]);
        }
        isFirstFieldInRow = true;
        WriteNewLine();
    }

    /// <summary>
    /// Writes a row with values and their corresponding format strings.
    /// </summary>
    /// <param name="values">The field values for the row.</param>
    /// <param name="formats">The format strings for each field (null for default formatting).</param>
    internal void WriteRowWithFormats(ReadOnlySpan<object?> values, ReadOnlySpan<string?> formats)
    {
        ThrowIfDisposed();

        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) WriteDelimiter();
            var format = i < formats.Length ? formats[i] : null;
            WriteFormattedValueWithFormat(values[i], format);
        }
        isFirstFieldInRow = true;
        WriteNewLine();
    }

    /// <summary>
    /// Ends the current row by writing a newline.
    /// </summary>
    public void EndRow()
    {
        ThrowIfDisposed();
        isFirstFieldInRow = true;
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
    private void WriteDelimiter()
    {
        EnsureCapacity(1);
        buffer[bufferPosition++] = options.Delimiter;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteNewLine()
    {
        var newLine = options.NewLine;
        EnsureCapacity(newLine.Length);
        newLine.AsSpan().CopyTo(buffer.AsSpan(bufferPosition));
        bufferPosition += newLine.Length;
    }

    private void WriteFieldValue(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
        {
            // Empty field - write quotes if AlwaysQuote, otherwise nothing
            if (options.QuoteStyle == QuoteStyle.Always)
            {
                EnsureCapacity(2);
                buffer[bufferPosition++] = options.Quote;
                buffer[bufferPosition++] = options.Quote;
            }
            return;
        }

        bool needsQuoting = options.QuoteStyle switch
        {
            QuoteStyle.Always => true,
            QuoteStyle.Never => false,
            _ => NeedsQuoting(value)
        };

        if (needsQuoting)
        {
            WriteQuotedField(value);
        }
        else
        {
            WriteUnquotedField(value);
        }
    }

    private void WriteUnquotedField(ReadOnlySpan<char> value)
    {
        EnsureCapacity(value.Length);
        value.CopyTo(buffer.AsSpan(bufferPosition));
        bufferPosition += value.Length;
    }

    private void WriteQuotedField(ReadOnlySpan<char> value)
    {
        char quote = options.Quote;

        // Count quotes to calculate required size
        int quoteCount = 0;
        foreach (char c in value)
        {
            if (c == quote) quoteCount++;
        }

        // Total size: opening quote + value + escaped quotes + closing quote
        int requiredSize = 2 + value.Length + quoteCount;
        EnsureCapacity(requiredSize);

        buffer[bufferPosition++] = quote;

        if (quoteCount == 0)
        {
            // Fast path: no quotes to escape
            value.CopyTo(buffer.AsSpan(bufferPosition));
            bufferPosition += value.Length;
        }
        else
        {
            // Escape quotes by doubling them
            foreach (char c in value)
            {
                if (c == quote)
                {
                    buffer[bufferPosition++] = quote;
                }
                buffer[bufferPosition++] = c;
            }
        }

        buffer[bufferPosition++] = quote;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool NeedsQuoting(ReadOnlySpan<char> value)
    {
        char delimiter = options.Delimiter;
        char quote = options.Quote;

        // Use SIMD for larger spans
        if (Avx2.IsSupported && value.Length >= Vector256<ushort>.Count)
        {
            return NeedsQuotingSimd256(value, delimiter, quote);
        }
        else if (Sse2.IsSupported && value.Length >= Vector128<ushort>.Count)
        {
            return NeedsQuotingSimd128(value, delimiter, quote);
        }

        return NeedsQuotingScalar(value, delimiter, quote);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool NeedsQuotingScalar(ReadOnlySpan<char> value, char delimiter, char quote)
    {
        foreach (char c in value)
        {
            if (c == delimiter || c == quote || c == '\r' || c == '\n')
            {
                return true;
            }
        }
        return false;
    }

    private static bool NeedsQuotingSimd256(ReadOnlySpan<char> value, char delimiter, char quote)
    {
        // Create vectors for special characters we need to find
        var delimiterVec = Vector256.Create((ushort)delimiter);
        var quoteVec = Vector256.Create((ushort)quote);
        var crVec = Vector256.Create((ushort)'\r');
        var lfVec = Vector256.Create((ushort)'\n');

        int i = 0;
        int vectorLength = Vector256<ushort>.Count;
        int lastVectorStart = value.Length - vectorLength;

        // Process 16 chars at a time
        while (i <= lastVectorStart)
        {
            var chars = Vector256.LoadUnsafe(ref Unsafe.As<char, ushort>(ref Unsafe.AsRef(in value[i])));

            var matchDelimiter = Vector256.Equals(chars, delimiterVec);
            var matchQuote = Vector256.Equals(chars, quoteVec);
            var matchCr = Vector256.Equals(chars, crVec);
            var matchLf = Vector256.Equals(chars, lfVec);

            var combined = Vector256.BitwiseOr(
                Vector256.BitwiseOr(matchDelimiter, matchQuote),
                Vector256.BitwiseOr(matchCr, matchLf));

            if (combined != Vector256<ushort>.Zero)
            {
                return true;
            }

            i += vectorLength;
        }

        // Handle remaining elements with scalar
        return NeedsQuotingScalar(value[i..], delimiter, quote);
    }

    private static bool NeedsQuotingSimd128(ReadOnlySpan<char> value, char delimiter, char quote)
    {
        // Create vectors for special characters we need to find
        var delimiterVec = Vector128.Create((ushort)delimiter);
        var quoteVec = Vector128.Create((ushort)quote);
        var crVec = Vector128.Create((ushort)'\r');
        var lfVec = Vector128.Create((ushort)'\n');

        int i = 0;
        int vectorLength = Vector128<ushort>.Count;
        int lastVectorStart = value.Length - vectorLength;

        // Process 8 chars at a time
        while (i <= lastVectorStart)
        {
            var chars = Vector128.LoadUnsafe(ref Unsafe.As<char, ushort>(ref Unsafe.AsRef(in value[i])));

            var matchDelimiter = Vector128.Equals(chars, delimiterVec);
            var matchQuote = Vector128.Equals(chars, quoteVec);
            var matchCr = Vector128.Equals(chars, crVec);
            var matchLf = Vector128.Equals(chars, lfVec);

            var combined = Vector128.BitwiseOr(
                Vector128.BitwiseOr(matchDelimiter, matchQuote),
                Vector128.BitwiseOr(matchCr, matchLf));

            if (combined != Vector128<ushort>.Zero)
            {
                return true;
            }

            i += vectorLength;
        }

        // Handle remaining elements with scalar
        return NeedsQuotingScalar(value[i..], delimiter, quote);
    }

    private void WriteFormattedValue(object? value)
    {
        WriteFormattedValueWithFormat(value, format: null);
    }

    private void WriteFormattedValueWithFormat(object? value, string? format)
    {
        if (value is null)
        {
            WriteFieldValue(options.NullValue.AsSpan());
            return;
        }

        // Use explicit format if provided, otherwise fall back to type-based format from options
        var effectiveFormat = format ?? GetFormatString(value);

        // Use ISpanFormattable for zero-allocation formatting when available
        switch (value)
        {
            case string s:
                WriteFieldValue(s.AsSpan());
                break;

            case ISpanFormattable spanFormattable:
                WriteSpanFormattable(spanFormattable, effectiveFormat);
                break;

            case IFormattable formattable:
                WriteFieldValue(formattable.ToString(effectiveFormat, options.Culture).AsSpan());
                break;

            default:
                WriteFieldValue((value.ToString() ?? string.Empty).AsSpan());
                break;
        }
    }

    private void WriteSpanFormattable(ISpanFormattable value, string? format)
    {
        // Try stack allocation first for small values
        Span<char> stackBuffer = stackalloc char[MAX_STACK_ALLOC_SIZE];

        if (value.TryFormat(stackBuffer, out int charsWritten, format, options.Culture))
        {
            WriteFieldValue(stackBuffer[..charsWritten]);
        }
        else
        {
            // Fall back to string allocation for large values
            WriteFieldValue(value.ToString(format, options.Culture).AsSpan());
        }
    }

    private string? GetFormatString(object value)
    {
        return value switch
        {
            DateTime => options.DateTimeFormat,
            DateTimeOffset => options.DateTimeFormat,
#if NET6_0_OR_GREATER
            DateOnly => options.DateOnlyFormat,
            TimeOnly => options.TimeOnlyFormat,
#endif
            // Numeric types use NumberFormat
            sbyte or byte or short or ushort or int or uint or long or ulong
                or float or double or decimal => options.NumberFormat,
            _ => null
        };
    }

    #endregion

    #region Buffer Management

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureCapacity(int required)
    {
        if (bufferPosition + required > buffer.Length)
        {
            FlushBuffer();

            // If still not enough capacity after flush, grow the buffer
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
            writer.Write(buffer.AsSpan(0, bufferPosition));
            bufferPosition = 0;
        }
    }

    private async ValueTask FlushBufferAsync(CancellationToken cancellationToken)
    {
        if (bufferPosition > 0)
        {
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
        buffer = ArrayPool<char>.Shared.Rent(newSize);
        ArrayPool<char>.Shared.Return(oldBuffer);
    }

    #endregion

    #region Disposal

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(CsvStreamWriter));
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
            ArrayPool<char>.Shared.Return(buffer);
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
            ArrayPool<char>.Shared.Return(buffer);
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
}
