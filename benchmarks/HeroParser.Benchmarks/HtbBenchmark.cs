using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using HeroParser.Htbs;

namespace HeroParser.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class HtbBenchmark
{
    private byte[] htbBytes = null!;
    private string csvString = null!;
    private byte[] csvBytes = null!;
    private string filePath = null!;
    private List<FlatRow> rows = null!;

    [Params(10_000, 100_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        rows = new List<FlatRow>(Rows);
        var csvSb = new StringBuilder(Rows * 80);
        csvSb.AppendLine("Id,Name,Amount,Active");

        for (int i = 0; i < Rows; i++)
        {
            string name = $"customer-{i}";
            decimal amount = (decimal)(i * 1.5);
            bool active = (i & 1) == 0;
            rows.Add(new FlatRow { Id = i, Name = name, Amount = amount, Active = active });

            csvSb.Append(i).Append(',')
                 .Append(name).Append(',')
                 .Append(amount.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                 .Append(active ? "True" : "False").Append('\n');
        }

        csvString = csvSb.ToString();
        csvBytes = Encoding.UTF8.GetBytes(csvString);

        // Pre-serialize HTB bytes
        using var ms = new MemoryStream();
        Htb.Write<FlatRow>().ToStream(ms, rows, leaveOpen: true);
        htbBytes = ms.ToArray();

        filePath = Path.Combine(Path.GetTempPath(), $"heroparser-htb-{Rows}-{Guid.NewGuid():N}.htb");
        File.WriteAllBytes(filePath, htbBytes);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                File.Delete(filePath);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    [Benchmark(Baseline = true)]
    public int ReadCsvFromStream()
    {
        using var ms = new MemoryStream(csvBytes, writable: false);
        int total = 0;
        foreach (FlatRow row in Csv.Read<FlatRow>().WithMaxRows(Rows + 10).FromStream(ms, out _))
        {
            total += row.Id;
        }
        return total;
    }

    [Benchmark]
    public int ReadHtbFromStream()
    {
        using var ms = new MemoryStream(htbBytes, writable: false);
        int total = 0;
        foreach (FlatRow row in Htb.Read<FlatRow>().FromStream(ms))
        {
            total += row.Id;
        }
        return total;
    }

    [Benchmark]
    public async Task<int> ReadHtbFromFileAsync()
    {
        int total = 0;
        await foreach (FlatRow row in Htb.Read<FlatRow>().FromFileAsync(filePath))
        {
            total += row.Id;
        }
        return total;
    }

    [Benchmark]
    public long WriteCsvToStream()
    {
        using var ms = new MemoryStream(capacity: csvBytes.Length);
        Csv.Write<FlatRow>().ToStream(ms, rows, leaveOpen: true);
        return ms.Length;
    }

    [Benchmark]
    public long WriteHtbToStream()
    {
        using var ms = new MemoryStream(capacity: htbBytes.Length);
        Htb.Write<FlatRow>().ToStream(ms, rows, leaveOpen: true);
        return ms.Length;
    }

    [GenerateBinder]
    public sealed class FlatRow
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public decimal Amount { get; set; }
        public bool Active { get; set; }
    }
}
