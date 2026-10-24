using System.Text.Json;
using HeroParser.Benchmarks;
using Xunit;

namespace HeroParser.Tests;

[Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
public class BenchmarkFairnessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SepScan_ProcessesEveryDataRow(bool withQuotes)
    {
        var benchmark = CreateCsvBenchmark(withQuotes);
        Assert.Equal(benchmark.Rows * benchmark.Columns, benchmark.Sep_Parse());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SylvanScan_ProcessesEveryDataRow(bool withQuotes)
    {
        var benchmark = CreateCsvBenchmark(withQuotes);
        Assert.Equal(benchmark.Rows * benchmark.Columns, benchmark.Sylvan_Parse());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CsvScans_ProcessTheSameNumberOfCells(bool withQuotes)
    {
        // Setup also checks every decoded field for all five readers before timing.
        var benchmark = CreateCsvBenchmark(withQuotes);
        int expected = benchmark.Rows * benchmark.Columns;
        Assert.Equal(expected, benchmark.CsvHelper_Parse());
        Assert.Equal(expected, benchmark.HeroParser_ParseString());
        Assert.Equal(expected, benchmark.HeroParser_ParseBytes());
    }

    [Fact]
    public async Task JsonlReaders_SourceGeneratedAndReflectionReadNonDefaultIds()
    {
        var benchmark = new JsonlBenchmark { Rows = 7 };
        try
        {
            benchmark.Setup();
            const long EXPECTED_ID_SUM = 21L;
            Assert.Equal(EXPECTED_ID_SUM, benchmark.ReadFromText());
            Assert.Equal(EXPECTED_ID_SUM, benchmark.ReadFromStream());
            Assert.Equal(EXPECTED_ID_SUM, await benchmark.ReadFromFileAsync());
            Assert.Equal(EXPECTED_ID_SUM, benchmark.ReadFromText_SourceGenerated());
            Assert.Equal(EXPECTED_ID_SUM, benchmark.ReadFromStream_SourceGenerated());
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    [Fact]
    public void JsonlWriters_SourceGeneratedAndReflectionPreserveEveryFieldAndCamelCase()
    {
        var benchmark = new JsonlBenchmark { Rows = 7 };
        try
        {
            benchmark.Setup();
            string generated = benchmark.WriteToText_SourceGenerated();
            Assert.Equal(benchmark.WriteToText(), generated);
            string[] lines = generated.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(benchmark.Rows, lines.Length);
            for (int i = 0; i < lines.Length; i++)
            {
                using var document = JsonDocument.Parse(lines[i]);
                var row = document.RootElement;
                Assert.Equal(i, row.GetProperty("id").GetInt32());
                Assert.Equal($"customer-{i}", row.GetProperty("name").GetString());
                Assert.Equal(i * 1.5m, row.GetProperty("amount").GetDecimal());
                Assert.Equal((i & 1) == 0, row.GetProperty("active").GetBoolean());
            }
            // A stream length match alone cannot catch two different payloads;
            // Setup verifies every UTF-8 byte from both stream variants.
            long expectedBytes = System.Text.Encoding.UTF8.GetByteCount(generated);
            Assert.Equal(expectedBytes, benchmark.WriteToStream());
            Assert.Equal(expectedBytes, benchmark.WriteToStream_SourceGenerated());
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    [Fact]
    public async Task JsonlConversionBenchmark_HandlesLargestFixtureIncludingHeader()
    {
        // CSV-to-JSONL reads the header as a parser row as well as the data.
        // The previous benchmark used the default 100,000-row limit for
        // 100,000 data records + one header, so its largest case threw.
        var benchmark = new JsonlBenchmark { Rows = 100_000 };
        try
        {
            benchmark.Setup();
            using var reader = new StringReader(benchmark.ConvertCsvToJsonlFlat());
            int count = 0;
            while (reader.ReadLine() is not null)
                count++;
            Assert.Equal(benchmark.Rows, count);
            // Int32 cannot represent the 100,000-row ID sum (4,999,950,000).
            // Test the actual timed readers, not only their setup validation.
            const long EXPECTED_ID_SUM = 4_999_950_000L;
            Assert.Equal(EXPECTED_ID_SUM, benchmark.ReadFromText());
            Assert.Equal(EXPECTED_ID_SUM, benchmark.ReadFromStream());
            Assert.Equal(EXPECTED_ID_SUM, benchmark.ReadFromText_SourceGenerated());
            Assert.Equal(EXPECTED_ID_SUM, benchmark.ReadFromStream_SourceGenerated());
            Assert.Equal(EXPECTED_ID_SUM, await benchmark.ReadFromFileAsync());
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    [Theory]
    [InlineData(10_000)]
    [InlineData(100_000)]
    public async Task FixedWidthTypedPipeBenchmarks_PreserveEveryIdAndFullName(int rows)
    {
        // The old D4 ID formatting grew to 5/6 characters above 9999, shifting
        // the following 6-character Name field. The largest fixture also exceeds
        // Int32 when summing every ID: 1+...+100000 = 5,000,050,000.
        // Exercise the actual four timed methods and require a checksum derived
        // independently from the intended fixed 6-digit ID / 7-char Name schema.
        var benchmark = new FixedWidthTypedPipeReaderBenchmarks { Rows = rows };
        benchmark.Setup();
        long expected = (long)rows * (rows + 1) / 2 + 7L * rows;

        long text = benchmark.BindFromText_SourceGenerated();
        long pipe = await benchmark.BindFromPipeReader_Static();
        long builder = await benchmark.BindFromPipeReader_Builder();
        long mapped = await benchmark.BindFromPipeReader_BuilderWithMap();

        Assert.Equal(expected, text);
        Assert.Equal(expected, pipe);
        Assert.Equal(expected, builder);
        Assert.Equal(expected, mapped);
    }

    private static VsSepReadingBenchmarks CreateCsvBenchmark(bool withQuotes)
    {
        var benchmark = new VsSepReadingBenchmarks { Rows = 12, Columns = 5, WithQuotes = withQuotes };
        benchmark.Setup();
        return benchmark;
    }
}
