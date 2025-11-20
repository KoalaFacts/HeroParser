using System;
using System.Collections.Generic;

namespace HeroParser;

/// <summary>
/// Resolves binders from generated code when available, falling back to runtime reflection.
/// </summary>
internal static partial class CsvRecordBinderFactory
{
    private static readonly Dictionary<Type, Func<CsvRecordOptions?, object>> GeneratedFactories;
    private static readonly object SyncRoot = new();

    static CsvRecordBinderFactory()
    {
        GeneratedFactories = new Dictionary<Type, Func<CsvRecordOptions?, object>>();
        RegisterGeneratedBinders(GeneratedFactories);
    }

    public static bool TryGetBinder<T>(CsvRecordOptions? options, out CsvRecordBinder<T>? binder)
        where T : class, new()
    {
        if (GeneratedFactories.TryGetValue(typeof(T), out var factory))
        {
            binder = (CsvRecordBinder<T>)factory(options);
            return true;
        }

        binder = null;
        return false;
    }

    /// <summary>
    /// Allows generated binders in referencing assemblies to register themselves at module load.
    /// </summary>
    /// <param name="type">The record type the binder handles.</param>
    /// <param name="factory">Factory for creating the binder with options.</param>
    public static void RegisterGeneratedBinder(Type type, Func<CsvRecordOptions?, object> factory)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(factory);

        lock (SyncRoot)
        {
            GeneratedFactories[type] = factory;
        }
    }

    /// <summary>
    /// Populated by the source generator; becomes a no-op when no generators run.
    /// </summary>
    /// <param name="factories">Cache to register binder factories into.</param>
    static partial void RegisterGeneratedBinders(Dictionary<Type, Func<CsvRecordOptions?, object>> factories);
}
