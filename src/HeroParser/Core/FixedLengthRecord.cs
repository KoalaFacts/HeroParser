using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace HeroParser.Core
{
    /// <summary>
    /// Represents a fixed-length record with COBOL copybook support and zero-allocation field access.
    /// Handles EBCDIC conversion, packed decimal fields, and PICTURE clause specifications.
    /// </summary>
    public readonly ref struct FixedLengthRecord
    {
        private readonly ReadOnlySpan<char> _rawData;
        private readonly ReadOnlySpan<FieldDefinition> _fieldDefinitions;
        private readonly int _recordLength;

        /// <summary>
        /// Initializes a new fixed-length record with field definitions.
        /// </summary>
        /// <param name="rawData">The raw fixed-length record data</param>
        /// <param name="fieldDefinitions">Field position and type definitions</param>
        public FixedLengthRecord(ReadOnlySpan<char> rawData, ReadOnlySpan<FieldDefinition> fieldDefinitions)
        {
            if (fieldDefinitions.Length == 0)
                throw new ArgumentException("Field definitions cannot be empty", nameof(fieldDefinitions));

            _rawData = rawData;
            _fieldDefinitions = fieldDefinitions;
            _recordLength = rawData.Length;

            ValidateFieldDefinitions();
        }

        /// <summary>
        /// Gets the total record length in characters.
        /// </summary>
        public int RecordLength => _recordLength;

        /// <summary>
        /// Gets the field definitions for this record.
        /// </summary>
        public ReadOnlySpan<FieldDefinition> FieldDefinitions => _fieldDefinitions;

        /// <summary>
        /// Gets the raw record data.
        /// </summary>
        public ReadOnlySpan<char> RawData => _rawData;

        /// <summary>
        /// Gets a field value by name with zero allocations.
        /// </summary>
        /// <param name="fieldName">Field name from COBOL copybook</param>
        /// <returns>Field value as ReadOnlySpan&lt;char&gt;</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<char> GetField(ReadOnlySpan<char> fieldName)
        {
            foreach (var field in _fieldDefinitions)
            {
                if (fieldName.SequenceEqual(field.Name.AsSpan()))
                {
                    return ExtractField(field);
                }
            }

            throw new ArgumentException($"Field '{fieldName.ToString()}' not found in record definition");
        }

        /// <summary>
        /// Gets a field value by name with zero allocations.
        /// </summary>
        /// <param name="fieldName">Field name from COBOL copybook</param>
        /// <returns>Field value as ReadOnlySpan&lt;char&gt;</returns>
        public ReadOnlySpan<char> GetField(string fieldName)
        {
            return GetField(fieldName.AsSpan());
        }

        /// <summary>
        /// Tries to get a field value by name, returning empty span if not found.
        /// </summary>
        /// <param name="fieldName">Field name from COBOL copybook</param>
        /// <returns>Field value as ReadOnlySpan&lt;char&gt; or empty span if not found</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<char> TryGetField(ReadOnlySpan<char> fieldName)
        {
            foreach (var field in _fieldDefinitions)
            {
                if (fieldName.SequenceEqual(field.Name.AsSpan()))
                {
                    return ExtractField(field);
                }
            }

            return ReadOnlySpan<char>.Empty;
        }

        /// <summary>
        /// Gets a field value by index with zero allocations.
        /// </summary>
        /// <param name="index">Field index (0-based)</param>
        /// <returns>Field value as ReadOnlySpan&lt;char&gt;</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<char> GetField(int index)
        {
            if ((uint)index >= (uint)_fieldDefinitions.Length)
                throw new ArgumentOutOfRangeException(nameof(index), $"Field index {index} is out of range [0, {_fieldDefinitions.Length - 1}]");

            return ExtractField(_fieldDefinitions[index]);
        }

        /// <summary>
        /// Extracts field data based on field definition with COBOL format handling.
        /// </summary>
        /// <param name="field">Field definition with position and format info</param>
        /// <returns>Extracted field value</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ReadOnlySpan<char> ExtractField(FieldDefinition field)
        {
            if (field.Position + field.Length > _rawData.Length)
                throw new InvalidOperationException($"Field '{field.Name}' extends beyond record boundary");

            var fieldData = _rawData.Slice(field.Position, field.Length);

            // Handle different COBOL PICTURE clause formats
            return field.PictureClause.Type switch
            {
                PictureType.Alphanumeric => HandleAlphanumericField(fieldData, field),
                PictureType.Numeric => HandleNumericField(fieldData, field),
                PictureType.AlphabeticOnly => HandleAlphabeticField(fieldData, field),
                PictureType.PackedDecimal => HandlePackedDecimalField(fieldData, field),
                PictureType.Binary => HandleBinaryField(fieldData, field),
                _ => fieldData
            };
        }

        /// <summary>
        /// Handles alphanumeric fields (PICTURE X) with optional trimming.
        /// </summary>
        private ReadOnlySpan<char> HandleAlphanumericField(ReadOnlySpan<char> fieldData, FieldDefinition field)
        {
            if (field.TrimWhitespace)
            {
                return fieldData.Trim();
            }
            return fieldData;
        }

        /// <summary>
        /// Handles numeric fields (PICTURE 9) with sign processing.
        /// </summary>
        private ReadOnlySpan<char> HandleNumericField(ReadOnlySpan<char> fieldData, FieldDefinition field)
        {
            // Handle leading/trailing signs and packed decimal conversion
            if (field.PictureClause.HasSign)
            {
                return ProcessSignedNumeric(fieldData, field);
            }

            return fieldData.Trim();
        }

        /// <summary>
        /// Handles alphabetic-only fields (PICTURE A).
        /// </summary>
        private ReadOnlySpan<char> HandleAlphabeticField(ReadOnlySpan<char> fieldData, FieldDefinition field)
        {
            return fieldData.Trim();
        }

        /// <summary>
        /// Handles packed decimal fields (COMP-3) with binary-coded decimal conversion.
        /// </summary>
        private ReadOnlySpan<char> HandlePackedDecimalField(ReadOnlySpan<char> fieldData, FieldDefinition field)
        {
            // TODO: Implement packed decimal (COMP-3) conversion
            // This requires binary data handling and BCD conversion
            throw new NotImplementedException("Packed decimal (COMP-3) conversion not yet implemented");
        }

        /// <summary>
        /// Handles binary fields (COMP/COMP-4) with integer conversion.
        /// </summary>
        private ReadOnlySpan<char> HandleBinaryField(ReadOnlySpan<char> fieldData, FieldDefinition field)
        {
            // TODO: Implement binary field conversion
            throw new NotImplementedException("Binary field (COMP/COMP-4) conversion not yet implemented");
        }

        /// <summary>
        /// Processes signed numeric fields with COBOL sign conventions.
        /// </summary>
        private ReadOnlySpan<char> ProcessSignedNumeric(ReadOnlySpan<char> fieldData, FieldDefinition field)
        {
            // TODO: Implement COBOL signed numeric processing
            // Handle separate sign, overpunch, etc.
            return fieldData.Trim();
        }

        /// <summary>
        /// Validates that all field definitions fit within the record length.
        /// </summary>
        private void ValidateFieldDefinitions()
        {
            foreach (var field in _fieldDefinitions)
            {
                if (field.Position < 0)
                    throw new ArgumentException($"Field '{field.Name}' has negative position: {field.Position}");
                if (field.Position + field.Length > _recordLength)
                    throw new ArgumentException($"Field '{field.Name}' extends beyond record length: position {field.Position} + length {field.Length} > {_recordLength}");
            }
        }

        /// <summary>
        /// Creates a dictionary from all fields (allocating operation for compatibility).
        /// </summary>
        /// <returns>Dictionary of field name to field value</returns>
        public Dictionary<string, string> ToDictionary()
        {
            var result = new Dictionary<string, string>(_fieldDefinitions.Length);
            foreach (var field in _fieldDefinitions)
            {
                result[field.Name] = ExtractField(field).ToString();
            }
            return result;
        }
    }

    /// <summary>
    /// Defines a field within a fixed-length record with COBOL copybook support.
    /// </summary>
    public readonly struct FieldDefinition
    {
        public readonly string Name;
        public readonly int Position;
        public readonly int Length;
        public readonly PictureClause PictureClause;
        public readonly bool TrimWhitespace;

        public FieldDefinition(string name, int position, int length, PictureClause pictureClause, bool trimWhitespace = true)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Position = position;
            Length = length;
            PictureClause = pictureClause;
            TrimWhitespace = trimWhitespace;
        }
    }

    /// <summary>
    /// Represents a COBOL PICTURE clause specification.
    /// </summary>
    public readonly struct PictureClause
    {
        public readonly PictureType Type;
        public readonly int Precision;
        public readonly int Scale;
        public readonly bool HasSign;
        public readonly SignType SignType;

        public PictureClause(PictureType type, int precision = 0, int scale = 0, bool hasSign = false, SignType signType = SignType.None)
        {
            Type = type;
            Precision = precision;
            Scale = scale;
            HasSign = hasSign;
            SignType = signType;
        }

        public static PictureClause Alphanumeric(int length) => new(PictureType.Alphanumeric, length);
        public static PictureClause Numeric(int precision, int scale = 0, bool signed = false) => new(PictureType.Numeric, precision, scale, signed);
        public static PictureClause PackedDecimal(int precision, int scale = 0) => new(PictureType.PackedDecimal, precision, scale);
    }

    /// <summary>
    /// COBOL PICTURE clause data types.
    /// </summary>
    public enum PictureType
    {
        Alphanumeric,    // X - any character
        Numeric,         // 9 - numeric only
        AlphabeticOnly,  // A - alphabetic only
        PackedDecimal,   // COMP-3
        Binary,          // COMP/COMP-4
        FloatingPoint    // COMP-1/COMP-2
    }

    /// <summary>
    /// COBOL sign representation types.
    /// </summary>
    public enum SignType
    {
        None,
        Leading,
        Trailing,
        Separate,
        Overpunch
    }
}