using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Shared;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Drives the TrackLineNumbers compile-time-specialized SIMD code paths in
/// CsvRowParser by enabling <see cref="CsvReadOptions.TrackSourceLineNumbers"/>
/// alongside multi-line quoted fields. Hits ~80 lines of SIMD code that
/// counts newlines inside quoted fields (lines 658-666, 1079-1087,
/// 1158-1168, 1346-1354, 1476-1484, 1529-1537, 1609-1619).
/// </summary>
[Trait("Category", "Unit")]
[Collection("AsyncWriterTests")]
public class CsvTrackLineNumbersSimdTests
{
    private static readonly CsvReadOptions trackEnabled = new()
    {
        TrackSourceLineNumbers = true,
        AllowNewlinesInsideQuotes = true
    };

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

    private static string SampleManyMultiLineQuoted(int rows)
    {
        var sb = new StringBuilder("A,B\n");
        for (int i = 0; i < rows; i++)
        {
            sb.Append('"');
            sb.Append("line1-").Append(i).Append('\n');
            sb.Append("line2-").Append(i).Append('\n');
            sb.Append("line3-").Append(i);
            sb.Append("\",col2-").Append(i).Append('\n');
        }
        return sb.ToString();
    }

    [Fact]
    public void Bytes_TrackLines_QuotedMultiLineFields_Avx512()
    {
        // Default capabilities: AVX-512 path runs.
        var csv = SampleManyMultiLineQuoted(20);
        Assert.Equal(21, CountByteRows(csv, trackEnabled));
    }

    [Fact]
    public void Bytes_TrackLines_QuotedMultiLineFields_Avx2_Fallback()
    {
        using (HardwareCapabilities.Override(avx512BW: false))
        {
            var csv = SampleManyMultiLineQuoted(20);
            Assert.Equal(21, CountByteRows(csv, trackEnabled));
        }
    }

    [Fact]
    public void Bytes_TrackLines_QuotedMultiLineFields_Avx2_NoPclmul()
    {
        // Forces the non-CLMUL quote-tracking path within AVX2.
        using (HardwareCapabilities.Override(avx512BW: false, pclmulqdq: false))
        {
            var csv = SampleManyMultiLineQuoted(20);
            Assert.Equal(21, CountByteRows(csv, trackEnabled));
        }
    }

    [Fact]
    public void Bytes_TrackLines_QuotedMultiLineFields_Scalar()
    {
        using (HardwareCapabilities.Override(avx512BW: false, avx2: false))
        {
            var csv = SampleManyMultiLineQuoted(10);
            Assert.Equal(11, CountByteRows(csv, trackEnabled));
        }
    }

    [Fact]
    public void Chars_TrackLines_QuotedMultiLineFields_Avx512()
    {
        var csv = SampleManyMultiLineQuoted(20);
        Assert.Equal(21, CountCharRows(csv, trackEnabled));
    }

    [Fact]
    public void Chars_TrackLines_QuotedMultiLineFields_Avx2_Fallback()
    {
        using (HardwareCapabilities.Override(avx512BW: false))
        {
            var csv = SampleManyMultiLineQuoted(20);
            Assert.Equal(21, CountCharRows(csv, trackEnabled));
        }
    }

    [Fact]
    public void Chars_TrackLines_QuotedMultiLineFields_Avx2_NoPclmul()
    {
        using (HardwareCapabilities.Override(avx512BW: false, pclmulqdq: false))
        {
            var csv = SampleManyMultiLineQuoted(20);
            Assert.Equal(21, CountCharRows(csv, trackEnabled));
        }
    }

    [Fact]
    public void Chars_TrackLines_QuotedMultiLineFields_Scalar()
    {
        using (HardwareCapabilities.Override(avx512BW: false, avx2: false))
        {
            var csv = SampleManyMultiLineQuoted(10);
            Assert.Equal(11, CountCharRows(csv, trackEnabled));
        }
    }

    [Fact]
    public void Bytes_TrackLines_LongQuotedFieldSpansChunks()
    {
        // Field with many embedded newlines spanning multiple SIMD chunks.
        var sb = new StringBuilder("A,B\n\"");
        for (int i = 0; i < 200; i++) sb.Append("line").Append(i).Append('\n');
        sb.Append("\",end\n");
        Assert.Equal(2, CountByteRows(sb.ToString(), trackEnabled));
    }

    [Fact]
    public void Chars_TrackLines_LongQuotedFieldSpansChunks()
    {
        var sb = new StringBuilder("A,B\n\"");
        for (int i = 0; i < 200; i++) sb.Append("line").Append(i).Append('\n');
        sb.Append("\",end\n");
        Assert.Equal(2, CountCharRows(sb.ToString(), trackEnabled));
    }

    [Fact]
    public void Bytes_TrackLines_QuotedField_WithCrLfInside()
    {
        var csv = "A,B\n\"line1\r\nline2\r\nline3\",x\n";
        Assert.Equal(2, CountByteRows(csv, trackEnabled));
    }

    [Fact]
    public void Chars_TrackLines_QuotedField_WithCrLfInside()
    {
        var csv = "A,B\n\"line1\r\nline2\r\nline3\",x\n";
        Assert.Equal(2, CountCharRows(csv, trackEnabled));
    }

    [Fact]
    public void Bytes_TrackLines_NoQuotes_SimpleCsv()
    {
        // Track-lines without any quoted fields exercises the lf/cr-counting code path.
        var opts = new CsvReadOptions { TrackSourceLineNumbers = true };
        var sb = new StringBuilder("A,B\n");
        for (int i = 0; i < 100; i++) sb.AppendLine($"row{i}A,row{i}B");
        Assert.Equal(101, CountByteRows(sb.ToString(), opts));
    }

    [Fact]
    public void Chars_TrackLines_NoQuotes_SimpleCsv()
    {
        var opts = new CsvReadOptions { TrackSourceLineNumbers = true };
        var sb = new StringBuilder("A,B\n");
        for (int i = 0; i < 100; i++) sb.AppendLine($"row{i}A,row{i}B");
        Assert.Equal(101, CountCharRows(sb.ToString(), opts));
    }
}
