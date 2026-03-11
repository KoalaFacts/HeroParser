using HeroParser.SeparatedValues.Detection;

namespace HeroParser;

public static partial class Csv
{
    /// <summary>
    /// Infers the schema of CSV data by analyzing sample rows to detect column types.
    /// </summary>
    /// <param name="data">The CSV data to analyze.</param>
    /// <param name="options">Optional inference options.</param>
    /// <returns>The inferred schema with column types, nullability, and statistics.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the data is empty.</exception>
    /// <remarks>
    /// <para>
    /// Schema inference samples up to 100 rows (configurable) and tries to parse each value
    /// as Boolean, Integer, Long, Decimal, Guid, DateTime, falling back to String.
    /// If a column has mixed types, it falls back to the widest compatible type
    /// (e.g., int + decimal = decimal, int + string = string).
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var schema = Csv.InferSchema(csvData);
    /// foreach (var col in schema.Columns)
    /// {
    ///     Console.WriteLine($"{col.Name}: {col.InferredType}{(col.IsNullable ? "?" : "")}");
    /// }
    /// </code>
    /// </example>
    public static CsvSchemaInferenceResult InferSchema(string data, CsvSchemaInferenceOptions? options = null)
    {
        return CsvSchemaInference.Infer(data, options);
    }
}
