using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace HeroParser;

/// <summary>
/// Parallel CSV reader for multi-core throughput (10+ GB/s target).
/// Splits CSV into chunks and processes them concurrently.
/// Note: Uses string instead of span because parallel processing requires lambda capture.
/// </summary>
public struct ParallelCsvReader
{
    private readonly string _csv;
    private readonly char _delimiter;
    private readonly int _threadCount;
    private readonly int _chunkSize;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ParallelCsvReader(string csv, char delimiter, int threadCount, int chunkSize)
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
        // Split CSV into chunks at line boundaries (store positions, not spans)
        var chunkRanges = SplitIntoChunks(_chunkSize);

        // Copy to local variables for lambda capture
        var csv = _csv;
        var delimiter = _delimiter;

        // Parse each chunk in parallel
        var results = new ConcurrentBag<(int Index, List<string[]> Rows)>();

        Parallel.ForEach(chunkRanges, new ParallelOptions { MaxDegreeOfParallelism = _threadCount }, chunkRange =>
        {
            var rows = new List<string[]>();
            var chunkSpan = csv.AsSpan(chunkRange.Start, chunkRange.Length);
            var reader = new CsvReader(chunkSpan, delimiter);

            while (reader.MoveNext())
            {
                rows.Add(reader.Current.ToStringArray());
            }

            results.Add((chunkRange.Index, rows));
        });

        // Combine results in order
        var orderedResults = results.OrderBy(r => r.Index).SelectMany(r => r.Rows);
        return orderedResults.ToArray();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private List<ChunkRange> SplitIntoChunks(int chunkSize)
    {
        var chunks = new List<ChunkRange>();
        int position = 0;
        int index = 0;
        int csvLength = _csv.Length;

        while (position < csvLength)
        {
            int endPosition = Math.Min(position + chunkSize, csvLength);

            // Find next line boundary (don't split mid-line)
            if (endPosition < csvLength)
            {
                while (endPosition < csvLength && _csv[endPosition] != '\n' && _csv[endPosition] != '\r')
                {
                    endPosition++;
                }

                // Include the newline
                if (endPosition < csvLength && _csv[endPosition] == '\r')
                    endPosition++;
                if (endPosition < csvLength && _csv[endPosition] == '\n')
                    endPosition++;
            }

            chunks.Add(new ChunkRange(index++, position, endPosition - position));
            position = endPosition;
        }

        return chunks;
    }

    private readonly struct ChunkRange
    {
        public readonly int Index;
        public readonly int Start;
        public readonly int Length;

        public ChunkRange(int index, int start, int length)
        {
            Index = index;
            Start = start;
            Length = length;
        }
    }
}
