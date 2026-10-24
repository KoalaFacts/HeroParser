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

        ValidateInputRecords();

        filePath = Path.Combine(Path.GetTempPath(), $"heroparser-htb-{Rows}-{Guid.NewGuid():N}.htb");
        File.WriteAllBytes(filePath, htbBytes);
    }

    private void ValidateInputRecords()
    {
        // A checksum alone can conceal silently defaulted typed fields.
        // Check every logical value, including the final row, outside timing.
        int csvCount = 0;
        using (var csvStream = new MemoryStream(csvBytes, writable: false))
        {
            foreach (FlatRow record in Csv.Read<FlatRow>().WithMaxRows(Rows + 10).FromStream(csvStream, out _))
            {
                ValidateRecord(record, csvCount, "CSV");
                csvCount++;
            }
        }

        if (csvCount != Rows)
            throw new InvalidOperationException($"CSV fixture yielded {csvCount} records; expected {Rows}.");

        int htbCount = 0;
        using (var htbStream = new MemoryStream(htbBytes, writable: false))
        {
            foreach (FlatRow record in Htb.Read<FlatRow>().FromStream(htbStream))
            {
                ValidateRecord(record, htbCount, "HTB");
                htbCount++;
            }
        }

        if (htbCount != Rows)
            throw new InvalidOperationException($"HTB fixture yielded {htbCount} records; expected {Rows}.");

        long expectedIdSum = (long)Rows * (Rows - 1) / 2;
        if (ReadCsvFromStream() != expectedIdSum || ReadHtbFromStream() != expectedIdSum)
            throw new InvalidOperationException("HTB benchmark timed readers did not preserve the complete ID checksum.");
    }

    private void ValidateRecord(FlatRow actual, int index, string source)
    {
        if (index >= rows.Count)
            throw new InvalidOperationException($"{source} fixture yielded an unexpected extra record {index}.");

        FlatRow expected = rows[index];
        if (actual.Id != expected.Id ||
            !string.Equals(actual.Name, expected.Name, StringComparison.Ordinal) ||
            actual.Amount != expected.Amount ||
            actual.Active != expected.Active)
        {
            throw new InvalidOperationException($"{source} fixture record {index} differs from the generated values.");
        }
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
    public long ReadCsvFromStream()
    {
        using var ms = new MemoryStream(csvBytes, writable: false);
        long total = 0;
        foreach (FlatRow row in Csv.Read<FlatRow>().WithMaxRows(Rows + 10).FromStream(ms, out _))
        {
            total += row.Id;
        }
        return total;
    }

    [Benchmark]
    public long ReadHtbFromStream()
    {
        using var ms = new MemoryStream(htbBytes, writable: false);
        long total = 0;
        foreach (FlatRow row in Htb.Read<FlatRow>().FromStream(ms))
        {
            total += row.Id;
        }
        return total;
    }

    [Benchmark]
    public async Task<long> ReadHtbFromFileAsync()
    {
        long total = 0;
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
