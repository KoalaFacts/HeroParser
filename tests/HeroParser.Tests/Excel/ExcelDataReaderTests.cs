using HeroParser.Tests.Fixtures.Excel;
using Xunit;

namespace HeroParser.Tests.Excel;

[Trait("Category", "Unit")]
public class ExcelDataReaderTests
{
    [Fact]
    public void Read_AdvancesThroughRows()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Age"],
            ["Alice", "30"],
            ["Bob", "25"]
        ]);

        using var reader = HeroParser.Excel.CreateDataReader(xlsx);

        Assert.True(reader.Read());
        Assert.True(reader.Read());
        Assert.False(reader.Read());
    }

    [Fact]
    public void FieldCount_ReturnsColumnCount()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Age", "City"],
            ["Alice", "30", "Seattle"]
        ]);

        using var reader = HeroParser.Excel.CreateDataReader(xlsx);

        Assert.Equal(3, reader.FieldCount);
    }

    [Fact]
    public void GetName_ReturnsHeaderNames()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Age", "City"],
            ["Alice", "30", "Seattle"]
        ]);

        using var reader = HeroParser.Excel.CreateDataReader(xlsx);

        Assert.Equal("Name", reader.GetName(0));
        Assert.Equal("Age", reader.GetName(1));
        Assert.Equal("City", reader.GetName(2));
    }

    [Fact]
    public void GetOrdinal_ReturnsColumnIndex()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Age", "City"],
            ["Alice", "30", "Seattle"]
        ]);

        using var reader = HeroParser.Excel.CreateDataReader(xlsx);

        Assert.Equal(0, reader.GetOrdinal("Name"));
        Assert.Equal(1, reader.GetOrdinal("Age"));
        Assert.Equal(2, reader.GetOrdinal("City"));
    }

    [Fact]
    public void GetOrdinal_CaseInsensitive()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Age"],
            ["Alice", "30"]
        ]);

        using var reader = HeroParser.Excel.CreateDataReader(xlsx);

        Assert.Equal(0, reader.GetOrdinal("name"));
        Assert.Equal(0, reader.GetOrdinal("NAME"));
    }

    [Fact]
    public void GetString_ReturnsCellValues()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Age"],
            ["Alice", "30"]
        ]);

        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.True(reader.Read());

        Assert.Equal("Alice", reader.GetString(0));
        Assert.Equal("30", reader.GetString(1));
    }

    [Fact]
    public void GetValue_ReturnsCellValues()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Value"],
            ["Widget", "42"]
        ]);

        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.True(reader.Read());

        Assert.Equal("Widget", reader.GetValue(0));
        Assert.Equal("42", reader.GetValue(1));
    }

    [Fact]
    public void IsDBNull_ForEmptyCells_ReturnsTrue()
    {
        // Create xlsx with sparse data - first row has 3 cols, second row has only 1
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["A", "B", "C"],
            ["Value", "", ""]
        ]);

        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.True(reader.Read());

        Assert.False(reader.IsDBNull(0));
        Assert.True(reader.IsDBNull(1));
        Assert.True(reader.IsDBNull(2));
    }

    [Fact]
    public void HasRows_WithData_ReturnsTrue()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name"],
            ["Alice"]
        ]);

        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.True(reader.HasRows);
    }

    [Fact]
    public void HasRows_HeaderOnly_ReturnsFalse()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name"]
        ]);

        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.False(reader.HasRows);
    }

    [Fact]
    public void Dispose_CleansUpResources()
    {
        var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name"],
            ["Alice"]
        ]);

        var reader = HeroParser.Excel.CreateDataReader(xlsx);
        reader.Dispose();

        Assert.True(reader.IsClosed);
    }

    [Fact]
    public void GetFieldType_ReturnsString()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name"],
            ["Alice"]
        ]);

        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.Equal(typeof(string), reader.GetFieldType(0));
    }

    [Fact]
    public void NextResult_ReturnsFalse()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name"],
            ["Alice"]
        ]);

        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.False(reader.NextResult());
    }

    [Fact]
    public void SheetSelection_ReadsNamedSheet()
    {
        var sheets = new Dictionary<string, string[][]>
        {
            ["First"] = [["Name"], ["A"]],
            ["Second"] = [["Name"], ["B"]],
        };
        using var xlsx = ExcelTestHelper.CreateXlsx(sheets);

        using var reader = HeroParser.Excel.CreateDataReader(xlsx, sheetName: "Second");
        Assert.True(reader.Read());
        Assert.Equal("B", reader.GetString(0));
    }

    [Fact]
    public void GetOrdinal_InvalidColumn_Throws()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name"],
            ["Alice"]
        ]);

        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetOrdinal("NonExistent"));
    }

    [Fact]
    public void Read_BeforeAccess_ThrowsInvalidOperation()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name"],
            ["Alice"]
        ]);

        using var reader = HeroParser.Excel.CreateDataReader(xlsx);

        // Read() not called yet
        Assert.Throws<InvalidOperationException>(() => reader.GetValue(0));
    }

    [Fact]
    public void Indexer_ByOrdinal_ReturnsValue()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Value"],
            ["Widget", "42"]
        ]);

        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.True(reader.Read());

        Assert.Equal("Widget", reader[0]);
        Assert.Equal("42", reader[1]);
    }

    [Fact]
    public void Indexer_ByName_ReturnsValue()
    {
        using var xlsx = ExcelTestHelper.CreateXlsx("Sheet1", [
            ["Name", "Value"],
            ["Widget", "42"]
        ]);

        using var reader = HeroParser.Excel.CreateDataReader(xlsx);
        Assert.True(reader.Read());

        Assert.Equal("Widget", reader["Name"]);
        Assert.Equal("42", reader["Value"]);
    }
}
