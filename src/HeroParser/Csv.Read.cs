using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Binders;
using HeroParser.SeparatedValues.Reading.Records;
using HeroParser.SeparatedValues.Reading.Rows;

namespace HeroParser;

public static partial class Csv
{
    /// <summary>
    /// Creates a fluent builder for reading and deserializing CSV records of type <typeparamref name="T"/>.
    /// </summary>
    public static CsvRecordReaderBuilder<T> Read<T>() where T : class, new() => new();

    /// <summary>
    /// Creates a fluent builder for manual row-by-row CSV reading.
    /// </summary>
    public static CsvRowReaderBuilder Read() => new();

    /// <summary>
    /// Creates a reader that iterates over CSV records stored in a managed <see cref="string"/>.
    /// </summary>
    public static CsvRowReader<char> ReadFromText(string data, CsvParserOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        return ReadFromCharSpan(data.AsSpan(), options);
    }

    /// <summary>
    /// Creates a reader over a UTF-16 span.
    /// </summary>
    public static CsvRowReader<char> ReadFromCharSpan(ReadOnlySpan<char> data, CsvParserOptions? options = null)
    {
        options ??= CsvParserOptions.Default;
        options.Validate();
        return new CsvRowReader<char>(data, options);
    }

    /// <summary>
    /// Creates a reader over UTF-8 encoded CSV data.
    /// </summary>
    public static CsvRowReader<byte> ReadFromByteSpan(ReadOnlySpan<byte> data, CsvParserOptions? options = null)
    {
        options ??= CsvParserOptions.Default;
        options.Validate();

        // Detect UTF-16 BOMs
        if (data.Length >= 2)
        {
            if (data[0] == 0xFF && data[1] == 0xFE)
                throw new CsvException(CsvErrorCode.InvalidOptions, "UTF-16 LE encoding detected. HeroParser only supports UTF-8.");
            if (data[0] == 0xFE && data[1] == 0xFF)
                throw new CsvException(CsvErrorCode.InvalidOptions, "UTF-16 BE encoding detected. HeroParser only supports UTF-8.");
        }

        // Strip UTF-8 BOM
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            data = data[3..];

        return new CsvRowReader<byte>(data, options);
    }

    /// <summary>
    /// Deserializes CSV data from text into strongly typed records.
    /// </summary>
    public static CsvRecordReader<char, T> DeserializeRecords<T>(
        string data,
        CsvRecordOptions? recordOptions = null,
        CsvParserOptions? parserOptions = null)
        where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(data);
        parserOptions ??= CsvParserOptions.Default;
        recordOptions ??= CsvRecordOptions.Default;

        var reader = ReadFromCharSpan(data.AsSpan(), parserOptions);
        var binder = CsvRecordBinderFactory.GetCharBinder<T>(recordOptions);
        return new CsvRecordReader<char, T>(reader, binder, recordOptions.SkipRows,
            recordOptions.Progress, recordOptions.ProgressIntervalRows);
    }

    /// <summary>
    /// Deserializes CSV data from UTF-8 bytes into strongly typed records.
    /// </summary>
    public static CsvRecordReader<byte, T> DeserializeRecordsFromBytes<T>(
        ReadOnlySpan<byte> data,
        CsvRecordOptions? recordOptions = null,
        CsvParserOptions? parserOptions = null)
        where T : class, new()
    {
        parserOptions ??= CsvParserOptions.Default;
        recordOptions ??= CsvRecordOptions.Default;

        var reader = ReadFromByteSpan(data, parserOptions);
        var binder = CsvRecordBinderFactory.GetByteBinder<T>(recordOptions);
        return new CsvRecordReader<byte, T>(reader, binder, recordOptions.SkipRows,
            recordOptions.Progress, recordOptions.ProgressIntervalRows);
    }
}
