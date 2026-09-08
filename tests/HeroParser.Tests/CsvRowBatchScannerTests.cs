using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Rows;
using HeroParser.SeparatedValues.Reading.Shared;
using Xunit;

namespace HeroParser.Tests;

/// <summary>
/// Differential tests for the scan-ahead row scanner: the UTF-16 per-row reader is an independent
/// implementation, so the batched UTF-8 reader must yield identical rows, line numbers and errors
/// for every input, at both a tiny batch capacity (forcing many batch boundaries) and the default.
/// </summary>
[Collection("HardwareCaps")]
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class CsvRowBatchScannerTests
{
    private static bool Avx2 => System.Runtime.Intrinsics.X86.Avx2.IsSupported;

    private sealed record RowSnapshot(int LineNumber, int SourceLineNumber, string[] Cells);

    private sealed record ReadOutcome(List<RowSnapshot> Rows, CsvErrorCode? Error, string? Message);

    private static CsvReadOptions Options(
        bool quotes,
        bool track,
        int maxColumns = 16,
        int? maxFieldSize = null,
        bool allowNewlinesInQuotes = true,
        int maxRows = 1_000_000,
        bool trimFields = false,
        char? commentCharacter = null,
        bool useSimd = true) => new()
        {
            EnableQuotedFields = quotes,
            AllowNewlinesInsideQuotes = quotes && allowNewlinesInQuotes,
            TrackSourceLineNumbers = track,
            MaxColumnCount = maxColumns,
            MaxRowCount = maxRows,
            MaxFieldSize = maxFieldSize,
            TrimFields = trimFields,
            CommentCharacter = commentCharacter,
            UseSimdIfAvailable = useSimd
        };

    /// <summary>
    /// Oracle: the scalar per-row path (SIMD off). The UTF-16 SIMD per-row path over-counts source
    /// lines around quoted fields, so it cannot serve as a reference.
    /// </summary>
    private static ReadOutcome ReadPerRowOracle(string csv, CsvReadOptions options)
    {
        var scalar = new CsvReadOptions
        {
            EnableQuotedFields = options.EnableQuotedFields,
            AllowNewlinesInsideQuotes = options.AllowNewlinesInsideQuotes,
            TrackSourceLineNumbers = options.TrackSourceLineNumbers,
            MaxColumnCount = options.MaxColumnCount,
            MaxRowCount = options.MaxRowCount,
            MaxFieldSize = options.MaxFieldSize,
            TrimFields = options.TrimFields,
            CommentCharacter = options.CommentCharacter,
            UseSimdIfAvailable = false
        };

        var rows = new List<RowSnapshot>();
        try
        {
            using var reader = Csv.ReadFromByteSpan(Encoding.UTF8.GetBytes(csv), scalar);
            while (reader.MoveNext())
                rows.Add(Snapshot(reader.Current));
            return new ReadOutcome(rows, null, null);
        }
        catch (CsvException ex)
        {
            return new ReadOutcome(rows, ex.ErrorCode, ex.Message);
        }
    }

    private static ReadOutcome ReadBatched(byte[] utf8, CsvReadOptions options, int capacity)
    {
        var rows = new List<RowSnapshot>();
        try
        {
            using var reader = new CsvRowReader<byte>(utf8, options, capacity);
            while (reader.MoveNext())
                rows.Add(Snapshot(reader.Current));
            return new ReadOutcome(rows, null, null);
        }
        catch (CsvException ex)
        {
            return new ReadOutcome(rows, ex.ErrorCode, ex.Message);
        }
    }

    private static ReadOutcome ReadBatchedChars(string csv, CsvReadOptions options, int capacity)
    {
        var rows = new List<RowSnapshot>();
        try
        {
            using var reader = new CsvRowReader<char>(csv.AsSpan(), options, capacity);
            while (reader.MoveNext())
                rows.Add(Snapshot(reader.Current));
            return new ReadOutcome(rows, null, null);
        }
        catch (CsvException ex)
        {
            return new ReadOutcome(rows, ex.ErrorCode, ex.Message);
        }
    }

    private static RowSnapshot Snapshot<T>(CsvRow<T> row) where T : unmanaged, IEquatable<T>
    {
        var cells = new string[row.ColumnCount];
        for (int i = 0; i < cells.Length; i++)
            cells[i] = row[i].ToString();
        return new RowSnapshot(row.LineNumber, row.SourceLineNumber, cells);
    }

    private static void AssertSameAsOracle(string csv, CsvReadOptions options)
    {
        var expected = ReadPerRowOracle(csv, options);
        var utf8 = Encoding.UTF8.GetBytes(csv);

        // Minimum capacity forces a batch boundary every row or two; default is the production size.
        // Both element types run the same scanner state machine through different vector front ends.
        foreach (int capacity in new[] { 1, CsvRowBatchScanner.DEFAULT_ENDS_CAPACITY })
        {
            AssertOutcomesEqual(expected, ReadBatched(utf8, options, capacity), $"utf8 capacity={capacity}");
            AssertOutcomesEqual(expected, ReadBatchedChars(csv, options, capacity), $"utf16 capacity={capacity}");
        }
    }

    private static void AssertOutcomesEqual(ReadOutcome expected, ReadOutcome actual, string context)
    {
        Assert.True(expected.Rows.Count == actual.Rows.Count, $"{context}: row count {actual.Rows.Count}, expected {expected.Rows.Count}");
        for (int r = 0; r < expected.Rows.Count; r++)
        {
            var e = expected.Rows[r];
            var a = actual.Rows[r];
            Assert.True(e.LineNumber == a.LineNumber, $"{context}: row {r} LineNumber {a.LineNumber}, expected {e.LineNumber}");
            Assert.True(e.SourceLineNumber == a.SourceLineNumber, $"{context}: row {r} SourceLineNumber {a.SourceLineNumber}, expected {e.SourceLineNumber}");
            Assert.True(e.Cells.Length == a.Cells.Length, $"{context}: row {r} column count {a.Cells.Length}, expected {e.Cells.Length}");
            for (int c = 0; c < e.Cells.Length; c++)
                Assert.True(e.Cells[c] == a.Cells[c], $"{context}: row {r} col {c} '{a.Cells[c]}', expected '{e.Cells[c]}'");
        }

        Assert.True(expected.Error == actual.Error, $"{context}: error {actual.Error}, expected {expected.Error}");
        Assert.True(expected.Message == actual.Message, $"{context}: message '{actual.Message}', expected '{expected.Message}'");
    }

    // ---------------------------------------------------------------------------------------------
    // Input generators
    // ---------------------------------------------------------------------------------------------

    /// <summary>Rows of varying width and cell length so line endings land on every chunk offset.</summary>
    private static string VaryingRows(string newline, int rows, bool trailingNewline = true)
    {
        var sb = new StringBuilder();
        for (int r = 0; r < rows; r++)
        {
            int cols = 1 + (r % 7);
            for (int c = 0; c < cols; c++)
            {
                if (c > 0) sb.Append(',');
                sb.Append('v').Append(r).Append('_').Append(new string((char)('a' + (c % 26)), (r + c) % 11));
            }
            if (r < rows - 1 || trailingNewline)
                sb.Append(newline);
        }
        return sb.ToString();
    }

    /// <summary>Rows sized so the CR of a CRLF sits on the last byte of a 32- or 64-byte chunk.</summary>
    private static string CrlfOnChunkEdges()
    {
        var sb = new StringBuilder();
        foreach (int targetLen in new[] { 62, 63, 30, 31, 126, 127, 254, 255, 64, 32, 5, 190, 191 })
        {
            // Row body of exactly targetLen bytes, then CRLF: CR lands at absolute offset (start + targetLen).
            int start = sb.Length;
            sb.Append("a,");
            while (sb.Length - start < targetLen - 1)
                sb.Append('x');
            sb.Append(",b");
            // Trim or pad to exact length
            while (sb.Length - start > targetLen) sb.Length--;
            while (sb.Length - start < targetLen) sb.Append('y');
            sb.Append("\r\n");
        }
        return sb.ToString();
    }

    private static string QuotedMix(string newline)
    {
        var sb = new StringBuilder();
        for (int r = 0; r < 40; r++)
        {
            sb.Append("id").Append(r).Append(',');
            switch (r % 5)
            {
                case 0: sb.Append("\"plain quoted\""); break;
                case 1: sb.Append("\"has, comma\""); break;
                case 2: sb.Append("\"embedded").Append(newline).Append("newline\""); break;
                case 3: sb.Append("\"doubled \"\"quote\"\" inside\""); break;
                default: sb.Append("unquoted"); break;
            }
            sb.Append(',').Append(new string('z', r % 9)).Append(newline);
        }
        return sb.ToString();
    }

    /// <summary>Quotes placed on chunk boundaries so the doubled-quote boundary check fires.</summary>
    private static string QuotesOnChunkEdges()
    {
        var sb = new StringBuilder();
        foreach (int prefix in new[] { 30, 31, 62, 63, 126, 127 })
        {
            sb.Append(new string('p', prefix)).Append(",\"q\"\"r\",tail\n");
            sb.Append(new string('p', prefix)).Append(",\"\",tail\n");
            sb.Append(new string('p', prefix - 1)).Append(",\"\"\"\",tail\n");
        }
        return sb.ToString();
    }

    // ---------------------------------------------------------------------------------------------
    // Differential tests (AVX-512 or AVX2, whichever the machine has)
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void Unquoted_VaryingRows_MatchesPerRow(string newline)
    {
        AssertSameAsOracle(VaryingRows(newline, 400), Options(quotes: false, track: false));
        AssertSameAsOracle(VaryingRows(newline, 400), Options(quotes: false, track: true));
    }

    [Fact]
    public void Unquoted_NoTrailingNewline_MatchesPerRow()
    {
        AssertSameAsOracle(VaryingRows("\n", 50, trailingNewline: false), Options(quotes: false, track: true));
        AssertSameAsOracle("single,row,no,newline", Options(quotes: false, track: true));
    }

    [Fact]
    public void Unquoted_CrlfOnChunkEdges_MatchesPerRow()
    {
        AssertSameAsOracle(CrlfOnChunkEdges(), Options(quotes: false, track: true));
    }

    [Fact]
    public void BlankLines_AreSkippedAndCounted_MatchesPerRow()
    {
        string csv = "\n\n" + "a,b\n" + "\n" + "c,d\r\n" + "\r\n\r\n" + "e,f\n" + "\n\n\n" + "g,h" + "\n\n";
        AssertSameAsOracle(csv, Options(quotes: false, track: true));
        AssertSameAsOracle(csv, Options(quotes: true, track: true));
        AssertSameAsOracle("\n\n\n", Options(quotes: false, track: true));
        AssertSameAsOracle("", Options(quotes: false, track: true));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Quoted_Mix_MatchesPerRow(string newline)
    {
        AssertSameAsOracle(QuotedMix(newline), Options(quotes: true, track: false));
        AssertSameAsOracle(QuotedMix(newline), Options(quotes: true, track: true));
    }

    [Fact]
    public void Quoted_QuotesOnChunkEdges_MatchesPerRow()
    {
        AssertSameAsOracle(QuotesOnChunkEdges(), Options(quotes: true, track: true));
    }

    [Fact]
    public void Quoted_UnterminatedQuote_SameErrorAfterSameRows()
    {
        string csv = VaryingRows("\n", 30) + "ok,\"fine\",row\n" + "bad,\"never closed,x\n" + "more,rows\n";
        AssertSameAsOracle(csv, Options(quotes: true, track: true));
    }

    [Fact]
    public void Quoted_NewlineInsideQuotesDisallowed_SameError()
    {
        string csv = VaryingRows("\n", 10) + "a,\"line1\nline2\",b\n" + "c,d\n";
        AssertSameAsOracle(csv, Options(quotes: true, track: true, allowNewlinesInQuotes: false));
    }

    [Fact]
    public void TooManyColumns_SameErrorAfterSameRows()
    {
        string csv = VaryingRows("\n", 20) + "1,2,3,4,5,6,7,8,9\n" + "after,error\n";
        AssertSameAsOracle(csv, Options(quotes: false, track: true, maxColumns: 8));
        AssertSameAsOracle(csv, Options(quotes: true, track: true, maxColumns: 8));
    }

    [Fact]
    public void MaxFieldSize_SameErrorAfterSameRows()
    {
        string csv = VaryingRows("\n", 20) + "short," + new string('L', 40) + ",short\n" + "after,error\n";
        AssertSameAsOracle(csv, Options(quotes: false, track: true, maxFieldSize: 20));
        AssertSameAsOracle(csv, Options(quotes: true, track: true, maxFieldSize: 20));
    }

    [Fact]
    public void MaxRowCount_Enforced_AcrossBatches()
    {
        var options = Options(quotes: false, track: false, maxRows: 25);
        string csv = VaryingRows("\n", 100);
        var utf8 = Encoding.UTF8.GetBytes(csv);
        var ex = Assert.Throws<CsvException>(() =>
        {
            using var reader = new CsvRowReader<byte>(utf8, options, 1);
            while (reader.MoveNext()) { }
        });
        Assert.Equal(CsvErrorCode.TooManyRows, ex.ErrorCode);

        var exChars = Assert.Throws<CsvException>(() =>
        {
            using var reader = new CsvRowReader<char>(csv.AsSpan(), options, 1);
            while (reader.MoveNext()) { }
        });
        Assert.Equal(CsvErrorCode.TooManyRows, exChars.ErrorCode);
    }

    /// <summary>
    /// Non-ASCII chars must not be mistaken for delimiters, quotes or line endings on the UTF-16 path.
    /// Covers chars in 0x0100-0x7FFF (saturate to 0xFF when packed) and at or above 0x8000, including
    /// surrogates and private-use chars (saturate to 0x00 when packed), plus U+FFFF.
    /// </summary>
    [Fact]
    public void Utf16_NonAsciiContent_MatchesPerRow()
    {
        var sb = new StringBuilder();
        for (int r = 0; r < 60; r++)
        {
            sb.Append("名前").Append(r).Append(",\"引用, 值\",émoji 🚀,").Append(new string('ß', r % 5))
              .Append(",￿�,").Append("한글 한글")
              .Append(r % 3 == 0 ? "\r\n" : "\n");
        }
        AssertSameAsOracle(sb.ToString(), Options(quotes: true, track: true));
        AssertSameAsOracle(sb.ToString().Replace("\"", string.Empty), Options(quotes: false, track: true));
    }

    /// <summary>
    /// The reader is a ref struct copied by foreach, and the cursor is shared by the copies. Using the
    /// original after a copy was disposed must fail loudly whatever the input size, not read as end of data.
    /// </summary>
    [Fact]
    public void MoveNext_AfterDisposeThroughCopy_Throws()
    {
        // Scanner-path behaviour: the per-row fallback (no AVX, e.g. Apple Silicon) caches its column
        // buffer and has never thrown here, and changing that is outside this test's scope.
        if (!CsvRowBatchScanner.IsSupported(Options(quotes: false, track: false))) return;

        var utf8 = Encoding.UTF8.GetBytes(VaryingRows("\n", 50));
        var reader = new CsvRowReader<byte>(utf8, Options(quotes: false, track: false));
        foreach (var _ in reader)
            break; // disposes the enumerator copy, which shares the cursor

        // A ref struct cannot be captured by a lambda, so assert the throw by hand.
        bool threw = false;
        try
        {
            reader.MoveNext();
        }
        catch (ObjectDisposedException)
        {
            threw = true;
        }
        Assert.True(threw, "MoveNext after a copy was disposed must throw ObjectDisposedException");
    }

    [Fact]
    public void TrimFields_AppliedToBatchedRows()
    {
        var options = Options(quotes: true, track: false, trimFields: true);
        var utf8 = Encoding.UTF8.GetBytes("  a  , b ,\" c \"\n x,y \n");
        using var reader = new CsvRowReader<byte>(utf8, options, 1);
        Assert.True(reader.MoveNext());
        Assert.Equal("a", reader.Current[0].ToString());
        Assert.Equal("b", reader.Current[1].ToString());
        Assert.Equal("\" c \"", reader.Current[2].ToString());
        Assert.True(reader.MoveNext());
        Assert.Equal("x", reader.Current[0].ToString());
        Assert.Equal("y", reader.Current[1].ToString());
        Assert.False(reader.MoveNext());
    }

    /// <summary>
    /// The reader struct is copied by foreach and disposed by both the copy and a using declaration.
    /// Pooled batch buffers must survive that double dispose: returning them twice would hand the
    /// same array to the next reader's ends and row starts, which then alias.
    /// </summary>
    [Fact]
    public void DoubleDispose_DoesNotCorruptNextReader()
    {
        var utf8 = Encoding.UTF8.GetBytes("a,b\n1,2\n");
        var options = Options(quotes: true, track: false, maxColumns: 100);

        for (int iteration = 0; iteration < 8; iteration++)
        {
            using var reader = Csv.ReadFromByteSpan(utf8, options);
            var cells = new List<string>();
            foreach (var row in reader)
            {
                for (int i = 0; i < row.ColumnCount; i++)
                    cells.Add(row[i].ToString());
            }
            Assert.Equal(["a", "b", "1", "2"], cells);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Forced AVX2 front end
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Avx2_AllShapes_MatchPerRow()
    {
        if (!Avx2) return;
        using var _scope = HardwareCapabilities.Override(avx512BW: false);

        AssertSameAsOracle(VaryingRows("\n", 300), Options(quotes: false, track: true));
        AssertSameAsOracle(VaryingRows("\r\n", 300), Options(quotes: false, track: true));
        AssertSameAsOracle(CrlfOnChunkEdges(), Options(quotes: false, track: true));
        AssertSameAsOracle(QuotedMix("\r\n"), Options(quotes: true, track: true));
        AssertSameAsOracle(QuotesOnChunkEdges(), Options(quotes: true, track: true));
        AssertSameAsOracle(VaryingRows("\n", 30) + "bad,\"never closed\n", Options(quotes: true, track: true));
    }

    // ---------------------------------------------------------------------------------------------
    // Fallbacks stay on the per-row path
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void CommentCharacter_UsesPerRowPath_AndSkipsComments()
    {
        var options = Options(quotes: false, track: true, commentCharacter: '#');
        Assert.False(CsvRowBatchScanner.IsSupported(options));

        var utf8 = Encoding.UTF8.GetBytes("# header comment\na,b\n  # indented comment\nc,d\n");
        using var reader = new CsvRowReader<byte>(utf8, options);
        Assert.True(reader.MoveNext());
        Assert.Equal("a", reader.Current[0].ToString());
        Assert.Equal(2, reader.Current.SourceLineNumber);
        Assert.True(reader.MoveNext());
        Assert.Equal("c", reader.Current[0].ToString());
        Assert.Equal(4, reader.Current.SourceLineNumber);
        Assert.False(reader.MoveNext());
    }

    [Fact]
    public void SimdDisabled_UsesPerRowPath()
    {
        var options = Options(quotes: true, track: true, useSimd: false);
        Assert.False(CsvRowBatchScanner.IsSupported(options));
        AssertSameAsOracle(QuotedMix("\n"), options);
    }
}
