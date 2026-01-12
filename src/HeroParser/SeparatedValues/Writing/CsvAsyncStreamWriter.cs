using System.Buffers;
using System.Globalization;
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
    private readonly string? numberFormat;
#if NET6_0_OR_GREATER
    private readonly string? dateOnlyFormat;
    private readonly string? timeOnlyFormat;
#endif

    // Security and DoS protection
    private readonly CsvInjectionProtection injectionProtection;
    private readonly IReadOnlySet<char>? additionalDangerousChars;
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

    // Default dangerous characters for CSV injection (always dangerous)
    // Note: '-' and '+' are handled separately with smart detection
    private static ReadOnlySpan<char> AlwaysDangerousChars => ['=', '@', '\t', '\r'];

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
        numberFormat = this.options.NumberFormat;
#if NET6_0_OR_GREATER
        dateOnlyFormat = this.options.DateOnlyFormat;
        timeOnlyFormat = this.options.TimeOnlyFormat;
#endif

        // Security and DoS protection options
        injectionProtection = this.options.InjectionProtection;
        additionalDangerousChars = this.options.AdditionalDangerousChars;
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
        await WriteFieldAsync(value.AsMemory(), cancellationToken).ConfigureAwait(false);
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

        // Calculate maximum possible size for this row:
        // For each field: max is 2*length (if all quotes doubled) + 2 (surrounding quotes)
        // Plus delimiters: values.Length - 1
        // Plus newline: newLineMemory.Length
        int estimatedMaxSize = newLineMemory.Length + (values.Length > 0 ? values.Length - 1 : 0);

        for (int i = 0; i < values.Length; i++)
        {
            var v = values[i];
            if (v is not null)
            {
                // Worst case: every char is a quote that needs escaping, plus surrounding quotes
                estimatedMaxSize += v.Length * 2 + 2;
            }
            else if (quoteStyle == QuoteStyle.Always)
            {
                estimatedMaxSize += 2; // empty quotes
            }
        }

        // Check if we have enough buffer space
        if (charBufferPosition + estimatedMaxSize > charBuffer.Length)
        {
            return false;
        }

        // Write the entire row synchronously
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0)
            {
                charBuffer[charBufferPosition++] = delimiter;
            }

            var value = values[i];
            if (!WriteFieldValueSync(value.AsSpan()))
            {
                // This shouldn't happen if our estimate was correct, but handle gracefully
                charBufferPosition = savedPosition;
                return false;
            }
        }

        // Write newline
        newLineMemory.Span.CopyTo(charBuffer.AsSpan(charBufferPosition));
        charBufferPosition += newLineMemory.Length;

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
        if (injectionProtection != CsvInjectionProtection.None && IsDangerousField(value))
        {
            // Injection protection requires more complex handling, fall back to async
            return false;
        }

        // Handle quoting based on style
        switch (quoteStyle)
        {
            case QuoteStyle.Always:
                {
                    int alwaysQuoteCount = CsvWriterQuoting.CountQuotes(value, quote);
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
                var (needsQuoting, quoteCount) = CsvWriterQuoting.AnalyzeFieldForQuoting(value, delimiter, quote);
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
        int quoteCount = CsvWriterQuoting.CountQuotes(value, quote);
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
    /// Tries to write an entire object row synchronously. Returns true if successful.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryWriteObjectRowSync(object?[] values)
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

            if (!WriteFormattedValueSync(values[i]))
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
                return WriteSpanFormattableSync(i, numberFormat);

            case long l:
                return WriteSpanFormattableSync(l, numberFormat);

            case double d:
                return WriteSpanFormattableSync(d, numberFormat);

            case decimal dec:
                return WriteSpanFormattableSync(dec, numberFormat);

            case bool b:
                // Booleans never need quoting - write directly
                ReadOnlySpan<char> boolStr = b ? "True" : "False";
                if (charBufferPosition + boolStr.Length > charBuffer.Length) return false;
                boolStr.CopyTo(charBuffer.AsSpan(charBufferPosition));
                charBufferPosition += boolStr.Length;
                return true;

            case DateTime dt:
                return WriteSpanFormattableSync(dt, dateTimeFormat);

            case DateTimeOffset dto:
                return WriteSpanFormattableSync(dto, dateTimeFormat);

#if NET6_0_OR_GREATER
            case DateOnly dateOnly:
                return WriteSpanFormattableSync(dateOnly, dateOnlyFormat);

            case TimeOnly timeOnly:
                return WriteSpanFormattableSync(timeOnly, timeOnlyFormat);
#endif

            case float f:
                return WriteSpanFormattableSync(f, numberFormat);

            case byte by:
                return WriteSpanFormattableSync(by, numberFormat);

            case short sh:
                return WriteSpanFormattableSync(sh, numberFormat);

            case uint ui:
                return WriteSpanFormattableSync(ui, numberFormat);

            case ulong ul:
                return WriteSpanFormattableSync(ul, numberFormat);

            case Guid g:
                return WriteSpanFormattableSync(g, null);

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
            if (IsDangerousField(valueSpan))
            {
                return false; // Fall back to async for injection handling
            }
        }

        if (quoteStyle == QuoteStyle.Always)
        {
            // Need to quote - shift content and add quotes
            var valueSpan = charBuffer.AsSpan(charBufferPosition, charsWritten);
            int quoteCount = CsvWriterQuoting.CountQuotes(valueSpan, quote);
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
            var (needsQuoting, quoteCount) = CsvWriterQuoting.AnalyzeFieldForQuoting(valueSpan, delimiter, quote);

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
    {
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) await WriteDelimiterAsync(cancellationToken).ConfigureAwait(false);
            await WriteFormattedValueAsync(values[i], cancellationToken).ConfigureAwait(false);
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

    #region Internal Writing Methods

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
        if (injectionProtection != CsvInjectionProtection.None && IsDangerousField(valueSpan))
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
                    CsvWriterQuoting.CountQuotes(valueSpan, quote),
                    cancellationToken).ConfigureAwait(false);
                break;
            case QuoteStyle.Never:
                await WriteUnquotedFieldInternalAsync(value, cancellationToken).ConfigureAwait(false);
                break;
            case QuoteStyle.WhenNeeded:
            default:
                // Single pass to check if quoting needed AND count quotes
                var (needsQuoting, quoteCount) = CsvWriterQuoting.AnalyzeFieldForQuoting(valueSpan, delimiter, quote);
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
    /// Checks if a field starts with a dangerous character that could trigger formula execution.
    /// Uses switch statement for O(1) lookup of common dangerous characters.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsDangerousField(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty) return false;

        char first = value[0];

        switch (first)
        {
            // Always-dangerous characters
            case '=':
            case '@':
            case '\t':
            case '\r':
                return true;

            // Smart detection for '-' and '+':
            // - If followed by digit or '.', it's likely a number/phone number (safe)
            // - If followed by letter or '(', it's likely a formula (dangerous)
            case '-':
            case '+':
                // Single character is safe
                if (value.Length == 1) return false;
                char second = value[1];
                // Safe patterns: -123, +1-555, -.5, +.5
                // Use uint comparison to check digit range in one instruction
                return !((uint)(second - '0') <= 9 || second == '.');

            default:
                // Check additional custom dangerous characters
                return additionalDangerousChars is not null && additionalDangerousChars.Contains(first);
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
                // Strip dangerous leading characters
                var sanitized = StripDangerousPrefix(valueSpan);
                // Convert span back to memory
                await WriteFieldValueWithoutInjectionCheckInternalAsync(sanitized.ToArray().AsMemory(), cancellationToken).ConfigureAwait(false);
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
                    CsvWriterQuoting.CountQuotes(valueSpan, quote),
                    cancellationToken).ConfigureAwait(false);
                break;
            case QuoteStyle.Never:
                await WriteUnquotedFieldInternalAsync(value, cancellationToken).ConfigureAwait(false);
                break;
            case QuoteStyle.WhenNeeded:
            default:
                var (needsQuoting, quoteCount) = CsvWriterQuoting.AnalyzeFieldForQuoting(valueSpan, delimiter, quote);
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
        int quoteCount = CsvWriterQuoting.CountQuotes(value.Span, quote);
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

    /// <summary>
    /// Strips dangerous leading characters from a value.
    /// </summary>
    private ReadOnlySpan<char> StripDangerousPrefix(ReadOnlySpan<char> value)
    {
        int start = 0;
        while (start < value.Length)
        {
            char c = value[start];
            // Strip always-dangerous chars, plus '-' and '+' that aren't followed by digit/decimal
            bool isDangerous = AlwaysDangerousChars.Contains(c) ||
                               (additionalDangerousChars is not null && additionalDangerousChars.Contains(c));

            // Smart handling for '-' and '+'
            if (!isDangerous && (c == '-' || c == '+'))
            {
                // Check if followed by digit or decimal (safe pattern)
                if (start + 1 < value.Length)
                {
                    char next = value[start + 1];
                    isDangerous = !char.IsDigit(next) && next != '.';
                }
            }

            if (!isDangerous)
            {
                break;
            }
            start++;
        }
        return value[start..];
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
                await WriteSpanFormattableDirectlyAsync(i, numberFormat, cancellationToken).ConfigureAwait(false);
                return;

            case long l:
                await WriteSpanFormattableDirectlyAsync(l, numberFormat, cancellationToken).ConfigureAwait(false);
                return;

            case double d:
                await WriteSpanFormattableDirectlyAsync(d, numberFormat, cancellationToken).ConfigureAwait(false);
                return;

            case decimal dec:
                await WriteSpanFormattableDirectlyAsync(dec, numberFormat, cancellationToken).ConfigureAwait(false);
                return;

            case bool b:
                // Booleans never need quoting - write directly
                await WriteUnquotedFieldInternalAsync((b ? "True" : "False").AsMemory(), cancellationToken).ConfigureAwait(false);
                return;

            case DateTime dt:
                await WriteSpanFormattableDirectlyAsync(dt, dateTimeFormat, cancellationToken).ConfigureAwait(false);
                return;

            case DateTimeOffset dto:
                await WriteSpanFormattableDirectlyAsync(dto, dateTimeFormat, cancellationToken).ConfigureAwait(false);
                return;

#if NET6_0_OR_GREATER
            case DateOnly dateOnly:
                await WriteSpanFormattableDirectlyAsync(dateOnly, dateOnlyFormat, cancellationToken).ConfigureAwait(false);
                return;

            case TimeOnly timeOnly:
                await WriteSpanFormattableDirectlyAsync(timeOnly, timeOnlyFormat, cancellationToken).ConfigureAwait(false);
                return;
#endif

            case float f:
                await WriteSpanFormattableDirectlyAsync(f, numberFormat, cancellationToken).ConfigureAwait(false);
                return;

            case byte by:
                await WriteSpanFormattableDirectlyAsync(by, numberFormat, cancellationToken).ConfigureAwait(false);
                return;

            case short sh:
                await WriteSpanFormattableDirectlyAsync(sh, numberFormat, cancellationToken).ConfigureAwait(false);
                return;

            case uint ui:
                await WriteSpanFormattableDirectlyAsync(ui, numberFormat, cancellationToken).ConfigureAwait(false);
                return;

            case ulong ul:
                await WriteSpanFormattableDirectlyAsync(ul, numberFormat, cancellationToken).ConfigureAwait(false);
                return;

            case Guid g:
                await WriteSpanFormattableDirectlyAsync(g, null, cancellationToken).ConfigureAwait(false);
                return;

            default:
                // Fall through to interface-based handling below
                break;
        }

        // Fallback for other ISpanFormattable types
        if (value is ISpanFormattable spanFormattable)
        {
            await WriteSpanFormattableAsync(spanFormattable, null, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Final fallback for IFormattable
        if (value is IFormattable formattable)
        {
            await WriteFieldValueAsync(formattable.ToString(null, culture).AsMemory(), cancellationToken).ConfigureAwait(false);
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
                // For numeric types in WhenNeeded mode, most don't need quoting
                // Check if we can skip the full WriteFieldValueAsync overhead
                if (quoteStyle != QuoteStyle.Always &&
                    injectionProtection == CsvInjectionProtection.None)
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
            await WriteFieldValueAsync(stackBuffer[..charsWritten].ToArray().AsMemory(), cancellationToken).ConfigureAwait(false);
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
        // The value is already in charBuffer starting at charBufferPosition
        // We need to check if it needs quoting and reprocess if so
        var valueSpan = charBuffer.AsSpan(charBufferPosition, length);

        // Check field size limit
        if (maxFieldSize.HasValue && length > maxFieldSize.Value)
        {
            throw new CsvException(
                CsvErrorCode.FieldSizeExceeded,
                $"Field size {length} exceeds maximum of {maxFieldSize.Value}");
        }

        // For AlwaysQuote or if quoting needed, we need to copy and rewrite
        if (quoteStyle == QuoteStyle.Always)
        {
            var temp = valueSpan.ToArray();
            await WriteQuotedFieldWithKnownQuoteCountInternalAsync(
                temp.AsMemory(),
                CsvWriterQuoting.CountQuotes(valueSpan, quote),
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var (needsQuoting, quoteCount) = CsvWriterQuoting.AnalyzeFieldForQuoting(valueSpan, delimiter, quote);
            if (needsQuoting)
            {
                var temp = valueSpan.ToArray();
                await WriteQuotedFieldWithKnownQuoteCountInternalAsync(temp.AsMemory(), quoteCount, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Value is already in buffer, just advance position
                charBufferPosition += length;
            }
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
            await WriteFieldValueAsync(stackBuffer[..charsWritten].ToArray().AsMemory(), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Fall back to string allocation for large values
            await WriteFieldValueAsync(value.ToString(format, culture).AsMemory(), cancellationToken).ConfigureAwait(false);
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

    #endregion

    #region Disposal

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

    #endregion

    #region Pool Helpers

    private char[] RentCharBuffer(int minimumLength)
    {
        var rented = charPool.Rent(minimumLength);
        Array.Clear(rented);
        return rented;
    }

    private byte[] RentByteBuffer(int minimumLength)
    {
        var rented = bytePool.Rent(minimumLength);
        // byte buffers are fully written before use; no clear required
        return rented;
    }

    private void ReturnCharBuffer(char[] toReturn)
    {
        charPool.Return(toReturn, clearArray: true);
    }

    private void ReturnByteBuffer(byte[] toReturn)
    {
        bytePool.Return(toReturn, clearArray: true);
    }

    #endregion
}

