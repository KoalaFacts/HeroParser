using System;
using System.Runtime.CompilerServices;
#if NETSTANDARD2_0
using HeroParser.Compatibility;
#endif

namespace HeroParser.Core
{
    /// <summary>
    /// Represents a CSV record with zero-allocation field access through lazy evaluation.
    /// Provides vectorized delimiter detection and span-based field extraction.
    /// </summary>
    public readonly ref struct CsvRecord
    {
        private readonly ReadOnlySpan<char> _rawData;
#if NETSTANDARD2_0
        private readonly ReadOnlySpan<FieldRange> _fieldSpans;
#else
        private readonly ReadOnlySpan<Range> _fieldSpans;
#endif
        private readonly int _lineNumber;

        /// <summary>
        /// Initializes a new CSV record with pre-calculated field spans.
        /// </summary>
        /// <param name="rawData">The raw CSV line data</param>
        /// <param name="fieldSpans">Pre-calculated field boundary ranges</param>
        /// <param name="lineNumber">Line number in source (1-based)</param>
#if NETSTANDARD2_0
        public CsvRecord(ReadOnlySpan<char> rawData, ReadOnlySpan<FieldRange> fieldSpans, int lineNumber)
#else
        public CsvRecord(ReadOnlySpan<char> rawData, ReadOnlySpan<Range> fieldSpans, int lineNumber)
#endif
        {
            if (lineNumber < 1)
                throw new ArgumentOutOfRangeException(nameof(lineNumber), "Line number must be >= 1");
            if (fieldSpans.Length == 0)
                throw new ArgumentException("Field count must be > 0", nameof(fieldSpans));

            _rawData = rawData;
            _fieldSpans = fieldSpans;
            _lineNumber = lineNumber;
        }

        /// <summary>
        /// Gets the number of fields in this CSV record.
        /// </summary>
        public int FieldCount => _fieldSpans.Length;

        /// <summary>
        /// Gets the line number in the source data (1-based).
        /// </summary>
        public int LineNumber => _lineNumber;

        /// <summary>
        /// Gets the raw CSV line data.
        /// </summary>
        public ReadOnlySpan<char> RawData => _rawData;

        /// <summary>
        /// Gets the field boundary ranges for zero-allocation access.
        /// </summary>
#if NETSTANDARD2_0
        public ReadOnlySpan<FieldRange> FieldSpans => _fieldSpans;
#else
        public ReadOnlySpan<Range> FieldSpans => _fieldSpans;
#endif

        /// <summary>
        /// Gets a field value by index with zero allocations using Range indexing.
        /// </summary>
        /// <param name="index">Field index (0-based)</param>
        /// <returns>Field value as ReadOnlySpan&lt;char&gt;</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<char> GetField(int index)
        {
            if ((uint)index >= (uint)_fieldSpans.Length)
                throw new ArgumentOutOfRangeException(nameof(index), $"Field index {index} is out of range [0, {_fieldSpans.Length - 1}]");

#if NETSTANDARD2_0
            var range = _fieldSpans[index];
            var start = range.Start;
            var length = range.Length;
#else
            var range = _fieldSpans[index];
            var (start, length) = range.GetOffsetAndLength(_rawData.Length);
#endif

            return _rawData.Slice(start, length);
        }

        /// <summary>
        /// Gets a field value by index with zero allocations, returning empty span if index is out of range.
        /// </summary>
        /// <param name="index">Field index (0-based)</param>
        /// <returns>Field value as ReadOnlySpan&lt;char&gt; or empty span if out of range</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<char> TryGetField(int index)
        {
            if ((uint)index >= (uint)_fieldSpans.Length)
                return ReadOnlySpan<char>.Empty;

#if NETSTANDARD2_0
            var range = _fieldSpans[index];
            var start = range.Start;
            var length = range.Length;
#else
            var range = _fieldSpans[index];
            var (start, length) = range.GetOffsetAndLength(_rawData.Length);
#endif

            return _rawData.Slice(start, length);
        }

        /// <summary>
        /// Creates a string array from all fields (allocating operation for compatibility).
        /// </summary>
        /// <returns>Array of field strings</returns>
        public string[] ToStringArray()
        {
            var result = new string[_fieldSpans.Length];
            for (int i = 0; i < _fieldSpans.Length; i++)
            {
                result[i] = GetField(i).ToString();
            }
            return result;
        }

        /// <summary>
        /// Validates field span ranges against raw data length.
        /// </summary>
        private bool ValidateFieldSpans()
        {
            foreach (var range in _fieldSpans)
            {
#if NETSTANDARD2_0
                var start = range.Start;
                var length = range.Length;
#else
                var (start, length) = range.GetOffsetAndLength(_rawData.Length);
#endif
                if (start < 0 || start + length > _rawData.Length)
                    return false;
            }
            return true;
        }
    }
}