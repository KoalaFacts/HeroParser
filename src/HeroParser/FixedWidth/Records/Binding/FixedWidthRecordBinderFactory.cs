using System.Collections.Concurrent;
using System.Globalization;

namespace HeroParser.FixedWidths.Records.Binding;

/// <summary>
/// Resolves binders from generated code when available, falling back to runtime reflection.
/// </summary>
/// <remarks>
/// Thread-Safety: All operations are thread-safe. Uses ConcurrentDictionary for lock-free reads.
/// Individual binders returned by <see cref="TryGetBinder{T}"/> are not shared between threads
/// and each factory invocation creates a new instance.
/// </remarks>
internal static partial class FixedWidthRecordBinderFactory
{
    private static readonly ConcurrentDictionary<Type, Func<CultureInfo?, FixedWidthDeserializeErrorHandler?, IReadOnlyList<string>?, object>> generatedFactories = new();

    // Legacy factories without nullValues support
    private static readonly ConcurrentDictionary<Type, Func<CultureInfo?, FixedWidthDeserializeErrorHandler?, object>> legacyFactories = new();

    // Typed binder factories (no boxing)
    private static readonly ConcurrentDictionary<Type, object> typedBinderFactories = new();

    static FixedWidthRecordBinderFactory()
    {
        RegisterGeneratedBinders(generatedFactories);
        RegisterGeneratedBindersLegacy(legacyFactories);
    }

    public static bool TryGetBinder<T>(
        CultureInfo? culture,
        FixedWidthDeserializeErrorHandler? errorHandler,
        IReadOnlyList<string>? nullValues,
        out FixedWidthRecordBinder<T>? binder)
        where T : class, new()
    {
        if (generatedFactories.TryGetValue(typeof(T), out var factory))
        {
            binder = (FixedWidthRecordBinder<T>)factory(culture, errorHandler, nullValues);
            return true;
        }

        // Try legacy factories (without nullValues support)
        if (legacyFactories.TryGetValue(typeof(T), out var legacyFactory))
        {
            binder = (FixedWidthRecordBinder<T>)legacyFactory(culture, errorHandler);
            return true;
        }

        binder = null;
        return false;
    }

    /// <summary>
    /// Tries to get a typed binder that avoids boxing during parsing.
    /// </summary>
    public static bool TryGetTypedBinder<T>(
        CultureInfo? culture,
        IReadOnlyList<string>? nullValues,
        out ITypedBinder<T>? binder)
        where T : class, new()
    {
        if (typedBinderFactories.TryGetValue(typeof(T), out var factoryObj) &&
            factoryObj is Func<CultureInfo?, IReadOnlyList<string>?, ITypedBinder<T>> factory)
        {
            binder = factory(culture, nullValues);
            return true;
        }

        binder = null;
        return false;
    }

    /// <summary>
    /// Registers a typed binder factory for boxing-free parsing.
    /// </summary>
    public static void RegisterTypedBinder<T>(
        Func<CultureInfo?, IReadOnlyList<string>?, ITypedBinder<T>> factory)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(factory);
        typedBinderFactories[typeof(T)] = factory;
    }

    /// <summary>
    /// Allows generated binders in referencing assemblies to register themselves at module load.
    /// </summary>
    /// <param name="type">The record type the binder handles.</param>
    /// <param name="factory">Factory for creating the binder with options.</param>
    public static void RegisterGeneratedBinder(
        Type type,
        Func<CultureInfo?, FixedWidthDeserializeErrorHandler?, IReadOnlyList<string>?, object> factory)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(factory);

        generatedFactories[type] = factory;
    }

    /// <summary>
    /// Populated by the source generator; becomes a no-op when no generators run.
    /// </summary>
    static partial void RegisterGeneratedBinders(
        ConcurrentDictionary<Type, Func<CultureInfo?, FixedWidthDeserializeErrorHandler?, IReadOnlyList<string>?, object>> factories);

    /// <summary>
    /// Legacy registration method for backwards compatibility.
    /// </summary>
    static partial void RegisterGeneratedBindersLegacy(
        ConcurrentDictionary<Type, Func<CultureInfo?, FixedWidthDeserializeErrorHandler?, object>> factories);
}
