using System.Buffers;
using System.Globalization;
using System.Numerics;
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

    // Cached options for hot path access
    private readonly char delimiter;
    private readonly char quote;
    private readonly ReadOnlyMemory<char> newLineMemory;
    private readonly QuoteStyle quoteStyle;
    private readonly CultureInfo culture;
    private readonly string nullValue;
    private readonly string? dateTimeFormat;
    private readonly string? numberFormat;
#if NET6_0_OR_GREATER
    private readonly string? dateOnlyFormat;
    private readonly string? timeOnlyFormat;
#endif

    private readonly char[] buffer;
    private readonly int bufferPosition;
    private readonly bool isFirstFieldInRow;
    private readonly bool disposed;

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

        // Cache frequently accessed options to avoid property access overhead in hot paths
        delimiter = this.options.Delimiter;
        quote = this.options.Quote;
        newLineMemory = this.options.NewLine.AsMemory();
        quoteStyle = this.options.QuoteStyle;
        culture = this.options.Culture;
        nullValue = this.options.NullValue;
        dateTimeFormat = this.options.DateTimeFormat;
        numberFormat = this.options.NumberFormat;
#if NET6_0_OR_GREATER
        dateOnlyFormat = this.options.DateOnlyFormat;
        timeOnlyFormat = this.options.TimeOnlyFormat;
#endif

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
        buffer[bufferPosition++] = delimiter;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteNewLine()
    {
        var newLineSpan = newLineMemory.Span;
        EnsureCapacity(newLineSpan.Length);
        newLineSpan.CopyTo(buffer.AsSpan(bufferPosition));
        bufferPosition += newLineSpan.Length;
    }

    private void WriteFieldValue(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
        {
            // Empty field - write quotes if AlwaysQuote, otherwise nothing
            if (quoteStyle == QuoteStyle.Always)
            {
                EnsureCapacity(2);
                buffer[bufferPosition++] = quote;
                buffer[bufferPosition++] = quote;
            }
            return;
        }

        // Handle quoting based on style
        switch (quoteStyle)
        {
            case QuoteStyle.Always:
                WriteQuotedFieldWithKnownQuoteCount(value, CountQuotes(value));
                break;
            case QuoteStyle.Never:
                WriteUnquotedField(value);
                break;
            case QuoteStyle.WhenNeeded:
            default:
                // Single pass to check if quoting needed AND count quotes
                var (needsQuoting, quoteCount) = AnalyzeFieldForQuoting(value);
                if (needsQuoting)
                {
                    WriteQuotedFieldWithKnownQuoteCount(value, quoteCount);
                }
                else
                {
                    WriteUnquotedField(value);
                }
                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteUnquotedField(ReadOnlySpan<char> value)
    {
        EnsureCapacity(value.Length);
        value.CopyTo(buffer.AsSpan(bufferPosition));
        bufferPosition += value.Length;
    }

    /// <summary>
    /// Writes a quoted field when we already know the quote count (from single-pass analysis).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteQuotedFieldWithKnownQuoteCount(ReadOnlySpan<char> value, int quoteCount)
    {
        // Total size: opening quote + value + escaped quotes + closing quote
        int requiredSize = 2 + value.Length + quoteCount;
        EnsureCapacity(requiredSize);

        buffer[bufferPosition++] = quote;

        if (quoteCount == 0)
        {
            // Fast path: no quotes to escape - just copy
            value.CopyTo(buffer.AsSpan(bufferPosition));
            bufferPosition += value.Length;
        }
        else
        {
            // Escape quotes by doubling them
            WriteWithQuoteEscaping(value);
        }

        buffer[bufferPosition++] = quote;
    }

    /// <summary>
    /// Single-pass analysis: determines if quoting is needed AND counts quotes simultaneously.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (bool needsQuoting, int quoteCount) AnalyzeFieldForQuoting(ReadOnlySpan<char> value)
    {
        // Use SIMD for larger spans
        if (Avx2.IsSupported && value.Length >= Vector256<ushort>.Count)
        {
            return AnalyzeFieldSimd256(value);
        }
        else if (Sse2.IsSupported && value.Length >= Vector128<ushort>.Count)
        {
            return AnalyzeFieldSimd128(value);
        }

        return AnalyzeFieldScalar(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private (bool needsQuoting, int quoteCount) AnalyzeFieldScalar(ReadOnlySpan<char> value)
    {
        bool needsQuoting = false;
        int quoteCount = 0;
        char d = delimiter;
        char q = quote;

        foreach (char c in value)
        {
            if (c == q)
            {
                needsQuoting = true;
                quoteCount++;
            }
            else if (c == d || c == '\r' || c == '\n')
            {
                needsQuoting = true;
            }
        }

        return (needsQuoting, quoteCount);
    }

    private (bool needsQuoting, int quoteCount) AnalyzeFieldSimd256(ReadOnlySpan<char> value)
    {
        var delimiterVec = Vector256.Create((ushort)delimiter);
        var quoteVec = Vector256.Create((ushort)quote);
        var crVec = Vector256.Create((ushort)'\r');
        var lfVec = Vector256.Create((ushort)'\n');

        bool needsQuoting = false;
        int quoteCount = 0;
        int i = 0;
        int vectorLength = Vector256<ushort>.Count;
        int lastVectorStart = value.Length - vectorLength;

        while (i <= lastVectorStart)
        {
            var chars = Vector256.LoadUnsafe(ref Unsafe.As<char, ushort>(ref Unsafe.AsRef(in value[i])));

            var matchDelimiter = Vector256.Equals(chars, delimiterVec);
            var matchQuote = Vector256.Equals(chars, quoteVec);
            var matchCr = Vector256.Equals(chars, crVec);
            var matchLf = Vector256.Equals(chars, lfVec);

            // Check if any special char found
            var combined = Vector256.BitwiseOr(
                Vector256.BitwiseOr(matchDelimiter, matchQuote),
                Vector256.BitwiseOr(matchCr, matchLf));

            if (combined != Vector256<ushort>.Zero)
            {
                needsQuoting = true;
                // Count quotes in this vector using population count
                if (matchQuote != Vector256<ushort>.Zero)
                {
                    quoteCount += BitOperations.PopCount(matchQuote.ExtractMostSignificantBits());
                }
            }

            i += vectorLength;
        }

        // Handle remaining elements with scalar
        for (; i < value.Length; i++)
        {
            char c = value[i];
            if (c == quote)
            {
                needsQuoting = true;
                quoteCount++;
            }
            else if (c == delimiter || c == '\r' || c == '\n')
            {
                needsQuoting = true;
            }
        }

        return (needsQuoting, quoteCount);
    }

    private (bool needsQuoting, int quoteCount) AnalyzeFieldSimd128(ReadOnlySpan<char> value)
    {
        var delimiterVec = Vector128.Create((ushort)delimiter);
        var quoteVec = Vector128.Create((ushort)quote);
        var crVec = Vector128.Create((ushort)'\r');
        var lfVec = Vector128.Create((ushort)'\n');

        bool needsQuoting = false;
        int quoteCount = 0;
        int i = 0;
        int vectorLength = Vector128<ushort>.Count;
        int lastVectorStart = value.Length - vectorLength;

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
                needsQuoting = true;
                if (matchQuote != Vector128<ushort>.Zero)
                {
                    quoteCount += BitOperations.PopCount(matchQuote.ExtractMostSignificantBits());
                }
            }

            i += vectorLength;
        }

        // Handle remaining elements with scalar
        for (; i < value.Length; i++)
        {
            char c = value[i];
            if (c == quote)
            {
                needsQuoting = true;
                quoteCount++;
            }
            else if (c == delimiter || c == '\r' || c == '\n')
            {
                needsQuoting = true;
            }
        }

        return (needsQuoting, quoteCount);
    }

    /// <summary>
    /// Counts quotes in a span (for AlwaysQuote mode).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int CountQuotes(ReadOnlySpan<char> value)
    {
        int count = 0;
        char q = quote;
        foreach (char c in value)
        {
            if (c == q) count++;
        }
        return count;
    }

    /// <summary>
    /// Writes value to buffer, escaping quotes by doubling them.
    /// </summary>
    private void WriteWithQuoteEscaping(ReadOnlySpan<char> value)
    {
        char q = quote;
        foreach (char c in value)
        {
            if (c == q)
            {
                buffer[bufferPosition++] = q;
            }
            buffer[bufferPosition++] = c;
        }
    }

    private void WriteFormattedValue(object? value)
    {
        WriteFormattedValueWithFormat(value, format: null);
    }

    private void WriteFormattedValueWithFormat(object? value, string? format)
    {
        if (value is null)
        {
            WriteFieldValue(nullValue.AsSpan());
            return;
        }

        // Direct type handling for common types - avoids interface dispatch overhead
        // Check concrete types first before falling back to interface
        switch (value)
        {
            case string s:
                WriteFieldValue(s.AsSpan());
                return;

            case int i:
                WriteSpanFormattableDirectly(i, format ?? numberFormat);
                return;

            case long l:
                WriteSpanFormattableDirectly(l, format ?? numberFormat);
                return;

            case double d:
                WriteSpanFormattableDirectly(d, format ?? numberFormat);
                return;

            case decimal dec:
                WriteSpanFormattableDirectly(dec, format ?? numberFormat);
                return;

            case bool b:
                // Booleans never need quoting - write directly
                WriteUnquotedField(b ? "True".AsSpan() : "False".AsSpan());
                return;

            case DateTime dt:
                WriteSpanFormattableDirectly(dt, format ?? dateTimeFormat);
                return;

            case DateTimeOffset dto:
                WriteSpanFormattableDirectly(dto, format ?? dateTimeFormat);
                return;

#if NET6_0_OR_GREATER
            case DateOnly dateOnly:
                WriteSpanFormattableDirectly(dateOnly, format ?? dateOnlyFormat);
                return;

            case TimeOnly timeOnly:
                WriteSpanFormattableDirectly(timeOnly, format ?? timeOnlyFormat);
                return;
#endif

            case float f:
                WriteSpanFormattableDirectly(f, format ?? numberFormat);
                return;

            case byte by:
                WriteSpanFormattableDirectly(by, format ?? numberFormat);
                return;

            case short sh:
                WriteSpanFormattableDirectly(sh, format ?? numberFormat);
                return;

            case uint ui:
                WriteSpanFormattableDirectly(ui, format ?? numberFormat);
                return;

            case ulong ul:
                WriteSpanFormattableDirectly(ul, format ?? numberFormat);
                return;

            case Guid g:
                WriteSpanFormattableDirectly(g, format);
                return;

            default:
                // Fall through to interface-based handling below
                break;
        }

        // Fallback for other ISpanFormattable types
        if (value is ISpanFormattable spanFormattable)
        {
            WriteSpanFormattable(spanFormattable, format);
            return;
        }

        // Final fallback for IFormattable
        if (value is IFormattable formattable)
        {
            WriteFieldValue(formattable.ToString(format, culture).AsSpan());
            return;
        }

        // Last resort: ToString()
        WriteFieldValue((value.ToString() ?? string.Empty).AsSpan());
    }

    /// <summary>
    /// Writes an ISpanFormattable value directly to the output buffer when possible.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteSpanFormattableDirectly<T>(T value, string? format) where T : ISpanFormattable
    {
        // Try stack allocation first for small values
        Span<char> stackBuffer = stackalloc char[MAX_STACK_ALLOC_SIZE];

        if (value.TryFormat(stackBuffer, out int charsWritten, format, culture))
        {
            WriteFieldValue(stackBuffer[..charsWritten]);
        }
        else
        {
            // Fall back to string allocation for large values
            WriteFieldValue(value.ToString(format, culture).AsSpan());
        }
    }

    private void WriteSpanFormattable(ISpanFormattable value, string? format)
    {
        // Try stack allocation first for small values
        Span<char> stackBuffer = stackalloc char[MAX_STACK_ALLOC_SIZE];

        if (value.TryFormat(stackBuffer, out int charsWritten, format, culture))
        {
            WriteFieldValue(stackBuffer[..charsWritten]);
        }
        else
        {
            // Fall back to string allocation for large values
            WriteFieldValue(value.ToString(format, culture).AsSpan());
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
