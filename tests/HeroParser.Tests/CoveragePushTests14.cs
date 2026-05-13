using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Writing;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests;

/// <summary>Fourteenth wave: CsvColumn UnquoteToString/TimeZoneInfo, async writer edge paths.</summary>
public class CoveragePushTests14
{
    // ---------- CsvColumn.UnquoteToString with escape + quote (char and byte path) ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvColumn_CharPath_UnquoteToString_QuoteAndEscape()
    {
        // Field uses backslash escape and embedded doubled quote.
        string csv = "\"hello \\\"world\\\" \"\"again\"\"\"\n";
        using var reader = Csv.Read().WithEscapeCharacter('\\').FromText(csv);
        Assert.True(reader.MoveNext());
        // Call UnquoteToString(quote, escape) overload.
        var col = reader.Current[0];
        var s = col.UnquoteToString('"', '\\');
        Assert.NotNull(s);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvColumn_BytePath_UnquoteToString_QuoteAndEscape()
    {
        string csv = "\"hello \\\"world\\\"\"\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        using var reader = Csv.Read().WithEscapeCharacter('\\').FromStream(stream, out _);
        Assert.True(reader.MoveNext());
        var col = reader.Current[0];
        var s = col.UnquoteToString((byte)'"', (byte)'\\');
        Assert.NotNull(s);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvColumn_CharPath_UnquoteToString_SimpleQuoteOnly()
    {
        string csv = "\"hello\"\n";
        using var reader = Csv.Read().FromText(csv);
        Assert.True(reader.MoveNext());
        Assert.Equal("hello", reader.Current[0].UnquoteToString('"'));
        Assert.Equal("hello", reader.Current[0].UnquoteToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvColumn_BytePath_UnquoteToString_SimpleQuoteOnly()
    {
        string csv = "\"hello\"\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        using var reader = Csv.Read().FromStream(stream, out _);
        Assert.True(reader.MoveNext());
        Assert.Equal("hello", reader.Current[0].UnquoteToString((byte)'"'));
        Assert.Equal("hello", reader.Current[0].UnquoteToString());
    }

    // ---------- CsvColumn TryParseTimeZoneInfo ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvColumn_CharPath_TryParseTimeZoneInfo_Utc()
    {
        string csv = "UTC\n";
        using var reader = Csv.Read().FromText(csv);
        Assert.True(reader.MoveNext());
        Assert.True(reader.Current[0].TryParseTimeZoneInfo(out var tz));
        Assert.Equal(TimeZoneInfo.Utc.Id, tz.Id);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvColumn_BytePath_TryParseTimeZoneInfo_Utc()
    {
        string csv = "UTC\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        using var reader = Csv.Read().FromStream(stream, out _);
        Assert.True(reader.MoveNext());
        Assert.True(reader.Current[0].TryParseTimeZoneInfo(out var tz));
        Assert.Equal(TimeZoneInfo.Utc.Id, tz.Id);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvColumn_TryParseTimeZoneInfo_BadValue_False()
    {
        string csv = "Not/A/Zone\n";
        using var reader = Csv.Read().FromText(csv);
        Assert.True(reader.MoveNext());
        Assert.False(reader.Current[0].TryParseTimeZoneInfo(out _));
    }

    // ---------- Async writer with embedded newlines ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_EmbeddedNewline_TriggersQuoting()
    {
        var rows = new[] { new CoveragePerson { Name = "line1\nline2", Age = 1 } };
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(ms, rows, cancellationToken: TestContext.Current.CancellationToken);
        string csv = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("\"line1\nline2\"", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_CarriageReturnInField()
    {
        var rows = new[] { new CoveragePerson { Name = "with\rcarriage", Age = 1 } };
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(ms, rows, cancellationToken: TestContext.Current.CancellationToken);
        string csv = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("\"with\rcarriage\"", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_CustomQuoteChar()
    {
        var rows = new[] { new CoveragePerson { Name = "with,comma", Age = 1 } };
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(
            ms,
            rows,
            options: new CsvWriteOptions { Quote = '\'' },
            cancellationToken: TestContext.Current.CancellationToken);
        string csv = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("'with,comma'", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_CustomQuoteAndCustomDelimiter()
    {
        var rows = new[] { new CoveragePerson { Name = "with;semi", Age = 1 } };
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(
            ms,
            rows,
            options: new CsvWriteOptions { Delimiter = ';', Quote = '\'' },
            cancellationToken: TestContext.Current.CancellationToken);
        string csv = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("'with;semi'", csv);
    }

    // ---------- AsyncWriter: large fields without quoting trigger ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_VeryLongUnquotedField()
    {
        var rows = new[] { new CoveragePerson { Name = new string('x', 20_000), Age = 0 } };
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(ms, rows, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 20_000);
    }

    // (Raw stream writer construction APIs are nuanced and vary; omitting.)

    // ---------- Csv writer: Encoding overload variation ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_WriteToFile_WithEncoding()
    {
        string path = Path.GetTempFileName();
        try
        {
            var rows = new[] { new CoveragePerson { Name = "Café", Age = 30 } };
            Csv.Write<CoveragePerson>().ToFile(path, rows);
            string content = File.ReadAllText(path);
            Assert.Contains("Café", content);
        }
        finally { File.Delete(path); }
    }

    // (ToFileAsync requires IAsyncEnumerable not array; omitting.)

    // ---------- FixedWidth writer to file with encoding ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_WriteToFile_WithEncoding()
    {
        string path = Path.GetTempFileName();
        try
        {
            var rows = new[] { new FixedAllTypes { L = 1L, S = 2, B = 3, D = 0.5, F = 0.25f, Bo = true, M = 1.5m } };
            global::HeroParser.FixedWidth.WriteToFile(path, rows, encoding: Encoding.UTF8);
            Assert.True(File.Exists(path));
        }
        finally { File.Delete(path); }
    }

    // ---------- Csv builder with options + write ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriter_WithMaxOutputSize_AlmostHits()
    {
        // Doesn't exceed limit.
        var rows = new[] { new CoveragePerson { Name = "A", Age = 1 } };
        string csv = Csv.WriteToText(rows, options: new CsvWriteOptions { MaxOutputSize = 10000 });
        Assert.NotEmpty(csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_MaxOutputSize_Throws()
    {
        var rows = Enumerable.Range(0, 1000).Select(i => new CoveragePerson { Name = $"P{i}", Age = i });
        using var ms = new MemoryStream();
        await Assert.ThrowsAsync<CsvException>(() => Csv.WriteToStreamAsync(
            ms,
            rows,
            options: new CsvWriteOptions { MaxOutputSize = 50 },
            cancellationToken: TestContext.Current.CancellationToken).AsTask());
    }
}
