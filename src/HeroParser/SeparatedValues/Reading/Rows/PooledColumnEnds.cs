using System;
using System.Buffers;

namespace HeroParser.SeparatedValues.Reading.Rows;

/// <summary>
/// Manages a pooled buffer for column end positions.
/// </summary>
/// <remarks>
/// Thread-Safety: This type is NOT thread-safe. Each instance should be used on a single thread.
/// The buffer is rented from ArrayPool on construction and returned to the pool on disposal.
/// </remarks>
internal sealed class PooledColumnEnds : IDisposable
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

    /// <summary>
    /// Returns the pooled buffer to the ArrayPool.
    /// </summary>
    /// <remarks>
    /// This method is safe to call multiple times. After the first call, subsequent calls are no-ops.
    /// Consider using Dispose() instead for guaranteed cleanup with using statements.
    /// </remarks>
    public void Return()
    {
        var local = buffer;
        if (local is null)
            return;
        buffer = null;
        ArrayPool<int>.Shared.Return(local, clearArray: false);
    }

    /// <summary>
    /// Returns the pooled buffer to the ArrayPool.
    /// </summary>
    /// <remarks>
    /// Implements IDisposable to enable using statements for guaranteed cleanup.
    /// This method calls Return() internally and is safe to call multiple times.
    /// </remarks>
    public void Dispose()
    {
        Return();
    }
}
