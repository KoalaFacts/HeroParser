using System.Buffers;

namespace HeroParser.Internal;

/// <summary>
/// Provides centralized access to ArrayPool instances for the HeroParser library.
/// </summary>
internal static class BufferPool
{
    /// <summary>
    /// Shared char buffer pool for CSV and FixedWidth operations.
    /// </summary>
    public static ArrayPool<char> Char { get; } = ArrayPool<char>.Shared;

    /// <summary>
    /// Shared byte buffer pool for encoding operations.
    /// </summary>
    public static ArrayPool<byte> Byte { get; } = ArrayPool<byte>.Shared;

    /// <summary>Rents a char buffer and clears it to avoid stale data.</summary>
    public static char[] RentChar(int minimumLength, bool clear = true)
    {
        var buffer = Char.Rent(minimumLength);
        if (clear)
        {
            Array.Clear(buffer);
        }
        return buffer;
    }

    /// <summary>Returns a char buffer, optionally clearing it.</summary>
    public static void ReturnChar(char[] buffer, bool clear = true)
    {
        Char.Return(buffer, clearArray: clear);
    }

    /// <summary>Rents a byte buffer; clear defaults to false for perf.</summary>
    public static byte[] RentByte(int minimumLength, bool clear = false)
    {
        var buffer = Byte.Rent(minimumLength);
        if (clear)
        {
            Array.Clear(buffer);
        }
        return buffer;
    }

    /// <summary>Returns a byte buffer, optionally clearing it.</summary>
    public static void ReturnByte(byte[] buffer, bool clear = false)
    {
        Byte.Return(buffer, clearArray: clear);
    }
}
