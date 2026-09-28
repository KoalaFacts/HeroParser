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

    /// <summary>Infers a UTF-8 or BOM-marked UTF-16 CSV file's schema from a bounded sample without loading the whole file.</summary>
    /// <param name="path">Path to the CSV file.</param>
    /// <param name="options">Optional inference options.</param>
    /// <param name="cancellationToken">Cancels file reads.</param>
    /// <returns>The inferred columns and number of sampled data rows.</returns>
    public static Task<CsvSchemaInferenceResult> InferSchemaFileAsync(
        string path, CsvSchemaInferenceOptions? options = null, CancellationToken cancellationToken = default)
    {
        return CsvSchemaInference.InferFileAsync(path, options, cancellationToken);
    }

    /// <summary>Infers a stream's CSV schema from a bounded number of data rows without closing the stream.</summary>
    /// <param name="stream">A readable UTF-8 stream or a BOM-marked UTF-16 stream.</param>
    /// <param name="options">Inference options. Non-seekable streams require an explicit delimiter.</param>
    /// <param name="cancellationToken">Cancels reads.</param>
    /// <returns>The inferred columns and number of sampled data rows.</returns>
    /// <remarks>
    /// A seekable stream is restored to its initial position. A non-seekable stream remains open but may
    /// be consumed beyond the final sampled row due to parser read-ahead; do not resume parsing it at
    /// the current position. To parse the same non-seekable input afterwards, buffer or replay it first.
    /// </remarks>
    public static Task<CsvSchemaInferenceResult> InferSchemaAsync(
        Stream stream, CsvSchemaInferenceOptions? options = null, CancellationToken cancellationToken = default)
    {
        return CsvSchemaInference.InferAsync(stream, options, cancellationToken);
    }
}
