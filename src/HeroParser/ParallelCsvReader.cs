using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace HeroParser;

/// <summary>
/// Parallel CSV reader for multi-core throughput (10+ GB/s target).
/// Splits CSV into chunks and processes them concurrently.
/// </summary>
public ref struct ParallelCsvReader
{
    private readonly ReadOnlySpan<char> _csv;
    private readonly char _delimiter;
    private readonly int _threadCount;
    private readonly int _chunkSize;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ParallelCsvReader(ReadOnlySpan<char> csv, char delimiter, int threadCount, int chunkSize)
    {
        _csv = csv;
        _delimiter = delimiter;
        _threadCount = threadCount;
        _chunkSize = chunkSize;
    }

    /// <summary>
    /// Parse all rows in parallel and return as array.
    /// Note: This allocates - use for large files where multi-threading wins outweigh allocation cost.
    /// </summary>
    public string[][] ParseAll()
    {
        // Split CSV into chunks at line boundaries
        var chunks = SplitIntoChunks(_csv, _chunkSize);

        // Parse each chunk in parallel
        var results = new ConcurrentBag<(int Index, List<string[]> Rows)>();

        Parallel.ForEach(chunks, new ParallelOptions { MaxDegreeOfParallelism = _threadCount }, chunk =>
        {
            var rows = new List<string[]>();
            var reader = new CsvReader(chunk.Data, _delimiter);

            while (reader.MoveNext())
            {
                rows.Add(reader.Current.ToStringArray());
            }

            results.Add((chunk.Index, rows));
        });

        // Combine results in order
        var orderedResults = results.OrderBy(r => r.Index).SelectMany(r => r.Rows);
        return orderedResults.ToArray();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static List<(int Index, ReadOnlySpan<char> Data)> SplitIntoChunks(ReadOnlySpan<char> csv, int chunkSize)
    {
        var chunks = new List<(int Index, ReadOnlySpan<char> Data)>();
        int position = 0;
        int index = 0;

        while (position < csv.Length)
        {
            int endPosition = Math.Min(position + chunkSize, csv.Length);

            // Find next line boundary (don't split mid-line)
            if (endPosition < csv.Length)
            {
                while (endPosition < csv.Length && csv[endPosition] != '\n' && csv[endPosition] != '\r')
                {
                    endPosition++;
                }

                // Include the newline
                if (endPosition < csv.Length && csv[endPosition] == '\r')
                    endPosition++;
                if (endPosition < csv.Length && csv[endPosition] == '\n')
                    endPosition++;
            }

            var chunk = csv.Slice(position, endPosition - position);
            chunks.Add((index++, chunk));

            position = endPosition;
        }

        return chunks;
    }
}
