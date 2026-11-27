using System.Buffers;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using HeroParser.Internal;
using HeroParser.SeparatedValues.Core;

namespace HeroParser.SeparatedValues.Writing;

/// <summary>
/// High-performance, low-allocation CSV writer that writes to a <see cref="TextWriter"/>.
/// </summary>
/// <remarks>
/// Uses per-instance pools for buffer management to minimize allocations and avoid shared pool contamination.
/// Call <see cref="Dispose"/> or use a <c>using</c> statement to return pooled buffers.
/// </remarks>
public sealed class CsvStreamWriter : IDisposable, IAsyncDisposable
{
    private readonly ArrayPool<char> charPool;
    private readonly TextWriter writer;
    private readonly CsvWriteOptions options;
    private readonly bool leaveOpen;

    // Cached options for hot path access
    private readonly char delimiter;
    private readonly char quote;
    private readonly string newLine;
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

    // State tracking
    private char[] buffer;
    private int bufferPosition;
    private bool isFirstFieldInRow;
    private bool disposed;
    private long totalCharsWritten;
    private int currentRowColumnCount;

    /// <summary>
    /// Gets the total number of characters written to the underlying writer.
    /// </summary>
    public long CharsWritten => totalCharsWritten + bufferPosition;

    /// <summary>
    /// Gets the CultureInfo used by the writer.
    /// </summary>
    public CultureInfo Culture => options.Culture;

    internal string NullValue => options.NullValue;

    internal string? NumberFormat => options.NumberFormat;

    internal int? MaxRowCount => options.MaxRowCount;

    private const int DEFAULT_BUFFER_SIZE = 16 * 1024; // 16KB
    private const int MAX_STACK_ALLOC_SIZE = 256;

    /// <summary>
    /// Creates a new CSV writer that writes to the specified <see cref="TextWriter"/>.
    /// </summary>
    /// <param name="writer">The underlying writer to write CSV output to.</param>
    /// <param name="options">Writer options; defaults to <see cref="CsvWriteOptions.Default"/>.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, the <paramref name="writer"/> is not disposed.</param>
    public CsvStreamWriter(TextWriter writer, CsvWriteOptions? options = null, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(writer);

        this.writer = writer;
        this.options = options ?? CsvWriteOptions.Default;
        this.options.Validate();
        this.leaveOpen = leaveOpen;

        // Cache frequently accessed options to avoid property access overhead in hot paths
        delimiter = this.options.Delimiter;
        quote = this.options.Quote;
        newLine = this.options.NewLine;
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
        buffer = RentBuffer(DEFAULT_BUFFER_SIZE);
        bufferPosition = 0;
        isFirstFieldInRow = true;
        totalCharsWritten = 0;
        currentRowColumnCount = 0;
    }

    /// <summary>
    /// Writes a single field value, adding delimiter if needed.
    /// </summary>
    /// <param name="value">The field value to write.</param>
    public void WriteField(string? value)
    {
        if (value is null)
        {
            WriteField([]);
        }
        else
        {
            WriteField(value.AsSpan());
        }
    }

    /// <summary>
    /// Writes a single field value from a span, adding delimiter if needed.
    /// </summary>
    /// <param name="value">The field value to write.</param>
    public void WriteField(ReadOnlySpan<char> value)
    {
        ThrowIfDisposed();

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
            WriteDelimiter();
        }
        isFirstFieldInRow = false;

        WriteFieldValue(value);
    }

    internal void WriteQuotedEmptyField()
    {
        ThrowIfDisposed();

        currentRowColumnCount++;
        if (maxColumnCount.HasValue && currentRowColumnCount > maxColumnCount.Value)
        {
            throw new CsvException(
                CsvErrorCode.TooManyColumnsWritten,
                $"Row exceeds maximum column count of {maxColumnCount.Value}");
        }

        if (!isFirstFieldInRow)
        {
            WriteDelimiter();
        }
        isFirstFieldInRow = false;

        EnsureCapacity(2);
        buffer[bufferPosition++] = quote;
        buffer[bufferPosition++] = quote;
    }

    internal void WriteUnquotedEmptyField()
    {
        ThrowIfDisposed();

        currentRowColumnCount++;
        if (maxColumnCount.HasValue && currentRowColumnCount > maxColumnCount.Value)
        {
            throw new CsvException(
                CsvErrorCode.TooManyColumnsWritten,
                $"Row exceeds maximum column count of {maxColumnCount.Value}");
        }

        if (!isFirstFieldInRow)
        {
            WriteDelimiter();
        }
        isFirstFieldInRow = false;
    }

    /// <summary>
    /// Writes a byte array field encoded as Base64, using a reversible <c>base64:</c> prefix when injection protection is active and the payload starts with a dangerous character.
    /// </summary>
    /// <param name="value">The byte array value to write.</param>
    public void WriteBase64Field(byte[]? value)
    {
        if (value is null)
        {
            WriteField(nullValue.AsSpan());
            return;
        }

        ThrowIfDisposed();

        currentRowColumnCount++;
        if (maxColumnCount.HasValue && currentRowColumnCount > maxColumnCount.Value)
        {
            throw new CsvException(
                CsvErrorCode.TooManyColumnsWritten,
                $"Row exceeds maximum column count of {maxColumnCount.Value}");
        }

        if (!isFirstFieldInRow)
        {
            WriteDelimiter();
        }
        isFirstFieldInRow = false;

        WriteBase64FieldValue(value);
    }

    private void WriteBase64FieldValue(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            WriteFieldValue(Utf8SpanParser.EMPTY_BASE64_TOKEN.AsSpan());
            return;
        }

        string b64 = Convert.ToBase64String(bytes);
        if (injectionProtection != CsvInjectionProtection.None && quoting.IsDangerousField(b64.AsSpan()))
            WriteFieldValue((Utf8SpanParser.EMPTY_BASE64_TOKEN + b64).AsSpan());
        else
            WriteFieldValue(b64.AsSpan());
    }

    /// <summary>Writes an Int32 field directly to the CSV output without boxing.</summary>
    public void WriteField(int value) => WriteSpanFormattableField(value, format: null, stackAllocSize: 32);

    /// <summary>Writes an Int64 field directly to the CSV output without boxing.</summary>
    public void WriteField(long value) => WriteSpanFormattableField(value, format: null, stackAllocSize: 32);

    /// <summary>Writes an Int16 field directly to the CSV output without boxing.</summary>
    public void WriteField(short value) => WriteSpanFormattableField(value, format: null, stackAllocSize: 16);

    /// <summary>Writes a UInt16 field directly to the CSV output without boxing.</summary>
    public void WriteField(ushort value) => WriteSpanFormattableField(value, format: null, stackAllocSize: 16);

    /// <summary>Writes a Byte field directly to the CSV output without boxing.</summary>
    public void WriteField(byte value) => WriteSpanFormattableField(value, format: null, stackAllocSize: 16);

    /// <summary>Writes an SByte field directly to the CSV output without boxing.</summary>
    public void WriteField(sbyte value) => WriteSpanFormattableField(value, format: null, stackAllocSize: 16);

    /// <summary>Writes a UInt32 field directly to the CSV output without boxing.</summary>
    public void WriteField(uint value) => WriteSpanFormattableField(value, format: null, stackAllocSize: 32);

    /// <summary>Writes a UInt64 field directly to the CSV output without boxing.</summary>
    public void WriteField(ulong value) => WriteSpanFormattableField(value, format: null, stackAllocSize: 32);

    /// <summary>Writes a Double field directly to the CSV output without boxing.</summary>
    public void WriteField(double value) => WriteSpanFormattableField(value, format: null, stackAllocSize: 64);

    /// <summary>Writes a Float field directly to the CSV output without boxing.</summary>
    public void WriteField(float value) => WriteSpanFormattableField(value, format: null, stackAllocSize: 32);

    /// <summary>Writes a Decimal field directly to the CSV output without boxing.</summary>
    public void WriteField(decimal value) => WriteSpanFormattableField(value, format: null, stackAllocSize: 64);

    /// <summary>Writes a Boolean field directly to the CSV output.</summary>
    public void WriteField(bool value) => WriteField(value ? "True".AsSpan() : "False".AsSpan());

    /// <summary>Writes a DateTime field directly to the CSV output without boxing.</summary>
    public void WriteField(DateTime value) => WriteSpanFormattableField(value, format: dateTimeFormat, stackAllocSize: 64);

    /// <summary>Writes a DateTimeOffset field directly to the CSV output without boxing.</summary>
    public void WriteField(DateTimeOffset value) => WriteSpanFormattableField(value, format: dateTimeFormat, stackAllocSize: 64);

    /// <summary>Writes a DateOnly field directly to the CSV output without boxing.</summary>
    public void WriteField(DateOnly value) => WriteSpanFormattableField(value, format: dateOnlyFormat, stackAllocSize: 32);

    /// <summary>Writes a TimeOnly field directly to the CSV output without boxing.</summary>
    public void WriteField(TimeOnly value) => WriteSpanFormattableField(value, format: timeOnlyFormat, stackAllocSize: 32);

    /// <summary>Writes a TimeSpan field directly to the CSV output without boxing.</summary>
    public void WriteField(TimeSpan value) => WriteSpanFormattableField(value, format: timeSpanFormat, stackAllocSize: 64);

    /// <summary>Writes a Char field directly to the CSV output without boxing.</summary>
    public void WriteField(char value)
    {
        ThrowIfDisposed();

        currentRowColumnCount++;
        if (maxColumnCount.HasValue && currentRowColumnCount > maxColumnCount.Value)
        {
            throw new CsvException(
                CsvErrorCode.TooManyColumnsWritten,
                $"Row exceeds maximum column count of {maxColumnCount.Value}");
        }

        if (!isFirstFieldInRow)
            WriteDelimiter();
        isFirstFieldInRow = false;

        WriteCharFieldValue(value);
    }

    private void WriteCharFieldValue(char value)
    {
        Span<char> single = [value];
        if (injectionProtection != CsvInjectionProtection.None && quoting.IsDangerousField(single))
        {
            Span<char> prefixed = ['c', 'h', 'a', 'r', ':', value];
            WriteFieldValue(prefixed);
        }
        else
        {
            WriteFieldValue(single);
        }
    }

    /// <summary>Writes a Half field directly to the CSV output without boxing.</summary>
    public void WriteField(Half value) => WriteSpanFormattableField(value, format: numberFormat, stackAllocSize: 32);

    /// <summary>Writes an Int128 field directly to the CSV output without boxing.</summary>
    public void WriteField(Int128 value) => WriteSpanFormattableField(value, format: numberFormat, stackAllocSize: 64);

    /// <summary>Writes a UInt128 field directly to the CSV output without boxing.</summary>
    public void WriteField(UInt128 value) => WriteSpanFormattableField(value, format: numberFormat, stackAllocSize: 64);

    /// <summary>Writes a Guid field directly to the CSV output without boxing.</summary>
    public void WriteField(Guid value) => WriteSpanFormattableField(value, format: null, stackAllocSize: 40);

    /// <summary>
    /// Writes an <see cref="ISpanFormattable"/> field directly to the CSV output with an explicit format string without boxing.
    /// </summary>
    /// <typeparam name="T">The span-formattable value type.</typeparam>
    /// <param name="value">The value to write.</param>
    /// <param name="format">Optional format string; when <see langword="null"/>, default formatting is used.</param>
    public void WriteField<T>(T value, string? format) where T : ISpanFormattable
        => WriteSpanFormattableField(value, format, stackAllocSize: 128);

    internal void WriteFormattedField(object? value)
    {
        ThrowIfDisposed();
        currentRowColumnCount++;
        if (maxColumnCount.HasValue && currentRowColumnCount > maxColumnCount.Value)
        {
            throw new CsvException(
                CsvErrorCode.TooManyColumnsWritten,
                $"Row exceeds maximum column count of {maxColumnCount.Value}");
        }

        if (!isFirstFieldInRow)
        {
            WriteDelimiter();
        }
        isFirstFieldInRow = false;

        WriteFormattedValue(value);
    }

    /// <summary>
    /// Writes a row with multiple string values.
    /// </summary>
    /// <param name="values">The field values for the row.</param>
    public void WriteRow(params string?[] values)
    {
        ThrowIfDisposed();

        // Check column count limit
        if (maxColumnCount.HasValue && values.Length > maxColumnCount.Value)
        {
            throw new CsvException(
                CsvErrorCode.TooManyColumnsWritten,
                $"Row has {values.Length} columns, exceeds maximum of {maxColumnCount.Value}");
        }

        if (!maxFieldSize.HasValue &&
            injectionProtection == CsvInjectionProtection.None &&
            quoteStyle == QuoteStyle.WhenNeeded)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) WriteDelimiter();
                var value = values[i].AsSpan();
                if (!value.IsEmpty)
                {
                    var (needsQuoting, quoteCount) = quoting.AnalyzeFieldForQuoting(value);
                    if (needsQuoting)
                    {
                        WriteQuotedFieldWithKnownQuoteCount(value, quoteCount);
                    }
                    else
                    {
                        WriteUnquotedField(value);
                    }
                }
            }
        }
        else
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) WriteDelimiter();
                WriteFieldValue(values[i].AsSpan());
            }
        }
        isFirstFieldInRow = true;
        currentRowColumnCount = 0;
        WriteNewLine();
    }

    /// <summary>
    /// Writes a row with multiple values, formatting each according to options.
    /// </summary>
    /// <param name="values">The field values for the row.</param>
    public void WriteRow(params object?[] values)
    {
        ThrowIfDisposed();

        // Check column count limit
        if (maxColumnCount.HasValue && values.Length > maxColumnCount.Value)
        {
            throw new CsvException(
                CsvErrorCode.TooManyColumnsWritten,
                $"Row has {values.Length} columns, exceeds maximum of {maxColumnCount.Value}");
        }

        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) WriteDelimiter();
            WriteFormattedValue(values[i]);
        }
        isFirstFieldInRow = true;
        currentRowColumnCount = 0;
        WriteNewLine();
    }

    /// <summary>
    /// Writes a row with values from a span, formatting each according to options.
    /// </summary>
    /// <param name="values">The field values for the row.</param>
    public void WriteRow(ReadOnlySpan<object?> values)
    {
        ThrowIfDisposed();

        // Check column count limit
        if (maxColumnCount.HasValue && values.Length > maxColumnCount.Value)
        {
            throw new CsvException(
                CsvErrorCode.TooManyColumnsWritten,
                $"Row has {values.Length} columns, exceeds maximum of {maxColumnCount.Value}");
        }

        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) WriteDelimiter();
            WriteFormattedValue(values[i]);
        }
        isFirstFieldInRow = true;
        currentRowColumnCount = 0;
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

        // Check column count limit
        if (maxColumnCount.HasValue && values.Length > maxColumnCount.Value)
        {
            throw new CsvException(
                CsvErrorCode.TooManyColumnsWritten,
                $"Row has {values.Length} columns, exceeds maximum of {maxColumnCount.Value}");
        }

        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) WriteDelimiter();
            var format = i < formats.Length ? formats[i] : null;
            WriteFormattedValueWithFormat(values[i], format);
        }
        isFirstFieldInRow = true;
        currentRowColumnCount = 0;
        WriteNewLine();
    }

    /// <summary>
    /// Ends the current row by writing a newline.
    /// </summary>
    public void EndRow()
    {
        ThrowIfDisposed();
        isFirstFieldInRow = true;
        currentRowColumnCount = 0;
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteDelimiter()
    {
        EnsureCapacity(1);
        buffer[bufferPosition++] = delimiter;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteNewLine()
    {
        int len = newLine.Length;
        EnsureCapacity(len);
        if (len == 1)
        {
            buffer[bufferPosition++] = newLine[0];
        }
        else if (len == 2)
        {
            int pos = bufferPosition;
            buffer[pos] = newLine[0];
            buffer[pos + 1] = newLine[1];
            bufferPosition = pos + 2;
        }
        else
        {
            newLine.AsSpan().CopyTo(buffer.AsSpan(bufferPosition));
            bufferPosition += len;
        }
    }

    private void WriteFieldValue(ReadOnlySpan<char> value)
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
            // Empty field - write quotes if AlwaysQuote, otherwise nothing
            if (quoteStyle == QuoteStyle.Always)
            {
                EnsureCapacity(2);
                buffer[bufferPosition++] = quote;
                buffer[bufferPosition++] = quote;
            }
            return;
        }

        // Check for injection and apply protection if needed
        if (injectionProtection != CsvInjectionProtection.None &&
            quoting.IsDangerousField(value))
        {
            WriteFieldWithInjectionProtection(value);
            return;
        }

        // Handle quoting based on style
        switch (quoteStyle)
        {
            case QuoteStyle.Always:
                WriteQuotedFieldWithKnownQuoteCount(value, quoting.CountQuotes(value));
                break;
            case QuoteStyle.Never:
                WriteUnquotedField(value);
                break;
            case QuoteStyle.WhenNeeded:
            default:
                // Single pass to check if quoting needed AND count quotes
                var (needsQuoting, quoteCount) = quoting.AnalyzeFieldForQuoting(value);
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

    /// <summary>
    /// Writes a field with injection protection applied.
    /// </summary>
    private void WriteFieldWithInjectionProtection(ReadOnlySpan<char> value)
    {
        switch (injectionProtection)
        {
            case CsvInjectionProtection.None:
                // Should not reach here since we check before calling, but handle gracefully
                WriteFieldValueWithoutInjectionCheck(value);
                break;

            case CsvInjectionProtection.EscapeWithQuote:
                // Prefix with single quote inside quotes: =SUM(A1) becomes "'=SUM(A1)"
                WriteQuotedFieldWithPrefix('\'', value);
                break;

            case CsvInjectionProtection.EscapeWithTab:
                // Prefix with tab inside quotes: =SUM(A1) becomes "\t=SUM(A1)"
                WriteQuotedFieldWithPrefix('\t', value);
                break;

            case CsvInjectionProtection.Sanitize:
                // Strip dangerous leading characters
                var sanitized = quoting.StripDangerousPrefix(value);
                WriteFieldValueWithoutInjectionCheck(sanitized);
                break;

            case CsvInjectionProtection.Reject:
                throw new CsvException(
                    CsvErrorCode.InjectionDetected,
                    $"CSV injection detected: field starts with dangerous character '{value[0]}'");

            default:
                // Handle any future enum values gracefully
                WriteFieldValueWithoutInjectionCheck(value);
                break;
        }
    }

    /// <summary>
    /// Writes a field value without checking for injection (used after sanitization).
    /// </summary>
    private void WriteFieldValueWithoutInjectionCheck(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
        {
            if (quoteStyle == QuoteStyle.Always)
            {
                EnsureCapacity(2);
                buffer[bufferPosition++] = quote;
                buffer[bufferPosition++] = quote;
            }
            return;
        }

        switch (quoteStyle)
        {
            case QuoteStyle.Always:
                WriteQuotedFieldWithKnownQuoteCount(value, quoting.CountQuotes(value));
                break;
            case QuoteStyle.Never:
                WriteUnquotedField(value);
                break;
            case QuoteStyle.WhenNeeded:
            default:
                var (needsQuoting, quoteCount) = quoting.AnalyzeFieldForQuoting(value);
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

    /// <summary>
    /// Writes a quoted field with a prefix character (for injection protection).
    /// </summary>
    private void WriteQuotedFieldWithPrefix(char prefix, ReadOnlySpan<char> value)
    {
        int quoteCount = quoting.CountQuotes(value);
        // Total size: opening quote + prefix + value + escaped quotes + closing quote
        int requiredSize = 3 + value.Length + quoteCount;
        EnsureCapacity(requiredSize);

        buffer[bufferPosition++] = quote;
        buffer[bufferPosition++] = prefix;

        if (quoteCount == 0)
        {
            value.CopyTo(buffer.AsSpan(bufferPosition));
            bufferPosition += value.Length;
        }
        else
        {
            WriteWithQuoteEscaping(value);
        }

        buffer[bufferPosition++] = quote;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteSpanFormattableField<T>(T value, string? format, int stackAllocSize = 32)
        where T : ISpanFormattable
    {
        ThrowIfDisposed();
        currentRowColumnCount++;
        if (maxColumnCount.HasValue && currentRowColumnCount > maxColumnCount.Value)
        {
            throw new CsvException(
                CsvErrorCode.TooManyColumnsWritten,
                $"Row exceeds maximum column count of {maxColumnCount.Value}");
        }

        if (!isFirstFieldInRow) WriteDelimiter();
        isFirstFieldInRow = false;

        Span<char> stackBuffer = stackalloc char[stackAllocSize];
        if (value.TryFormat(stackBuffer, out int charsWritten, format, culture))
        {
            WriteFieldValue(stackBuffer[..charsWritten]);
        }
        else
        {
            WriteFieldValue(value.ToString(format, culture).AsSpan());
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

            case ushort ush:
                WriteSpanFormattableDirectly(ush, format ?? numberFormat);
                return;

            case sbyte sb:
                WriteSpanFormattableDirectly(sb, format ?? numberFormat);
                return;

            case TimeSpan ts:
                WriteSpanFormattableDirectly(ts, format ?? timeSpanFormat);
                return;

            case char ch:
                WriteCharFieldValue(ch);
                return;

            case Half half:
                WriteSpanFormattableDirectly(half, format ?? numberFormat);
                return;

            case Int128 i128:
                WriteSpanFormattableDirectly(i128, format ?? numberFormat);
                return;

            case UInt128 u128:
                WriteSpanFormattableDirectly(u128, format ?? numberFormat);
                return;

            case BigInteger bi:
                WriteSpanFormattableDirectly(bi, format ?? numberFormat);
                return;

            case Uri uri:
                WriteFieldValue(uri.OriginalString.AsSpan());
                return;

            case byte[] bytes:
                WriteBase64FieldValue(bytes);
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
            // Track total output size for DoS protection
            totalCharsWritten += bufferPosition;
            if (maxOutputSize.HasValue && totalCharsWritten > maxOutputSize.Value)
            {
                throw new CsvException(
                    CsvErrorCode.OutputSizeExceeded,
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
            // Track total output size for DoS protection
            totalCharsWritten += bufferPosition;
            if (maxOutputSize.HasValue && totalCharsWritten > maxOutputSize.Value)
            {
                throw new CsvException(
                    CsvErrorCode.OutputSizeExceeded,
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

    private char[] RentBuffer(int minimumLength)
    {
        return charPool.Rent(minimumLength);
    }

    private void ReturnBuffer(char[] toReturn)
    {
        charPool.Return(toReturn, clearArray: false);
    }
}

