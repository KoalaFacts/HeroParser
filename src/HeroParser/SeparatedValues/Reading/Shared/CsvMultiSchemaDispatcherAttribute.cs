namespace HeroParser.SeparatedValues.Reading.Shared;

/// <summary>
/// Marks a partial class for source-generated multi-schema CSV dispatch.
/// The generator creates optimized switch-based dispatch code that eliminates
/// interface dispatch, dictionary lookups, and boxing overhead.
/// </summary>
/// <remarks>
/// <para>
/// Use this attribute with <see cref="CsvDiscriminatorAttribute"/> on partial methods
/// to define type mappings. The generator will create an optimized Dispatch method.
/// </para>
/// <example>
/// <code>
/// [CsvMultiSchemaDispatcher(DiscriminatorIndex = 0)]
/// public partial class BankingFileDispatcher
/// {
///     [CsvDiscriminator("H")]
///     public static partial HeaderRecord? BindHeader(CsvRow&lt;char&gt; row, int rowNumber);
///
///     [CsvDiscriminator("D")]
///     public static partial DetailRecord? BindDetail(CsvRow&lt;char&gt; row, int rowNumber);
///
///     [CsvDiscriminator("T")]
///     public static partial TrailerRecord? BindTrailer(CsvRow&lt;char&gt; row, int rowNumber);
/// }
///
/// // Usage:
/// foreach (var row in Csv.Read().Rows().FromText(csv))
/// {
///     var record = BankingFileDispatcher.Dispatch(row, rowNumber);
///     // record is typed based on discriminator
/// }
/// </code>
/// </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CsvMultiSchemaDispatcherAttribute : Attribute
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
/// Maps a discriminator value to a partial method that binds the corresponding record type.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class CsvDiscriminatorAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance with the discriminator value.
    /// </summary>
    /// <param name="value">The discriminator value that identifies this record type.</param>
    public CsvDiscriminatorAttribute(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Initializes a new instance with an integer discriminator value.
    /// </summary>
    /// <param name="value">The integer discriminator value.</param>
    public CsvDiscriminatorAttribute(int value)
    {
        Value = value.ToString();
    }

    /// <summary>
    /// Gets the discriminator value.
    /// </summary>
    public string Value { get; }
}
