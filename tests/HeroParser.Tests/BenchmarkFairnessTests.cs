using System.Globalization;
using System.Text.Json;
using HeroParser.Benchmarks;
using Xunit;

namespace HeroParser.Tests;

[Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
public class BenchmarkFairnessTests
{
    [Theory]
    [InlineData(1_000)]
    [InlineData(10_000)]
    public void MultiSchemaBenchmarks_UseCultureInvariantFractionalAmounts(int rows)
    {
        // CSV decimal fixtures must not depend on the benchmark host culture:
        // "10,5" under fr-FR would become two CSV columns instead of one.
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var benchmark = new MultiSchemaBenchmarks { Rows = rows };
            benchmark.Setup();

            int expectedDetails = 0;
            for (int i = 0; i < rows - 2; i++)
            {
                expectedDetails += 21 * i / 2; // (int)(i * 10.5m), independently.
            }

            Assert.Equal(rows * (rows - 1) / 2, benchmark.SingleSchema_TypedBinder());
            Assert.Equal(expectedDetails, benchmark.MultiSchema_SingleCharDiscriminator());
            Assert.Equal(expectedDetails, benchmark.MultiSchema_ThreeCharDiscriminator());
            Assert.Equal(expectedDetails, benchmark.MultiSchema_IndexBasedDiscriminator());
            Assert.Equal(expectedDetails, benchmark.MultiSchema_SourceGenerated());
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void BinderOverheadBenchmark_ActuallyAllocatesObjects()
    {
        // The benchmark previously constructed an unused SimpleRecord. RyuJIT
        // could remove that allocation, turning the object-allocation case
        // into the same work as the raw column-count baseline.
        const int ROWS = 1_000;
        var benchmark = new BinderOverheadBenchmarks { Rows = ROWS };
        benchmark.Setup();

        int expectedColumns = (ROWS + 1) * 4; // This raw API includes the header.
        Assert.Equal(expectedColumns, benchmark.RawParse_CountColumns());
        Assert.Equal(expectedColumns, benchmark.RawParse_CreateObjects());

        // Warm these exact benchmark methods before comparing allocation bytes.
        // This is an allocation-presence assertion, NOT a speed benchmark.
        for (int i = 0; i < 3; i++)
        {
            _ = benchmark.RawParse_CountColumns();
            _ = benchmark.RawParse_CreateObjects();
            _ = benchmark.RawParse_CreateStrings();
            _ = benchmark.RawParse_CreateObjectsWithStrings();
        }

        long rawBytes = AllocatedBytes(benchmark.RawParse_CountColumns);
        long objectBytes = AllocatedBytes(benchmark.RawParse_CreateObjects);
        Assert.True(objectBytes - rawBytes >= ROWS * 16L,
            $"Object creation was not observable: baseline {rawBytes} B; object path {objectBytes} B.");

        long stringsBytes = AllocatedBytes(benchmark.RawParse_CreateStrings);
        long objectAndStringsBytes = AllocatedBytes(benchmark.RawParse_CreateObjectsWithStrings);
        Assert.True(objectAndStringsBytes - stringsBytes >= ROWS * 16L,
            $"Bound objects may have been elided: string path {stringsBytes} B; object path {objectAndStringsBytes} B.");

        int expectedValues = ROWS * (ROWS - 1) / 2;
        Assert.Equal(expectedValues, benchmark.RawParse_WithIntParse());
        Assert.Equal(expectedValues, benchmark.Manual_SkipHeader());
        Assert.Equal(expectedValues, benchmark.TypedBinder_PreCreatedOptions());
    }

    private static long AllocatedBytes(Func<int> action)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        _ = action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

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
    [InlineData(1_000)]
    [InlineData(10_000)]
    [InlineData(100_000)]
    public void FixedWidthRecordBindingBenchmarks_ReturnExactChecksum(int rows)
    {
        // Each typed row contains Id = i and Value = 100 * i.
        // At 10,000 records the correct sum already exceeds Int32.MaxValue.
        // Verify the actual timed methods, not only their generated fixture.
        var benchmark = new FixedWidthBenchmarks { Rows = rows };
        benchmark.Setup();

        long expected = 101L * rows * (rows + 1) / 2;
        long sourceGenerated = benchmark.BindToRecords_SourceGenerated();
        long reused = benchmark.BindToRecords_ForEach();

        Assert.Equal(expected, sourceGenerated);
        Assert.Equal(expected, reused);
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

    [Theory]
    [InlineData(7)]
    [InlineData(100_000)]
    public async Task HtbBenchmarks_CsvAndBinaryReadersPreserveExactIdChecksum(int rows)
    {
        // The largest fixture has IDs 0..99999: 4,999,950,000 exceeds Int32.
        // Assert the actual timed methods return an untruncated long result.
        var benchmark = new HtbBenchmark { Rows = rows };
        try
        {
            benchmark.Setup();
            long expected = (long)rows * (rows - 1) / 2;
            Assert.Equal(expected, Assert.IsType<long>(benchmark.ReadCsvFromStream()));
            Assert.Equal(expected, Assert.IsType<long>(benchmark.ReadHtbFromStream()));
            Assert.Equal(expected, Assert.IsType<long>(await benchmark.ReadHtbFromFileAsync()));
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    [Fact]
    public void FixedWidthFieldParseBenchmark_PreservesUnsignedAndAlignedFields()
    {
        // 4294967295 is a valid UInt32 but cannot be narrowed to Int32.
        // The timestamp must occupy its exact 20-character field so its final
        // second does not shift into the following 5-character boolean field.
        var benchmark = new FixedWidthFieldParseBenchmarks();
        benchmark.Setup();

        Assert.Equal(4_294_967_423L, Assert.IsType<long>(benchmark.ParseAllFields()));
        Assert.Equal(4_294_967_418L, Assert.IsType<long>(benchmark.ParseIntsOnly()));
        Assert.Equal(56, benchmark.ParseDateTimeOnly());
        Assert.Equal(3, benchmark.ParseDoubleOnly());
    }

    [Fact]
    public void ColumnParseBenchmarks_PreserveFullUnsignedValueAcrossCharAndUtf8()
    {
        // The input's 4,294,967,295 UInt32 used to be narrowed to -1,
        // so the timed checksums silently claimed different work.
        var benchmark = new ColumnParseBenchmarks();
        benchmark.Setup();

        const long EXPECTED_TOTAL = 4_294_967_424L;
        Assert.Equal(EXPECTED_TOTAL, Assert.IsType<long>(benchmark.Char_Parse()));
        Assert.Equal(EXPECTED_TOTAL, Assert.IsType<long>(benchmark.Utf8_Parse()));
        Assert.Equal(35, benchmark.Utf8_Parse_Culture());
    }

    [Theory]
    [InlineData(8, 4)]
    [InlineData(9, 4)]
    [InlineData(1_000, 10)]
    public void ReadInjectionBenchmarks_ActuallyConsumeValidationResults(int rows, int columns)
    {
        // A real dangerous field must change the checksum. The old benchmark
        // discarded every predicate result and could time optimized-away work.
        // Signed numbers and phone numbers must remain safe under both paths.
        var benchmark = new ReadInjectionProtectionBenchmarks { Rows = rows, Columns = columns };
        benchmark.Setup();
        int totalCells = (rows + 1) * columns; // header is intentionally parsed
        int flaggedRows = (rows + 7) / 8; // data rows 0, 8, 16, ...

        Assert.Equal(totalCells, benchmark.Read_NoValidation());

        Assert.Equal(totalCells + flaggedRows, benchmark.Read_OldApproach_NormalData());
        Assert.Equal(totalCells + flaggedRows, benchmark.Read_Optimized_NormalData());
        Assert.Equal(totalCells + flaggedRows, benchmark.Read_OptimizedPerCol_NormalData());

        Assert.Equal(totalCells, benchmark.Read_OldApproach_NumbersData());
        Assert.Equal(totalCells, benchmark.Read_Optimized_NumbersData());
        Assert.Equal(totalCells, benchmark.Read_OptimizedPerCol_NumbersData());

        Assert.Equal(totalCells, benchmark.Read_OldApproach_PhoneData());
        Assert.Equal(totalCells, benchmark.Read_Optimized_PhoneData());
        Assert.Equal(totalCells, benchmark.Read_OptimizedPerCol_PhoneData());
    }

    [Theory]
    [InlineData("Plain", "an ordinary field without CSV escapes")]
    [InlineData("DoubledQuotes", "she said \"hello\" to the reader")]
    [InlineData("Escaped", "she said \"hello\" to the reader")]
    [InlineData("Unicode", "你好 \"世界\" 😀")]
    [InlineData("InvalidUtf8", "\uFFFD\uFFFDx")]
    public void CsvPipeColumnDecodeBenchmarks_PreserveExpectedTextAcrossSegments(string scenario, string expected)
    {
        // Exercise the actual benchmark entry points, not a separate decoder.
        // InvalidUtf8 must replace the malformed lead byte before unescaping a quote.
        var benchmark = new CsvPipeColumnDecodingBenchmarks { Scenario = scenario };
        benchmark.Setup();
        Assert.Equal(expected, benchmark.OwnedColumn());
        Assert.Equal(expected, benchmark.SegmentedColumn());
    }

    [Fact]
    public void CsvPipeColumnDecodeBenchmarks_PreserveLongEscapedField()
    {
        var benchmark = new CsvPipeColumnDecodingBenchmarks { Scenario = "LongEscaped" };
        benchmark.Setup();
        string expected = string.Concat(Enumerable.Repeat("value\" ", 512));
        Assert.Equal(expected, benchmark.OwnedColumn());
        Assert.Equal(expected, benchmark.SegmentedColumn());
    }


    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(1_000)]
    public void WriteInjectionDangerousBenchmarks_ValidateRealPositiveInputs(int rows)
    {
        // Same four logical fields for each policy, including a real formula
        // prefix, safe signed numbers, and a comma requiring CSV quotes.
        // Verify every output byte (including row endings) rather than just
        // timing the safe fast path or comparing string lengths.
        var benchmark = new WriteInjectionDangerousBenchmarks { Rows = rows };
        benchmark.Setup();

        const string RAW_ROW = "=cmd,-100,\"hello,world\",+42\r\n";
        const string SANITIZED_ROW = "cmd,-100,\"hello,world\",+42\r\n";
        const string QUOTE_PREFIX_ROW = "\"'=cmd\",-100,\"hello,world\",+42\r\n";
        const string TAB_PREFIX_ROW = "\"\t=cmd\",-100,\"hello,world\",+42\r\n";

        Assert.Equal(string.Concat(Enumerable.Repeat(RAW_ROW, rows)), benchmark.Write_NoProtection());
        Assert.Equal(string.Concat(Enumerable.Repeat(SANITIZED_ROW, rows)), benchmark.Write_Sanitize());
        Assert.Equal(string.Concat(Enumerable.Repeat(QUOTE_PREFIX_ROW, rows)), benchmark.Write_EscapeWithQuote());
        Assert.Equal(string.Concat(Enumerable.Repeat(TAB_PREFIX_ROW, rows)), benchmark.Write_EscapeWithTab());
    }

    [Theory]
    [InlineData(1_000)]
    [InlineData(10_000)]
    [InlineData(100_000)]
    public void FixedWidthByteSpanBenchmarks_DoNotOverflowOrOmitRows(int rows)
    {
        // Exercise the actual timed char and UTF-8 entry points. The ID sum
        // exceeds Int32.MaxValue at 100,000 rows; adding 100*ID overflows
        // the all-fields variant already at 10,000 rows.
        var benchmark = new FixedWidthByteSpanBenchmarks { Rows = rows };
        benchmark.Setup();

        long idSum = (long)rows * (rows + 1) / 2;
        long nameLengths = 0;
        for (int i = 1; i <= rows; i++)
        {
            nameLengths += 4 + i.ToString(System.Globalization.CultureInfo.InvariantCulture).Length;
        }

        Assert.Equal(idSum, Assert.IsType<long>(benchmark.ParseFromCharSpan()));
        Assert.Equal(idSum, Assert.IsType<long>(benchmark.ParseFromUtf8ByteSpan()));
        // "AllFields" parses ID and Value, and counts the decoded Name span's
        // length; it is not a typed/materialized business record benchmark.
        Assert.Equal(101 * idSum + nameLengths,
            Assert.IsType<long>(benchmark.ParseFromUtf8ByteSpan_AllFields()));
    }

    private static VsSepReadingBenchmarks CreateCsvBenchmark(bool withQuotes)
    {
        var benchmark = new VsSepReadingBenchmarks { Rows = 12, Columns = 5, WithQuotes = withQuotes };
        benchmark.Setup();
        return benchmark;
    }
}
