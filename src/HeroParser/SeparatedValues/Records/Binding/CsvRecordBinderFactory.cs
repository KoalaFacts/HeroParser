using System.Collections.Concurrent;
using HeroParser.SeparatedValues.Records;
using HeroParser.SeparatedValues.Records.Binding;

namespace HeroParser.SeparatedValues.Records.Binding;

/// <summary>
/// Resolves binders from generated code using descriptor-based binding.
/// </summary>
/// <remarks>
/// Thread-Safety: All operations are thread-safe. Uses ConcurrentDictionary for lock-free reads.
/// Descriptors are immutable and shared, while binders are created per-request.
/// </remarks>
internal static class CsvRecordBinderFactory
{
    private static readonly ConcurrentDictionary<Type, object> descriptorFactories = new();

    /// <summary>
    /// Registers a descriptor factory for high-performance binding.
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
    public static bool TryGetDescriptor<T>(out CsvRecordDescriptor<T>? descriptor)
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
    /// Creates a descriptor-based binder for maximum performance.
    /// Uses the cached descriptor with the optimized binding loop.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="options">CSV record options.</param>
    /// <param name="binder">The created binder.</param>
    /// <returns>True if a descriptor was found and binder created, false otherwise.</returns>
    public static bool TryCreateDescriptorBinder<T>(CsvRecordOptions? options, out ICsvBinder<T>? binder)
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
