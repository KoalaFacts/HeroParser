using System.Diagnostics;
using System.Text;

namespace StreamingPrototype;

internal static class Program
{
    private const int Rows = 10_000;
    private const int Columns = 25;

    static void Main()
    {
        var csv = SampleDataGenerator.Generate(Rows, Columns);
        var utf8 = Encoding.UTF8.GetBytes(csv);

        Console.WriteLine($"CSV bytes: {utf8.Length:N0}");

        Verify(utf8);
        Console.WriteLine("Streaming parser matches reference parser ✅");

        RunBenchmarks(utf8);
    }

    private static void Verify(ReadOnlySpan<byte> data)
    {
        int offset = 0;
        int row = 0;
        while (offset < data.Length)
        {
            var fast = StreamingParser.ParseRow(data[offset..]);
            var slow = ReferenceParser.ParseRow(data[offset..]);

            if (fast != slow)
                throw new InvalidOperationException($"Row {row} mismatch fast={fast} slow={slow}");

            if (fast.CharsConsumed == 0)
                break;

            offset += fast.CharsConsumed;
            row++;
        }
    }

    private static void RunBenchmarks(byte[] data)
    {
        const int iterations = 50;
        Console.WriteLine($"Benchmarking {iterations} iterations…");

        double streaming = Benchmark(data, static span => StreamingParser.ParseRow(span), iterations);
        double reference = Benchmark(data, static span => ReferenceParser.ParseRow(span), iterations);

        Console.WriteLine($"Streaming Parser : {streaming:F2} MB/s");
        Console.WriteLine($"Reference Parser : {reference:F2} MB/s");
        Console.WriteLine($"Speedup (x)      : {(reference == 0 ? 0 : streaming / reference):F2}");
    }

    private static double Benchmark(byte[] data, Func<ReadOnlySpan<byte>, RowParseResult> parser, int iterations)
    {
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            int offset = 0;
            while (offset < data.Length)
            {
                var result = parser(data.AsSpan(offset));
                if (result.CharsConsumed == 0)
                    break;
                offset += result.CharsConsumed;
            }
        }
        sw.Stop();

        double totalBytes = (double)data.Length * iterations;
        return totalBytes / sw.Elapsed.TotalSeconds / (1024 * 1024);
    }

    private static class SampleDataGenerator
    {
        public static string Generate(int rows, int columns)
        {
            var sb = new StringBuilder();
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    if (c > 0)
                        sb.Append(',');

                    string value = $"value_{r}_{c}";

                    if (c % 5 == 0)
                        sb.Append('"').Append(value.Replace("\"", "\"\"")).Append('"');
                    else
                        sb.Append(value);
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
