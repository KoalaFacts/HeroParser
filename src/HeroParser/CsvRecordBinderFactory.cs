using System;
using System.Collections.Generic;

namespace HeroParser;

/// <summary>
/// Resolves binders from generated code when available, falling back to runtime reflection.
/// </summary>
internal static partial class CsvRecordBinderFactory
{
    private static readonly Dictionary<Type, Func<CsvRecordOptions?, object>> GeneratedFactories;

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
    /// Populated by the source generator; becomes a no-op when no generators run.
    /// </summary>
    /// <param name="factories">Cache to register binder factories into.</param>
    static partial void RegisterGeneratedBinders(Dictionary<Type, Func<CsvRecordOptions?, object>> factories);
}
