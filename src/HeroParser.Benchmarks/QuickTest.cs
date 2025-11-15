using System.Diagnostics;
using System.Text;

namespace HeroParser.Benchmarks;

/// <summary>
/// Quick performance test (no BenchmarkDotNet overhead).
/// Useful for rapid iteration during development.
/// </summary>
public static class QuickTest
{
    public static void Run()
    {
        Console.WriteLine("Generating test data...");
        var csv = GenerateLargeCsv(100_000, 25); // ~10 MB
        var csvSize = csv.Length * sizeof(char);
        Console.WriteLine($"Test CSV: {csvSize / 1024.0 / 1024.0:F2} MB ({csv.Length:N0} chars)");
        Console.WriteLine();

        // Warmup
        Console.WriteLine("Warming up...");
        for (int i = 0; i < 3; i++)
        {
            RunParse(csv);
        }
        Console.WriteLine("Warmup complete.");
        Console.WriteLine();

        // Actual measurement
        Console.WriteLine("Running benchmark (10 iterations)...");
        var times = new List<double>();

        for (int i = 0; i < 10; i++)
        {
            var sw = Stopwatch.StartNew();
            int count = RunParse(csv);
            sw.Stop();

            var throughput = csvSize / sw.Elapsed.TotalSeconds / 1_000_000_000.0; // GB/s
            times.Add(throughput);

            Console.WriteLine($"  Iteration {i + 1}: {sw.ElapsedMilliseconds:N0} ms - {throughput:F2} GB/s - {count} columns total");
        }

        Console.WriteLine();
        Console.WriteLine($"Average:  {times.Average():F2} GB/s");
        Console.WriteLine($"Median:   {times.OrderBy(x => x).ElementAt(times.Count / 2):F2} GB/s");
        Console.WriteLine($"Best:     {times.Max():F2} GB/s");
        Console.WriteLine($"Worst:    {times.Min():F2} GB/s");
        Console.WriteLine();

        if (times.Average() > 21.0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("🎉 SUCCESS! Beat Sep's 21 GB/s benchmark!");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"⚠️  Not quite there yet. Need {21.0 - times.Average():F2} GB/s more to beat Sep.");
            Console.ResetColor();
        }
    }

    private static int RunParse(string csv)
    {
        int totalColumns = 0;
        foreach (var row in Csv.Parse(csv.AsSpan()))
        {
            totalColumns += row.Count;
        }
        return totalColumns;
    }

    private static string GenerateLargeCsv(int rows, int columns)
    {
        var sb = new StringBuilder();

        // Header
        for (int c = 0; c < columns; c++)
        {
            if (c > 0) sb.Append(',');
            sb.Append($"Column{c}");
        }
        sb.AppendLine();

        // Data rows
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                if (c > 0) sb.Append(',');
                sb.Append($"Value{r}_{c}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
