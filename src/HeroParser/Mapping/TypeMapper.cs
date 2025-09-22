using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using HeroParser.Configuration;

namespace HeroParser.Mapping
{
    /// <summary>
    /// High-performance type mapping system with zero-allocation conversions.
    /// Provides AOT-compatible type mapping with built-in and custom converter support.
    /// </summary>
    public static class TypeMapper
    {
        private static readonly Dictionary<Type, ITypeConverter> s_converters = new();
        private static readonly object s_lock = new();

        /// <summary>
        /// Static constructor to register built-in type converters.
        /// </summary>
        static TypeMapper()
        {
            RegisterBuiltInConverters();
        }

        /// <summary>
        /// Converts a field value to the specified type using registered converters.
        /// </summary>
        /// <typeparam name="T">Target type</typeparam>
        /// <param name="value">Field value to convert</param>
        /// <param name="fieldName">Field name for error reporting</param>
        /// <returns>Converted value</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Convert<T>(ReadOnlySpan<char> value, string? fieldName = null)
        {
            var targetType = typeof(T);

            // Fast path for common types to avoid dictionary lookup
            if (targetType == typeof(string))
                return (T)(object)value.ToString();

            if (targetType == typeof(int))
                return (T)(object)ConvertToInt32(value, fieldName);

            if (targetType == typeof(long))
                return (T)(object)ConvertToInt64(value, fieldName);

            if (targetType == typeof(decimal))
                return (T)(object)ConvertToDecimal(value, fieldName);

            if (targetType == typeof(DateTime))
                return (T)(object)ConvertToDateTime(value, fieldName);

            // Handle nullable types
            if (IsNullableType(targetType))
            {
                if (value.IsEmpty || value.IsWhiteSpace())
                    return default!;

                var underlyingType = Nullable.GetUnderlyingType(targetType)!;
                var convertedValue = ConvertToType(value, underlyingType, fieldName);
                return (T)convertedValue;
            }

            // Use registered converter
            return (T)ConvertToType(value, targetType, fieldName);
        }

        /// <summary>
        /// Registers a custom type converter.
        /// </summary>
        /// <typeparam name="T">Type to convert to</typeparam>
        /// <param name="converter">Type converter</param>
        public static void RegisterConverter<T>(ITypeConverter<T> converter)
        {
            if (converter == null) throw new ArgumentNullException(nameof(converter));

            lock (s_lock)
            {
                s_converters[typeof(T)] = new TypeConverterWrapper<T>(converter);
            }
        }

        /// <summary>
        /// Converts a value to the specified type using registered converters.
        /// </summary>
        /// <param name="value">Value to convert</param>
        /// <param name="targetType">Target type</param>
        /// <param name="fieldName">Field name for error reporting</param>
        /// <returns>Converted value</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static object ConvertToType(ReadOnlySpan<char> value, Type targetType, string? fieldName = null)
        {
            if (s_converters.TryGetValue(targetType, out var converter))
            {
                return converter.Convert(value, fieldName);
            }

            // Fallback to built-in conversions
            return ConvertBuiltInType(value, targetType, fieldName);
        }

        /// <summary>
        /// Registers built-in type converters for common types.
        /// </summary>
        private static void RegisterBuiltInConverters()
        {
            // Primitive types
            s_converters[typeof(string)] = new StringConverter();
            s_converters[typeof(int)] = new Int32Converter();
            s_converters[typeof(long)] = new Int64Converter();
            s_converters[typeof(decimal)] = new DecimalConverter();
            s_converters[typeof(DateTime)] = new DateTimeConverter();
            s_converters[typeof(bool)] = new BooleanConverter();
            s_converters[typeof(double)] = new DoubleConverter();
            s_converters[typeof(float)] = new SingleConverter();
            s_converters[typeof(short)] = new Int16Converter();
            s_converters[typeof(byte)] = new ByteConverter();
            s_converters[typeof(Guid)] = new GuidConverter();
        }

        /// <summary>
        /// Determines if a type is a nullable value type.
        /// </summary>
        /// <param name="type">Type to check</param>
        /// <returns>True if nullable value type</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsNullableType(Type type)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);
        }

        /// <summary>
        /// Converts built-in types without using the converter dictionary.
        /// </summary>
        /// <param name="value">Value to convert</param>
        /// <param name="targetType">Target type</param>
        /// <param name="fieldName">Field name for error reporting</param>
        /// <returns>Converted value</returns>
        private static object ConvertBuiltInType(ReadOnlySpan<char> value, Type targetType, string? fieldName)
        {
            if (targetType == typeof(string))
                return value.ToString();

            if (targetType == typeof(int))
                return ConvertToInt32(value, fieldName);

            if (targetType == typeof(long))
                return ConvertToInt64(value, fieldName);

            if (targetType == typeof(decimal))
                return ConvertToDecimal(value, fieldName);

            if (targetType == typeof(DateTime))
                return ConvertToDateTime(value, fieldName);

            if (targetType == typeof(bool))
                return ConvertToBoolean(value, fieldName);

            if (targetType == typeof(double))
                return ConvertToDouble(value, fieldName);

            if (targetType == typeof(float))
                return ConvertToSingle(value, fieldName);

            if (targetType == typeof(short))
                return ConvertToInt16(value, fieldName);

            if (targetType == typeof(byte))
                return ConvertToByte(value, fieldName);

            if (targetType == typeof(Guid))
                return ConvertToGuid(value, fieldName);

            throw new NotSupportedException($"Type {targetType.Name} is not supported for conversion");
        }

        // Fast conversion methods for common types
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int ConvertToInt32(ReadOnlySpan<char> value, string? fieldName)
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
                return result;

            throw new FormatException($"Unable to convert '{value.ToString()}' to Int32{(fieldName != null ? $" for field '{fieldName}'" : "")}");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long ConvertToInt64(ReadOnlySpan<char> value, string? fieldName)
        {
            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
                return result;

            throw new FormatException($"Unable to convert '{value.ToString()}' to Int64{(fieldName != null ? $" for field '{fieldName}'" : "")}");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static decimal ConvertToDecimal(ReadOnlySpan<char> value, string? fieldName)
        {
            if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var result))
                return result;

            throw new FormatException($"Unable to convert '{value.ToString()}' to Decimal{(fieldName != null ? $" for field '{fieldName}'" : "")}");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static DateTime ConvertToDateTime(ReadOnlySpan<char> value, string? fieldName)
        {
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
                return result;

            throw new FormatException($"Unable to convert '{value.ToString()}' to DateTime{(fieldName != null ? $" for field '{fieldName}'" : "")}");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ConvertToBoolean(ReadOnlySpan<char> value, string? fieldName)
        {
            // Handle common boolean representations
            if (value.Length == 1)
            {
                var c = char.ToLowerInvariant(value[0]);
                if (c == 't' || c == '1' || c == 'y') return true;
                if (c == 'f' || c == '0' || c == 'n') return false;
            }

            if (bool.TryParse(value, out var result))
                return result;

            throw new FormatException($"Unable to convert '{value.ToString()}' to Boolean{(fieldName != null ? $" for field '{fieldName}'" : "")}");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double ConvertToDouble(ReadOnlySpan<char> value, string? fieldName)
        {
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
                return result;

            throw new FormatException($"Unable to convert '{value.ToString()}' to Double{(fieldName != null ? $" for field '{fieldName}'" : "")}");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ConvertToSingle(ReadOnlySpan<char> value, string? fieldName)
        {
            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
                return result;

            throw new FormatException($"Unable to convert '{value.ToString()}' to Single{(fieldName != null ? $" for field '{fieldName}'" : "")}");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static short ConvertToInt16(ReadOnlySpan<char> value, string? fieldName)
        {
            if (short.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
                return result;

            throw new FormatException($"Unable to convert '{value.ToString()}' to Int16{(fieldName != null ? $" for field '{fieldName}'" : "")}");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte ConvertToByte(ReadOnlySpan<char> value, string? fieldName)
        {
            if (byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
                return result;

            throw new FormatException($"Unable to convert '{value.ToString()}' to Byte{(fieldName != null ? $" for field '{fieldName}'" : "")}");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Guid ConvertToGuid(ReadOnlySpan<char> value, string? fieldName)
        {
            if (Guid.TryParse(value, out var result))
                return result;

            throw new FormatException($"Unable to convert '{value.ToString()}' to Guid{(fieldName != null ? $" for field '{fieldName}'" : "")}");
        }
    }

    /// <summary>
    /// Interface for custom type converters.
    /// </summary>
    /// <typeparam name="T">Type to convert to</typeparam>
    public interface ITypeConverter<T>
    {
        /// <summary>
        /// Converts a field value to the target type.
        /// </summary>
        /// <param name="value">Field value</param>
        /// <param name="fieldName">Field name for error reporting</param>
        /// <returns>Converted value</returns>
        T Convert(ReadOnlySpan<char> value, string? fieldName = null);
    }

    /// <summary>
    /// Non-generic interface for type converters.
    /// </summary>
    internal interface ITypeConverter
    {
        /// <summary>
        /// Converts a field value to the target type.
        /// </summary>
        /// <param name="value">Field value</param>
        /// <param name="fieldName">Field name for error reporting</param>
        /// <returns>Converted value</returns>
        object Convert(ReadOnlySpan<char> value, string? fieldName = null);
    }

    /// <summary>
    /// Wrapper to adapt generic type converters to non-generic interface.
    /// </summary>
    /// <typeparam name="T">Target type</typeparam>
    internal sealed class TypeConverterWrapper<T> : ITypeConverter
    {
        private readonly ITypeConverter<T> _converter;

        public TypeConverterWrapper(ITypeConverter<T> converter)
        {
            _converter = converter;
        }

        public object Convert(ReadOnlySpan<char> value, string? fieldName = null)
        {
            return _converter.Convert(value, fieldName)!;
        }
    }

    // Built-in converter implementations
    internal sealed class StringConverter : ITypeConverter
    {
        public object Convert(ReadOnlySpan<char> value, string? fieldName = null) => value.ToString();
    }

    internal sealed class Int32Converter : ITypeConverter
    {
        public object Convert(ReadOnlySpan<char> value, string? fieldName = null)
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
                return result;
            throw new FormatException($"Unable to convert '{value.ToString()}' to Int32{(fieldName != null ? $" for field '{fieldName}'" : "")}");
        }
    }

    internal sealed class Int64Converter : ITypeConverter
    {
        public object Convert(ReadOnlySpan<char> value, string? fieldName = null)
        {
            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
                return result;
            throw new FormatException($"Unable to convert '{value.ToString()}' to Int64{(fieldName != null ? $" for field '{fieldName}'" : "")}");
        }
    }

    internal sealed class DecimalConverter : ITypeConverter
    {
        public object Convert(ReadOnlySpan<char> value, string? fieldName = null)
        {
            if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var result))
                return result;
            throw new FormatException($"Unable to convert '{value.ToString()}' to Decimal{(fieldName != null ? $" for field '{fieldName}'" : "")}");
        }
    }

    internal sealed class DateTimeConverter : ITypeConverter
    {
        public object Convert(ReadOnlySpan<char> value, string? fieldName = null)
        {
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
                return result;
            throw new FormatException($"Unable to convert '{value.ToString()}' to DateTime{(fieldName != null ? $" for field '{fieldName}'" : "")}");
        }
    }

    internal sealed class BooleanConverter : ITypeConverter
    {
        public object Convert(ReadOnlySpan<char> value, string? fieldName = null)
        {
            // Handle common boolean representations
            if (value.Length == 1)
            {
                var c = char.ToLowerInvariant(value[0]);
                if (c == 't' || c == '1' || c == 'y') return true;
                if (c == 'f' || c == '0' || c == 'n') return false;
            }

            if (bool.TryParse(value, out var result))
                return result;
            throw new FormatException($"Unable to convert '{value.ToString()}' to Boolean{(fieldName != null ? $" for field '{fieldName}'" : "")}");
        }
    }

    internal sealed class DoubleConverter : ITypeConverter
    {
        public object Convert(ReadOnlySpan<char> value, string? fieldName = null)
        {
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
                return result;
            throw new FormatException($"Unable to convert '{value.ToString()}' to Double{(fieldName != null ? $" for field '{fieldName}'" : "")}");
        }
    }

    internal sealed class SingleConverter : ITypeConverter
    {
        public object Convert(ReadOnlySpan<char> value, string? fieldName = null)
        {
            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
                return result;
            throw new FormatException($"Unable to convert '{value.ToString()}' to Single{(fieldName != null ? $" for field '{fieldName}'" : "")}");
        }
    }

    internal sealed class Int16Converter : ITypeConverter
    {
        public object Convert(ReadOnlySpan<char> value, string? fieldName = null)
        {
            if (short.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
                return result;
            throw new FormatException($"Unable to convert '{value.ToString()}' to Int16{(fieldName != null ? $" for field '{fieldName}'" : "")}");
        }
    }

    internal sealed class ByteConverter : ITypeConverter
    {
        public object Convert(ReadOnlySpan<char> value, string? fieldName = null)
        {
            if (byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
                return result;
            throw new FormatException($"Unable to convert '{value.ToString()}' to Byte{(fieldName != null ? $" for field '{fieldName}'" : "")}");
        }
    }

    internal sealed class GuidConverter : ITypeConverter
    {
        public object Convert(ReadOnlySpan<char> value, string? fieldName = null)
        {
            if (Guid.TryParse(value, out var result))
                return result;
            throw new FormatException($"Unable to convert '{value.ToString()}' to Guid{(fieldName != null ? $" for field '{fieldName}'" : "")}");
        }
    }
}