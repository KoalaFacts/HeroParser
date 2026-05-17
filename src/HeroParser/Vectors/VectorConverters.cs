using HeroParser.SeparatedValues.Reading.Records;

namespace HeroParser.Vectors;

/// <summary>
/// Ready-made <see cref="CsvTypeConverter{T}"/> instances that plug into
/// <c>CsvRecordReaderBuilder&lt;T&gt;.RegisterConverter(...)</c> for properties of type
/// <see cref="float"/>[] or <see cref="double"/>[].
/// </summary>
/// <example>
/// <code>
/// using HeroParser.Vectors;
///
/// Csv.Read&lt;Document&gt;()
///    .RegisterConverter(VectorConverters.FloatArray)
///    .FromFile("embeddings.csv");
/// </code>
/// </example>
public static class VectorConverters
{
    /// <summary>Converter for <see cref="float"/>[] columns containing inline vectors.</summary>
    public static CsvTypeConverter<float[]> FloatArray { get; } =
        (value, culture, _, out result) => VectorParser.TryParseFloats(value, culture, out result);

    /// <summary>Converter for <see cref="double"/>[] columns containing inline vectors.</summary>
    public static CsvTypeConverter<double[]> DoubleArray { get; } =
        (value, culture, _, out result) => VectorParser.TryParseDoubles(value, culture, out result);
}
