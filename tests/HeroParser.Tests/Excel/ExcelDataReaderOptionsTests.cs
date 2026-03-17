using HeroParser.Excels.Reading.Data;
using HeroParser.Tests.Fixtures.Excel;
using Xunit;

namespace HeroParser.Tests.Excel;

[Trait("Category", "Unit")]
public class ExcelDataReaderOptionsTests
{
    // ──────────────────────────────────────────────
    // Default options behave like previous API
    // ──────────────────────────────────────────────

    [Fact]
    public void Default_Options_ReadsHeaderAndData()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Age"],
            ["Alice", "30"]
        ]);

        using var reader = HeroParser.Excel.CreateDataReader(xlsx, ExcelDataReaderOptions.Default);

        Assert.Equal(2, reader.FieldCount);
        Assert.Equal("Name", reader.GetName(0));
        Assert.Equal("Age", reader.GetName(1));
        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));
        Assert.Equal("30", reader.GetString(1));
        Assert.False(reader.Read());
    }

    // ──────────────────────────────────────────────
    // CaseSensitiveHeaders
    // ──────────────────────────────────────────────

    [Fact]
    public void CaseSensitiveHeaders_False_AllowsCaseInsensitiveLookup()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Age"],
            ["Bob", "25"]
        ]);

        var options = new ExcelDataReaderOptions { CaseSensitiveHeaders = false };
        using var reader = HeroParser.Excel.CreateDataReader(xlsx, options);

        // Case-insensitive lookup should work
        Assert.Equal(0, reader.GetOrdinal("name"));
        Assert.Equal(0, reader.GetOrdinal("NAME"));
        Assert.Equal(1, reader.GetOrdinal("age"));
    }

    [Fact]
    public void CaseSensitiveHeaders_True_RejectsWrongCase()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Age"],
            ["Bob", "25"]
        ]);

        var options = new ExcelDataReaderOptions { CaseSensitiveHeaders = true };
        using var reader = HeroParser.Excel.CreateDataReader(xlsx, options);

        // Exact case works
        Assert.Equal(0, reader.GetOrdinal("Name"));
        Assert.Equal(1, reader.GetOrdinal("Age"));

        // Wrong case throws
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetOrdinal("name"));
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetOrdinal("NAME"));
    }

    // ──────────────────────────────────────────────
    // NullValues
    // ──────────────────────────────────────────────

    [Fact]
    public void NullValues_CellMatchingNullValue_ReturnsDBNull()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Age"],
            ["Alice", "N/A"]
        ]);

        var options = new ExcelDataReaderOptions { NullValues = ["N/A", "NULL"] };
        using var reader = HeroParser.Excel.CreateDataReader(xlsx, options);

        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));
        Assert.True(reader.IsDBNull(1));
        Assert.Equal(DBNull.Value, reader.GetValue(1));
    }

    [Fact]
    public void NullValues_EmptyCellAlwaysNull_Regardless()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Age"],
            ["Alice", ""]
        ]);

        var options = new ExcelDataReaderOptions { NullValues = ["N/A"] };
        using var reader = HeroParser.Excel.CreateDataReader(xlsx, options);

        Assert.True(reader.Read());
        Assert.True(reader.IsDBNull(1));
    }

    [Fact]
    public void NullValues_NonMatchingValue_ReturnsValue()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Age"],
            ["Alice", "30"]
        ]);

        var options = new ExcelDataReaderOptions { NullValues = ["N/A"] };
        using var reader = HeroParser.Excel.CreateDataReader(xlsx, options);

        Assert.True(reader.Read());
        Assert.False(reader.IsDBNull(1));
        Assert.Equal("30", reader.GetString(1));
    }

    // ──────────────────────────────────────────────
    // ColumnNames override
    // ──────────────────────────────────────────────

    [Fact]
    public void ColumnNames_Override_ReplacesHeaderNames()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["OriginalName", "OriginalAge"],
            ["Alice", "30"]
        ]);

        var options = new ExcelDataReaderOptions { ColumnNames = ["CustomerName", "CustomerAge"] };
        using var reader = HeroParser.Excel.CreateDataReader(xlsx, options);

        // Header row is consumed, but column names are overridden
        Assert.Equal("CustomerName", reader.GetName(0));
        Assert.Equal("CustomerAge", reader.GetName(1));

        // Lookup by override name works
        Assert.Equal(0, reader.GetOrdinal("CustomerName"));
        Assert.Equal(1, reader.GetOrdinal("CustomerAge"));

        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));
        Assert.Equal("30", reader.GetString(1));
    }

    [Fact]
    public void ColumnNames_WithNoHeader_DefinesSchema()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Alice", "30"],
            ["Bob", "25"]
        ]);

        var options = new ExcelDataReaderOptions
        {
            HasHeaderRow = false,
            ColumnNames = ["Name", "Age"]
        };
        using var reader = HeroParser.Excel.CreateDataReader(xlsx, options);

        Assert.Equal("Name", reader.GetName(0));
        Assert.Equal("Age", reader.GetName(1));
        Assert.Equal(0, reader.GetOrdinal("Name"));

        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));
    }

    // ──────────────────────────────────────────────
    // SkipRows
    // ──────────────────────────────────────────────

    [Fact]
    public void SkipRows_SkipsInitialRows_BeforeHeader()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Comment: ignore this"],
            ["Name", "Age"],
            ["Alice", "30"]
        ]);

        var options = new ExcelDataReaderOptions { SkipRows = 1 };
        using var reader = HeroParser.Excel.CreateDataReader(xlsx, options);

        Assert.Equal("Name", reader.GetName(0));
        Assert.Equal("Age", reader.GetName(1));
        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));
        Assert.False(reader.Read());
    }

    // ──────────────────────────────────────────────
    // HasHeaderRow = false
    // ──────────────────────────────────────────────

    [Fact]
    public void HasHeaderRow_False_FirstRowIsData()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Alice", "30"],
            ["Bob", "25"]
        ]);

        var options = new ExcelDataReaderOptions { HasHeaderRow = false };
        using var reader = HeroParser.Excel.CreateDataReader(xlsx, options);

        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));
        Assert.True(reader.Read());
        Assert.Equal("Bob", reader.GetString(0));
        Assert.False(reader.Read());
    }

    // ──────────────────────────────────────────────
    // Static Default instance
    // ──────────────────────────────────────────────

    [Fact]
    public void Default_StaticInstance_HasExpectedDefaults()
    {
        var defaults = ExcelDataReaderOptions.Default;

        Assert.True(defaults.HasHeaderRow);
        Assert.False(defaults.CaseSensitiveHeaders);
        Assert.False(defaults.AllowMissingColumns);
        Assert.Null(defaults.NullValues);
        Assert.Null(defaults.ColumnNames);
        Assert.Equal(0, defaults.SkipRows);
    }
}
