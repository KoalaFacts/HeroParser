using System.Buffers;
using HeroParser.SeparatedValues.Reading.Shared;

namespace HeroParser.SeparatedValues.Reading.Rows;

/// <summary>
/// Pooled buffers for the scan-ahead row batch (see <see cref="CsvRowBatchScanner"/>).
/// </summary>
/// <remarks>
/// <para>
/// Lives in a class, not in the <see cref="CsvRowReader{T}"/> struct, for the same reason as
/// <see cref="PooledColumnEnds"/>: the reader struct is copied by <c>foreach</c> and by callers, so
/// <see cref="Dispose"/> can run once per copy. Returning the same array to the pool twice hands it
/// out to two owners, and the next reader would see its ends and row starts alias one another.
/// A shared instance with an idempotent return makes every copy's dispose safe.
/// </para>
/// <para>Thread-Safety: NOT thread-safe; each instance belongs to a single reader on a single thread.</para>
/// </remarks>
internal sealed class PooledRowBatch : IDisposable
{
    private int[]? ends;
    private int[]? rowStarts;

    public PooledRowBatch(int endsCapacity, bool trackLineNumbers)
    {
        ends = ArrayPool<int>.Shared.Rent(endsCapacity);
        rowStarts = ArrayPool<int>.Shared.Rent(CsvRowBatchScanner.RowStartsCapacity(endsCapacity));
        SourceLines = trackLineNumbers ? ArrayPool<int>.Shared.Rent(rowStarts.Length) : null;
    }

    /// <summary>Absolute column-end offsets for the rows in the current batch.</summary>
    public int[] Ends => ends ?? throw new ObjectDisposedException(nameof(PooledRowBatch));

    /// <summary>Index into <see cref="Ends"/> of each row's sentinel, terminated by one extra entry.</summary>
    public int[] RowStarts => rowStarts ?? throw new ObjectDisposedException(nameof(PooledRowBatch));

    /// <summary>Source line of each row's start, or <see langword="null"/> when line tracking is off or after disposal.</summary>
    public int[]? SourceLines { get; private set; }

    public void Dispose()
    {
        var e = ends;
        if (e is null)
            return;

        ends = null;
        ArrayPool<int>.Shared.Return(e, clearArray: false);

        var r = rowStarts;
        rowStarts = null;
        if (r is not null)
            ArrayPool<int>.Shared.Return(r, clearArray: false);

        var s = SourceLines;
        SourceLines = null;
        if (s is not null)
            ArrayPool<int>.Shared.Return(s, clearArray: false);
    }
}
