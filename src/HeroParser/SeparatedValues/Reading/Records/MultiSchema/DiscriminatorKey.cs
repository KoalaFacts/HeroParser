using System.Runtime.CompilerServices;

namespace HeroParser.SeparatedValues.Reading.Records.MultiSchema;

/// <summary>
/// A packed discriminator key for zero-allocation dictionary lookups.
/// Supports discriminators up to 8 ASCII characters by packing them into a long.
/// </summary>
/// <remarks>
/// <para>
/// For discriminators that fit in 8 ASCII characters (common in banking formats like "H", "D", "T", "01", "02"),
/// this enables O(1) lookup without string allocation.
/// </para>
/// <para>
/// For longer discriminators or those containing non-ASCII characters, callers should fall back to string-based lookup.
/// </para>
/// </remarks>
internal readonly struct DiscriminatorKey : IEquatable<DiscriminatorKey>
{
    /// <summary>
    /// Maximum number of characters that can be packed into a key.
    /// </summary>
    public const int MAX_PACKED_LENGTH = 8;

    private readonly long packedValue;
    private readonly byte length;

    private DiscriminatorKey(long packed, byte len)
    {
        packedValue = packed;
        length = len;
    }

    /// <summary>
    /// Gets the length of the original discriminator value.
    /// </summary>
    public int Length => length;

    /// <summary>
    /// Attempts to create a packed key from a char span.
    /// </summary>
    /// <param name="value">The discriminator value to pack.</param>
    /// <param name="key">The resulting packed key if successful.</param>
    /// <returns>True if the value was successfully packed; false if it's too long or contains non-ASCII characters.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryCreate(ReadOnlySpan<char> value, out DiscriminatorKey key)
    {
        if (value.Length > MAX_PACKED_LENGTH)
        {
            key = default;
            return false;
        }

        long packed = 0;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c > 127)
            {
                // Non-ASCII character, cannot pack
                key = default;
                return false;
            }
            packed |= ((long)c) << (i * 8);
        }

        key = new DiscriminatorKey(packed, (byte)value.Length);
        return true;
    }

    /// <summary>
    /// Attempts to create a lowercase packed key from a char span.
    /// Used for case-insensitive matching without allocation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryCreateLowercase(ReadOnlySpan<char> value, out DiscriminatorKey key)
    {
        if (value.Length > MAX_PACKED_LENGTH)
        {
            key = default;
            return false;
        }

        long packed = 0;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c > 127)
            {
                key = default;
                return false;
            }
            // Inline lowercase: if 'A'-'Z', add 32 to get 'a'-'z'
            if ((uint)(c - 'A') <= 'Z' - 'A')
            {
                c = (char)(c + 32);
            }
            packed |= ((long)c) << (i * 8);
        }

        key = new DiscriminatorKey(packed, (byte)value.Length);
        return true;
    }

    /// <summary>
    /// Attempts to create a packed key from a UTF-8 byte span.
    /// </summary>
    /// <param name="value">The discriminator value to pack.</param>
    /// <param name="key">The resulting packed key if successful.</param>
    /// <returns>True if the value was successfully packed; false if it's too long or contains non-ASCII bytes.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryCreate(ReadOnlySpan<byte> value, out DiscriminatorKey key)
    {
        if (value.Length > MAX_PACKED_LENGTH)
        {
            key = default;
            return false;
        }

        long packed = 0;
        for (int i = 0; i < value.Length; i++)
        {
            byte b = value[i];
            if (b > 127)
            {
                // Non-ASCII byte (UTF-8 multi-byte sequence), cannot pack
                key = default;
                return false;
            }
            packed |= ((long)b) << (i * 8);
        }

        key = new DiscriminatorKey(packed, (byte)value.Length);
        return true;
    }

    /// <summary>
    /// Attempts to create a lowercase packed key from a UTF-8 byte span.
    /// Used for case-insensitive matching without allocation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryCreateLowercase(ReadOnlySpan<byte> value, out DiscriminatorKey key)
    {
        if (value.Length > MAX_PACKED_LENGTH)
        {
            key = default;
            return false;
        }

        long packed = 0;
        for (int i = 0; i < value.Length; i++)
        {
            byte b = value[i];
            if (b > 127)
            {
                key = default;
                return false;
            }
            // Inline lowercase: if 'A'-'Z', add 32 to get 'a'-'z'
            if ((uint)(b - 'A') <= 'Z' - 'A')
            {
                b = (byte)(b + 32);
            }
            packed |= ((long)b) << (i * 8);
        }

        key = new DiscriminatorKey(packed, (byte)value.Length);
        return true;
    }

    /// <summary>
    /// Creates a packed key from a string for registration purposes.
    /// </summary>
    /// <param name="value">The discriminator value.</param>
    /// <param name="lowercase">If true, converts to lowercase for case-insensitive matching.</param>
    /// <returns>The packed key.</returns>
    /// <exception cref="ArgumentException">Thrown if the value is too long or contains non-ASCII characters.</exception>
    public static DiscriminatorKey FromString(string value, bool lowercase = false)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (lowercase)
        {
            Span<char> buffer = stackalloc char[value.Length];
            for (int i = 0; i < value.Length; i++)
            {
                buffer[i] = char.ToLowerInvariant(value[i]);
            }
            if (!TryCreate(buffer, out var key))
            {
                throw new ArgumentException(
                    $"Discriminator value '{value}' cannot be packed. " +
                    $"It must be at most {MAX_PACKED_LENGTH} ASCII characters.",
                    nameof(value));
            }
            return key;
        }
        else
        {
            if (!TryCreate(value.AsSpan(), out var key))
            {
                throw new ArgumentException(
                    $"Discriminator value '{value}' cannot be packed. " +
                    $"It must be at most {MAX_PACKED_LENGTH} ASCII characters.",
                    nameof(value));
            }
            return key;
        }
    }

    /// <summary>
    /// Creates a packed key from an integer discriminator value.
    /// The integer is converted to its string representation and packed.
    /// </summary>
    /// <param name="value">The integer discriminator value.</param>
    /// <returns>The packed key.</returns>
    public static DiscriminatorKey FromInt(int value)
    {
        // Use stack allocation for small integers (common case)
        Span<char> buffer = stackalloc char[16];
        if (!value.TryFormat(buffer, out int charsWritten))
        {
            // Should never happen for int
            throw new InvalidOperationException("Failed to format integer.");
        }

        if (!TryCreate(buffer[..charsWritten], out var key))
        {
            throw new ArgumentException(
                $"Integer discriminator value '{value}' cannot be packed.",
                nameof(value));
        }

        return key;
    }

    /// <summary>
    /// Determines whether this key equals another key.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(DiscriminatorKey other)
        => packedValue == other.packedValue && length == other.length;

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is DiscriminatorKey other && Equals(other);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode()
    {
        // Fast hash: XOR high and low 32-bit parts with length
        // Avoids HashCode.Combine overhead for hot path dictionary lookups
        return (int)packedValue ^ (int)(packedValue >> 32) ^ (length << 24);
    }

    /// <summary>
    /// Equality operator.
    /// </summary>
    public static bool operator ==(DiscriminatorKey left, DiscriminatorKey right)
        => left.Equals(right);

    /// <summary>
    /// Inequality operator.
    /// </summary>
    public static bool operator !=(DiscriminatorKey left, DiscriminatorKey right)
        => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString()
    {
        if (length == 0)
            return string.Empty;

        Span<char> chars = stackalloc char[length];
        for (int i = 0; i < length; i++)
        {
            chars[i] = (char)((packedValue >> (i * 8)) & 0xFF);
        }
        return new string(chars);
    }
}
