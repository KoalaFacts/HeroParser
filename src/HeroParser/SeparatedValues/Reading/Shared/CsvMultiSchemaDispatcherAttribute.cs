namespace HeroParser.SeparatedValues.Reading.Shared;

/// <summary>
/// Marks a partial class for source-generated multi-schema CSV dispatch.
/// The generator creates optimized switch-based dispatch code that eliminates
/// interface dispatch, dictionary lookups, and boxing overhead.
/// </summary>
/// <remarks>
/// <para>
/// Use this attribute with <see cref="CsvSchemaMappingAttribute"/> to define type mappings.
/// The generator will create an optimized Dispatch method and all binding methods automatically.
/// </para>
/// <example>
/// <code>
/// [CsvGenerateDispatcher(DiscriminatorIndex = 0)]
/// [CsvSchemaMapping("H", typeof(HeaderRecord))]
/// [CsvSchemaMapping("D", typeof(DetailRecord))]
/// [CsvSchemaMapping("T", typeof(TrailerRecord))]
/// public partial class BankingFileDispatcher
/// {
///     // Everything is auto-generated!
/// }
///
/// // Usage:
/// var reader = Csv.Read().FromText(csv);
/// while (reader.MoveNext())
/// {
///     var record = BankingFileDispatcher.Dispatch(reader.Current, rowNumber);
///     // record is typed based on discriminator
/// }
/// </code>
/// </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CsvGenerateDispatcherAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the zero-based column index containing the discriminator value.
    /// Either this or <see cref="DiscriminatorColumn"/> must be set.
    /// </summary>
    public int DiscriminatorIndex { get; set; } = -1;

    /// <summary>
    /// Gets or sets the column name containing the discriminator value.
    /// Either this or <see cref="DiscriminatorIndex"/> must be set.
    /// </summary>
    public string? DiscriminatorColumn { get; set; }

    /// <summary>
    /// Gets or sets whether discriminator matching is case-insensitive. Default is false.
    /// </summary>
    public bool CaseInsensitive { get; set; }
}

/// <summary>
/// Maps a discriminator value to a record type for source-generated multi-schema dispatch.
/// Apply multiple instances of this attribute to a class with <see cref="CsvGenerateDispatcherAttribute"/>.
/// </summary>
/// <remarks>
/// The generator will automatically create binding methods for each mapped type.
/// All mapped types must have the <c>[CsvGenerateBinder]</c> attribute for AOT compatibility.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class CsvSchemaMappingAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance with a discriminator value and record type.
    /// </summary>
    /// <param name="discriminator">The discriminator value that identifies this record type.</param>
    /// <param name="recordType">The record type to bind when this discriminator is matched.</param>
    public CsvSchemaMappingAttribute(string discriminator, Type recordType)
    {
        Discriminator = discriminator;
        RecordType = recordType;
    }

    /// <summary>
    /// Initializes a new instance with an integer discriminator value and record type.
    /// </summary>
    /// <param name="discriminator">The integer discriminator value.</param>
    /// <param name="recordType">The record type to bind when this discriminator is matched.</param>
    public CsvSchemaMappingAttribute(int discriminator, Type recordType)
    {
        Discriminator = discriminator.ToString();
        RecordType = recordType;
    }

    /// <summary>
    /// Gets the discriminator value.
    /// </summary>
    public string Discriminator { get; }

    /// <summary>
    /// Gets the record type to bind.
    /// </summary>
    public Type RecordType { get; }
}
