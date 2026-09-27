extern alias baseline;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Text;
using BaselineCsv = baseline::HeroParser.Csv;
using BaselineOptions = baseline::HeroParser.SeparatedValues.Core.CsvReadOptions;
using CandidateCsv = HeroParser.Csv;
using CandidateOptions = HeroParser.SeparatedValues.Core.CsvReadOptions;

if ((OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
    && Environment.GetEnvironmentVariable("HERO_PARSER_AB_AFFINITY") is { Length: > 0 } affinity)
{
    Process.GetCurrentProcess().ProcessorAffinity = (nint)long.Parse(affinity, System.Globalization.NumberStyles.HexNumber);
}

Console.WriteLine($"baseline={typeof(BaselineCsv).Assembly.GetName().Name}, candidate={typeof(CandidateCsv).Assembly.GetName().Name}");
foreach (int columns in new[] { 4, 8 })
{
    var builder = new StringBuilder();
    for (int row = 0; row < 100_000; row++)
    {
        for (int column = 0; column < columns; column++)
        {
            if (column != 0) builder.Append(',');
            builder.Append("val").Append(row).Append('_').Append(column);
        }
        builder.Append('\n');
    }
    byte[] data = Encoding.UTF8.GetBytes(builder.ToString());
    var ratios = new List<double>();
    int wins = 0;
    for (int pair = -6; pair < 20; pair++)
    {
        bool candidateFirst = (pair & 1) == 0;
        double first = await MeasureAsync(data, columns, candidateFirst);
        double second = await MeasureAsync(data, columns, !candidateFirst);
        if (pair < 0) continue;
        double candidate = candidateFirst ? first : second;
        double baseline = candidateFirst ? second : first;
        ratios.Add(candidate / baseline);
        if (candidate < baseline) wins++;
    }
    ratios.Sort();
    Console.WriteLine($"{columns} cols: median={ratios[10]:F3}, p10={ratios[2]:F3}, p90={ratios[18]:F3}, wins={wins}/20");
}

static async Task<double> MeasureAsync(byte[] data, int columns, bool candidate)
{
    double elapsed = 0;
    for (int repeat = 0; repeat < 10; repeat++)
    {
        elapsed += candidate ? await CandidateAsync(data, columns) : await BaselineAsync(data, columns);
    }
    return elapsed;
}

static async Task<double> CandidateAsync(byte[] data, int columns)
{
    using var stream = new MemoryStream(data, writable: false);
    await using var reader = CandidateCsv.CreatePipeSequenceReader(PipeReader.Create(stream), new CandidateOptions
    {
        MaxColumnCount = columns + 4,
        MaxRowCount = 100_100
    });
    long start = Stopwatch.GetTimestamp();
    int count = 0;
    while (await reader.MoveNextAsync()) count += reader.Current.ColumnCount;
    if (count != 100_000 * columns) throw new Exception($"Bad candidate count: {count}");
    return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
}

static async Task<double> BaselineAsync(byte[] data, int columns)
{
    using var stream = new MemoryStream(data, writable: false);
    await using var reader = BaselineCsv.CreatePipeSequenceReader(PipeReader.Create(stream), new BaselineOptions
    {
        MaxColumnCount = columns + 4,
        MaxRowCount = 100_100
    });
    long start = Stopwatch.GetTimestamp();
    int count = 0;
    while (await reader.MoveNextAsync()) count += reader.Current.ColumnCount;
    if (count != 100_000 * columns) throw new Exception($"Bad baseline count: {count}");
    return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
}
