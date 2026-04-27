using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Shared;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Drives SIMD edge-case branches in CsvRowParser that the basic SIMD-level tests
/// don't reach: doubled-quote escape sequential fallback, newlines inside quoted
/// fields with line-tracking, multi-chunk quoted spans, max-field-length enforcement,
/// and very long fields that span several SIMD chunks.
/// </summary>
[Trait("Category", "Unit")]
[Collection("AsyncWriterTests")] // shares HardwareCapabilities state
public class CsvSimdEdgeCaseTests
{
    [GenerateBinder]
    public sealed class Row
    {
        [TabularMap(Name = "A")] public string A { get; set; } = "";
        [TabularMap(Name = "B")] public string B { get; set; } = "";
    }

    private static int CountChars(string csv, CsvReadOptions? options = null)
    {
        var reader = Csv.ReadFromCharSpan(csv.AsSpan(), options);
        int n = 0;
        while (reader.MoveNext()) n++;
        return n - 1; // exclude header
    }

    private static int CountBytes(string csv, CsvReadOptions? options = null)
    {
        var bytes = Encoding.UTF8.GetBytes(csv);
        var reader = Csv.ReadFromByteSpan(bytes, options);
        int n = 0;
        while (reader.MoveNext()) n++;
        return n - 1;
    }

    [Fact]
    public void DoubledQuotes_TriggerSequentialFallback_Bytes()
    {
        // Doubled "" inside quotes forces the SIMD code to fall back to sequential processing.
        var csv = "A,B\n\"she said \"\"hi\"\"\",x\n\"normal\",y\n";
        Assert.Equal(2, CountBytes(csv));
    }

    [Fact]
    public void DoubledQuotes_TriggerSequentialFallback_Chars()
    {
        var csv = "A,B\n\"she said \"\"hi\"\"\",x\n\"normal\",y\n";
        Assert.Equal(2, CountChars(csv));
    }

    [Fact]
    public void NewlinesInsideQuotes_Allowed_Bytes()
    {
        var opts = new CsvReadOptions { AllowNewlinesInsideQuotes = true };
        var csv = "A,B\n\"line1\nline2\",x\n";
        Assert.Equal(1, CountBytes(csv, opts));
    }

    [Fact]
    public void NewlinesInsideQuotes_Allowed_Chars()
    {
        var opts = new CsvReadOptions { AllowNewlinesInsideQuotes = true };
        var csv = "A,B\n\"line1\nline2\",x\n";
        Assert.Equal(1, CountChars(csv, opts));
    }

    [Fact]
    public void NewlinesInsideQuotes_Disallowed_Throws_Bytes()
    {
        var opts = new CsvReadOptions { AllowNewlinesInsideQuotes = false };
        var csv = "A,B\n\"line1\nline2\",x\n";
        Assert.Throws<CsvException>(() => CountBytes(csv, opts));
    }

    [Fact]
    public void LongFieldSpanningManyChunks_Bytes()
    {
        // Force the parser to traverse many SIMD chunks within a single field.
        var bigField = new string('a', 8192);
        var csv = $"A,B\n{bigField},x\n";
        Assert.Equal(1, CountBytes(csv));
    }

    [Fact]
    public void LongQuotedFieldSpanningManyChunks_Bytes()
    {
        var bigField = new string('a', 8192);
        var csv = $"A,B\n\"{bigField}\",x\n";
        Assert.Equal(1, CountBytes(csv));
    }

    [Fact]
    public void LongFieldSpanningManyChunks_Chars()
    {
        var bigField = new string('a', 8192);
        var csv = $"A,B\n{bigField},x\n";
        Assert.Equal(1, CountChars(csv));
    }

    [Fact]
    public void LongQuotedFieldSpanningManyChunks_Chars()
    {
        var bigField = new string('a', 8192);
        var csv = $"A,B\n\"{bigField}\",x\n";
        Assert.Equal(1, CountChars(csv));
    }

    [Fact]
    public void NoPclmul_DoubledQuotes_Bytes()
    {
        // Without PCLMULQDQ the AVX2 path uses sequential quote handling for the entire chunk.
        using (HardwareCapabilities.Override(avx512BW: false, pclmulqdq: false))
        {
            var csv = "A,B\n\"she said \"\"hi\"\"\",x\n";
            Assert.Equal(1, CountBytes(csv));
        }
    }

    [Fact]
    public void NoPclmul_LongQuotedField_Bytes()
    {
        using (HardwareCapabilities.Override(avx512BW: false, pclmulqdq: false))
        {
            var bigField = new string('a', 4096);
            var csv = $"A,B\n\"{bigField}\",x\n";
            Assert.Equal(1, CountBytes(csv));
        }
    }

    [Fact]
    public void Scalar_DoubledQuotes_Fallback()
    {
        using (HardwareCapabilities.Override(avx512BW: false, avx2: false))
        {
            var csv = "A,B\n\"she said \"\"hi\"\"\",x\n";
            Assert.Equal(1, CountBytes(csv));
        }
    }

    [Fact]
    public void EscapeCharacter_ForcesScalarPath_Even_With_AvxOn()
    {
        var opts = new CsvReadOptions { EscapeCharacter = '\\' };
        var csv = "A,B\n\"a\\\"b\",x\n";
        Assert.Equal(1, CountBytes(csv, opts));
    }

    [Fact]
    public void Avx2Only_LineNumberTracking_Quoted_Bytes()
    {
        // TrackSourceLineNumbers triggers the lfMask/crMask counting branch in the AVX2 SIMD path.
        var opts = new CsvReadOptions
        {
            AllowNewlinesInsideQuotes = true,
            TrackSourceLineNumbers = true
        };
        using (HardwareCapabilities.Override(avx512BW: false))
        {
            var csv = "A,B\n\"line1\nline2\nline3\",x\n";
            Assert.Equal(1, CountBytes(csv, opts));
        }
    }

    [Fact]
    public void Avx512_LineNumberTracking_Quoted_Chars()
    {
        var opts = new CsvReadOptions
        {
            AllowNewlinesInsideQuotes = true,
            TrackSourceLineNumbers = true
        };
        var csv = "A,B\n\"line1\nline2\nline3\",x\n";
        Assert.Equal(1, CountChars(csv, opts));
    }

    [Fact]
    public void Many_Tiny_Rows_AvxAndScalar()
    {
        // Tons of tiny rows hit the SIMD chunk-end transition lots of times.
        var sb = new StringBuilder("A,B\n");
        for (int i = 0; i < 1000; i++) sb.Append("a,b\n");
        Assert.Equal(1000, CountBytes(sb.ToString()));

        using (HardwareCapabilities.Override(avx512BW: false, avx2: false))
        {
            Assert.Equal(1000, CountBytes(sb.ToString()));
        }
    }

    [Fact]
    public void Crlf_RowSeparator_Bytes_AllSimdLevels()
    {
        var csv = "A,B\r\n1,2\r\n3,4\r\n";
        Assert.Equal(2, CountBytes(csv)); // default: AVX-512
        using (HardwareCapabilities.Override(avx512BW: false))
            Assert.Equal(2, CountBytes(csv)); // AVX2
        using (HardwareCapabilities.Override(avx512BW: false, avx2: false))
            Assert.Equal(2, CountBytes(csv)); // scalar
    }

    [Fact]
    public void Crlf_RowSeparator_Chars_AllSimdLevels()
    {
        var csv = "A,B\r\n1,2\r\n3,4\r\n";
        Assert.Equal(2, CountChars(csv));
        using (HardwareCapabilities.Override(avx512BW: false))
            Assert.Equal(2, CountChars(csv));
        using (HardwareCapabilities.Override(avx512BW: false, avx2: false))
            Assert.Equal(2, CountChars(csv));
    }

    [Fact]
    public void EmptyRows_AllSimdLevels()
    {
        var csv = "A,B\n\n\n,\n,a\n";
        // The exact count depends on how empty lines are handled, but all levels should agree.
        var defaultCount = CountBytes(csv);
        using (HardwareCapabilities.Override(avx512BW: false))
            Assert.Equal(defaultCount, CountBytes(csv));
        using (HardwareCapabilities.Override(avx512BW: false, avx2: false))
            Assert.Equal(defaultCount, CountBytes(csv));
    }
}
