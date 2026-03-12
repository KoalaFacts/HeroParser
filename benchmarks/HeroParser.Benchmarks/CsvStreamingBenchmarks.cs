using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using HeroParser.SeparatedValues.Core;
using System.Text;

namespace HeroParser.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class CsvStreamingBenchmarks
{
    private string csv = null!;
    private byte[] csvUtf8 = null!;
    private string filePath = null!;
    private CsvReadOptions options = null!;

    [Params(10_000, 100_000)]
    public int Rows { get; set; }

    [Params(4, 8)]
    public int Columns { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        csv = GenerateCsv(Rows, Columns);
        csvUtf8 = Encoding.UTF8.GetBytes(csv);
        filePath = Path.Combine(Path.GetTempPath(), $"heroparser-csv-stream-{Rows}-{Columns}-{Guid.NewGuid():N}.csv");
        File.WriteAllBytes(filePath, csvUtf8);

        options = new CsvReadOptions
        {
            MaxColumnCount = Columns + 4,
            MaxRowCount = Rows + 100
        };
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
            // best-effort cleanup
        }
    }

    [Benchmark(Baseline = true)]
    public int ParseFromByteSpan()
    {
        using var reader = Csv.ReadFromByteSpan(csvUtf8, options);
        int total = 0;
        foreach (var row in reader)
        {
            total += row.ColumnCount;
        }
        return total;
    }

    [Benchmark]
    public async Task<int> ParseFromAsyncStreamReader_MemoryStream()
    {
        using var stream = new MemoryStream(csvUtf8, writable: false);
        await using var reader = Csv.CreateAsyncStreamReader(stream, options);

        int total = 0;
        while (await reader.MoveNextAsync().ConfigureAwait(false))
        {
            total += reader.Current.ColumnCount;
        }
        return total;
    }

    [Benchmark]
    public async Task<int> ParseFromAsyncStreamReader_File()
    {
        await using var reader = Csv.CreateAsyncStreamReader(filePath, options);

        int total = 0;
        while (await reader.MoveNextAsync().ConfigureAwait(false))
        {
            total += reader.Current.ColumnCount;
        }
        return total;
    }

    private static string GenerateCsv(int rows, int columns)
    {
        var sb = new StringBuilder();
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                if (c > 0)
                {
                    sb.Append(',');
                }

                sb.Append($"val{r}_{c}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }
}
