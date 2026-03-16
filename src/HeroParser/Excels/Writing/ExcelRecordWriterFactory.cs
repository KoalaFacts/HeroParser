using HeroParser.Excels.Core;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace HeroParser.Excels.Writing;

/// <summary>
/// Factory for creating <see cref="ExcelRecordWriter{T}"/> instances.
/// </summary>
/// <remarks>
/// Resolves writers from source-generated code when available, falling back to reflection-based writers.
/// Thread-Safety: All operations are thread-safe. Each call to <see cref="GetWriter{T}"/> creates a new writer
/// instance with its own reusable buffers. Property accessor metadata is cached internally.
/// </remarks>
public static class ExcelRecordWriterFactory
{
    private static readonly ConcurrentDictionary<Type, object> generatedFactories = new();

    /// <summary>
    /// Registers a source-generated writer factory for type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The record type the factory produces writers for.</typeparam>
    /// <param name="factory">Factory delegate that creates a writer given <see cref="ExcelWriteOptions"/>.</param>
    public static void RegisterGeneratedWriter<T>(Func<ExcelWriteOptions, ExcelRecordWriter<T>> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        generatedFactories[typeof(T)] = factory;
    }

    /// <summary>
    /// Registers a source-generated writer factory using a non-generic type key.
    /// Used by source generators that cannot express the generic constraint at registration time.
    /// </summary>
    /// <param name="type">The record type the factory produces writers for.</param>
    /// <param name="factory">Factory delegate returning an untyped writer instance.</param>
    public static void RegisterGeneratedWriter(Type type, Func<ExcelWriteOptions, object> factory)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(factory);
        generatedFactories[type] = factory;
    }

    /// <summary>
    /// Creates a new record writer for the specified type and options.
    /// Prefers source-generated writers when available, falling back to reflection-based writers.
    /// </summary>
    /// <typeparam name="T">The record type to write.</typeparam>
    /// <param name="options">Writer options, or <see langword="null"/> to use <see cref="ExcelWriteOptions.Default"/>.</param>
    /// <returns>A new <see cref="ExcelRecordWriter{T}"/> instance.</returns>
    [RequiresUnreferencedCode("Falls back to reflection when no generated writer is registered. Use [GenerateBinder] for AOT/trimming support.")]
    [RequiresDynamicCode("Falls back to reflection when no generated writer is registered. Use [GenerateBinder] for AOT support.")]
    public static ExcelRecordWriter<T> GetWriter<T>(ExcelWriteOptions? options = null)
    {
        options ??= ExcelWriteOptions.Default;

        if (generatedFactories.TryGetValue(typeof(T), out var factory))
        {
            if (factory is Func<ExcelWriteOptions, ExcelRecordWriter<T>> typedFactory)
                return typedFactory(options);

            if (factory is Func<ExcelWriteOptions, object> objFactory)
                return (ExcelRecordWriter<T>)objFactory(options);
        }

        return new ExcelRecordWriter<T>(options);
    }
}
