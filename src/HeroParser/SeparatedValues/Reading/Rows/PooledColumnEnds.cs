using System;
using System.Buffers;

namespace HeroParser.SeparatedValues.Reading.Rows;

/// <summary>
/// Manages a pooled buffer for column end positions.
/// </summary>
internal sealed class PooledColumnEnds
{
    private int[]? buffer;
    private readonly int length;

    public PooledColumnEnds(int length)
    {
        this.length = length;
        buffer = ArrayPool<int>.Shared.Rent(length);
    }

    public Span<int> Span => (buffer ?? throw new ObjectDisposedException(nameof(PooledColumnEnds))).AsSpan(0, length);

    public int[] Buffer => buffer ?? throw new ObjectDisposedException(nameof(PooledColumnEnds));

    public void Return()
    {
        var local = buffer;
        if (local is null)
            return;
        buffer = null;
        ArrayPool<int>.Shared.Return(local, clearArray: false);
    }
}
