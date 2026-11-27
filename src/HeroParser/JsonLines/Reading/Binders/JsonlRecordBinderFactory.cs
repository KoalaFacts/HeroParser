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
    private static readonly ConcurrentDictionary<Type, object> validators = new();
    private static readonly ConcurrentDictionary<Type, (string ClrName, string? CustomJsonName)[]> requiredValueFields = new();

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

    /// <summary>
    /// Registers a generated record validator for the specified record type.
    /// </summary>
    /// <typeparam name="T">The record type the validator handles.</typeparam>
    /// <param name="validator">The validator instance.</param>
    public static void RegisterValidator<T>(IJsonlRecordValidator<T> validator)
    {
        ArgumentNullException.ThrowIfNull(validator);
        validators[typeof(T)] = validator;
    }

    /// <summary>
    /// Tries to get a registered record validator for the specified type.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="validator">The registered validator if found.</param>
    /// <returns><see langword="true"/> if a validator was found; otherwise <see langword="false"/>.</returns>
    public static bool TryGetValidator<T>([NotNullWhen(true)] out IJsonlRecordValidator<T>? validator)
    {
        if (validators.TryGetValue(typeof(T), out var obj))
        {
            validator = (IJsonlRecordValidator<T>)obj;
            return true;
        }

        validator = null;
        return false;
    }

    /// <summary>
    /// Registers a no-op record validator for a <see cref="GenerateBinderAttribute"/> record type with no validation rules.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    public static void RegisterNoOpValidator<T>()
    {
        validators[typeof(T)] = NoOpValidator<T>.Instance;
    }

    /// <summary>
    /// Registers non-nullable value-type members decorated with <c>[Validate(NotNull = true)]</c> so key presence can be checked when a <see cref="System.Text.Json.Serialization.Metadata.JsonTypeInfo"/> contract exposes no property metadata.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="fields">The CLR member names and optional explicit JSON property names.</param>
    public static void RegisterRequiredValueFields<T>((string ClrName, string? CustomJsonName)[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        requiredValueFields[typeof(T)] = fields;
    }

    internal static bool TryGetRequiredValueFields(Type type, [NotNullWhen(true)] out (string ClrName, string? CustomJsonName)[]? fields)
        => requiredValueFields.TryGetValue(type, out fields);

    private sealed class NoOpValidator<T> : IJsonlRecordValidator<T>
    {
        public static readonly NoOpValidator<T> Instance = new();

        public void Validate(T record, int rowNumber)
        {
        }
    }
}
