using System.Buffers;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
#if NET6_0_OR_GREATER
using System.Threading.Tasks.Sources;
#endif
using HeroParser.SeparatedValues.Core;

namespace HeroParser.SeparatedValues.Writing;

/// <summary>
/// High-performance async CSV writer that writes to a <see cref="Stream"/> without blocking.
/// </summary>
/// <remarks>
/// <para>
/// Uses per-instance pools for buffer management to minimize allocations and isolate pooled buffers.
/// Supports all <see cref="CsvWriteOptions"/> including quote styles, injection protection,
/// and DoS protection limits.
/// </para>
/// <para>
/// Thread-Safety: This class is not thread-safe. Do not call methods concurrently.
/// </para>
/// </remarks>
public sealed class CsvAsyncStreamWriter : IAsyncDisposable
{
    private readonly Stream stream;
    private readonly CsvWriteOptions options;
    private readonly bool leaveOpen;
    private readonly Encoding encoding;

    // Cached options for hot path access
    private readonly char delimiter;
    private readonly char quote;
    private readonly ReadOnlyMemory<char> newLineMemory;
    private readonly QuoteStyle quoteStyle;
    private readonly CultureInfo culture;
    private readonly string nullValue;
    private readonly string? dateTimeFormat;
    private readonly string? timeSpanFormat;
    private readonly string? numberFormat;
#if NET6_0_OR_GREATER
    private readonly string? dateOnlyFormat;
    private readonly string? timeOnlyFormat;
#endif

    // Security and DoS protection
    private readonly CsvInjectionProtection injectionProtection;
    private readonly CsvWriterQuoting quoting;
    private readonly long? maxOutputSize;
    private readonly int? maxFieldSize;
    private readonly int? maxColumnCount;

    private readonly ArrayPool<char> charPool;
    private readonly ArrayPool<byte> bytePool;
    // State tracking
    private char[] charBuffer;
    private int charBufferPosition;
    private byte[] byteBuffer;
    private bool isFirstFieldInRow;
    private bool disposed;
    private long totalCharsWritten;
    private int currentRowColumnCount;

    /// <summary>
    /// Gets the total number of characters written to the underlying stream.
    /// </summary>
    public long CharsWritten => totalCharsWritten + charBufferPosition;

    /// <summary>
    /// Gets the CultureInfo used by the writer.
    /// </summary>
    public CultureInfo Culture => options.Culture;

    internal string NullValue => options.NullValue;

    internal string? NumberFormat => options.NumberFormat;

    private const int DEFAULT_CHAR_BUFFER_SIZE = 16 * 1024; // 16KB chars
    private const int DEFAULT_BYTE_BUFFER_SIZE = 16 * 1024; // 16KB bytes
    private const int MAX_STACK_ALLOC_SIZE = 256;

    /// <summary>
    /// Creates a new async CSV writer that writes to the specified stream.
    /// </summary>
    /// <param name="stream">The underlying stream to write CSV output to.</param>
    /// <param name="options">Writer options; defaults to <see cref="CsvWriteOptions.Default"/>.</param>
    /// <param name="encoding">Text encoding; defaults to UTF-8.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, the <paramref name="stream"/> is not disposed.</param>
    public CsvAsyncStreamWriter(Stream stream, CsvWriteOptions? options = null, Encoding? encoding = null, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);

        this.stream = stream;
        this.options = options ?? CsvWriteOptions.Default;
        this.options.Validate();
        this.encoding = encoding ?? Encoding.UTF8;
        this.leaveOpen = leaveOpen;

        // Cache frequently accessed options to avoid property access overhead in hot paths
        delimiter = this.options.Delimiter;
        quote = this.options.Quote;
        newLineMemory = this.options.NewLine.AsMemory();
        quoteStyle = this.options.QuoteStyle;
        culture = this.options.Culture;
        nullValue = this.options.NullValue;
        dateTimeFormat = this.options.DateTimeFormat;
        timeSpanFormat = this.options.TimeSpanFormat;
        numberFormat = this.options.NumberFormat;
#if NET6_0_OR_GREATER
        dateOnlyFormat = this.options.DateOnlyFormat;
        timeOnlyFormat = this.options.TimeOnlyFormat;
#endif

        // Security and DoS protection options
        injectionProtection = this.options.InjectionProtection;
        quoting = new(delimiter, quote, this.options.AdditionalDangerousChars);
        maxOutputSize = this.options.MaxOutputSize;
        maxFieldSize = this.options.MaxFieldSize;
        maxColumnCount = this.options.MaxColumnCount;

        // Use shared pool for better memory efficiency - arrays are always cleared on return
        charPool = ArrayPool<char>.Shared;
        bytePool = ArrayPool<byte>.Shared;
        charBuffer = RentCharBuffer(DEFAULT_CHAR_BUFFER_SIZE);
        byteBuffer = RentByteBuffer(DEFAULT_BYTE_BUFFER_SIZE);
        // Note: byteBuffer doesn't need clearing as it's overwritten during encoding
        charBufferPosition = 0;
        isFirstFieldInRow = true;
        totalCharsWritten = 0;
        currentRowColumnCount = 0;
    }

    /// <summary>
    /// Asynchronously writes a single field value, adding delimiter if needed.
    /// </summary>
    /// <param name="value">The field value to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    public async ValueTask WriteFieldAsync(string? value, CancellationToken cancellationToken = default)
    {
        if (value is null)
        {
            await WriteFieldAsync(ReadOnlyMemory<char>.Empty, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await WriteFieldAsync(value.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Asynchronously writes a single field value from a span, adding delimiter if needed.
    /// </summary>
    /// <param name="value">The field value to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    public async ValueTask WriteFieldAsync(ReadOnlyMemory<char> value, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        // Check column count limit
        currentRowColumnCount++;
        if (maxColumnCount.HasValue && currentRowColumnCount > maxColumnCount.Value)
        {
            throw new CsvException(
                CsvErrorCode.TooManyColumnsWritten,
                $"Row exceeds maximum column count of {maxColumnCount.Value}");
        }

        if (!isFirstFieldInRow)
        {
            await WriteDelimiterAsync(cancellationToken).ConfigureAwait(false);
        }
        isFirstFieldInRow = false;

        await WriteFieldValueAsync(value, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Asynchronously writes an Int32 field directly without boxing.</summary>
    public ValueTask WriteFieldAsync(int value, CancellationToken cancellationToken = default)
        => WriteSpanFormattableFieldAsync(value, format: null, cancellationToken);

    /// <summary>Asynchronously writes an Int64 field directly without boxing.</summary>
    public ValueTask WriteFieldAsync(long value, CancellationToken cancellationToken = default)
        => WriteSpanFormattableFieldAsync(value, format: null, cancellationToken);

    /// <summary>Asynchronously writes an Int16 field directly without boxing.</summary>
    public ValueTask WriteFieldAsync(short value, CancellationToken cancellationToken = default)
        => WriteSpanFormattableFieldAsync(value, format: null, cancellationToken);

    /// <summary>Asynchronously writes a UInt16 field directly without boxing.</summary>
    public ValueTask WriteFieldAsync(ushort value, CancellationToken cancellationToken = default)
        => WriteSpanFormattableFieldAsync(value, format: null, cancellationToken);

    /// <summary>Asynchronously writes a Byte field directly without boxing.</summary>
    public ValueTask WriteFieldAsync(byte value, CancellationToken cancellationToken = default)
        => WriteSpanFormattableFieldAsync(value, format: null, cancellationToken);

    /// <summary>Asynchronously writes an SByte field directly without boxing.</summary>
    public ValueTask WriteFieldAsync(sbyte value, CancellationToken cancellationToken = default)
        => WriteSpanFormattableFieldAsync(value, format: null, cancellationToken);

    /// <summary>Asynchronously writes a UInt32 field directly without boxing.</summary>
    public ValueTask WriteFieldAsync(uint value, CancellationToken cancellationToken = default)
        => WriteSpanFormattableFieldAsync(value, format: null, cancellationToken);

    /// <summary>Asynchronously writes a UInt64 field directly without boxing.</summary>
    public ValueTask WriteFieldAsync(ulong value, CancellationToken cancellationToken = default)
        => WriteSpanFormattableFieldAsync(value, format: null, cancellationToken);

    /// <summary>Asynchronously writes a Double field directly without boxing.</summary>
    public ValueTask WriteFieldAsync(double value, CancellationToken cancellationToken = default)
        => WriteSpanFormattableFieldAsync(value, format: null, cancellationToken);

    /// <summary>Asynchronously writes a Float field directly without boxing.</summary>
    public ValueTask WriteFieldAsync(float value, CancellationToken cancellationToken = default)
        => WriteSpanFormattableFieldAsync(value, format: null, cancellationToken);

    /// <summary>Asynchronously writes a Decimal field directly without boxing.</summary>
    public ValueTask WriteFieldAsync(decimal value, CancellationToken cancellationToken = default)
        => WriteSpanFormattableFieldAsync(value, format: null, cancellationToken);

    /// <summary>Asynchronously writes a Boolean field directly without boxing.</summary>
    public ValueTask WriteFieldAsync(bool value, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        currentRowColumnCount++;
        if (maxColumnCount.HasValue && currentRowColumnCount > maxColumnCount.Value)
        {
            throw new CsvException(CsvErrorCode.TooManyColumnsWritten, $"Row exceeds maximum column count of {maxColumnCount.Value}");
        }

        if (!isFirstFieldInRow)
        {
            if (charBufferPosition + 1 <= charBuffer.Length)
            {
                charBuffer[charBufferPosition++] = delimiter;
            }
            else
            {
                return WriteFieldSlowAsync(value, cancellationToken);
            }
        }
        isFirstFieldInRow = false;

        ReadOnlySpan<char> boolStr = value ? "True" : "False";
        if (charBufferPosition + boolStr.Length <= charBuffer.Length)
        {
            boolStr.CopyTo(charBuffer.AsSpan(charBufferPosition));
            charBufferPosition += boolStr.Length;
            return ValueTask.CompletedTask;
        }

        return WriteFieldValueAsync(value ? "True".AsMemory() : "False".AsMemory(), cancellationToken);
    }

    private async ValueTask WriteFieldSlowAsync(bool value, CancellationToken cancellationToken)
    {
        await WriteDelimiterAsync(cancellationToken).ConfigureAwait(false);
        ReadOnlySpan<char> boolStr = value ? "True" : "False";
        if (charBufferPosition + boolStr.Length <= charBuffer.Length)
        {
            boolStr.CopyTo(charBuffer.AsSpan(charBufferPosition));
            charBufferPosition += boolStr.Length;
        }
        else
        {
            await WriteFieldValueAsync(value ? "True".AsMemory() : "False".AsMemory(), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Asynchronously writes a DateTime field directly without boxing.</summary>
    public ValueTask WriteFieldAsync(DateTime value, CancellationToken cancellationToken = default)
        => WriteSpanFormattableFieldAsync(value, format: dateTimeFormat, cancellationToken);

    /// <summary>Asynchronously writes a DateTimeOffset field directly without boxing.</summary>
    public ValueTask WriteFieldAsync(DateTimeOffset value, CancellationToken cancellationToken = default)
        => WriteSpanFormattableFieldAsync(value, format: dateTimeFormat, cancellationToken);

    /// <summary>Asynchronously writes a DateOnly field directly without boxing.</summary>
    public ValueTask WriteFieldAsync(DateOnly value, CancellationToken cancellationToken = default)
        => WriteSpanFormattableFieldAsync(value, format: dateOnlyFormat, cancellationToken);

    /// <summary>Asynchronously writes a TimeOnly field directly without boxing.</summary>
    public ValueTask WriteFieldAsync(TimeOnly value, CancellationToken cancellationToken = default)
        => WriteSpanFormattableFieldAsync(value, format: timeOnlyFormat, cancellationToken);

    /// <summary>Asynchronously writes a TimeSpan field directly without boxing.</summary>
    public ValueTask WriteFieldAsync(TimeSpan value, CancellationToken cancellationToken = default)
        => WriteSpanFormattableFieldAsync(value, format: timeSpanFormat, cancellationToken);

    /// <summary>Asynchronously writes a Char field directly without heap string allocation.</summary>
    public ValueTask WriteFieldAsync(char value, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        currentRowColumnCount++;
        if (maxColumnCount.HasValue && currentRowColumnCount > maxColumnCount.Value)
        {
            throw new CsvException(
                CsvErrorCode.TooManyColumnsWritten,
                $"Row exceeds maximum column count of {maxColumnCount.Value}");
        }

        if (!isFirstFieldInRow)
        {
            if (charBufferPosition + 1 <= charBuffer.Length)
            {
                charBuffer[charBufferPosition++] = delimiter;
            }
            else
            {
                isFirstFieldInRow = false;
                return WriteCharAfterDelimiterSlowAsync(value, cancellationToken);
            }
        }
        isFirstFieldInRow = false;

        if (TryWriteCharFast(value))
            return ValueTask.CompletedTask;

        return WriteCharSlowAsync(value, cancellationToken);
    }

    private bool TryWriteCharFast(char value)
    {
        if (maxFieldSize.HasValue && 1 > maxFieldSize.Value)
        {
            throw new CsvException(
                CsvErrorCode.FieldSizeExceeded,
                $"Field size 1 exceeds maximum of {maxFieldSize.Value}");
        }

        if (quoteStyle != QuoteStyle.Always &&
            injectionProtection == CsvInjectionProtection.None &&
            (quoteStyle == QuoteStyle.Never ||
             (value != delimiter && value != quote && value != '\r' && value != '\n')) &&
            charBufferPosition + 1 <= charBuffer.Length)
        {
            charBuffer[charBufferPosition++] = value;
            return true;
        }

        return false;
    }

#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    private async ValueTask WriteCharAfterDelimiterSlowAsync(char value, CancellationToken cancellationToken)
    {
        await WriteDelimiterAsync(cancellationToken).ConfigureAwait(false);
        if (!TryWriteCharFast(value))
        {
            await WriteCharSlowAsync(value, cancellationToken).ConfigureAwait(false);
        }
    }

#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    private async ValueTask WriteCharSlowAsync(char value, CancellationToken cancellationToken)
    {
        char[] rented = charPool.Rent(1);
        rented[0] = value;
        try
        {
            await WriteFieldValueAsync(rented.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            charPool.Return(rented, clearArray: true);
        }
    }

    /// <summary>Asynchronously writes a Half field directly without boxing.</summary>
    public ValueTask WriteFieldAsync(Half value, CancellationToken cancellationToken = default)
        => WriteSpanFormattableFieldAsync(value, format: numberFormat, cancellationToken);

    /// <summary>Asynchronously writes an Int128 field directly without boxing.</summary>
    public ValueTask WriteFieldAsync(Int128 value, CancellationToken cancellationToken = default)
        => WriteSpanFormattableFieldAsync(value, format: numberFormat, cancellationToken);

    /// <summary>Asynchronously writes a UInt128 field directly without boxing.</summary>
    public ValueTask WriteFieldAsync(UInt128 value, CancellationToken cancellationToken = default)
        => WriteSpanFormattableFieldAsync(value, format: numberFormat, cancellationToken);

    /// <summary>Asynchronously writes a Guid field directly without boxing.</summary>
    public ValueTask WriteFieldAsync(Guid value, CancellationToken cancellationToken = default)
        => WriteSpanFormattableFieldAsync(value, format: null, cancellationToken);

    /// <summary>
    /// Asynchronously writes an <see cref="ISpanFormattable"/> field directly with an explicit format string without boxing.
    /// </summary>
    /// <typeparam name="T">The span-formattable value type.</typeparam>
    /// <param name="value">The value to write.</param>
    /// <param name="format">Optional format string; when <see langword="null"/>, default formatting is used.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public ValueTask WriteFieldAsync<T>(T value, string? format, CancellationToken cancellationToken = default)
        where T : ISpanFormattable
        => WriteSpanFormattableFieldAsync(value, format, cancellationToken);

    internal ValueTask WriteFormattedFieldAsync(object? value, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        currentRowColumnCount++;
        if (maxColumnCount.HasValue && currentRowColumnCount > maxColumnCount.Value)
        {
            throw new CsvException(
                CsvErrorCode.TooManyColumnsWritten,
                $"Row exceeds maximum column count of {maxColumnCount.Value}");
        }

        if (!isFirstFieldInRow)
        {
            if (charBufferPosition + 1 <= charBuffer.Length)
            {
                charBuffer[charBufferPosition++] = delimiter;
            }
            else
            {
                isFirstFieldInRow = false;
                return WriteFormattedFieldSlowAsync(value, cancellationToken);
            }
        }
        isFirstFieldInRow = false;

        return WriteFormattedValueAsync(value, format: null, cancellationToken);
    }

#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    private async ValueTask WriteFormattedFieldSlowAsync(object? value, CancellationToken cancellationToken)
    {
        await WriteDelimiterAsync(cancellationToken).ConfigureAwait(false);
        await WriteFormattedValueAsync(value, format: null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously writes a row with multiple string values.
    /// Uses batched sync fast path when entire row fits in buffer.
    /// </summary>
    /// <param name="values">The field values for the row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public ValueTask WriteRowAsync(string?[] values, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        // Check column count limit
        if (maxColumnCount.HasValue && values.Length > maxColumnCount.Value)
        {
            throw new CsvException(
                CsvErrorCode.TooManyColumnsWritten,
                $"Row has {values.Length} columns, exceeds maximum of {maxColumnCount.Value}");
        }

        // Try sync fast path: write entire row without any async transitions
        if (TryWriteRowSync(values))
        {
            isFirstFieldInRow = true;
            currentRowColumnCount = 0;
            return default;
        }

        // Fall back to async path
        return WriteRowSlowAsync(values, cancellationToken);
    }

    /// <summary>
    /// Tries to write an entire row synchronously. Returns true if successful.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryWriteRowSync(string?[] values)
    {
        // Save position so we can restore on failure
        int savedPosition = charBufferPosition;

        // Write the entire row synchronously
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0)
            {
                if (charBufferPosition + 1 > charBuffer.Length)
                {
                    charBufferPosition = savedPosition;
                    return false;
                }
                charBuffer[charBufferPosition++] = delimiter;
            }

            if (!WriteFieldValueSync(values[i].AsSpan()))
            {
                charBufferPosition = savedPosition;
                return false;
            }
        }

        // Write newline
        int newLineLen = newLineMemory.Length;
        if (charBufferPosition + newLineLen > charBuffer.Length)
        {
            charBufferPosition = savedPosition;
            return false;
        }
        newLineMemory.Span.CopyTo(charBuffer.AsSpan(charBufferPosition));
        charBufferPosition += newLineLen;

        return true;
    }

    /// <summary>
    /// Writes a single field value synchronously. Returns true if successful.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool WriteFieldValueSync(ReadOnlySpan<char> value)
    {
        // Check field size limit
        if (maxFieldSize.HasValue && value.Length > maxFieldSize.Value)
        {
            throw new CsvException(
                CsvErrorCode.FieldSizeExceeded,
                $"Field size {value.Length} exceeds maximum of {maxFieldSize.Value}");
        }

        if (value.IsEmpty)
        {
            if (quoteStyle == QuoteStyle.Always)
            {
                if (charBufferPosition + 2 > charBuffer.Length) return false;
                charBuffer[charBufferPosition++] = quote;
                charBuffer[charBufferPosition++] = quote;
            }
            return true;
        }

        // Check for injection
        if (injectionProtection != CsvInjectionProtection.None &&
            quoting.IsDangerousField(value))
        {
            // Injection protection requires more complex handling, fall back to async
            return false;
        }

        // Handle quoting based on style
        switch (quoteStyle)
        {
            case QuoteStyle.Always:
                {
                    int alwaysQuoteCount = quoting.CountQuotes(value);
                    int requiredSize = 2 + value.Length + alwaysQuoteCount;
                    if (charBufferPosition + requiredSize > charBuffer.Length) return false;
                    WriteQuotedFieldSync(value);
                }
                break;
            case QuoteStyle.Never:
                if (charBufferPosition + value.Length > charBuffer.Length) return false;
                value.CopyTo(charBuffer.AsSpan(charBufferPosition));
                charBufferPosition += value.Length;
                break;
            case QuoteStyle.WhenNeeded:
            default:
                var (needsQuoting, quoteCount) = quoting.AnalyzeFieldForQuoting(value);
                if (needsQuoting)
                {
                    int requiredSize = 2 + value.Length + quoteCount;
                    if (charBufferPosition + requiredSize > charBuffer.Length) return false;
                    WriteQuotedFieldWithKnownQuoteCountSync(value, quoteCount);
                }
                else
                {
                    if (charBufferPosition + value.Length > charBuffer.Length) return false;
                    value.CopyTo(charBuffer.AsSpan(charBufferPosition));
                    charBufferPosition += value.Length;
                }
                break;
        }

        return true;
    }

    /// <summary>
    /// Writes a quoted field synchronously.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteQuotedFieldSync(ReadOnlySpan<char> value)
    {
        int quoteCount = quoting.CountQuotes(value);
        WriteQuotedFieldWithKnownQuoteCountSync(value, quoteCount);
    }

    /// <summary>
    /// Writes a quoted field with known quote count synchronously.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteQuotedFieldWithKnownQuoteCountSync(ReadOnlySpan<char> value, int quoteCount)
    {
        charBuffer[charBufferPosition++] = quote;

        if (quoteCount == 0)
        {
            value.CopyTo(charBuffer.AsSpan(charBufferPosition));
            charBufferPosition += value.Length;
        }
        else
        {
            WriteWithQuoteEscaping(value);
        }

        charBuffer[charBufferPosition++] = quote;
    }

#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    private async ValueTask WriteRowSlowAsync(string?[] values, CancellationToken cancellationToken)
    {
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) await WriteDelimiterAsync(cancellationToken).ConfigureAwait(false);
            await WriteFieldValueAsync(values[i].AsMemory(), cancellationToken).ConfigureAwait(false);
        }
        isFirstFieldInRow = true;
        currentRowColumnCount = 0;
        await WriteNewLineAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously writes a row with multiple values, formatting each according to options.
    /// Uses batched sync fast path when entire row fits in buffer.
    /// </summary>
    /// <param name="values">The field values for the row.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public ValueTask WriteRowAsync(object?[] values, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        // Check column count limit
        if (maxColumnCount.HasValue && values.Length > maxColumnCount.Value)
        {
            throw new CsvException(
                CsvErrorCode.TooManyColumnsWritten,
                $"Row has {values.Length} columns, exceeds maximum of {maxColumnCount.Value}");
        }

        // Try sync fast path: write entire row without any async transitions
        if (TryWriteObjectRowSync(values))
        {
            isFirstFieldInRow = true;
            currentRowColumnCount = 0;
            return default;
        }

        // Fall back to async path
        return WriteObjectRowSlowAsync(values, cancellationToken);
    }

    /// <summary>
    /// Asynchronously writes a row with multiple values and per-column format strings.
    /// </summary>
    internal ValueTask WriteRowWithFormatsAsync(
        object?[] values,
        string?[] formats,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        if (maxColumnCount.HasValue && values.Length > maxColumnCount.Value)
        {
            throw new CsvException(
                CsvErrorCode.TooManyColumnsWritten,
                $"Row has {values.Length} columns, exceeds maximum of {maxColumnCount.Value}");
        }

        if (TryWriteObjectRowSync(values, formats))
        {
            isFirstFieldInRow = true;
            currentRowColumnCount = 0;
            return default;
        }

        return WriteObjectRowSlowAsync(values, formats, cancellationToken);
    }

    /// <summary>
    /// Tries to write an entire object row synchronously. Returns true if successful.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryWriteObjectRowSync(object?[] values, string?[]? formats = null)
    {
        // Save position so we can restore on failure
        int savedPosition = charBufferPosition;

        // Write the entire row synchronously
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0)
            {
                if (charBufferPosition + 1 > charBuffer.Length)
                {
                    charBufferPosition = savedPosition;
                    return false;
                }
                charBuffer[charBufferPosition++] = delimiter;
            }

            string? format = formats is not null && i < formats.Length ? formats[i] : null;
            if (!WriteFormattedValueSync(values[i], format))
            {
                // Value couldn't be written synchronously, fall back to async
                charBufferPosition = savedPosition;
                return false;
            }
        }

        // Write newline
        int newLineLen = newLineMemory.Length;
        if (charBufferPosition + newLineLen > charBuffer.Length)
        {
            charBufferPosition = savedPosition;
            return false;
        }
        newLineMemory.Span.CopyTo(charBuffer.AsSpan(charBufferPosition));
        charBufferPosition += newLineLen;

        return true;
    }

    /// <summary>
    /// Writes a formatted value synchronously. Returns true if successful.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool WriteFormattedValueSync(object? value)
        => WriteFormattedValueSync(value, format: null);

    /// <summary>
    /// Writes a formatted value synchronously with an optional format string.
    /// Returns true if successful.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool WriteFormattedValueSync(object? value, string? format)
    {
        if (value is null)
        {
            return WriteFieldValueSync(nullValue.AsSpan());
        }

        // Direct type handling for common types
        switch (value)
        {
            case string s:
                return WriteFieldValueSync(s.AsSpan());

            case int i:
                return WriteSpanFormattableSync(i, format ?? numberFormat);

            case long l:
                return WriteSpanFormattableSync(l, format ?? numberFormat);

            case double d:
                return WriteSpanFormattableSync(d, format ?? numberFormat);

            case decimal dec:
                return WriteSpanFormattableSync(dec, format ?? numberFormat);

            case bool b:
                // Booleans never need quoting - write directly
                ReadOnlySpan<char> boolStr = b ? "True" : "False";
                if (charBufferPosition + boolStr.Length > charBuffer.Length) return false;
                boolStr.CopyTo(charBuffer.AsSpan(charBufferPosition));
                charBufferPosition += boolStr.Length;
                return true;

            case DateTime dt:
                return WriteSpanFormattableSync(dt, format ?? dateTimeFormat);

            case DateTimeOffset dto:
                return WriteSpanFormattableSync(dto, format ?? dateTimeFormat);

#if NET6_0_OR_GREATER
            case DateOnly dateOnly:
                return WriteSpanFormattableSync(dateOnly, format ?? dateOnlyFormat);

            case TimeOnly timeOnly:
                return WriteSpanFormattableSync(timeOnly, format ?? timeOnlyFormat);
#endif

            case float f:
                return WriteSpanFormattableSync(f, format ?? numberFormat);

            case byte by:
                return WriteSpanFormattableSync(by, format ?? numberFormat);

            case short sh:
                return WriteSpanFormattableSync(sh, format ?? numberFormat);

            case uint ui:
                return WriteSpanFormattableSync(ui, format ?? numberFormat);

            case ulong ul:
                return WriteSpanFormattableSync(ul, format ?? numberFormat);

            case ushort ush:
                return WriteSpanFormattableSync(ush, format ?? numberFormat);

            case sbyte sb:
                return WriteSpanFormattableSync(sb, format ?? numberFormat);

            case TimeSpan ts:
                return WriteSpanFormattableSync(ts, format ?? timeSpanFormat);

            case char ch:
                {
                    Span<char> chBuf = [ch];
                    return WriteFieldValueSync(chBuf);
                }

            case Half half:
                return WriteSpanFormattableSync(half, format ?? numberFormat);

            case Int128 i128:
                return WriteSpanFormattableSync(i128, format ?? numberFormat);

            case UInt128 u128:
                return WriteSpanFormattableSync(u128, format ?? numberFormat);

            case BigInteger bi:
                return WriteSpanFormattableSync(bi, format ?? numberFormat);

            case Uri uri:
                return WriteFieldValueSync(uri.OriginalString.AsSpan());

            case byte[] bytes:
                if (bytes.Length <= 180)
                {
                    Span<char> b64 = stackalloc char[256];
                    if (Convert.TryToBase64Chars(bytes, b64, out int b64Written))
                        return WriteFieldValueSync(b64[..b64Written]);
                }
                return false;

            case Guid g:
                return WriteSpanFormattableSync(g, format);

            default:
                // For other types, fall back to async path
                return false;
        }
    }

    /// <summary>
    /// Writes an ISpanFormattable value synchronously. Returns true if successful.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool WriteSpanFormattableSync<T>(T value, string? format) where T : ISpanFormattable
    {
        // Try to format directly into our char buffer
        int availableSpace = charBuffer.Length - charBufferPosition;
        if (availableSpace < MAX_STACK_ALLOC_SIZE)
        {
            return false;
        }

        var targetSpan = charBuffer.AsSpan(charBufferPosition, MAX_STACK_ALLOC_SIZE);
        if (!value.TryFormat(targetSpan, out int charsWritten, format, culture))
        {
            return false;
        }

        // Check field size limit
        if (maxFieldSize.HasValue && charsWritten > maxFieldSize.Value)
        {
            throw new CsvException(
                CsvErrorCode.FieldSizeExceeded,
                $"Field size {charsWritten} exceeds maximum of {maxFieldSize.Value}");
        }

        // For numeric types with injection protection or AlwaysQuote, we need to process
        if (injectionProtection != CsvInjectionProtection.None)
        {
            var valueSpan = charBuffer.AsSpan(charBufferPosition, charsWritten);
            if (quoting.IsDangerousField(valueSpan))
            {
                return false; // Fall back to async for injection handling
            }
        }

        if (quoteStyle == QuoteStyle.Always)
        {
            // Need to quote - shift content and add quotes
            var valueSpan = charBuffer.AsSpan(charBufferPosition, charsWritten);
            int quoteCount = quoting.CountQuotes(valueSpan);
            int totalSize = 2 + charsWritten + quoteCount;

            if (charBufferPosition + totalSize > charBuffer.Length)
            {
                return false;
            }

            // Copy to temp, then write quoted
            Span<char> temp = stackalloc char[charsWritten];
            valueSpan.CopyTo(temp);
            WriteQuotedFieldWithKnownQuoteCountSync(temp, quoteCount);
        }
        else if (quoteStyle == QuoteStyle.WhenNeeded)
        {
            // Check if quoting is needed
            var valueSpan = charBuffer.AsSpan(charBufferPosition, charsWritten);
            var (needsQuoting, quoteCount) = quoting.AnalyzeFieldForQuoting(valueSpan);

            if (needsQuoting)
            {
                int totalSize = 2 + charsWritten + quoteCount;
                if (charBufferPosition + totalSize > charBuffer.Length)
                {
                    return false;
                }

                // Copy to temp, then write quoted
                Span<char> temp = stackalloc char[charsWritten];
                valueSpan.CopyTo(temp);
                WriteQuotedFieldWithKnownQuoteCountSync(temp, quoteCount);
            }
            else
            {
                // Value is already in buffer, just advance position
                charBufferPosition += charsWritten;
            }
        }
        else
        {
            // Never quote - just advance position
            charBufferPosition += charsWritten;
        }

        return true;
    }

#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    private async ValueTask WriteObjectRowSlowAsync(object?[] values, CancellationToken cancellationToken)
        => await WriteObjectRowSlowAsync(values, formats: null, cancellationToken).ConfigureAwait(false);

#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    private async ValueTask WriteObjectRowSlowAsync(object?[] values, string?[]? formats, CancellationToken cancellationToken)
    {
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) await WriteDelimiterAsync(cancellationToken).ConfigureAwait(false);
            string? format = formats is not null && i < formats.Length ? formats[i] : null;
            await WriteFormattedValueAsync(values[i], format, cancellationToken).ConfigureAwait(false);
        }
        isFirstFieldInRow = true;
        currentRowColumnCount = 0;
        await WriteNewLineAsync(cancellationToken).ConfigureAwait(false);
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
        isFirstFieldInRow = true;
        currentRowColumnCount = 0;
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

    /// <summary>
    /// Writes a delimiter. Uses sync fast path when buffer has space.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ValueTask WriteDelimiterAsync(CancellationToken cancellationToken)
    {
        // Sync fast path - most common case
        if (charBufferPosition + 1 <= charBuffer.Length)
        {
            charBuffer[charBufferPosition++] = delimiter;
            return default;
        }
        return WriteDelimiterSlowAsync(cancellationToken);
    }

#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    private async ValueTask WriteDelimiterSlowAsync(CancellationToken cancellationToken)
    {
        await FlushCharBufferAsync(cancellationToken).ConfigureAwait(false);
        charBuffer[charBufferPosition++] = delimiter;
    }

    /// <summary>
    /// Writes a newline. Uses sync fast path when buffer has space.
    /// </summary>
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
    private async ValueTask WriteFieldValueAsync(ReadOnlyMemory<char> value, CancellationToken cancellationToken)
    {
        var valueSpan = value.Span;

        // Check field size limit
        if (maxFieldSize.HasValue && valueSpan.Length > maxFieldSize.Value)
        {
            throw new CsvException(
                CsvErrorCode.FieldSizeExceeded,
                $"Field size {valueSpan.Length} exceeds maximum of {maxFieldSize.Value}");
        }

        if (valueSpan.IsEmpty)
        {
            // Empty field - write quotes if AlwaysQuote, otherwise nothing
            if (quoteStyle == QuoteStyle.Always)
            {
                await EnsureCapacityAsync(2, cancellationToken).ConfigureAwait(false);
                charBuffer[charBufferPosition++] = quote;
                charBuffer[charBufferPosition++] = quote;
            }
            return;
        }

        // Check for injection and apply protection if needed
        if (injectionProtection != CsvInjectionProtection.None &&
            quoting.IsDangerousField(valueSpan))
        {
            await WriteFieldWithInjectionProtectionInternalAsync(value, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Handle quoting based on style
        switch (quoteStyle)
        {
            case QuoteStyle.Always:
                await WriteQuotedFieldWithKnownQuoteCountInternalAsync(
                    value,
                    quoting.CountQuotes(valueSpan),
                    cancellationToken).ConfigureAwait(false);
                break;
            case QuoteStyle.Never:
                await WriteUnquotedFieldInternalAsync(value, cancellationToken).ConfigureAwait(false);
                break;
            case QuoteStyle.WhenNeeded:
            default:
                // Single pass to check if quoting needed AND count quotes
                var (needsQuoting, quoteCount) = quoting.AnalyzeFieldForQuoting(valueSpan);
                if (needsQuoting)
                {
                    await WriteQuotedFieldWithKnownQuoteCountInternalAsync(value, quoteCount, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await WriteUnquotedFieldInternalAsync(value, cancellationToken).ConfigureAwait(false);
                }
                break;
        }
    }

    /// <summary>
    /// Writes a field with injection protection applied.
    /// </summary>
#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    private async ValueTask WriteFieldWithInjectionProtectionInternalAsync(ReadOnlyMemory<char> value, CancellationToken cancellationToken)
    {
        var valueSpan = value.Span;

        switch (injectionProtection)
        {
            case CsvInjectionProtection.None:
                // Should not reach here since we check before calling, but handle gracefully
                await WriteFieldValueWithoutInjectionCheckInternalAsync(value, cancellationToken).ConfigureAwait(false);
                break;

            case CsvInjectionProtection.EscapeWithQuote:
                // Prefix with single quote inside quotes: =SUM(A1) becomes "'=SUM(A1)"
                await WriteQuotedFieldWithPrefixInternalAsync('\'', value, cancellationToken).ConfigureAwait(false);
                break;

            case CsvInjectionProtection.EscapeWithTab:
                // Prefix with tab inside quotes: =SUM(A1) becomes "\t=SUM(A1)"
                await WriteQuotedFieldWithPrefixInternalAsync('\t', value, cancellationToken).ConfigureAwait(false);
                break;

            case CsvInjectionProtection.Sanitize:
                // Strip dangerous leading characters. Copy into a pooled buffer (not .ToArray())
                // so the temp memory is recycled across writes.
                var sanitized = quoting.StripDangerousPrefix(valueSpan);
                char[] sanitizedRented = charPool.Rent(sanitized.Length);
                try
                {
                    sanitized.CopyTo(sanitizedRented);
                    await WriteFieldValueWithoutInjectionCheckInternalAsync(
                        sanitizedRented.AsMemory(0, sanitized.Length),
                        cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    charPool.Return(sanitizedRented, clearArray: true);
                }
                break;

            case CsvInjectionProtection.Reject:
                throw new CsvException(
                    CsvErrorCode.InjectionDetected,
                    $"CSV injection detected: field starts with dangerous character '{valueSpan[0]}'");

            default:
                // Handle any future enum values gracefully
                await WriteFieldValueWithoutInjectionCheckInternalAsync(value, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    /// <summary>
    /// Writes a field value without checking for injection (used after sanitization).
    /// </summary>
#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    private async ValueTask WriteFieldValueWithoutInjectionCheckInternalAsync(ReadOnlyMemory<char> value, CancellationToken cancellationToken)
    {
        var valueSpan = value.Span;

        if (valueSpan.IsEmpty)
        {
            if (quoteStyle == QuoteStyle.Always)
            {
                await EnsureCapacityAsync(2, cancellationToken).ConfigureAwait(false);
                charBuffer[charBufferPosition++] = quote;
                charBuffer[charBufferPosition++] = quote;
            }
            return;
        }

        switch (quoteStyle)
        {
            case QuoteStyle.Always:
                await WriteQuotedFieldWithKnownQuoteCountInternalAsync(
                    value,
                    quoting.CountQuotes(valueSpan),
                    cancellationToken).ConfigureAwait(false);
                break;
            case QuoteStyle.Never:
                await WriteUnquotedFieldInternalAsync(value, cancellationToken).ConfigureAwait(false);
                break;
            case QuoteStyle.WhenNeeded:
            default:
                var (needsQuoting, quoteCount) = quoting.AnalyzeFieldForQuoting(valueSpan);
                if (needsQuoting)
                {
                    await WriteQuotedFieldWithKnownQuoteCountInternalAsync(value, quoteCount, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await WriteUnquotedFieldInternalAsync(value, cancellationToken).ConfigureAwait(false);
                }
                break;
        }
    }

    /// <summary>
    /// Writes a quoted field with a prefix character (for injection protection).
    /// Uses sync fast path when buffer has space.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ValueTask WriteQuotedFieldWithPrefixInternalAsync(char prefix, ReadOnlyMemory<char> value, CancellationToken cancellationToken)
    {
        var valueLen = value.Length;
        int quoteCount = quoting.CountQuotes(value.Span);
        // Total size: opening quote + prefix + value + escaped quotes + closing quote
        int requiredSize = 3 + valueLen + quoteCount;

        // Sync fast path - most common case
        if (charBufferPosition + requiredSize <= charBuffer.Length)
        {
            charBuffer[charBufferPosition++] = quote;
            charBuffer[charBufferPosition++] = prefix;

            var valueSpan = value.Span;
            if (quoteCount == 0)
            {
                valueSpan.CopyTo(charBuffer.AsSpan(charBufferPosition));
                charBufferPosition += valueLen;
            }
            else
            {
                WriteWithQuoteEscaping(valueSpan);
            }

            charBuffer[charBufferPosition++] = quote;
            return default;
        }
        return WriteQuotedFieldWithPrefixSlowAsync(prefix, value, quoteCount, cancellationToken);
    }

#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    private async ValueTask WriteQuotedFieldWithPrefixSlowAsync(char prefix, ReadOnlyMemory<char> value, int quoteCount, CancellationToken cancellationToken)
    {
        var valueLen = value.Length;
        int requiredSize = 3 + valueLen + quoteCount;
        await FlushCharBufferAsync(cancellationToken).ConfigureAwait(false);
        if (requiredSize > charBuffer.Length)
        {
            GrowCharBuffer(requiredSize);
        }

        charBuffer[charBufferPosition++] = quote;
        charBuffer[charBufferPosition++] = prefix;

        var valueSpan = value.Span;
        if (quoteCount == 0)
        {
            valueSpan.CopyTo(charBuffer.AsSpan(charBufferPosition));
            charBufferPosition += valueLen;
        }
        else
        {
            WriteWithQuoteEscaping(valueSpan);
        }

        charBuffer[charBufferPosition++] = quote;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ValueTask WriteSpanFormattableFieldAsync<T>(T value, string? format, CancellationToken cancellationToken)
        where T : ISpanFormattable
    {
        ThrowIfDisposed();
        currentRowColumnCount++;
        if (maxColumnCount.HasValue && currentRowColumnCount > maxColumnCount.Value)
        {
            throw new CsvException(CsvErrorCode.TooManyColumnsWritten, $"Row exceeds maximum column count of {maxColumnCount.Value}");
        }

        if (!isFirstFieldInRow)
        {
            if (charBufferPosition + 1 <= charBuffer.Length)
            {
                charBuffer[charBufferPosition++] = delimiter;
            }
            else
            {
                isFirstFieldInRow = false;
                return WriteSpanFormattableFieldSlowAsync(value, format, cancellationToken);
            }
        }
        isFirstFieldInRow = false;

        return WriteSpanFormattableDirectlyAsync(value, format, cancellationToken);
    }

    private async ValueTask WriteSpanFormattableFieldSlowAsync<T>(T value, string? format, CancellationToken cancellationToken)
        where T : ISpanFormattable
    {
        await WriteDelimiterAsync(cancellationToken).ConfigureAwait(false);
        await WriteSpanFormattableDirectlyAsync(value, format, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes an unquoted field. Uses sync fast path when buffer has space.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ValueTask WriteUnquotedFieldInternalAsync(ReadOnlyMemory<char> value, CancellationToken cancellationToken)
    {
        var valueLen = value.Length;
        // Sync fast path - most common case
        if (charBufferPosition + valueLen <= charBuffer.Length)
        {
            value.Span.CopyTo(charBuffer.AsSpan(charBufferPosition));
            charBufferPosition += valueLen;
            return default;
        }
        return WriteUnquotedFieldSlowAsync(value, cancellationToken);
    }

#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    private async ValueTask WriteUnquotedFieldSlowAsync(ReadOnlyMemory<char> value, CancellationToken cancellationToken)
    {
        var valueLen = value.Length;
        await FlushCharBufferAsync(cancellationToken).ConfigureAwait(false);
        if (valueLen > charBuffer.Length)
        {
            GrowCharBuffer(valueLen);
        }
        value.Span.CopyTo(charBuffer.AsSpan(charBufferPosition));
        charBufferPosition += valueLen;
    }

    /// <summary>
    /// Writes a quoted field when we already know the quote count (from single-pass analysis).
    /// Uses sync fast path when buffer has space.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ValueTask WriteQuotedFieldWithKnownQuoteCountInternalAsync(ReadOnlyMemory<char> value, int quoteCount, CancellationToken cancellationToken)
    {
        var valueLen = value.Length;
        // Total size: opening quote + value + escaped quotes + closing quote
        int requiredSize = 2 + valueLen + quoteCount;

        // Sync fast path - most common case
        if (charBufferPosition + requiredSize <= charBuffer.Length)
        {
            charBuffer[charBufferPosition++] = quote;

            var valueSpan = value.Span;
            if (quoteCount == 0)
            {
                // Fast path: no quotes to escape - just copy
                valueSpan.CopyTo(charBuffer.AsSpan(charBufferPosition));
                charBufferPosition += valueLen;
            }
            else
            {
                // Escape quotes by doubling them
                WriteWithQuoteEscaping(valueSpan);
            }

            charBuffer[charBufferPosition++] = quote;
            return default;
        }
        return WriteQuotedFieldSlowAsync(value, quoteCount, cancellationToken);
    }

#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    private async ValueTask WriteQuotedFieldSlowAsync(ReadOnlyMemory<char> value, int quoteCount, CancellationToken cancellationToken)
    {
        var valueLen = value.Length;
        int requiredSize = 2 + valueLen + quoteCount;
        await FlushCharBufferAsync(cancellationToken).ConfigureAwait(false);
        if (requiredSize > charBuffer.Length)
        {
            GrowCharBuffer(requiredSize);
        }

        charBuffer[charBufferPosition++] = quote;

        var valueSpan = value.Span;
        if (quoteCount == 0)
        {
            valueSpan.CopyTo(charBuffer.AsSpan(charBufferPosition));
            charBufferPosition += valueLen;
        }
        else
        {
            WriteWithQuoteEscaping(valueSpan);
        }

        charBuffer[charBufferPosition++] = quote;
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
                charBuffer[charBufferPosition++] = q;
            }
            charBuffer[charBufferPosition++] = c;
        }
    }

#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    private async ValueTask WriteFormattedValueAsync(object? value, CancellationToken cancellationToken)
        => await WriteFormattedValueAsync(value, format: null, cancellationToken).ConfigureAwait(false);

#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    private async ValueTask WriteFormattedValueAsync(object? value, string? format, CancellationToken cancellationToken)
    {
        if (value is null)
        {
            await WriteFieldValueAsync(nullValue.AsMemory(), cancellationToken).ConfigureAwait(false);
            return;
        }

        // Direct type handling for common types - avoids interface dispatch overhead
        // Check concrete types first before falling back to interface
        switch (value)
        {
            case string s:
                await WriteFieldValueAsync(s.AsMemory(), cancellationToken).ConfigureAwait(false);
                return;

            case int i:
                await WriteSpanFormattableDirectlyAsync(i, format ?? numberFormat, cancellationToken).ConfigureAwait(false);
                return;

            case long l:
                await WriteSpanFormattableDirectlyAsync(l, format ?? numberFormat, cancellationToken).ConfigureAwait(false);
                return;

            case double d:
                await WriteSpanFormattableDirectlyAsync(d, format ?? numberFormat, cancellationToken).ConfigureAwait(false);
                return;

            case decimal dec:
                await WriteSpanFormattableDirectlyAsync(dec, format ?? numberFormat, cancellationToken).ConfigureAwait(false);
                return;

            case bool b:
                // Booleans never need quoting - write directly
                await WriteUnquotedFieldInternalAsync((b ? "True" : "False").AsMemory(), cancellationToken).ConfigureAwait(false);
                return;

            case DateTime dt:
                await WriteSpanFormattableDirectlyAsync(dt, format ?? dateTimeFormat, cancellationToken).ConfigureAwait(false);
                return;

            case DateTimeOffset dto:
                await WriteSpanFormattableDirectlyAsync(dto, format ?? dateTimeFormat, cancellationToken).ConfigureAwait(false);
                return;

#if NET6_0_OR_GREATER
            case DateOnly dateOnly:
                await WriteSpanFormattableDirectlyAsync(dateOnly, format ?? dateOnlyFormat, cancellationToken).ConfigureAwait(false);
                return;

            case TimeOnly timeOnly:
                await WriteSpanFormattableDirectlyAsync(timeOnly, format ?? timeOnlyFormat, cancellationToken).ConfigureAwait(false);
                return;
#endif

            case float f:
                await WriteSpanFormattableDirectlyAsync(f, format ?? numberFormat, cancellationToken).ConfigureAwait(false);
                return;

            case byte by:
                await WriteSpanFormattableDirectlyAsync(by, format ?? numberFormat, cancellationToken).ConfigureAwait(false);
                return;

            case short sh:
                await WriteSpanFormattableDirectlyAsync(sh, format ?? numberFormat, cancellationToken).ConfigureAwait(false);
                return;

            case uint ui:
                await WriteSpanFormattableDirectlyAsync(ui, format ?? numberFormat, cancellationToken).ConfigureAwait(false);
                return;

            case ulong ul:
                await WriteSpanFormattableDirectlyAsync(ul, format ?? numberFormat, cancellationToken).ConfigureAwait(false);
                return;

            case ushort ush:
                await WriteSpanFormattableDirectlyAsync(ush, format ?? numberFormat, cancellationToken).ConfigureAwait(false);
                return;

            case sbyte sb:
                await WriteSpanFormattableDirectlyAsync(sb, format ?? numberFormat, cancellationToken).ConfigureAwait(false);
                return;

            case TimeSpan ts:
                await WriteSpanFormattableDirectlyAsync(ts, format ?? timeSpanFormat, cancellationToken).ConfigureAwait(false);
                return;

            case char ch:
                if (!TryWriteCharFast(ch))
                    await WriteCharSlowAsync(ch, cancellationToken).ConfigureAwait(false);
                return;

            case Half half:
                await WriteSpanFormattableDirectlyAsync(half, format ?? numberFormat, cancellationToken).ConfigureAwait(false);
                return;

            case Int128 i128:
                await WriteSpanFormattableDirectlyAsync(i128, format ?? numberFormat, cancellationToken).ConfigureAwait(false);
                return;

            case UInt128 u128:
                await WriteSpanFormattableDirectlyAsync(u128, format ?? numberFormat, cancellationToken).ConfigureAwait(false);
                return;

            case BigInteger bi:
                await WriteSpanFormattableDirectlyAsync(bi, format ?? numberFormat, cancellationToken).ConfigureAwait(false);
                return;

            case Uri uri:
                await WriteFieldValueAsync(uri.OriginalString.AsMemory(), cancellationToken).ConfigureAwait(false);
                return;

            case byte[] bytes:
                await WriteFieldValueAsync(Convert.ToBase64String(bytes).AsMemory(), cancellationToken).ConfigureAwait(false);
                return;

            case Guid g:
                await WriteSpanFormattableDirectlyAsync(g, format, cancellationToken).ConfigureAwait(false);
                return;

            default:
                // Fall through to interface-based handling below
                break;
        }

        // Fallback for other ISpanFormattable types
        if (value is ISpanFormattable spanFormattable)
        {
            await WriteSpanFormattableAsync(spanFormattable, format, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Final fallback for IFormattable
        if (value is IFormattable formattable)
        {
            await WriteFieldValueAsync(formattable.ToString(format, culture).AsMemory(), cancellationToken).ConfigureAwait(false);
            return;
        }

        // Last resort: ToString()
        await WriteFieldValueAsync((value.ToString() ?? string.Empty).AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes an ISpanFormattable value directly to the output buffer when possible.
    /// Optimized to write directly to char buffer when possible, avoiding allocations.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ValueTask WriteSpanFormattableDirectlyAsync<T>(T value, string? format, CancellationToken cancellationToken) where T : ISpanFormattable
    {
        // Fast path: try to format directly into our char buffer if there's enough space
        // Most numeric values are small (< 64 chars), so this is the common case
        int availableSpace = charBuffer.Length - charBufferPosition;
        if (availableSpace >= MAX_STACK_ALLOC_SIZE)
        {
            // Format directly into char buffer - zero allocation
            var targetSpan = charBuffer.AsSpan(charBufferPosition, MAX_STACK_ALLOC_SIZE);
            if (value.TryFormat(targetSpan, out int charsWritten, format, culture))
            {
                if (maxFieldSize.HasValue && charsWritten > maxFieldSize.Value)
                {
                    throw new CsvException(
                        CsvErrorCode.FieldSizeExceeded,
                        $"Field size {charsWritten} exceeds maximum of {maxFieldSize.Value}");
                }

                // For numeric types in WhenNeeded mode without injection protection, most don't need quoting.
                if (quoteStyle != QuoteStyle.Always &&
                    injectionProtection == CsvInjectionProtection.None &&
                    (quoteStyle == QuoteStyle.Never ||
                     !quoting.AnalyzeFieldForQuoting(targetSpan[..charsWritten]).needsQuoting))
                {
                    // Simple unquoted write - just advance buffer position
                    charBufferPosition += charsWritten;
                    return default;
                }

                // Need to go through full field processing for quoting/injection
                return WriteFieldValueFromBufferAsync(charsWritten, cancellationToken);
            }
        }

        // Slow path: use stack allocation then copy
        return WriteSpanFormattableSlowAsync(value, format, cancellationToken);
    }

#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    private async ValueTask WriteSpanFormattableSlowAsync<T>(T value, string? format, CancellationToken cancellationToken) where T : ISpanFormattable
    {
        Span<char> stackBuffer = stackalloc char[MAX_STACK_ALLOC_SIZE];
        if (value.TryFormat(stackBuffer, out int charsWritten, format, culture))
        {
            // Copy out of stackalloc into a pooled buffer that can survive the await.
            char[] rented = charPool.Rent(charsWritten);
            try
            {
                stackBuffer[..charsWritten].CopyTo(rented);
                await WriteFieldValueAsync(rented.AsMemory(0, charsWritten), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                charPool.Return(rented, clearArray: true);
            }
        }
        else
        {
            await WriteFieldValueAsync(value.ToString(format, culture).AsMemory(), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Processes a field value that was formatted directly into the char buffer at charBufferPosition.
    /// </summary>
#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    private async ValueTask WriteFieldValueFromBufferAsync(int length, CancellationToken cancellationToken)
    {
        // The value is already in charBuffer starting at charBufferPosition.
        var valueSpan = charBuffer.AsSpan(charBufferPosition, length);

        // Check field size limit
        if (maxFieldSize.HasValue && length > maxFieldSize.Value)
        {
            throw new CsvException(
                CsvErrorCode.FieldSizeExceeded,
                $"Field size {length} exceeds maximum of {maxFieldSize.Value}");
        }

        if (injectionProtection != CsvInjectionProtection.None &&
            quoting.IsDangerousField(valueSpan))
        {
            char[] rented = charPool.Rent(length);
            try
            {
                valueSpan.CopyTo(rented);
                await WriteFieldWithInjectionProtectionInternalAsync(
                    rented.AsMemory(0, length),
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                charPool.Return(rented, clearArray: true);
            }
            return;
        }

        // For AlwaysQuote or WhenNeeded quoting, copy out to a pooled buffer because quoted writes may shift/flush charBuffer.
        if (quoteStyle == QuoteStyle.Always)
        {
            int quoteCount = quoting.CountQuotes(valueSpan);
            char[] rented = charPool.Rent(length);
            try
            {
                valueSpan.CopyTo(rented);
                await WriteQuotedFieldWithKnownQuoteCountInternalAsync(
                    rented.AsMemory(0, length),
                    quoteCount,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                charPool.Return(rented, clearArray: true);
            }
        }
        else if (quoteStyle == QuoteStyle.WhenNeeded)
        {
            var (needsQuoting, quoteCount) = quoting.AnalyzeFieldForQuoting(valueSpan);
            if (needsQuoting)
            {
                char[] rented = charPool.Rent(length);
                try
                {
                    valueSpan.CopyTo(rented);
                    await WriteQuotedFieldWithKnownQuoteCountInternalAsync(
                        rented.AsMemory(0, length),
                        quoteCount,
                        cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    charPool.Return(rented, clearArray: true);
                }
            }
            else
            {
                // Value is already in buffer, just advance position
                charBufferPosition += length;
            }
        }
        else
        {
            // QuoteStyle.Never: value is already in buffer, just advance position
            charBufferPosition += length;
        }
    }

#if NET6_0_OR_GREATER
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
#endif
    private async ValueTask WriteSpanFormattableAsync(ISpanFormattable value, string? format, CancellationToken cancellationToken)
    {
        // Try stack allocation first for small values
        Span<char> stackBuffer = stackalloc char[MAX_STACK_ALLOC_SIZE];

        if (value.TryFormat(stackBuffer, out int charsWritten, format, culture))
        {
            // Copy out of stackalloc into a pooled buffer that can survive the await.
            char[] rented = charPool.Rent(charsWritten);
            try
            {
                stackBuffer[..charsWritten].CopyTo(rented);
                await WriteFieldValueAsync(rented.AsMemory(0, charsWritten), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                charPool.Return(rented, clearArray: true);
            }
        }
        else
        {
            // Fall back to string allocation for large values
            await WriteFieldValueAsync(value.ToString(format, culture).AsMemory(), cancellationToken).ConfigureAwait(false);
        }
    }

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
                throw new CsvException(
                    CsvErrorCode.OutputSizeExceeded,
                    $"Output size {totalCharsWritten} exceeds maximum of {maxOutputSize.Value}");
            }

            // Convert chars to bytes
            int byteCount = encoding.GetByteCount(charBuffer.AsSpan(0, charBufferPosition));

            // Ensure byte buffer is large enough
            if (byteCount > byteBuffer.Length)
            {
                var oldByteBuffer = byteBuffer;
                byteBuffer = RentByteBuffer(byteCount);
                ReturnByteBuffer(oldByteBuffer);
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
        charBuffer = RentCharBuffer(newSize);
        ReturnCharBuffer(oldBuffer);
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(CsvAsyncStreamWriter));
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
            ReturnCharBuffer(charBuffer);
            ReturnByteBuffer(byteBuffer);
            charBuffer = null!;
            byteBuffer = null!;

            if (!leaveOpen)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private char[] RentCharBuffer(int minimumLength)
    {
        return charPool.Rent(minimumLength);
    }

    private byte[] RentByteBuffer(int minimumLength)
    {
        return bytePool.Rent(minimumLength);
    }

    private void ReturnCharBuffer(char[] toReturn)
    {
        charPool.Return(toReturn, clearArray: false);
    }

    private void ReturnByteBuffer(byte[] toReturn)
    {
        bytePool.Return(toReturn, clearArray: false);
    }
}

