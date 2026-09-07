using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Rows;
using HeroParser.SeparatedValues.Reading.Shared;
using Xunit;

namespace HeroParser.Tests;

/// <summary>
/// Regression: a multi-line quoted field must only advance <c>SourceLineNumber</c> for the row that
/// contains it. The SIMD paths used to credit every in-quote newline in the current 32/64-byte chunk to
/// whichever row was being parsed, so rows preceding such a field in the same chunk were off by one.
/// </summary>
[Collection("HardwareCaps")]
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class SourceLineNumberQuotedChunkTests
{
    private static bool Avx2 => System.Runtime.Intrinsics.X86.Avx2.IsSupported;

    // Rows 0-2 total exactly 64 bytes, so one AVX-512 chunk (and two AVX2 chunks) span all three,
    // and the embedded newline in row 2 sits in the same chunk as rows 0 and 1.
    private const string CSV =
        "id0,\"plain quoted\",\n" +
        "id1,\"has, comma\",z\n" +
        "id2,\"embedded\nnewline\",zz\n" +
        "id3,\"doubled \"\"quote\"\" inside\",zzz\n" +
        "id4,unquoted,zzzz\n";

    private static readonly int[] expectedLines = [1, 2, 3, 5, 6];

    private static CsvReadOptions Options(bool simd, char? comment = null) => new()
    {
        EnableQuotedFields = true,
        AllowNewlinesInsideQuotes = true,
        TrackSourceLineNumbers = true,
        MaxColumnCount = 16,
        MaxRowCount = 1000,
        UseSimdIfAvailable = simd,
        CommentCharacter = comment
    };

    private static int[] Lines<T>(CsvRowReader<T> reader) where T : unmanaged, IEquatable<T>
    {
        var lines = new List<int>();
        while (reader.MoveNext())
            lines.Add(reader.Current.SourceLineNumber);
        reader.Dispose();
        return [.. lines];
    }

    [Fact]
    public void Scalar_Utf16_IsGroundTruth()
        => Assert.Equal(expectedLines, Lines(Csv.ReadFromCharSpan(CSV.AsSpan(), Options(simd: false))));

    [Fact]
    public void Simd_Utf16_PerRow()
        => Assert.Equal(expectedLines, Lines(Csv.ReadFromCharSpan(CSV.AsSpan(), Options(simd: true))));

    [Fact]
    public void Simd_Utf8_PerRow()
    {
        // A comment character keeps the byte reader on the per-row path (see CsvRowBatchScanner.IsSupported).
        var utf8 = Encoding.UTF8.GetBytes(CSV);
        Assert.Equal(expectedLines, Lines(new CsvRowReader<byte>(utf8, Options(simd: true, comment: '#'))));
    }

    [Fact]
    public void Simd_Utf8_Batched()
    {
        var utf8 = Encoding.UTF8.GetBytes(CSV);
        Assert.Equal(expectedLines, Lines(new CsvRowReader<byte>(utf8, Options(simd: true))));
        Assert.Equal(expectedLines, Lines(new CsvRowReader<byte>(utf8, Options(simd: true), 1)));
    }

    [Fact]
    public void Avx2_AllPaths()
    {
        if (!Avx2) return;
        using var _scope = HardwareCapabilities.Override(avx512BW: false);
        var utf8 = Encoding.UTF8.GetBytes(CSV);
        Assert.Equal(expectedLines, Lines(Csv.ReadFromCharSpan(CSV.AsSpan(), Options(simd: true))));
        Assert.Equal(expectedLines, Lines(new CsvRowReader<byte>(utf8, Options(simd: true, comment: '#'))));
        Assert.Equal(expectedLines, Lines(new CsvRowReader<byte>(utf8, Options(simd: true))));
    }
}
