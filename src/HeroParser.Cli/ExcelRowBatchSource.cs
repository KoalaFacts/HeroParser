using HeroParser.Excels.Reading.Data;

namespace HeroParser.Cli;

internal sealed class ExcelRowBatchSource : IRowBatchSource
{
    private readonly ExcelDataReader reader;

    private ExcelRowBatchSource(ExcelDataReader reader)
    {
        this.reader = reader;
        Headers = new string[reader.FieldCount];
        for (int i = 0; i < Headers.Length; i++)
            Headers[i] = reader.GetName(i);
    }

    public string[] Headers { get; }

    public static ExcelRowBatchSource Open(string path, string? sheet)
    {
        var reader = Excel.CreateDataReader(path,
            new ExcelDataReaderOptions { AllowMissingColumns = true }, sheet ?? "Sheet1");
        try
        {
            return new ExcelRowBatchSource(reader);
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    public Task<long> CountRemainingRowsAsync()
    {
        long count = 0;
        while (reader.Read())
            count++;
        return Task.FromResult(count);
    }

    public Task<List<string[]>> ReadBatchAsync(int batchSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        var batch = new List<string[]>(Math.Min(batchSize, 1024));
        while (batch.Count < batchSize && reader.Read())
        {
            var values = new string[Headers.Length];
            for (int i = 0; i < values.Length; i++)
                values[i] = reader.GetValue(i) as string ?? "";
            batch.Add(values);
        }
        return Task.FromResult(batch);
    }

    public ValueTask DisposeAsync() => reader.DisposeAsync();
}
