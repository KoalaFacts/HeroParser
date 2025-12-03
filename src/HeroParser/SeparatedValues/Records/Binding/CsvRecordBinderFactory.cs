using System.Collections.Concurrent;
using System.Globalization;
using HeroParser.SeparatedValues.Records;
using HeroParser.SeparatedValues.Records.Binding;

namespace HeroParser.SeparatedValues.Records.Binding;

/// <summary>
/// Resolves binders from generated code when available, falling back to runtime reflection.
/// </summary>
/// <remarks>
/// Thread-Safety: All operations are thread-safe. Uses ConcurrentDictionary for lock-free reads.
/// Individual binders returned by <see cref="TryGetBinder{T}"/> are not shared between threads
/// and each factory invocation creates a new instance.
/// </remarks>
internal static partial class CsvRecordBinderFactory
{
    private static readonly ConcurrentDictionary<Type, Func<CsvRecordOptions?, object>> generatedFactories = new();
    private static readonly ConcurrentDictionary<Type, Func<CsvRecordOptions?, object>> typedBinderFactories = new();

    static CsvRecordBinderFactory()
    {
        RegisterGeneratedBinders(generatedFactories);
    }

    /// <summary>
    /// Tries to get a generated binder for the specified type.
    /// </summary>
    public static bool TryGetBinder<T>(CsvRecordOptions? options, out CsvRecordBinder<T>? binder)
        where T : class, new()
    {
        if (generatedFactories.TryGetValue(typeof(T), out var factory))
        {
            binder = (CsvRecordBinder<T>)factory(options);
            return true;
        }

        binder = null;
        return false;
    }

    /// <summary>
    /// Tries to get a typed binder (boxing-free) for the specified type.
    /// </summary>
    /// <typeparam name="T">The record type to bind.</typeparam>
    /// <param name="options">CSV record options.</param>
    /// <param name="binder">The typed binder if found.</param>
    /// <returns>True if a typed binder was found, false otherwise.</returns>
    public static bool TryGetTypedBinder<T>(CsvRecordOptions? options, out ICsvTypedBinder<T>? binder)
        where T : class, new()
    {
        if (typedBinderFactories.TryGetValue(typeof(T), out var factory))
        {
            binder = (ICsvTypedBinder<T>)factory(options);
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

        generatedFactories[type] = factory;
    }

    /// <summary>
    /// Registers a typed binder factory for maximum performance (boxing-free).
    /// Called by source-generated code at module initialization.
    /// </summary>
    /// <typeparam name="T">The record type the binder handles.</typeparam>
    /// <param name="factory">Factory for creating the typed binder with options.</param>
    public static void RegisterTypedBinder<T>(Func<CsvRecordOptions?, ICsvTypedBinder<T>> factory)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(factory);
        typedBinderFactories[typeof(T)] = options => factory(options);
    }

    /// <summary>
    /// Populated by the source generator; becomes a no-op when no generators run.
    /// </summary>
    /// <param name="factories">Cache to register binder factories into.</param>
    static partial void RegisterGeneratedBinders(ConcurrentDictionary<Type, Func<CsvRecordOptions?, object>> factories);
}
