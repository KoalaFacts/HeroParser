using System.Collections.Concurrent;
using HeroParser.SeparatedValues.Reading.Records;
using HeroParser.SeparatedValues.Reading.Shared;

namespace HeroParser.SeparatedValues.Reading.Binders;

/// <summary>
/// Resolves record binders from generated code.
/// Supports both char (UTF-16) and byte (UTF-8) binders with inline or descriptor-based binding.
/// </summary>
/// <remarks>
/// Thread-Safety: All operations are thread-safe. Uses ConcurrentDictionary for lock-free reads.
/// </remarks>
public static class CsvRecordBinderFactory
{
    private static readonly ConcurrentDictionary<Type, object> charBinderFactories = new();
    private static readonly ConcurrentDictionary<Type, object> byteBinderFactories = new();
    private static readonly ConcurrentDictionary<Type, object> descriptorFactories = new();

    #region Char Binder Registration

    /// <summary>
    /// Registers a char binder factory for maximum performance inline binding.
    /// </summary>
    /// <typeparam name="T">The record type the binder handles.</typeparam>
    /// <param name="factory">Factory that creates binder instances.</param>
    public static void RegisterCharBinder<T>(Func<CsvRecordOptions?, ICsvBinder<char, T>> factory)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(factory);
        charBinderFactories[typeof(T)] = factory;
    }

    /// <summary>
    /// Gets a char binder for the specified type, or throws if not found.
    /// </summary>
    public static ICsvBinder<char, T> GetCharBinder<T>(CsvRecordOptions? options = null)
        where T : class, new()
    {
        // First try inline binder (highest performance)
        if (charBinderFactories.TryGetValue(typeof(T), out var binderFactory))
            return ((Func<CsvRecordOptions?, ICsvBinder<char, T>>)binderFactory)(options);

        // Fall back to descriptor-based binder
        if (TryGetDescriptor<T>(out var descriptor) && descriptor is not null)
            return new CsvDescriptorBinder<T>(descriptor, options);

        throw new InvalidOperationException(
            $"No binder found for type {typeof(T).Name}. Add [CsvGenerateBinder] attribute to the type.");
    }

    #endregion

    #region Byte Binder Registration

    /// <summary>
    /// Registers a byte binder factory for maximum performance inline binding with Utf8Parser.
    /// </summary>
    /// <typeparam name="T">The record type the binder handles.</typeparam>
    /// <param name="factory">Factory that creates binder instances.</param>
    public static void RegisterByteBinder<T>(Func<CsvRecordOptions?, ICsvBinder<byte, T>> factory)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(factory);
        byteBinderFactories[typeof(T)] = factory;
    }

    /// <summary>
    /// Gets a byte binder for the specified type, or throws if not found.
    /// </summary>
    public static ICsvBinder<byte, T> GetByteBinder<T>(CsvRecordOptions? options = null)
        where T : class, new()
    {
        if (byteBinderFactories.TryGetValue(typeof(T), out var binderFactory))
            return ((Func<CsvRecordOptions?, ICsvBinder<byte, T>>)binderFactory)(options);

        throw new InvalidOperationException(
            $"No byte binder found for type {typeof(T).Name}. Add [CsvGenerateBinder] attribute to the type.");
    }

    #endregion

    #region Descriptor Registration

    /// <summary>
    /// Registers a descriptor factory for descriptor-based binding.
    /// The descriptor is cached and shared across all binding operations.
    /// </summary>
    /// <typeparam name="T">The record type the descriptor handles.</typeparam>
    /// <param name="factory">Factory for getting the cached descriptor.</param>
    public static void RegisterDescriptor<T>(Func<CsvRecordDescriptor<T>> factory)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(factory);
        descriptorFactories[typeof(T)] = factory;
    }

    /// <summary>
    /// Tries to get a cached descriptor for the specified type.
    /// </summary>
    internal static bool TryGetDescriptor<T>(out CsvRecordDescriptor<T>? descriptor)
        where T : class, new()
    {
        if (descriptorFactories.TryGetValue(typeof(T), out var factory))
        {
            descriptor = ((Func<CsvRecordDescriptor<T>>)factory)();
            return true;
        }

        descriptor = null;
        return false;
    }

    #endregion
}
