#if NET9_0_OR_GREATER
using HeroParser;
using HeroParser.SeparatedValues.Records;
using HeroParser.SeparatedValues.Records.Binding;
using Lock = System.Threading.Lock;
#else
using HeroParser;
using HeroParser.SeparatedValues.Records;
using HeroParser.SeparatedValues.Records.Binding;
using Lock = System.Object;
#endif

namespace HeroParser.SeparatedValues.Records.Binding;

/// <summary>
/// Resolves binders from generated code when available, falling back to runtime reflection.
/// </summary>
/// <remarks>
/// Thread-Safety: Registration of binders via <see cref="RegisterGeneratedBinder"/> is thread-safe.
/// Individual binders returned by <see cref="TryGetBinder{T}"/> are not shared between threads
/// and each factory invocation creates a new instance.
/// </remarks>
internal static partial class CsvRecordBinderFactory
{
    private static readonly Dictionary<Type, Func<CsvRecordOptions?, object>> generatedFactories;
    private static readonly Lock syncRoot = new();

    static CsvRecordBinderFactory()
    {
        generatedFactories = [];
        RegisterGeneratedBinders(generatedFactories);
    }

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
    /// Allows generated binders in referencing assemblies to register themselves at module load.
    /// </summary>
    /// <param name="type">The record type the binder handles.</param>
    /// <param name="factory">Factory for creating the binder with options.</param>
    public static void RegisterGeneratedBinder(Type type, Func<CsvRecordOptions?, object> factory)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(factory);

        lock (syncRoot)
        {
            generatedFactories[type] = factory;
        }
    }

    /// <summary>
    /// Populated by the source generator; becomes a no-op when no generators run.
    /// </summary>
    /// <param name="factories">Cache to register binder factories into.</param>
    static partial void RegisterGeneratedBinders(Dictionary<Type, Func<CsvRecordOptions?, object>> factories);
}
