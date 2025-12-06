namespace HeroParser.SeparatedValues.Reading.Records;

public sealed partial class CsvRecordReaderBuilder<T>
{
    /// <summary>
    /// Reads records from a CSV string.
    /// </summary>
    public CsvRecordReader<char, T> FromText(string csvText)
    {
        ArgumentNullException.ThrowIfNull(csvText);
        var (parserOptions, recordOptions) = GetOptions();
        return Csv.DeserializeRecords<T>(csvText, recordOptions, parserOptions);
    }
}
