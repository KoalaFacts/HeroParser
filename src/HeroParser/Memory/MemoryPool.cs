using System.Buffers;
using System.Runtime.CompilerServices;

namespace HeroParser.Memory;

/// <summary>
/// High-performance memory pool management for zero-allocation parsing.
/// Provides centralized access to ArrayPool instances with optimized rental strategies.
/// </summary>
public static class MemoryPool
{
    /// <summary>
    /// Shared pool for character arrays used in string operations.
    /// </summary>
    private static readonly ArrayPool<char> CharPool = ArrayPool<char>.Shared;

    /// <summary>
    /// Shared pool for byte arrays used in I/O operations.
    /// </summary>
    private static readonly ArrayPool<byte> BytePool = ArrayPool<byte>.Shared;

    /// <summary>
    /// Shared pool for string arrays used in record storage.
    /// </summary>
    private static readonly ArrayPool<string> StringPool = ArrayPool<string>.Shared;

    /// <summary>
    /// Default minimum buffer size for rentals (4KB aligned).
    /// </summary>
    public const int DefaultMinBufferSize = 4096;

    /// <summary>
    /// Large buffer threshold (64KB) for special handling.
    /// </summary>
    public const int LargeBufferThreshold = 65536;

    /// <summary>
    /// Rents a character array from the pool with at least the specified length.
    /// </summary>
    /// <param name="minimumLength">The minimum length of the array needed.</param>
    /// <returns>A rented character array that must be returned to the pool.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static char[] RentCharArray(int minimumLength)
    {
        return CharPool.Rent(Math.Max(minimumLength, DefaultMinBufferSize));
    }

    /// <summary>
    /// Returns a character array to the pool.
    /// </summary>
    /// <param name="array">The array to return.</param>
    /// <param name="clearArray">If true, clears the array before returning it to the pool.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ReturnCharArray(char[] array, bool clearArray = false)
    {
        if (array != null)
        {
            CharPool.Return(array, clearArray);
        }
    }

    /// <summary>
    /// Rents a byte array from the pool with at least the specified length.
    /// </summary>
    /// <param name="minimumLength">The minimum length of the array needed.</param>
    /// <returns>A rented byte array that must be returned to the pool.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] RentByteArray(int minimumLength)
    {
        return BytePool.Rent(Math.Max(minimumLength, DefaultMinBufferSize));
    }

    /// <summary>
    /// Returns a byte array to the pool.
    /// </summary>
    /// <param name="array">The array to return.</param>
    /// <param name="clearArray">If true, clears the array before returning it to the pool.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ReturnByteArray(byte[] array, bool clearArray = false)
    {
        if (array != null)
        {
            BytePool.Return(array, clearArray);
        }
    }

    /// <summary>
    /// Rents a string array from the pool with at least the specified length.
    /// </summary>
    /// <param name="minimumLength">The minimum length of the array needed.</param>
    /// <returns>A rented string array that must be returned to the pool.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string[] RentStringArray(int minimumLength)
    {
        return StringPool.Rent(minimumLength);
    }

    /// <summary>
    /// Returns a string array to the pool.
    /// </summary>
    /// <param name="array">The array to return.</param>
    /// <param name="clearArray">If true, clears the array before returning it to the pool.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ReturnStringArray(string[] array, bool clearArray = true)
    {
        if (array != null)
        {
            StringPool.Return(array, clearArray);
        }
    }

    /// <summary>
    /// Provides a scope-based rental for automatic return of arrays.
    /// </summary>
    public readonly struct CharArrayRental : IDisposable
    {
        private readonly char[] _array;
        private readonly bool _clearOnReturn;

        /// <summary>
        /// Initializes a new character array rental.
        /// </summary>
        /// <param name="minimumLength">The minimum length of the array needed.</param>
        /// <param name="clearOnReturn">If true, clears the array when disposed.</param>
        public CharArrayRental(int minimumLength, bool clearOnReturn = false)
        {
            _array = RentCharArray(minimumLength);
            _clearOnReturn = clearOnReturn;
        }

        /// <summary>
        /// Gets the rented character array.
        /// </summary>
        public char[] Array => _array;

        /// <summary>
        /// Gets a span over the rented character array.
        /// </summary>
        public Span<char> Span => _array.AsSpan();

        /// <summary>
        /// Returns the rented array to the pool.
        /// </summary>
        public void Dispose()
        {
            ReturnCharArray(_array, _clearOnReturn);
        }
    }

    /// <summary>
    /// Provides a scope-based rental for automatic return of byte arrays.
    /// </summary>
    public readonly struct ByteArrayRental : IDisposable
    {
        private readonly byte[] _array;
        private readonly bool _clearOnReturn;

        /// <summary>
        /// Initializes a new byte array rental.
        /// </summary>
        /// <param name="minimumLength">The minimum length of the array needed.</param>
        /// <param name="clearOnReturn">If true, clears the array when disposed.</param>
        public ByteArrayRental(int minimumLength, bool clearOnReturn = false)
        {
            _array = RentByteArray(minimumLength);
            _clearOnReturn = clearOnReturn;
        }

        /// <summary>
        /// Gets the rented byte array.
        /// </summary>
        public byte[] Array => _array;

        /// <summary>
        /// Gets a span over the rented byte array.
        /// </summary>
        public Span<byte> Span => _array.AsSpan();

        /// <summary>
        /// Returns the rented array to the pool.
        /// </summary>
        public void Dispose()
        {
            ReturnByteArray(_array, _clearOnReturn);
        }
    }

    /// <summary>
    /// Creates a scope-based character array rental.
    /// </summary>
    /// <param name="minimumLength">The minimum length of the array needed.</param>
    /// <param name="clearOnReturn">If true, clears the array when disposed.</param>
    /// <returns>A disposable rental that automatically returns the array.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static CharArrayRental RentCharArrayScope(int minimumLength, bool clearOnReturn = false)
    {
        return new CharArrayRental(minimumLength, clearOnReturn);
    }

    /// <summary>
    /// Creates a scope-based byte array rental.
    /// </summary>
    /// <param name="minimumLength">The minimum length of the array needed.</param>
    /// <param name="clearOnReturn">If true, clears the array when disposed.</param>
    /// <returns>A disposable rental that automatically returns the array.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ByteArrayRental RentByteArrayScope(int minimumLength, bool clearOnReturn = false)
    {
        return new ByteArrayRental(minimumLength, clearOnReturn);
    }
}