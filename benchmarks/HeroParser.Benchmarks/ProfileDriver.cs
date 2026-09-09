using System.Diagnostics;
using System.Text;
using HeroParser.SeparatedValues.Core;
using nietras.SeparatedValues;

namespace HeroParser.Benchmarks;

/// <summary>
/// Runs one Sep-comparison workload in a tight loop for a fixed wall time so an external sampling
/// profiler (Linux <c>perf</c> with <c>DOTNET_PerfMapEnabled=1</c>) can attribute cycles to JIT-compiled
/// code. Not a benchmark: it prints a rough time per iteration only as a sanity check.
/// </summary>
/// <remarks>
/// Usage: <c>--profile &lt;case&gt; [seconds]</c>, where case is one of
/// <c>hero-utf8</c>, <c>hero-utf16</c>, <c>sep-utf16</c>, each optionally suffixed <c>-quoted</c>.
/// The data is the 10,000 x 25 set of <see cref="VsSepReadingBenchmarks"/>.
/// </remarks>
internal static class ProfileDriver
{
    public static void Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("usage: --profile <hero-utf8|hero-utf16|sep-utf16>[-quoted] [seconds]");
            return;
        }

        string name = args[1];
        int seconds = args.Length > 2 ? int.Parse(args[2]) : 8;
        bool quoted = name.EndsWith("-quoted", StringComparison.Ordinal);
        string kind = quoted ? name[..^"-quoted".Length] : name;

        string csv = VsSepReadingBenchmarks.GenerateCsv(10_000, 25, quoted);
        byte[] utf8 = Encoding.UTF8.GetBytes(csv);
        var options = new CsvReadOptions
        {
            MaxColumnCount = 1_000,
            MaxRowCount = 1_000_000,
            EnableQuotedFields = quoted,
            AllowNewlinesInsideQuotes = quoted
        };

        Func<int> work = kind switch
        {
            "hero-utf8" => () => ReadHeroBytes(utf8, options),
            "hero-utf16" => () => ReadHeroText(csv, options),
            "sep-utf16" => () => ReadSep(csv),
            _ => throw new ArgumentException($"unknown profile case '{name}'")
        };

        // Warm up through tiering, then loop for the requested wall time.
        var warm = Stopwatch.StartNew();
        int checksum = 0;
        while (warm.Elapsed.TotalSeconds < 2)
            checksum ^= work();

        var clock = Stopwatch.StartNew();
        long iterations = 0;
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            checksum ^= work();
            iterations++;
        }

        double perIteration = clock.Elapsed.TotalMilliseconds * 1000.0 / iterations;
        Console.WriteLine($"{name}: {iterations} iterations in {clock.Elapsed.TotalSeconds:F1} s, {perIteration:F1} us/iteration (checksum {checksum})");
    }

    private static int ReadHeroBytes(byte[] utf8, CsvReadOptions options)
    {
        using var reader = Csv.ReadFromByteSpan(utf8, options);
        int total = 0;
        foreach (var row in reader)
            total += row.ColumnCount;
        return total;
    }

    private static int ReadHeroText(string csv, CsvReadOptions options)
    {
        using var reader = Csv.ReadFromText(csv, options);
        int total = 0;
        foreach (var row in reader)
            total += row.ColumnCount;
        return total;
    }

    private static int ReadSep(string csv)
    {
        using var reader = Sep.Reader().FromText(csv);
        int total = 0;
        foreach (var row in reader)
            total += row.ColCount;
        return total;
    }
}
