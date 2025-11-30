using System.Buffers;

namespace HeroParser.Internal;

/// <summary>
/// Provides dedicated ArrayPool instances for the HeroParser library.
/// Using private pools instead of ArrayPool.Shared prevents buffer contamination
/// when multiple independent users of ArrayPool.Shared run concurrently.
/// </summary>
internal static class BufferPool
{
    /// <summary>
    /// Dedicated char buffer pool for CSV and FixedWidth operations.
    /// Isolated from ArrayPool&lt;char&gt;.Shared to prevent cross-contamination.
    /// </summary>
    public static ArrayPool<char> Char { get; } = ArrayPool<char>.Create();

    /// <summary>
    /// Dedicated byte buffer pool for encoding operations.
    /// Isolated from ArrayPool&lt;byte&gt;.Shared to prevent cross-contamination.
    /// </summary>
    public static ArrayPool<byte> Byte { get; } = ArrayPool<byte>.Create();
}
