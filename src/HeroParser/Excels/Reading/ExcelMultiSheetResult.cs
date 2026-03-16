namespace HeroParser.Excels.Reading;

/// <summary>
/// Contains the results of a multi-sheet Excel reading operation where each sheet
/// is mapped to a different record type.
/// </summary>
public sealed class ExcelMultiSheetResult
{
    private readonly Dictionary<Type, object> results;

    internal ExcelMultiSheetResult(Dictionary<Type, object> results)
    {
        this.results = results;
    }

    /// <summary>
    /// Gets the list of records for the specified type.
    /// </summary>
    /// <typeparam name="T">The record type that was registered with <see cref="ExcelMultiSheetBuilder.WithSheet{T}"/>.</typeparam>
    /// <returns>The list of deserialized records.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the type was not registered in the multi-sheet builder.</exception>
    public List<T> Get<T>() where T : new()
    {
        if (results.TryGetValue(typeof(T), out var value))
            return (List<T>)value;

        throw new InvalidOperationException(
            $"Type '{typeof(T).Name}' was not registered in the multi-sheet builder. " +
            $"Call WithSheet<{typeof(T).Name}>(sheetName) before reading.");
    }
}
