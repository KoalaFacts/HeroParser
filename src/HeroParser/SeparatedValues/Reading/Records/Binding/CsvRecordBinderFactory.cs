using System.Collections.Concurrent;
using HeroParser.SeparatedValues.Reading.Records;

namespace HeroParser.SeparatedValues.Reading.Records.Binding;

/// <summary>
/// Resolves binders from generated code.
/// Supports both inline binders (highest performance) and descriptor-based binders.
/// </summary>
/// <remarks>
/// Thread-Safety: All operations are thread-safe. Uses ConcurrentDictionary for lock-free reads.
/// </remarks>
public static class CsvRecordBinderFactory
{
    private static readonly ConcurrentDictionary<Type, object> binderFactories = new();
    private static readonly ConcurrentDictionary<Type, object> descriptorFactories = new();

    /// <summary>
    /// Registers a binder factory for maximum performance inline binding.
    /// The factory creates binder instances with all parsing logic inlined.
    /// </summary>
    /// <typeparam name="T">The record type the binder handles.</typeparam>
    /// <param name="factory">Factory that creates binder instances.</param>
    public static void RegisterBinder<T>(Func<CsvRecordOptions?, ICsvBinder<T>> factory)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(factory);
        binderFactories[typeof(T)] = factory;
    }

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
    /// Descriptors are immutable and thread-safe, shared across all binding operations.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="descriptor">The descriptor if found.</param>
    /// <returns>True if a descriptor was found, false otherwise.</returns>
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

    /// <summary>
    /// Creates a binder for the specified type. Prefers inline binders over descriptor-based.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="options">CSV record options.</param>
    /// <param name="binder">The created binder.</param>
    /// <returns>True if a binder was found and created, false otherwise.</returns>
    internal static bool TryCreateBinder<T>(CsvRecordOptions? options, out ICsvBinder<T>? binder)
        where T : class, new()
    {
        // First try inline binder (highest performance)
        if (binderFactories.TryGetValue(typeof(T), out var binderFactory))
        {
            binder = ((Func<CsvRecordOptions?, ICsvBinder<T>>)binderFactory)(options);
            return true;
        }

        // Fall back to descriptor-based binder
        if (TryGetDescriptor<T>(out var descriptor) && descriptor is not null)
        {
            binder = new CsvDescriptorBinder<T>(descriptor, options);
            return true;
        }

        binder = null;
        return false;
    }

    /// <summary>
    /// Creates a descriptor-based binder for maximum performance.
    /// Uses the cached descriptor with the optimized binding loop.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="options">CSV record options.</param>
    /// <param name="binder">The created binder.</param>
    /// <returns>True if a descriptor was found and binder created, false otherwise.</returns>
    internal static bool TryCreateDescriptorBinder<T>(CsvRecordOptions? options, out ICsvBinder<T>? binder)
        where T : class, new()
    {
        if (TryGetDescriptor<T>(out var descriptor) && descriptor is not null)
        {
            binder = new CsvDescriptorBinder<T>(descriptor, options);
            return true;
        }

        binder = null;
        return false;
    }
}
