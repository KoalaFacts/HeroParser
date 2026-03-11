using System.Collections.Concurrent;
using System.Globalization;

namespace HeroParser.FixedWidths.Records.Binding;

/// <summary>
/// Resolves binders from generated code using descriptor-based binding.
/// </summary>
/// <remarks>
/// Thread-Safety: All operations are thread-safe. Uses ConcurrentDictionary for lock-free reads.
/// Descriptors are immutable and shared, while binders are created per-request.
/// </remarks>
public static class FixedWidthRecordBinderFactory
{
    private static readonly ConcurrentDictionary<Type, object> descriptorFactories = new();
    private static readonly ConcurrentDictionary<Type, object> generatedBinderFactories = new();

    /// <summary>
    /// Registers a descriptor factory for high-performance binding.
    /// The descriptor is cached and shared across all binding operations.
    /// </summary>
    /// <typeparam name="T">The record type the descriptor handles.</typeparam>
    /// <param name="factory">Factory for getting the cached descriptor.</param>
    public static void RegisterDescriptor<T>(Func<FixedWidthRecordDescriptor<T>> factory)
        where T : new()
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
    public static bool TryGetDescriptor<T>(out FixedWidthRecordDescriptor<T>? descriptor)
        where T : new()
    {
        if (descriptorFactories.TryGetValue(typeof(T), out var factory))
        {
            descriptor = ((Func<FixedWidthRecordDescriptor<T>>)factory)();
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
    /// <param name="culture">Culture for parsing.</param>
    /// <param name="nullValues">Values to treat as null.</param>
    /// <param name="binder">The created binder.</param>
    /// <returns>True if a descriptor was found and binder created, false otherwise.</returns>
    public static bool TryCreateDescriptorBinder<T>(
        CultureInfo? culture,
        IReadOnlyList<string>? nullValues,
        out IFixedWidthBinder<T>? binder)
        where T : new()
    {
        if (TryGetDescriptor<T>(out var descriptor) && descriptor is not null)
        {
            binder = new FixedWidthDescriptorBinder<T>(descriptor, culture, nullValues);
            return true;
        }

        binder = null;
        return false;
    }

    /// <summary>
    /// Registers a source-generated binder factory for a record type.
    /// Generated binders support inline validation and are preferred over descriptor binders.
    /// </summary>
    /// <typeparam name="T">The record type the binder handles.</typeparam>
    /// <param name="factory">Factory that creates a binder given culture and null values.</param>
    public static void RegisterGeneratedBinder<T>(
        Func<CultureInfo?, IReadOnlyList<string>?, IFixedWidthBinder<T>> factory)
        where T : new()
    {
        ArgumentNullException.ThrowIfNull(factory);
        generatedBinderFactories[typeof(T)] = factory;
    }

    /// <summary>
    /// Tries to create a source-generated binder for the specified type.
    /// Generated binders support inline validation and are preferred over descriptor binders.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="culture">Culture for parsing.</param>
    /// <param name="nullValues">Values to treat as null.</param>
    /// <param name="binder">The created binder.</param>
    /// <returns>True if a generated binder was found and created, false otherwise.</returns>
    public static bool TryCreateGeneratedBinder<T>(
        CultureInfo? culture,
        IReadOnlyList<string>? nullValues,
        out IFixedWidthBinder<T>? binder)
        where T : new()
    {
        if (generatedBinderFactories.TryGetValue(typeof(T), out var factory))
        {
            binder = ((Func<CultureInfo?, IReadOnlyList<string>?, IFixedWidthBinder<T>>)factory)(culture, nullValues);
            return true;
        }

        binder = null;
        return false;
    }
}
