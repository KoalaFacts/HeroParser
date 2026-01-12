using System.Collections.Concurrent;
using HeroParser.SeparatedValues.Reading.Records;
using HeroParser.SeparatedValues.Reading.Shared;

namespace HeroParser.SeparatedValues.Reading.Binders;

/// <summary>
/// Resolves record binders from generated code.
/// Supports byte (UTF-8) binders with inline binding for SIMD-accelerated parsing.
/// Char binders use an adapter over byte binders for backward compatibility.
/// </summary>
/// <remarks>
/// Thread-Safety: All operations are thread-safe. Uses ConcurrentDictionary for lock-free reads.
/// </remarks>
public static class CsvRecordBinderFactory
{
    private static readonly ConcurrentDictionary<Type, object> byteBinderFactories = new();
    private static readonly ConcurrentDictionary<Type, object> descriptorFactories = new();

    #region Char Binder (Adapter over Byte Binder)

    /// <summary>
    /// Gets a char binder for the specified type by adapting the byte binder.
    /// This provides backward compatibility for char-based APIs while using the optimized byte binder internally.
    /// </summary>
    /// <param name="options">Optional record deserialization options.</param>
    /// <param name="delimiter">The delimiter character used in the CSV. Defaults to comma.</param>
    /// <remarks>
    /// <para>
    /// <strong>Performance Warning:</strong> The char binder allocates multiple arrays per row
    /// for the char-to-byte conversion. For high-performance scenarios, use <see cref="GetByteBinder{T}"/>
    /// directly with UTF-8 byte-based APIs (<c>FromFile</c>, <c>FromStream</c>, or <c>FromText</c>
    /// with the <c>out byte[]</c> overload).
    /// </para>
    /// </remarks>
    public static ICsvBinder<char, T> GetCharBinder<T>(CsvRecordOptions? options = null, char delimiter = ',')
        where T : new()
    {
        // Get the byte binder and wrap it in an adapter
        var byteBinder = GetByteBinder<T>(options);
        return new CsvCharToByteBinderAdapter<T>(byteBinder, delimiter);
    }

    #endregion

    #region Byte Binder Registration

    /// <summary>
    /// Registers a byte binder factory for maximum performance inline binding with Utf8Parser.
    /// </summary>
    /// <typeparam name="T">The record type the binder handles.</typeparam>
    /// <param name="factory">Factory that creates binder instances.</param>
    public static void RegisterByteBinder<T>(Func<CsvRecordOptions?, ICsvBinder<byte, T>> factory)
        where T : new()
    {
        ArgumentNullException.ThrowIfNull(factory);
        byteBinderFactories[typeof(T)] = factory;
    }

    /// <summary>
    /// Gets a byte binder for the specified type, or throws if not found.
    /// </summary>
    public static ICsvBinder<byte, T> GetByteBinder<T>(CsvRecordOptions? options = null)
        where T : new()
    {
        if (byteBinderFactories.TryGetValue(typeof(T), out var binderFactory))
        {
            if (binderFactory is not Func<CsvRecordOptions?, ICsvBinder<byte, T>> typedFactory)
                throw new InvalidOperationException($"Byte binder factory for type {typeof(T).Name} was registered with incorrect delegate type.");
            return typedFactory(options);
        }

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
        where T : new()
    {
        ArgumentNullException.ThrowIfNull(factory);
        descriptorFactories[typeof(T)] = factory;
    }

    /// <summary>
    /// Tries to get a cached descriptor for the specified type.
    /// </summary>
    internal static bool TryGetDescriptor<T>(out CsvRecordDescriptor<T>? descriptor)
        where T : new()
    {
        if (descriptorFactories.TryGetValue(typeof(T), out var factory))
        {
            if (factory is not Func<CsvRecordDescriptor<T>> typedFactory)
                throw new InvalidOperationException($"Descriptor factory for type {typeof(T).Name} was registered with incorrect delegate type.");
            descriptor = typedFactory();
            return true;
        }

        descriptor = null;
        return false;
    }

    #endregion
}
