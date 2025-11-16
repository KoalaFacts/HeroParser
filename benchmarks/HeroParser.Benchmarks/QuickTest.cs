using System.Diagnostics;
using System.Text;

namespace HeroParser.Benchmarks;

/// <summary>
/// Quick throughput test without BenchmarkDotNet overhead.
/// Useful for rapid iteration and validation.
/// </summary>
public static class QuickTest
{
    public static void Run()
    {
        Console.WriteLine("=== HeroParser Quick Throughput Test ===");
        Console.WriteLine($"Hardware: {HeroParser.Simd.SimdParserFactory.GetHardwareInfo()}");
        Console.WriteLine();

        // Generate test data
        Console.WriteLine("Generating test data...");
        var csv = GenerateCsv(100_000, 10); // 100k rows x 10 columns
        var sizeBytes = csv.Length * sizeof(char);
        Console.WriteLine($"Test data: {csv.Length:N0} chars ({sizeBytes:N0} bytes, {sizeBytes / 1_000_000.0:F2} MB)");
        Console.WriteLine();

        // Warm up
        Console.WriteLine("Warming up...");
        for (int i = 0; i < 3; i++)
        {
            ParseCsv(csv);
        }

        // Benchmark
        Console.WriteLine("Running benchmark...");
        var iterations = 10;
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            ParseCsv(csv);
        }

        sw.Stop();

        // Calculate throughput
        var totalBytes = (long)sizeBytes * iterations;
        var throughputBytesPerSec = totalBytes / sw.Elapsed.TotalSeconds;
        var throughputGBps = throughputBytesPerSec / 1_000_000_000.0;

        Console.WriteLine();
        Console.WriteLine("=== Results ===");
        Console.WriteLine($"Total time: {sw.ElapsedMilliseconds:N0} ms");
        Console.WriteLine($"Average per iteration: {sw.ElapsedMilliseconds / (double)iterations:F2} ms");
        Console.WriteLine($"Throughput: {throughputGBps:F2} GB/s");
        Console.WriteLine();

        if (throughputGBps >= 30)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✓ TARGET ACHIEVED: {throughputGBps:F2} GB/s >= 30 GB/s");
        }
        else if (throughputGBps >= 20)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"⚠ GOOD: {throughputGBps:F2} GB/s (AVX2 range)");
        }
        else if (throughputGBps >= 10)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"⚠ OK: {throughputGBps:F2} GB/s (NEON range)");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"✗ BELOW TARGET: {throughputGBps:F2} GB/s < 30 GB/s");
        }
        Console.ResetColor();
    }

    private static int ParseCsv(string csv)
    {
        var reader = Csv.Parse(csv);
        int total = 0;
        foreach (var row in reader)
        {
            total += row.Count;
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
                if (c > 0) sb.Append(',');
                sb.Append($"val{r}_{c}");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
