using System.Globalization;

namespace HeroParser.SeparatedValues.Reading.Records;

public sealed partial class CsvRecordReaderBuilder<T>
{
    /// <summary>
    /// Indicates that the CSV includes a header row (default).
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public CsvRecordReaderBuilder<T> WithHeader()
    {
        hasHeaderRow = true;
        return this;
    }

    /// <summary>
    /// Indicates that the CSV does not include a header row.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public CsvRecordReaderBuilder<T> WithoutHeader()
    {
        hasHeaderRow = false;
        return this;
    }

    /// <summary>
    /// Enables case-sensitive header matching.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public CsvRecordReaderBuilder<T> CaseSensitiveHeaders()
    {
        caseSensitiveHeaders = true;
        return this;
    }

    /// <summary>
    /// Allows missing columns without throwing an exception.
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    public CsvRecordReaderBuilder<T> AllowMissingColumns()
    {
        allowMissingColumns = true;
        return this;
    }

    /// <summary>
    /// Sets values that should be treated as null during parsing.
    /// </summary>
    /// <param name="values">The string values to treat as null.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvRecordReaderBuilder<T> WithNullValues(params string[] values)
    {
        nullValues = values;
        return this;
    }

    /// <summary>
    /// Sets the culture for parsing values.
    /// </summary>
    /// <param name="culture">The culture to use.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvRecordReaderBuilder<T> WithCulture(CultureInfo culture)
    {
        this.culture = culture ?? CultureInfo.InvariantCulture;
        return this;
    }

    /// <summary>
    /// Sets the culture for parsing values using a culture name.
    /// </summary>
    /// <param name="cultureName">The culture name (e.g., "en-US", "de-DE").</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvRecordReaderBuilder<T> WithCulture(string cultureName)
    {
        culture = CultureInfo.GetCultureInfo(cultureName);
        return this;
    }

    /// <summary>
    /// Skips the specified number of rows before parsing data.
    /// </summary>
    /// <param name="rowCount">The number of rows to skip.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvRecordReaderBuilder<T> SkipRows(int rowCount)
    {
        skipRows = rowCount;
        return this;
    }

    /// <summary>
    /// Sets the progress reporter for receiving parsing progress updates.
    /// </summary>
    /// <param name="progress">The progress reporter.</param>
    /// <param name="intervalRows">Rows between progress updates (default 1000).</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvRecordReaderBuilder<T> WithProgress(IProgress<CsvProgress> progress, int intervalRows = 1000)
    {
        this.progress = progress;
        progressIntervalRows = intervalRows;
        return this;
    }

    /// <summary>
    /// Registers a custom type converter.
    /// </summary>
    /// <typeparam name="TValue">The type to convert to.</typeparam>
    /// <param name="converter">The converter delegate.</param>
    /// <returns>This builder for method chaining.</returns>
    public CsvRecordReaderBuilder<T> RegisterConverter<TValue>(CsvTypeConverter<TValue> converter)
    {
        ArgumentNullException.ThrowIfNull(converter);

        converterRegistrations ??= [];
        // Store a func that captures the typed converter and returns the new options
        converterRegistrations.Add(options => options.RegisterConverter(converter));
        return this;
    }
}
