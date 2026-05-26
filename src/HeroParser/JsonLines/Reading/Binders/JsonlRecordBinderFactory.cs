using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace HeroParser.JsonLines.Reading.Binders;

/// <summary>
/// Resolves JSONL record binders from generated code.
/// </summary>
public static class JsonlRecordBinderFactory
{
    private static readonly ConcurrentDictionary<Type, object> binders = new();

    /// <summary>
    /// Registers a JSONL binder for a record type.
    /// </summary>
    /// <typeparam name="T">The record type the binder handles.</typeparam>
    /// <param name="binder">The binder instance.</param>
    public static void RegisterBinder<T>(IJsonlBinder<T> binder)
    {
        ArgumentNullException.ThrowIfNull(binder);
        binders[typeof(T)] = binder;
    }

    /// <summary>
    /// Tries to get a registered JSONL binder for the specified type.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="binder">The registered binder if found.</param>
    /// <returns>True if a binder was found; otherwise false.</returns>
    public static bool TryGetBinder<T>([NotNullWhen(true)] out IJsonlBinder<T>? binder)
    {
        if (binders.TryGetValue(typeof(T), out var obj))
        {
            binder = (IJsonlBinder<T>)obj;
            return true;
        }

        binder = null;
        return false;
    }
}
