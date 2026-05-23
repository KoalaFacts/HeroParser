using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Shared;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Drives the QuotesDisabled compile-time-specialized SIMD code paths in
/// CsvRowParser by setting <see cref="CsvReadOptions.EnableQuotedFields"/> = false.
/// Hits 200+ lines of byte/char/Avx2/Avx512 SIMD fast-path code in CsvRowParser.cs
/// (lines 585-731, 1010-1170, 1454-1620) that are JIT-eliminated when quotes
/// are enabled.
/// </summary>
[Trait("Category", "Unit")]
[Collection("AsyncWriterTests")] // shares HardwareCapabilities state with other SIMD tests
public class CsvQuotesDisabledSimdTests
{
    private static readonly CsvReadOptions noQuotes = new() { EnableQuotedFields = false };
    private static readonly CsvReadOptions noQuotesNoSimd = new() { EnableQuotedFields = false, UseSimdIfAvailable = false };

    private static int CountByteRows(string csv, CsvReadOptions options)
    {
        var bytes = Encoding.UTF8.GetBytes(csv);
        var reader = HeroParser.Csv.ReadFromByteSpan(bytes, options);
        int n = 0;
        while (reader.MoveNext()) n++;
        return n;
    }

    private static int CountCharRows(string csv, CsvReadOptions options)
    {
        var reader = HeroParser.Csv.ReadFromCharSpan(csv.AsSpan(), options);
        int n = 0;
        while (reader.MoveNext()) n++;
        return n;
    }

    private static string SampleManyRows(int n)
    {
        var sb = new StringBuilder("A,B,C\n");
        for (int i = 0; i < n; i++) sb.AppendLine($"row{i}A,row{i}B,row{i}C");
        return sb.ToString();
    }

    private static string SampleWideRow(int columns, int rows)
    {
        var sb = new StringBuilder();
        for (int j = 0; j < columns; j++)
        {
            if (j > 0) sb.Append(',');
            sb.Append($"col{j}");
        }
        sb.AppendLine();
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < columns; j++)
            {
                if (j > 0) sb.Append(',');
                sb.Append($"r{i}c{j}");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    // ───── Byte path (UTF-8 SIMD) ─────

    [Fact]
    public void Bytes_QuotesDisabled_BasicRows()
    {
        Assert.Equal(101, CountByteRows(SampleManyRows(100), noQuotes));
    }

    [Fact]
    public void Bytes_QuotesDisabled_ManyDelimitersPerRow()
    {
        // Wide rows with many delimiters trigger the FAST PATH 1 (delimMask == mask) loop.
        Assert.Equal(11, CountByteRows(SampleWideRow(20, 10), noQuotes));
    }

    [Fact]
    public void Bytes_QuotesDisabled_LongRowSpansChunks()
    {
        var bigField = new string('a', 8192);
        var csv = $"A,B\n{bigField},x\n";
        Assert.Equal(2, CountByteRows(csv, noQuotes));
    }

    [Fact]
    public void Bytes_QuotesDisabled_LiteralQuoteCharacter_PreservedAsData()
    {
        // With quotes disabled, " is just a normal character in the data.
        var csv = "A,B\n\"x\",\"y\"\n";
        Assert.Equal(2, CountByteRows(csv, noQuotes));
    }

    [Fact]
    public void Bytes_QuotesDisabled_CrLfRows()
    {
        var csv = "A,B\r\n1,2\r\n3,4\r\n";
        Assert.Equal(3, CountByteRows(csv, noQuotes));
    }

    [Fact]
    public void Bytes_QuotesDisabled_CustomDelimiter()
    {
        var opts = new CsvReadOptions { EnableQuotedFields = false, Delimiter = '|' };
        Assert.Equal(3, CountByteRows("A|B|C\nfoo|bar|baz\nfoo2|bar2|baz2\n", opts));
    }

    [Fact]
    public void Bytes_QuotesDisabled_AllSimdLevels_AgreeOnRowCount()
    {
        var csv = SampleManyRows(50);
        var defaultCount = CountByteRows(csv, noQuotes);

        using (HardwareCapabilities.Override(avx512BW: false))
            Assert.Equal(defaultCount, CountByteRows(csv, noQuotes));
        using (HardwareCapabilities.Override(avx512BW: false, avx2: false))
            Assert.Equal(defaultCount, CountByteRows(csv, noQuotes));
        Assert.Equal(defaultCount, CountByteRows(csv, noQuotesNoSimd));
    }

    // ───── Char path (UTF-16 SIMD) ─────

    [Fact]
    public void Chars_QuotesDisabled_BasicRows()
    {
        Assert.Equal(101, CountCharRows(SampleManyRows(100), noQuotes));
    }

    [Fact]
    public void Chars_QuotesDisabled_ManyDelimitersPerRow()
    {
        Assert.Equal(11, CountCharRows(SampleWideRow(20, 10), noQuotes));
    }

    [Fact]
    public void Chars_QuotesDisabled_LongRowSpansChunks()
    {
        var bigField = new string('a', 8192);
        var csv = $"A,B\n{bigField},x\n";
        Assert.Equal(2, CountCharRows(csv, noQuotes));
    }

    [Fact]
    public void Chars_QuotesDisabled_LiteralQuoteCharacter_PreservedAsData()
    {
        var csv = "A,B\n\"x\",\"y\"\n";
        Assert.Equal(2, CountCharRows(csv, noQuotes));
    }

    [Fact]
    public void Chars_QuotesDisabled_CrLfRows()
    {
        var csv = "A,B\r\n1,2\r\n3,4\r\n";
        Assert.Equal(3, CountCharRows(csv, noQuotes));
    }

    [Fact]
    public void Chars_QuotesDisabled_CustomDelimiter_Tab()
    {
        var opts = new CsvReadOptions { EnableQuotedFields = false, Delimiter = '\t' };
        Assert.Equal(3, CountCharRows("A\tB\nfoo\tbar\nbaz\tqux\n", opts));
    }

    [Fact]
    public void Chars_QuotesDisabled_Avx2_Path()
    {
        // Force AVX-512 off so the AVX2 char path runs.
        using (HardwareCapabilities.Override(avx512BW: false))
        {
            Assert.Equal(101, CountCharRows(SampleManyRows(100), noQuotes));
        }
    }

    [Fact]
    public void Chars_QuotesDisabled_Scalar_Path()
    {
        using (HardwareCapabilities.Override(avx512BW: false, avx2: false))
        {
            Assert.Equal(101, CountCharRows(SampleManyRows(100), noQuotes));
        }
    }

    // ───── MaxColumnCount + MaxFieldSize on QuotesDisabled SIMD path ─────

    [Fact]
    public void Bytes_QuotesDisabled_MaxColumnCount_Throws()
    {
        var opts = new CsvReadOptions { EnableQuotedFields = false, MaxColumnCount = 5 };
        Assert.Throws<CsvException>(() => CountByteRows(SampleWideRow(20, 5), opts));
    }

    [Fact]
    public void Chars_QuotesDisabled_MaxColumnCount_Throws()
    {
        var opts = new CsvReadOptions { EnableQuotedFields = false, MaxColumnCount = 5 };
        Assert.Throws<CsvException>(() => CountCharRows(SampleWideRow(20, 5), opts));
    }

    [Fact]
    public void Bytes_QuotesDisabled_MaxFieldSize_Throws()
    {
        var opts = new CsvReadOptions { EnableQuotedFields = false, MaxFieldSize = 100 };
        var bigField = new string('a', 200);
        Assert.Throws<CsvException>(() => CountByteRows($"A,B\n{bigField},x\n", opts));
    }

    [Fact]
    public void Chars_QuotesDisabled_MaxFieldSize_Throws()
    {
        var opts = new CsvReadOptions { EnableQuotedFields = false, MaxFieldSize = 100 };
        var bigField = new string('a', 200);
        Assert.Throws<CsvException>(() => CountCharRows($"A,B\n{bigField},x\n", opts));
    }

    [Fact]
    public void Bytes_QuotesDisabled_TrackLineNumbers()
    {
        var opts = new CsvReadOptions { EnableQuotedFields = false, TrackSourceLineNumbers = true };
        Assert.Equal(101, CountByteRows(SampleManyRows(100), opts));
    }

    [Fact]
    public void Chars_QuotesDisabled_TrackLineNumbers()
    {
        var opts = new CsvReadOptions { EnableQuotedFields = false, TrackSourceLineNumbers = true };
        Assert.Equal(101, CountCharRows(SampleManyRows(100), opts));
    }
}
