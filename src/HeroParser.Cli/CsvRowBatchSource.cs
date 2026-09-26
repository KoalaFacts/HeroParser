using HeroParser.SeparatedValues.Reading.Streaming;

namespace HeroParser.Cli;

internal sealed class CsvRowBatchSource : IAsyncDisposable
{
    private readonly CsvAsyncStreamReader reader;

    private CsvRowBatchSource(CsvAsyncStreamReader reader, string[] headers)
    {
        this.reader = reader;
        Headers = headers;
    }

    public string[] Headers { get; }

    public static async Task<CsvRowBatchSource> OpenAsync(string path, char delimiter, int maxColumns = 100)
    {
        var reader = Csv.Read()
            .WithDelimiter(delimiter)
            .WithMaxColumns(maxColumns)
            .WithMaxRows(int.MaxValue)
            .AllowNewlinesInQuotes()
            .FromFileAsync(path);
        try
        {
            if (!await reader.MoveNextAsync().ConfigureAwait(false))
                return new CsvRowBatchSource(reader, []);

            var header = reader.Current;
            var headers = new string[header.ColumnCount];
            for (int i = 0; i < headers.Length; i++)
                headers[i] = header.GetString(i);
            return new CsvRowBatchSource(reader, headers);
        }
        catch
        {
            await reader.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task<long> CountRemainingRowsAsync()
    {
        long count = 0;
        while (await reader.MoveNextAsync().ConfigureAwait(false))
            count++;
        return count;
    }

    public async Task<List<string[]>> ReadBatchAsync(int batchSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        var batch = new List<string[]>(Math.Min(batchSize, 1024));
        while (batch.Count < batchSize && await reader.MoveNextAsync().ConfigureAwait(false))
        {
            var row = reader.Current;
            var values = new string[Headers.Length];
            for (int i = 0; i < values.Length; i++)
                values[i] = i < row.ColumnCount ? row.GetString(i) : "";
            batch.Add(values);
        }
        return batch;
    }

    public ValueTask DisposeAsync() => reader.DisposeAsync();
}
