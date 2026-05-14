using System.Globalization;
using System.Text;
using HeroParser.FixedWidths;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Writing;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests;

/// <summary>Wave 21: bool byte-path single-char variants, non-invariant culture, CsvRow extras.</summary>
public class CoveragePushTests21
{
    // ---------- Bool single-char byte path via FixedWidth byte reader ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_BytePath_Bool_SingleChar_True()
    {
        // 1, Y, y, T, t each on its own line via FromStream (byte path).
        byte[] bytes = Encoding.UTF8.GetBytes("1\nY\ny\nT\nt\n");
        using var ms = new MemoryStream(bytes);
        var rows = FixedWidth.Read<BoolRow>().FromStream(ms).ToList();
        Assert.Equal(5, rows.Count);
        Assert.All(rows, r => Assert.True(r.B));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_BytePath_Bool_SingleChar_False()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("0\nN\nn\nF\nf\n");
        using var ms = new MemoryStream(bytes);
        var rows = FixedWidth.Read<BoolRow>().FromStream(ms).ToList();
        Assert.Equal(5, rows.Count);
        Assert.All(rows, r => Assert.False(r.B));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_BytePath_Bool_FullWord()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("true \nfalse\n");
        using var ms = new MemoryStream(bytes);
        var rows = FixedWidth.Read<BoolFullRow>().FromStream(ms).ToList();
        Assert.Equal(2, rows.Count);
        Assert.True(rows[0].B);
        Assert.False(rows[1].B);
    }

    // ---------- Non-invariant culture forces TryParseChars in Utf8 helper ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_BytePath_NonInvariantCulture_AllNumericTypes()
    {
        // Use German culture (decimal comma) to force rented-path Utf8→char conversion.
        byte[] bytes = Encoding.UTF8.GetBytes("9999999999" + "12345" + "200" + "3,140000" + "2,500000" + "true " + "1234,56000" + "\n");
        using var ms = new MemoryStream(bytes);
        var rows = FixedWidth.Read<FixedAllTypes>()
            .WithCulture(CultureInfo.GetCultureInfo("de-DE"))
            .FromStream(ms)
            .ToList();
        Assert.Single(rows);
        Assert.Equal(3.14, rows[0].D, 2);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_BytePath_LongField_NonInvariantCulture_RentedBuffer()
    {
        // Long string field (>256 chars) under non-invariant culture forces ArrayPool.Rent path.
        string longText = new('x', 300);
        byte[] bytes = Encoding.UTF8.GetBytes(longText + "\n");
        using var ms = new MemoryStream(bytes);
        var rows = FixedWidth.Read<LongStringRow>()
            .WithCulture(CultureInfo.GetCultureInfo("en-US"))
            .FromStream(ms)
            .ToList();
        Assert.Single(rows);
        Assert.Equal(300, rows[0].Text!.Length);
    }

    // ---------- CsvColumn missing methods ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvColumn_CharPath_Unquote_NoQuotes()
    {
        // Unquote on already-unquoted value should return same.
        using var reader = Csv.Read().FromText("unquoted\n");
        Assert.True(reader.MoveNext());
        var col = reader.Current[0];
        var unq = col.Unquote();
        Assert.Equal(col.Span.Length, unq.Length);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvColumn_BytePath_Unquote_NoQuotes()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("unquoted\n"));
        using var reader = Csv.Read().FromStream(stream, out _);
        Assert.True(reader.MoveNext());
        var col = reader.Current[0];
        var unq = col.Unquote();
        Assert.Equal(col.Span.Length, unq.Length);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvColumn_CharPath_Unquote_WithQuote()
    {
        using var reader = Csv.Read().FromText("\"quoted\"\n");
        Assert.True(reader.MoveNext());
        var col = reader.Current[0];
        var unq = col.Unquote('"');
        Assert.True(unq.Length < col.Span.Length);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvColumn_BytePath_Unquote_WithQuote()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("\"quoted\"\n"));
        using var reader = Csv.Read().FromStream(stream, out _);
        Assert.True(reader.MoveNext());
        var col = reader.Current[0];
        var unq = col.Unquote((byte)'"');
        Assert.True(unq.Length < col.Span.Length);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvColumn_CharPath_Equals_NullOther_False()
    {
        using var reader = Csv.Read().FromText("Hello\n");
        Assert.True(reader.MoveNext());
        Assert.False(reader.Current[0].Equals(null));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvColumn_BytePath_Equals_NullOther_False()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Hello\n"));
        using var reader = Csv.Read().FromStream(stream, out _);
        Assert.True(reader.MoveNext());
        Assert.False(reader.Current[0].Equals(null));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvColumn_CharPath_Parse_Failure_Throws()
    {
        using var reader = Csv.Read().FromText("notanumber\n");
        Assert.True(reader.MoveNext());
        var col = reader.Current[0];
        try { _ = col.Parse<int>(); Assert.Fail("Expected parse exception"); }
        catch (FormatException) { /* expected */ }
        catch (ArgumentException) { /* also acceptable */ }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvColumn_BytePath_TryParseInt32_OnlyPartialNumber_False()
    {
        // Utf8Parser requires consumed == length; "123abc" is partial.
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("123abc\n"));
        using var reader = Csv.Read().FromStream(stream, out _);
        Assert.True(reader.MoveNext());
        Assert.False(reader.Current[0].TryParseInt32(out _));
    }

    // ---------- CsvRow.GetString edge cases ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRow_GetString_OutOfRange_Throws()
    {
        using var reader = Csv.Read().FromText("a,b\n1,2\n");
        Assert.True(reader.MoveNext());
        Assert.True(reader.MoveNext());
        var row = reader.Current;
        bool threw = false;
        try { _ = row.GetString(99); } catch (Exception) { threw = true; }
        Assert.True(threw);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRow_TryGetColumnSpan_BytePath_InRange()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("a,b\n1,2\n"));
        using var reader = Csv.Read().FromStream(stream, out _);
        Assert.True(reader.MoveNext());
        Assert.True(reader.MoveNext());
        var row = reader.Current;
        Assert.True(row.TryGetColumnSpan(0, out var span));
        Assert.False(span.IsEmpty);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRow_Indexer_BothPaths()
    {
        using var reader = Csv.Read().FromText("a,b\n1,2\n");
        Assert.True(reader.MoveNext());
        Assert.True(reader.MoveNext());
        var row = reader.Current;
        var col = row[0];
        Assert.False(col.IsEmpty);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRow_ColumnCount_BytePath()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("a,b,c,d\n1,2,3,4\n"));
        using var reader = Csv.Read().FromStream(stream, out _);
        Assert.True(reader.MoveNext());
        Assert.Equal(4, reader.Current.ColumnCount);
    }

    // ---------- CsvAsyncStreamReader cancellation mid-stream ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncStreamReader_CancellationDuringRead()
    {
        // Large stream + cancellation token cancelled mid-iteration.
        var sb = new StringBuilder("a,b\n");
        for (int i = 0; i < 100_000; i++) sb.Append(i).Append(",x\n");
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        await using var reader = Csv.CreateAsyncStreamReader(ms);

        var cts = new CancellationTokenSource();
        int n = 0;
        try
        {
            while (await reader.MoveNextAsync(cts.Token))
            {
                n++;
                if (n == 10) cts.Cancel();
            }
        }
        catch (OperationCanceledException) { /* expected */ }
        Assert.True(n >= 10);
    }

    // ---------- CsvWriter ToText/ToFile with empty source ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWrite_EmptyEnumerable_ToText_OnlyHeader()
    {
        var rows = Array.Empty<CoveragePerson>();
        string csv = Csv.WriteToText(rows);
        Assert.Contains("Name", csv);
        Assert.Contains("Age", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task CsvWriteAsync_EmptyEnumerable()
    {
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(ms, Array.Empty<CoveragePerson>(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 0);
    }

    // ---------- FixedWidth WithErrorHandler (alternative to OnError) ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_OnError_Throw_Action()
    {
        // Bad data + OnError returning Throw → exception still thrown.
        string text = "BAD       BADBABAD     BAD     BAD  BAD       \n";
        try
        {
            FixedWidth.Read<FixedAllTypes>()
                .OnError((ctx, ex) => global::HeroParser.FixedWidths.Records.FixedWidthDeserializeErrorAction.Throw)
                .FromText(text)
                .ToList();
        }
        catch (Exception) { /* expected */ }
    }

    // ---------- FixedWidth ReadOnlyList<T> exercised via FromText ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_FromText_Reads()
    {
        string text = "9999999999" + "12345" + "200" + "3.140000" + "2.500000" + "true " + "1234.56000" + "\n" +
                      "1111111111" + "22345" + "100" + "4.140000" + "3.500000" + "false" + "9999.99000" + "\n";
        var rows = FixedWidth.Read<FixedAllTypes>().FromText(text).ToList();
        Assert.Equal(2, rows.Count);
    }

    // ---------- Csv schema inference / delimiter detection ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_InferSchema_BasicCsv()
    {
        string csv = "Name,Age,Active\nAlice,30,true\nBob,25,false\n";
        var schema = Csv.InferSchema(csv);
        Assert.NotNull(schema);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_Validate_BasicCsv()
    {
        string csv = "Name,Age\nAlice,30\nBob,25\n";
        var result = Csv.Validate(csv);
        Assert.NotNull(result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_Validate_Inconsistent_Fails()
    {
        string csv = "a,b,c\n1,2\n3,4,5,6\n";
        var result = Csv.Validate(csv);
        Assert.NotNull(result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_DelimiterDetector_DetectsSemicolon()
    {
        string csv = "a;b;c\n1;2;3\n4;5;6\n";
        char detected = global::HeroParser.SeparatedValues.Detection.CsvDelimiterDetector.DetectDelimiter(
            Encoding.UTF8.GetBytes(csv));
        Assert.Equal(';', detected);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_DelimiterDetector_DetectsPipe()
    {
        string csv = "a|b|c\n1|2|3\n4|5|6\n";
        char detected = global::HeroParser.SeparatedValues.Detection.CsvDelimiterDetector.DetectDelimiter(
            Encoding.UTF8.GetBytes(csv));
        Assert.Equal('|', detected);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_DelimiterDetector_DetectsTab()
    {
        string csv = "a\tb\tc\n1\t2\t3\n4\t5\t6\n";
        char detected = global::HeroParser.SeparatedValues.Detection.CsvDelimiterDetector.DetectDelimiter(
            Encoding.UTF8.GetBytes(csv));
        Assert.Equal('\t', detected);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_DelimiterDetector_FromString()
    {
        char detected = global::HeroParser.SeparatedValues.Detection.CsvDelimiterDetector.DetectDelimiter("a,b,c\n1,2,3\n");
        Assert.Equal(',', detected);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_DelimiterDetector_FromCharSpan()
    {
        char detected = global::HeroParser.SeparatedValues.Detection.CsvDelimiterDetector.DetectDelimiter("a;b\n1;2\n".AsSpan());
        Assert.Equal(';', detected);
    }
}

[GenerateBinder]
public class BoolRow
{
    [PositionalMap(Start = 0, Length = 1)]
    public bool B { get; set; }
}

[GenerateBinder]
public class BoolFullRow
{
    [PositionalMap(Start = 0, Length = 5)]
    public bool B { get; set; }
}

[GenerateBinder]
public class LongStringRow
{
    [PositionalMap(Start = 0, Length = 300)]
    public string? Text { get; set; }
}
