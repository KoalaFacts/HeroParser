using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Shared;
using Xunit;

namespace HeroParser.Tests;

/// <summary>
/// Pins the behaviour of the unquoted SIMD "block" path (4 vectors per iteration) in
/// <c>CsvRowParser</c>. That path is only taken for unquoted input with at least
/// 4 vectors remaining, so every row here is long enough to enter it, and the line
/// ending is placed in each of the four vector slots of a block in turn.
/// </summary>
[Collection("HardwareCaps")]
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class CsvRowParserBlockPathTests
{
    private static bool Avx2 => System.Runtime.Intrinsics.X86.Avx2.IsSupported;
    private static bool Avx512BW => System.Runtime.Intrinsics.X86.Avx512BW.IsSupported;

    private static CsvReadOptions Unquoted(int? maxFieldSize = null) => new()
    {
        EnableQuotedFields = false,
        AllowNewlinesInsideQuotes = false,
        MaxColumnCount = 1_000,
        MaxFieldSize = maxFieldSize
    };

    /// <summary>
    /// Builds rows whose lengths sweep across a whole block so the line ending lands in
    /// vector 0, 1, 2 and 3 of the 4-vector block for the given vector width.
    /// Returns the CSV bytes plus the expected cells per row.
    /// </summary>
    private static (byte[] Utf8, List<string[]> Expected) BuildSweep(int vectorWidth)
    {
        var sb = new StringBuilder();
        var expected = new List<string[]>();
        int block = 4 * vectorWidth;

        // Row lengths from just under one block to just over two blocks, stepping by
        // roughly a third of a vector, so every vector slot of the block gets a line ending.
        for (int targetLength = block - 8; targetLength <= (2 * block) + 8; targetLength += vectorWidth / 3)
        {
            var cells = new List<string>();
            int length = 0;
            int c = 0;
            while (length < targetLength)
            {
                string cell = $"c{c}_{new string('x', c % 7)}";
                cells.Add(cell);
                length += cell.Length + 1;
                c++;
            }
            expected.Add([.. cells]);
            sb.Append(string.Join(',', cells)).Append('\n');
        }

        // Trailing padding rows so the last sweep row still has 4 vectors of lookahead.
        for (int i = 0; i < 20; i++)
        {
            string[] pad = ["p1", "p2", "p3", "p4", "p5", "p6", "p7", "p8"];
            expected.Add(pad);
            sb.Append(string.Join(',', pad)).Append('\n');
        }

        return (Encoding.UTF8.GetBytes(sb.ToString()), expected);
    }

    private static void AssertRoundTrips(byte[] utf8, List<string[]> expected)
    {
        using var reader = Csv.ReadFromByteSpan(utf8, Unquoted());
        int rowIndex = 0;
        while (reader.MoveNext())
        {
            var row = reader.Current;
            var want = expected[rowIndex];
            Assert.Equal(want.Length, row.ColumnCount);
            for (int i = 0; i < want.Length; i++)
                Assert.Equal(want[i], row[i].ToString());
            rowIndex++;
        }
        Assert.Equal(expected.Count, rowIndex);
    }

    [Fact]
    public void BlockPath_Avx512_LineEndingInEveryVectorSlot_RoundTrips()
    {
        if (!Avx512BW) return;
        var (utf8, expected) = BuildSweep(64);
        AssertRoundTrips(utf8, expected);
    }

    [Fact]
    public void BlockPath_Avx2_LineEndingInEveryVectorSlot_RoundTrips()
    {
        if (!Avx2) return;
        using var _scope = HardwareCapabilities.Override(avx512BW: false);
        var (utf8, expected) = BuildSweep(32);
        AssertRoundTrips(utf8, expected);
    }

    /// <summary>
    /// A row longer than one block whose oversize field sits inside the first vector of a
    /// block. MaxFieldSize must still be enforced even though that field is consumed by the
    /// block path rather than the per-vector loop.
    /// </summary>
    private static byte[] OversizeFieldInsideBlock(int vectorWidth)
    {
        // First field is 40 chars (> MaxFieldSize of 20), then enough short fields to make
        // the row span past one full block, then a newline. A few padding rows follow so
        // the block path has lookahead.
        var sb = new StringBuilder();
        sb.Append(new string('A', 40));
        while (sb.Length < (4 * vectorWidth) + (vectorWidth / 2))
            sb.Append(",ab");
        sb.Append('\n');
        for (int i = 0; i < 10; i++) sb.Append("a,b,c,d,e,f,g,h\n");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    [Fact]
    public void BlockPath_Avx512_MaxFieldSize_EnforcedInsideBlock()
    {
        if (!Avx512BW) return;
        var utf8 = OversizeFieldInsideBlock(64);
        Assert.Throws<CsvException>(() =>
        {
            using var reader = Csv.ReadFromByteSpan(utf8, Unquoted(maxFieldSize: 20));
            while (reader.MoveNext()) { }
        });
    }

    [Fact]
    public void BlockPath_Avx2_MaxFieldSize_EnforcedInsideBlock()
    {
        if (!Avx2) return;
        using var _scope = HardwareCapabilities.Override(avx512BW: false);
        var utf8 = OversizeFieldInsideBlock(32);
        Assert.Throws<CsvException>(() =>
        {
            using var reader = Csv.ReadFromByteSpan(utf8, Unquoted(maxFieldSize: 20));
            while (reader.MoveNext()) { }
        });
    }
}
