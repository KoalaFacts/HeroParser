using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace HeroParser.Htbs.Records;

/// <summary>
/// Factory for resolving High-Throughput Tabular Binary (HTB) binders.
/// </summary>
public static class HtbRecordBinderFactory
{
    private static readonly ConcurrentDictionary<Type, object> binderFactories = new();

    /// <summary>
    /// Registers a binder factory for a record type.
    /// </summary>
    public static void RegisterBinder<T>(Func<HtbSchema, IHtbBinder<T>> factory) where T : new()
    {
        ArgumentNullException.ThrowIfNull(factory);
        binderFactories[typeof(T)] = factory;
    }

    /// <summary>
    /// Resolves the binder for the specified record type and schema.
    /// Falls back to reflection-based binding if no generated binder is registered.
    /// </summary>
    [RequiresUnreferencedCode("Reflection fallback is not Native AOT-safe.")]
    public static IHtbBinder<T> GetBinder<T>(HtbSchema schema) where T : new()
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (binderFactories.TryGetValue(typeof(T), out var factoryObj))
        {
            if (factoryObj is Func<HtbSchema, IHtbBinder<T>> factory)
            {
                return factory(schema);
            }
        }

        return new HtbRecordBinder<T>(schema);
    }

    /// <summary>
    /// Checks if a binder is registered for the specified type.
    /// </summary>
    public static bool IsBinderRegistered<T>() where T : new()
    {
        return binderFactories.ContainsKey(typeof(T));
    }

    /// <summary>
    /// Tries to resolve a registered binder for the specified type and schema.
    /// Returns null if no generated binder is registered.
    /// </summary>
    public static IHtbBinder<T>? TryGetBinder<T>(HtbSchema schema) where T : new()
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (binderFactories.TryGetValue(typeof(T), out var factoryObj))
        {
            if (factoryObj is Func<HtbSchema, IHtbBinder<T>> factory)
            {
                return factory(schema);
            }
        }
        return null;
    }
}
