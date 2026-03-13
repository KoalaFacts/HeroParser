using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Mapping;

namespace HeroParser.SeparatedValues.Reading.Records;

/// <summary>
/// Fluent builder for configuring and executing CSV reading operations with record deserialization.
/// </summary>
/// <typeparam name="T">The record type to deserialize.</typeparam>
public sealed partial class CsvRecordReaderBuilder<T> where T : new()
{
    // Parser options
    private char delimiter = ',';
    private char quote = '"';
    private int maxColumnCount = 100;
    private int maxRowCount = 100_000;
    private bool useSimdIfAvailable = true;
    private bool allowNewlinesInQuotes = false;
    private bool enableQuotedFields = true;
    private char? commentCharacter = null;
    private bool trimFields = false;
    private int? maxFieldSize = null;
    private char? escapeCharacter = null;
    private int? maxRowSize = 512 * 1024;

    // Record options
    private bool hasHeaderRow = true;
    private bool caseSensitiveHeaders = false;
    private bool allowMissingColumns = false;
    private IReadOnlyList<string>? nullValues = null;
    private CultureInfo culture = CultureInfo.InvariantCulture;
    private int skipRows = 0;
    private IProgress<CsvProgress>? progress = null;
    private int progressIntervalRows = 1000;
    private List<Func<CsvRecordOptions, CsvRecordOptions>>? converterRegistrations;

    // Fluent mapping
    private ICsvReadMapSource<T>? mapSource;

    internal CsvRecordReaderBuilder() { }

    /// <summary>
    /// Gets whether a fluent map has been configured via <see cref="WithMap"/> or <see cref="Map{TProperty}"/>.
    /// </summary>
    internal bool HasMap => mapSource is not null;

    /// <summary>
    /// Configures the builder to use a fluent <see cref="CsvMap{T}"/> for column mapping and validation.
    /// When set, terminal methods produce <c>char</c>-based readers using descriptor binding.
    /// </summary>
    /// <param name="map">The pre-configured CSV map instance.</param>
    /// <returns>This builder for method chaining.</returns>
    [RequiresUnreferencedCode("Fluent mapping uses reflection. Use [GenerateBinder] for AOT/trimming support.")]
    [RequiresDynamicCode("Fluent mapping uses expression compilation. Use [GenerateBinder] for AOT support.")]
    public CsvRecordReaderBuilder<T> WithMap(ICsvReadMapSource<T> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        mapSource = map;
        return this;
    }

    /// <summary>
    /// Maps a property to a CSV column inline, creating a <see cref="CsvMap{T}"/> if one has not been set.
    /// Cannot be mixed with <see cref="WithMap"/>; use one approach or the other.
    /// </summary>
    /// <typeparam name="TProperty">The property type.</typeparam>
    /// <param name="property">An expression selecting the property to map (e.g., <c>t =&gt; t.Name</c>).</param>
    /// <param name="configure">Optional column configuration action.</param>
    /// <returns>This builder for method chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown when <see cref="WithMap"/> was already called.</exception>
    [RequiresUnreferencedCode("Fluent mapping uses reflection. Use [GenerateBinder] for AOT/trimming support.")]
    [RequiresDynamicCode("Fluent mapping uses expression compilation. Use [GenerateBinder] for AOT support.")]
    public CsvRecordReaderBuilder<T> Map<TProperty>(
        Expression<Func<T, TProperty>> property,
        Action<CsvColumnBuilder>? configure = null)
    {
        if (mapSource is not null and not InlineCsvMapWrapper<T>)
        {
            throw new InvalidOperationException(
                "Cannot call Map() after WithMap(). Either use WithMap() with a fully configured CsvMap<T>, " +
                "or use Map() calls exclusively for inline mapping.");
        }

        var wrapper = (mapSource as InlineCsvMapWrapper<T>) ?? CreateInlineWrapper();
        wrapper.Map(property, configure);
        return this;
    }

    [RequiresUnreferencedCode("Fluent mapping uses reflection. Use [GenerateBinder] for AOT/trimming support.")]
    [RequiresDynamicCode("Fluent mapping uses expression compilation. Use [GenerateBinder] for AOT support.")]
    private InlineCsvMapWrapper<T> CreateInlineWrapper()
    {
        var wrapper = new InlineCsvMapWrapper<T>();
        mapSource = wrapper;
        return wrapper;
    }

    private (CsvReadOptions parser, CsvRecordOptions record) GetOptions()
    {
        var parser = new CsvReadOptions
        {
            Delimiter = delimiter,
            Quote = quote,
            MaxColumnCount = maxColumnCount,
            MaxRowCount = maxRowCount,
            UseSimdIfAvailable = useSimdIfAvailable,
            AllowNewlinesInsideQuotes = allowNewlinesInQuotes,
            EnableQuotedFields = enableQuotedFields,
            CommentCharacter = commentCharacter,
            TrimFields = trimFields,
            MaxFieldSize = maxFieldSize,
            EscapeCharacter = escapeCharacter,
            MaxRowSize = maxRowSize
        };

        var record = CreateRecordOptions();

        return (parser, record);
    }

    private CsvRecordOptions CreateRecordOptions()
    {
        var options = new CsvRecordOptions
        {
            HasHeaderRow = hasHeaderRow,
            CaseSensitiveHeaders = caseSensitiveHeaders,
            AllowMissingColumns = allowMissingColumns,
            NullValues = nullValues,
            Culture = culture,
            SkipRows = skipRows,
            Progress = progress,
            ProgressIntervalRows = progressIntervalRows
        };

        // Apply custom converter registrations
        if (converterRegistrations is { Count: > 0 })
        {
            foreach (var registration in converterRegistrations)
            {
                options = registration(options);
            }
        }

        return options;
    }
}
