using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace HeroParser.Htbs.Records;

/// <summary>
/// Factory for resolving High-Throughput Tabular Binary (HTB) writers.
/// </summary>
public static class HtbRecordWriterFactory
{
    private static readonly ConcurrentDictionary<Type, object> writerFactories = new();

    /// <summary>
    /// Registers a writer factory for a record type.
    /// </summary>
    public static void RegisterWriter<T>(Func<IHtbWriter<T>> factory) where T : new()
    {
        ArgumentNullException.ThrowIfNull(factory);
        writerFactories[typeof(T)] = factory;
    }

    /// <summary>
    /// Resolves the writer for the specified record type.
    /// </summary>
    public static IHtbWriter<T>? GetWriter<T>() where T : new()
    {
        if (writerFactories.TryGetValue(typeof(T), out var factoryObj))
        {
            if (factoryObj is Func<IHtbWriter<T>> factory)
            {
                return factory();
            }
        }

        return null;
    }
}
