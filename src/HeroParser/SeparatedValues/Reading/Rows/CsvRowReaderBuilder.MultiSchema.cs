using HeroParser.SeparatedValues.Reading.Records.MultiSchema;

namespace HeroParser.SeparatedValues.Reading.Rows;

public sealed partial class CsvRowReaderBuilder
{
    /// <summary>
    /// Transitions to multi-schema mode where different rows can be mapped to different record types
    /// based on a discriminator column value.
    /// </summary>
    /// <returns>A builder for configuring multi-schema CSV reading.</returns>
    /// <remarks>
    /// <para>
    /// Multi-schema parsing is common in banking and financial formats like NACHA, BAI, and EDI,
    /// where files contain header, detail, and trailer records identified by a record type code.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// foreach (var record in Csv.Read()
    ///     .WithMultiSchema()
    ///     .WithDiscriminator("Type")
    ///     .MapRecord&lt;HeaderRecord&gt;("H")
    ///     .MapRecord&lt;DetailRecord&gt;("D")
    ///     .MapRecord&lt;TrailerRecord&gt;("T")
    ///     .FromText(csv))
    /// {
    ///     switch (record)
    ///     {
    ///         case HeaderRecord h: // ...
    ///         case DetailRecord d: // ...
    ///         case TrailerRecord t: // ...
    ///     }
    /// }
    /// </code>
    /// </example>
    public CsvMultiSchemaReaderBuilder WithMultiSchema()
    {
        return new CsvMultiSchemaReaderBuilder(GetOptions());
    }
}
