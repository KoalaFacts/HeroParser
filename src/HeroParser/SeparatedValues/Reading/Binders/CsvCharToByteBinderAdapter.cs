using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using HeroParser.SeparatedValues.Reading.Rows;

namespace HeroParser.SeparatedValues.Reading.Binders;

/// <summary>
/// Adapter that wraps a byte binder to provide char-based binding.
/// Converts char rows to UTF-8 bytes internally for backward compatibility.
/// </summary>
/// <typeparam name="T">The record type.</typeparam>
/// <remarks>
/// <para>
/// This adapter enables backward compatibility for char-based APIs while
/// using the optimized byte binder internally. For high-performance scenarios,
/// use byte-based APIs directly (<c>FromFile</c>, <c>FromStream</c>, or <c>FromText</c>
/// with the <c>out byte[]</c> overload).
/// </para>
/// <para>
/// This adapter exists for backward compatibility with existing char-based code.
/// New code should prefer the UTF-8 byte APIs for SIMD-accelerated parsing.
/// </para>
/// </remarks>
internal sealed class CsvCharToByteBinderAdapter<T> : ICsvBinder<char, T> where T : new()
{
    /// <summary>
    /// Maximum columns to use stackalloc for column byte lengths.
    /// Beyond this, we allocate an array (rare case for very wide CSVs).
    /// </summary>
    private const int MAX_STACK_ALLOC_COLUMNS = 128;

    private readonly ICsvBinder<byte, T> byteBinder;
    private readonly byte delimiterByte;

    public CsvCharToByteBinderAdapter(ICsvBinder<byte, T> byteBinder, char delimiter)
    {
        this.byteBinder = byteBinder ?? throw new ArgumentNullException(nameof(byteBinder));

        // Delimiter must be ASCII for proper byte conversion
        if (delimiter > 127)
            throw new ArgumentException($"Delimiter '{delimiter}' is not ASCII", nameof(delimiter));

        delimiterByte = (byte)delimiter;
    }

    /// <inheritdoc/>
    public bool NeedsHeaderResolution => byteBinder.NeedsHeaderResolution;

    /// <inheritdoc/>
    public void BindHeader(CsvRow<char> headerRow, int rowNumber)
    {
        using var conversion = ConvertToByteRow(headerRow, delimiterByte);
        byteBinder.BindHeader(conversion.Row, rowNumber);
    }

    /// <inheritdoc/>
    public bool TryBind(CsvRow<char> row, int rowNumber, out T result)
    {
        using var conversion = ConvertToByteRow(row, delimiterByte);
        return byteBinder.TryBind(conversion.Row, rowNumber, out result);
    }

    /// <inheritdoc/>
    public bool BindInto(ref T instance, CsvRow<char> row, int rowNumber)
    {
        using var conversion = ConvertToByteRow(row, delimiterByte);
        return byteBinder.BindInto(ref instance, conversion.Row, rowNumber);
    }

    /// <summary>
    /// Converts a char row to a byte row by encoding to UTF-8.
    /// Uses ArrayPool for the byte buffer to reduce allocations.
    /// </summary>
    /// <param name="charRow">The character-based CSV row to convert.</param>
    /// <param name="delimiter">The delimiter byte to insert between columns.</param>
    /// <remarks>
    /// <para>
    /// CsvRow uses "Ends-only" storage format where:
    /// - columnEnds[0] = -1 (sentinel)
    /// - columnEnds[i+1] = position of delimiter after column i (or buffer end for last column)
    /// - Column i: start = columnEnds[i]+1, length = columnEnds[i+1] - columnEnds[i] - 1
    /// </para>
    /// <para>
    /// The buffer must include delimiters between columns for correct slicing.
    /// </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static PooledByteRowConversion ConvertToByteRow(CsvRow<char> charRow, byte delimiter)
    {
        var columnCount = charRow.ColumnCount;

        if (columnCount == 0)
        {
            return new PooledByteRowConversion(
                new CsvRow<byte>(
                    [],
                    [-1, 0],
                    0,
                    charRow.LineNumber,
                    charRow.SourceLineNumber,
                    trimFields: false),
                null);
        }

        // Use stackalloc for column byte lengths (typically < 128 columns)
        Span<int> columnByteLengths = columnCount <= MAX_STACK_ALLOC_COLUMNS
            ? stackalloc int[columnCount]
            : new int[columnCount];

        // First pass: calculate byte lengths directly from char spans (no string allocation)
        int totalBytes = 0;
        for (int i = 0; i < columnCount; i++)
        {
            var charSpan = charRow[i].Span;
            var byteCount = Encoding.UTF8.GetByteCount(charSpan);
            columnByteLengths[i] = byteCount;
            totalBytes += byteCount;
        }

        // Add space for delimiters between columns
        if (columnCount > 1)
            totalBytes += columnCount - 1;

        // Rent buffer from pool (reduces GC pressure significantly)
        var rentedBuffer = ArrayPool<byte>.Shared.Rent(totalBytes);
        var buffer = rentedBuffer.AsSpan(0, totalBytes);

        // columnEnds must be a regular array (CsvRow stores reference)
        var columnEnds = new int[columnCount + 1];
        columnEnds[0] = -1; // Sentinel

        // Second pass: encode directly from char spans to buffer
        int offset = 0;
        for (int i = 0; i < columnCount; i++)
        {
            var charSpan = charRow[i].Span;
            int bytesWritten = Encoding.UTF8.GetBytes(charSpan, buffer[offset..]);
            offset += bytesWritten;

            // Write delimiter (except after last column)
            if (i < columnCount - 1)
            {
                buffer[offset] = delimiter;
                columnEnds[i + 1] = offset; // Position of delimiter
                offset++;
            }
            else
            {
                // Last column: columnEnds points to position after last byte
                columnEnds[i + 1] = offset;
            }
        }

        // Create the byte row (uses the exact slice, not the full rented array)
        var byteRow = new CsvRow<byte>(
            buffer,
            columnEnds,
            columnCount,
            charRow.LineNumber,
            charRow.SourceLineNumber,
            trimFields: false);

        return new PooledByteRowConversion(byteRow, rentedBuffer);
    }

    /// <summary>
    /// Wrapper that holds the converted row and manages the pooled buffer lifetime.
    /// Implements IDisposable to return the buffer to the pool after binding completes.
    /// </summary>
    private readonly ref struct PooledByteRowConversion
    {
        public readonly CsvRow<byte> Row;
        private readonly byte[]? rentedBuffer;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PooledByteRowConversion(CsvRow<byte> row, byte[]? rentedBuffer)
        {
            Row = row;
            this.rentedBuffer = rentedBuffer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            if (rentedBuffer != null)
            {
                ArrayPool<byte>.Shared.Return(rentedBuffer);
            }
        }
    }
}
