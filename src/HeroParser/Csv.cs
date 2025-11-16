using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace HeroParser;

/// <summary>
/// Ultra-fast CSV parser targeting 30+ GB/s throughput using SIMD optimization.
/// Zero allocations in hot path, no unsafe code.
/// </summary>
public static class Csv
{
    /// <summary>
    /// Parse CSV data synchronously.
    /// </summary>
    /// <param name="csv">The CSV content to parse</param>
    /// <param name="options">Parser options (null for defaults: comma delimiter, 10k columns, 100k rows)</param>
    /// <returns>A zero-allocation CSV reader</returns>
    /// <exception cref="CsvException">Thrown when parsing fails or limits are exceeded</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static CsvReader Parse(string csv, CsvParserOptions? options = null)
    {
        if (csv == null)
            throw new ArgumentNullException(nameof(csv));

        options ??= CsvParserOptions.Default;
        options.Validate();

        return new CsvReader(csv.AsSpan(), options);
    }

    /// <summary>
    /// Parse CSV data asynchronously (for large files/streams).
    /// Yields rows one at a time without loading entire CSV into memory.
    /// </summary>
    /// <param name="csv">The CSV content to parse</param>
    /// <param name="options">Parser options (null for defaults)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Async enumerable of CSV rows</returns>
    /// <exception cref="CsvException">Thrown when parsing fails or limits are exceeded</exception>
    public static async IAsyncEnumerable<CsvRow> ParseAsync(
        string csv,
        CsvParserOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (csv == null)
            throw new ArgumentNullException(nameof(csv));

        options ??= CsvParserOptions.Default;
        options.Validate();

        // Process in chunks to avoid blocking
        const int ChunkSize = 64 * 1024; // 64KB chunks
        int position = 0;
        int rowCount = 0;

        while (position < csv.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Find chunk boundary (don't split rows)
            int chunkEnd = Math.Min(position + ChunkSize, csv.Length);
            if (chunkEnd < csv.Length)
            {
                // Scan forward to next newline
                while (chunkEnd < csv.Length &&
                       csv[chunkEnd] != '\n' &&
                       csv[chunkEnd] != '\r')
                {
                    chunkEnd++;
                }

                // Include the newline
                if (chunkEnd < csv.Length)
                {
                    if (csv[chunkEnd] == '\r' && chunkEnd + 1 < csv.Length && csv[chunkEnd + 1] == '\n')
                        chunkEnd += 2; // CRLF
                    else
                        chunkEnd++; // LF or CR
                }
            }

            var chunk = csv.AsSpan(position, chunkEnd - position);
            var reader = new CsvReader(chunk, options);

            foreach (var row in reader)
            {
                if (++rowCount > options.MaxRows)
                {
                    throw new CsvException(
                        CsvErrorCode.TooManyRows,
                        $"CSV exceeds maximum row limit of {options.MaxRows}");
                }

                yield return row;

                // Yield periodically to avoid blocking
                if (rowCount % 1000 == 0)
                {
                    await Task.Yield();
                }
            }

            position = chunkEnd;
        }
    }
}
