using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Shared;
using Xunit;

namespace HeroParser.Tests;

/// <summary>
/// Differential tests for the per-row parser: whatever SIMD path <see cref="CsvRowParser.ParseRow{T}"/>
/// takes must return the same result, column ends and exception as the scalar loop (SIMD off), for
/// both element types, at the machine's native vector width and with AVX-512 masked off.
/// </summary>
[Collection("HardwareCaps")]
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class CsvRowParserSingleRowTests
{
    private static bool Avx2 => System.Runtime.Intrinsics.X86.Avx2.IsSupported;

    private sealed record ParseOutcome(
        CsvRowParseResult? Result,
        int[]? Ends,
        CsvErrorCode? Error,
        string? Message,
        int? QuoteStart);

    private static CsvReadOptions Options(
        bool quotes = true,
        bool track = true,
        int maxColumns = 16,
        int? maxFieldSize = null,
        bool allowNewlines = true,
        char? comment = null,
        char? escape = null,
        char delimiter = ',',
        bool simd = true) => new()
        {
            EnableQuotedFields = quotes,
            AllowNewlinesInsideQuotes = quotes && allowNewlines,
            TrackSourceLineNumbers = track,
            MaxColumnCount = maxColumns,
            MaxFieldSize = maxFieldSize,
            CommentCharacter = comment,
            EscapeCharacter = escape,
            Delimiter = delimiter,
            UseSimdIfAvailable = simd
        };

    private static CsvReadOptions WithoutSimd(CsvReadOptions o) => new()
    {
        EnableQuotedFields = o.EnableQuotedFields,
        AllowNewlinesInsideQuotes = o.AllowNewlinesInsideQuotes,
        TrackSourceLineNumbers = o.TrackSourceLineNumbers,
        MaxColumnCount = o.MaxColumnCount,
        MaxFieldSize = o.MaxFieldSize,
        CommentCharacter = o.CommentCharacter,
        EscapeCharacter = o.EscapeCharacter,
        Delimiter = o.Delimiter,
        Quote = o.Quote,
        UseSimdIfAvailable = false
    };

    private static ParseOutcome Parse<T>(ReadOnlySpan<T> data, CsvReadOptions options, int endsLength)
        where T : unmanaged, IEquatable<T>
    {
        var ends = new int[endsLength];
        try
        {
            var result = CsvRowParser.ParseRow(data, options, ends, options.TrackSourceLineNumbers);
            return new ParseOutcome(result, ends[..(result.ColumnCount + 1)], null, null, null);
        }
        catch (CsvException ex)
        {
            return new ParseOutcome(null, null, ex.ErrorCode, ex.Message, ex.QuoteStartPosition);
        }
    }

    private static void AssertSameOutcome(ParseOutcome expected, ParseOutcome actual, string label)
    {
        Assert.True(expected.Result == actual.Result, $"{label}: result {actual.Result} != scalar {expected.Result}");
        Assert.True(expected.Error == actual.Error, $"{label}: error {actual.Error} != scalar {expected.Error}");
        Assert.True(expected.Message == actual.Message, $"{label}: message '{actual.Message}' != scalar '{expected.Message}'");
        Assert.True(expected.QuoteStart == actual.QuoteStart, $"{label}: quote start {actual.QuoteStart} != scalar {expected.QuoteStart}");
        if (expected.Ends is not null)
            Assert.True(expected.Ends.AsSpan().SequenceEqual(actual.Ends), $"{label}: column ends differ");
    }

    /// <summary>Runs the SIMD parser and the scalar oracle on <paramref name="text"/> as UTF-8 and as UTF-16.</summary>
    private static void AssertSameAsScalar(string text, CsvReadOptions options, int? endsLength = null)
    {
        int length = endsLength ?? CsvRowBatchScanner.MinEndsCapacity(options.MaxColumnCount);
        var scalar = WithoutSimd(options);
        var utf8 = Encoding.UTF8.GetBytes(text);
        string label = $"'{Printable(text)}' track={options.TrackSourceLineNumbers} quotes={options.EnableQuotedFields}";

        AssertSameOutcome(Parse<byte>(utf8, scalar, length), Parse<byte>(utf8, options, length), label + " utf8");
        AssertSameOutcome(Parse<char>(text, scalar, length), Parse<char>(text, options, length), label + " utf16");
    }

    private static void AssertSameAsScalarBothTracking(string text, bool quotes = true)
    {
        AssertSameAsScalar(text, Options(quotes: quotes, track: true));
        AssertSameAsScalar(text, Options(quotes: quotes, track: false));
    }

    private static string Printable(string s)
    {
        var shown = s.Length > 60 ? s[..60] + "..." : s;
        return shown.Replace("\r", "\\r").Replace("\n", "\\n");
    }

    private static readonly string[] Terminators = ["\n", "\r\n", "\r"];

    /// <summary>A row of "ab," cells padded to exactly <paramref name="length"/> characters.</summary>
    private static string RowOfLength(int length)
    {
        var sb = new StringBuilder(length);
        while (sb.Length + 3 <= length)
            sb.Append("ab,");
        while (sb.Length < length)
            sb.Append('z');
        return sb.ToString();
    }

    [Fact]
    public void Terminators_OnChunkEdges_MatchScalar()
    {
        int[] lengths = [0, 1, 30, 31, 32, 33, 62, 63, 64, 65, 126, 127, 128, 129, 200, 255, 256, 257];
        foreach (var terminator in Terminators)
        {
            foreach (int length in lengths)
            {
                string row = RowOfLength(length);
                AssertSameAsScalarBothTracking(row + terminator + "next,row\n");
                AssertSameAsScalarBothTracking(row + terminator);
                AssertSameAsScalarBothTracking(row + terminator, quotes: false);
            }
        }
    }

    [Fact]
    public void FinalRow_WithoutTerminator_MatchScalar()
    {
        AssertSameAsScalarBothTracking("a,b,c");
        AssertSameAsScalarBothTracking("a,\"b\"");
        AssertSameAsScalarBothTracking(RowOfLength(200));
        AssertSameAsScalarBothTracking("abc\r"); // a CR as the last element is a complete terminator here
        AssertSameAsScalarBothTracking("");
    }

    [Fact]
    public void QuotedFields_MatchScalar()
    {
        string[] rows =
        [
            "\"a,b\",\"c\"\"d\",e\nnext\n",
            "\"line1\nline2\",x\nnext\n",
            "\"l1\r\nl2\",x\nnext\n",
            "\"l1\rl2\",x\nnext\n",
            "\"" + new string('y', 70) + "\",z\nnext\n",
            "\"" + new string('y', 31) + "\"\"" + new string('q', 40) + "\",z\n",
            "a,\"\",b\n",
            "\"\"\n",
            "\"\r\n\r\n\",x\r\n",
            "a,\"b\ncd\r\ne\rf\",\"g\"\"h\"\n",
        ];
        foreach (var row in rows)
            AssertSameAsScalarBothTracking(row);
    }

    [Fact]
    public void BlankLines_First_MatchScalar()
    {
        foreach (var terminator in Terminators)
        {
            AssertSameAsScalarBothTracking(terminator);
            AssertSameAsScalarBothTracking(terminator + "abc,def\n");
            AssertSameAsScalarBothTracking(terminator, quotes: false);
        }
    }

    [Fact]
    public void CommentLines_MatchScalar()
    {
        var options = Options(comment: '#');
        AssertSameAsScalar("# note\nabc,def\n", options);
        AssertSameAsScalar("  # indented\r\nx\n", options);
        AssertSameAsScalar("\t#tab\rx\n", options);
        AssertSameAsScalar("#only", options);
        AssertSameAsScalar("abc,def\n", options);
        AssertSameAsScalar("abc,#not,a,comment\n", options);
        AssertSameAsScalar(RowOfLength(150) + "\n", options);
    }

    [Fact]
    public void EscapeCharacter_MatchScalar()
    {
        var options = Options(escape: '\\');
        AssertSameAsScalar("a\\,b,c\n", options);
        AssertSameAsScalar("\"a\\\"b\",c\n", options);
    }

    [Fact]
    public void NonAsciiDelimiter_MatchScalar()
    {
        var options = Options(delimiter: '\u2502');
        AssertSameAsScalar("a\u2502b\u2502c\nnext\n", options);
        AssertSameAsScalar("\"a\u2502b\"\u2502c\n", options);
    }

    [Fact]
    public void LimitViolations_MatchScalar()
    {
        AssertSameAsScalar("a,b,c,d,e\n", Options(maxColumns: 3));
        AssertSameAsScalar("a,b,c,d,e", Options(maxColumns: 3));
        AssertSameAsScalar(RowOfLength(200) + "\n", Options(maxColumns: 10));
        AssertSameAsScalar("aaaaaa,b\n", Options(maxFieldSize: 3));
        AssertSameAsScalar("a,b,cccccc\n", Options(maxFieldSize: 3));
        AssertSameAsScalar("a,b,cccccc", Options(maxFieldSize: 3));
        AssertSameAsScalar(RowOfLength(200) + ",toolongfield\n", Options(maxFieldSize: 5, maxColumns: 100));
        AssertSameAsScalar("\"a\nb\",c\n", Options(allowNewlines: false));
        AssertSameAsScalar("\"a\r\nb\",c\n", Options(allowNewlines: false));
        AssertSameAsScalar("\"abc,def\n", Options());
        AssertSameAsScalar("\"abc", Options());
        AssertSameAsScalar("x,\"abc", Options());
        AssertSameAsScalar("x," + new string('y', 70) + "\"abc", Options());
    }

    [Fact]
    public void EndsBuffer_AtAndBelowMinimum_MatchScalar()
    {
        var options = Options(maxColumns: 8);
        int minimum = CsvRowBatchScanner.MinEndsCapacity(8);
        string row = "a,b,c,d,e,f,g,h\nnext\n";
        AssertSameAsScalar(row, options, endsLength: minimum);
        AssertSameAsScalar(row, options, endsLength: minimum - 1);
        AssertSameAsScalar(row, options, endsLength: 9);
    }

    [Fact]
    public void RandomRows_MatchScalar()
    {
        var random = new Random(20260909);
        const string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789 ";
        for (int i = 0; i < 400; i++)
        {
            var sb = new StringBuilder();
            int columns = random.Next(1, 12);
            for (int c = 0; c < columns; c++)
            {
                if (c > 0) sb.Append(',');
                int length = random.Next(0, 40);
                bool quoted = random.Next(4) == 0;
                if (quoted) sb.Append('"');
                for (int k = 0; k < length; k++)
                {
                    int roll = random.Next(quoted ? 24 : 40);
                    if (quoted && roll == 0) sb.Append("\"\"");
                    else if (quoted && roll == 1) sb.Append(',');
                    else if (quoted && roll == 2) sb.Append('\n');
                    else if (quoted && roll == 3) sb.Append("\r\n");
                    else sb.Append(alphabet[random.Next(alphabet.Length)]);
                }
                if (quoted) sb.Append('"');
            }
            sb.Append(Terminators[random.Next(Terminators.Length)]);
            if (random.Next(2) == 0) sb.Append("trailing,row\n");
            AssertSameAsScalarBothTracking(sb.ToString());
        }
    }

    [Fact]
    public void Avx2Only_MatchScalar()
    {
        if (!Avx2) return;
        using var scope = HardwareCapabilities.Override(avx512BW: false);

        foreach (var terminator in Terminators)
        {
            AssertSameAsScalarBothTracking(RowOfLength(31) + terminator + "x\n");
            AssertSameAsScalarBothTracking(RowOfLength(63) + terminator + "x\n");
            AssertSameAsScalarBothTracking(RowOfLength(127) + terminator);
        }
        AssertSameAsScalarBothTracking("\"l1\r\nl2\",x\nnext\n");
        AssertSameAsScalarBothTracking("\"" + new string('y', 70) + "\",z\n");
        AssertSameAsScalar("a,b,c,d,e\n", Options(maxColumns: 3));
        AssertSameAsScalar("\"abc", Options());
    }
}
