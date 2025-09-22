#if NETSTANDARD2_0
using System;

namespace HeroParser.Compatibility
{
    /// <summary>
    /// Compatibility struct for netstandard2.0 to replace System.Range functionality.
    /// Represents a range with start position and length for field extraction.
    /// </summary>
    public readonly struct FieldRange : IEquatable<FieldRange>
    {
        /// <summary>
        /// Gets the start position of the range.
        /// </summary>
        public int Start { get; }

        /// <summary>
        /// Gets the length of the range.
        /// </summary>
        public int Length { get; }

        /// <summary>
        /// Gets the end position (start + length).
        /// </summary>
        public int End => Start + Length;

        /// <summary>
        /// Initializes a new field range.
        /// </summary>
        /// <param name="start">Start position</param>
        /// <param name="length">Length of the range</param>
        public FieldRange(int start, int length)
        {
            if (start < 0)
                throw new ArgumentOutOfRangeException(nameof(start), "Start position cannot be negative");
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length), "Length cannot be negative");

            Start = start;
            Length = length;
        }

        /// <summary>
        /// Creates a range from start and end positions.
        /// </summary>
        /// <param name="start">Start position</param>
        /// <param name="end">End position (exclusive)</param>
        /// <returns>New field range</returns>
        public static FieldRange FromStartEnd(int start, int end)
        {
            if (end < start)
                throw new ArgumentException("End position must be >= start position");

            return new FieldRange(start, end - start);
        }

        /// <summary>
        /// Determines whether this range is empty (length is 0).
        /// </summary>
        public bool IsEmpty => Length == 0;

        /// <summary>
        /// Determines whether the specified position is within this range.
        /// </summary>
        /// <param name="position">Position to check</param>
        /// <returns>True if position is within range</returns>
        public bool Contains(int position)
        {
            return position >= Start && position < End;
        }

        /// <summary>
        /// Determines whether this range overlaps with another range.
        /// </summary>
        /// <param name="other">Other range to check</param>
        /// <returns>True if ranges overlap</returns>
        public bool Overlaps(FieldRange other)
        {
            return Start < other.End && other.Start < End;
        }

        /// <summary>
        /// Determines whether the current range is equal to another range.
        /// </summary>
        public bool Equals(FieldRange other)
        {
            return Start == other.Start && Length == other.Length;
        }

        /// <summary>
        /// Determines whether the current range is equal to the specified object.
        /// </summary>
        public override bool Equals(object? obj)
        {
            return obj is FieldRange other && Equals(other);
        }

        /// <summary>
        /// Returns the hash code for this range.
        /// </summary>
        public override int GetHashCode()
        {
            return HashCode.Combine(Start, Length);
        }

        /// <summary>
        /// Returns a string representation of this range.
        /// </summary>
        public override string ToString()
        {
            return $"[{Start}..{End}] (length: {Length})";
        }

        /// <summary>
        /// Determines whether two ranges are equal.
        /// </summary>
        public static bool operator ==(FieldRange left, FieldRange right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether two ranges are not equal.
        /// </summary>
        public static bool operator !=(FieldRange left, FieldRange right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Compatibility class for HashCode.Combine in netstandard2.0.
    /// </summary>
    internal static class HashCode
    {
        public static int Combine<T1, T2>(T1 value1, T2 value2)
        {
            var hash1 = value1?.GetHashCode() ?? 0;
            var hash2 = value2?.GetHashCode() ?? 0;
            return ((hash1 << 5) + hash1) ^ hash2;
        }
    }
}
#endif