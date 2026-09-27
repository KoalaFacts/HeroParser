namespace HeroParser.Cli;

internal interface IRowBatchSource : IAsyncDisposable
{
    string[] Headers { get; }

    Task<long> CountRemainingRowsAsync();

    Task<List<string[]>> ReadBatchAsync(int batchSize);
}
