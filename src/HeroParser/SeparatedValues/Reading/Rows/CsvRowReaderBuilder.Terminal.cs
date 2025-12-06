namespace HeroParser.SeparatedValues.Reading.Rows;

public sealed partial class CsvRowReaderBuilder
{
    /// <summary>
    /// Creates a reader from CSV text for manual row-by-row reading.
    /// </summary>
    public CsvRowReader<char> FromText(string csvText)
    {
        ArgumentNullException.ThrowIfNull(csvText);
        var reader = Csv.ReadFromText(csvText, GetOptions());
        SkipInitialRows(ref reader);
        return reader;
    }

    private void SkipInitialRows(ref CsvRowReader<char> reader)
    {
        for (int i = 0; i < skipRows && reader.MoveNext(); i++) { }
    }
}
