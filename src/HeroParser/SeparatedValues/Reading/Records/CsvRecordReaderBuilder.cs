using System.Globalization;
using HeroParser.SeparatedValues.Core;

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

    internal CsvRecordReaderBuilder() { }

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

