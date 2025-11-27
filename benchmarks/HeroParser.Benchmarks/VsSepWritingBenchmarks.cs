using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using HeroParser.SeparatedValues;
using HeroParser.SeparatedValues.Writing;
using nietras.SeparatedValues;
using System.Text;

namespace HeroParser.Benchmarks;

/// <summary>
/// Head-to-head CSV WRITING comparison: HeroParser vs Sep library.
/// Sep by nietras (https://github.com/nietras/Sep) is currently one of the fastest CSV parsers for .NET.
/// These benchmarks ensure HeroParser remains competitive with Sep's writing performance.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 5, warmupCount: 3)]
public class VsSepWritingBenchmarks
{
    // Pre-generated data to write
    private string[][] data = null!;

    [Params(100, 1_000, 10_000, 100_000)]
    public int Rows { get; set; }

    [Params(10, 25, 50)]
    public int Columns { get; set; }

    [Params(false, true)]
    public bool WithQuotes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Pre-generate the data to isolate writing performance from data generation
        data = new string[Rows][];
        for (int r = 0; r < Rows; r++)
        {
            data[r] = new string[Columns];
            for (int c = 0; c < Columns; c++)
            {
                // 50% of values contain commas (requiring quoting) when WithQuotes is true
                if (WithQuotes && (r * Columns + c) % 2 == 0)
                {
                    data[r][c] = $"value,{r},{c}"; // Contains comma, needs quoting
                }
                else
                {
                    data[r][c] = $"value_{r}_{c}";
                }
            }
        }
    }

    [Benchmark(Baseline = true, Description = "Sep")]
    public string Sep_Write()
    {
        using var writer = Sep.Writer().ToText();

        for (int r = 0; r < data.Length; r++)
        {
            using var row = writer.NewRow();
            var rowData = data[r];
            for (int c = 0; c < rowData.Length; c++)
            {
                row[$"Col{c}"].Set(rowData[c]);
            }
        }

        return writer.ToString();
    }

    [Benchmark(Description = "HeroParser (row-by-row)")]
    public string HeroParser_WriteRowByRow()
    {
        using var stringWriter = new StringWriter();
        using var writer = Csv.CreateWriter(stringWriter, leaveOpen: true);

        // Write header
        var headers = new string[Columns];
        for (int c = 0; c < Columns; c++)
        {
            headers[c] = $"Col{c}";
        }
        writer.WriteRow(headers);

        // Write data rows
        for (int r = 0; r < data.Length; r++)
        {
            writer.WriteRow(data[r]);
        }

        writer.Flush();
        return stringWriter.ToString();
    }

    [Benchmark(Description = "HeroParser (WriteRow object[])")]
    public string HeroParser_WriteRowObjects()
    {
        using var stringWriter = new StringWriter();
        using var writer = Csv.CreateWriter(stringWriter, leaveOpen: true);

        // Write header
        var headers = new object[Columns];
        for (int c = 0; c < Columns; c++)
        {
            headers[c] = $"Col{c}";
        }
        writer.WriteRow(headers);

        // Write data rows (using object[] overload)
        var rowObjects = new object[Columns];
        for (int r = 0; r < data.Length; r++)
        {
            var rowData = data[r];
            for (int c = 0; c < rowData.Length; c++)
            {
                rowObjects[c] = rowData[c];
            }
            writer.WriteRow(rowObjects);
        }

        writer.Flush();
        return stringWriter.ToString();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Console.WriteLine();
        Console.WriteLine("=== Writing Comparison Analysis ===");
        Console.WriteLine($"Rows: {Rows:N0}, Columns: {Columns}");
        Console.WriteLine($"Data contains values requiring quotes: {WithQuotes}");
        Console.WriteLine($"Total values written: {Rows * Columns:N0}");
        Console.WriteLine();
    }
}
