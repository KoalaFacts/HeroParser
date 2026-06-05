using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace HeroParser.JsonLines.Reading.Binders;

/// <summary>
/// Resolves JSONL record binders from generated code.
/// </summary>
public static class JsonlRecordBinderFactory
{
    private static readonly ConcurrentDictionary<Type, object> charBinders = new();
    private static readonly ConcurrentDictionary<Type, object> byteBinders = new();

    /// <summary>
    /// Registers a character-based JSONL binder for a record type.
    /// </summary>
    /// <typeparam name="T">The record type the binder handles.</typeparam>
    /// <param name="binder">The binder instance.</param>
    public static void RegisterCharBinder<T>(IJsonlSourceBinder<char, T> binder)
    {
        ArgumentNullException.ThrowIfNull(binder);
        charBinders[typeof(T)] = binder;
    }

    /// <summary>
    /// Registers a byte-based JSONL binder for a record type.
    /// </summary>
    /// <typeparam name="T">The record type the binder handles.</typeparam>
    /// <param name="binder">The binder instance.</param>
    public static void RegisterByteBinder<T>(IJsonlSourceBinder<byte, T> binder)
    {
        ArgumentNullException.ThrowIfNull(binder);
        byteBinders[typeof(T)] = binder;
    }

    /// <summary>
    /// Tries to get a registered character-based JSONL binder for the specified type.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="binder">The registered binder if found.</param>
    /// <returns>True if a binder was found; otherwise false.</returns>
    public static bool TryGetCharBinder<T>([NotNullWhen(true)] out IJsonlSourceBinder<char, T>? binder)
    {
        if (charBinders.TryGetValue(typeof(T), out var obj))
        {
            binder = (IJsonlSourceBinder<char, T>)obj;
            return true;
        }

        binder = null;
        return false;
    }

    /// <summary>
    /// Tries to get a registered byte-based JSONL binder for the specified type.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="binder">The registered binder if found.</param>
    /// <returns>True if a binder was found; otherwise false.</returns>
    public static bool TryGetByteBinder<T>([NotNullWhen(true)] out IJsonlSourceBinder<byte, T>? binder)
    {
        if (byteBinders.TryGetValue(typeof(T), out var obj))
        {
            binder = (IJsonlSourceBinder<byte, T>)obj;
            return true;
        }

        binder = null;
        return false;
    }
}
