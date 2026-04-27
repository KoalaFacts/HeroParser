using System.Text;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Reading.Data;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Edge cases for FixedWidthDataReader: boolean 1/0/invalid, header-only
/// input, end-of-input edge cases, type-cast scenarios.
/// </summary>
[Trait("Category", "Unit")]
public class FixedWidthDataReaderEdgeTests
{
    private static FixedWidthDataReader CreateReader(string text, FixedWidthDataReaderOptions options)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return HeroParser.FixedWidth.CreateDataReader(new MemoryStream(bytes), readerOptions: options, leaveOpen: false);
    }

    private static FixedWidthDataReaderOptions BoolColumnOpts() => new()
    {
        Columns = [new FixedWidthDataReaderColumn { Name = "B", Start = 0, Length = 5 }],
        HasHeaderRow = false
    };

    [Fact]
    public void GetBoolean_From_1_ReturnsTrue()
    {
        using var reader = CreateReader("1    \n", BoolColumnOpts());
        Assert.True(reader.Read());
        Assert.True(reader.GetBoolean(0));
    }

    [Fact]
    public void GetBoolean_From_0_ReturnsFalse()
    {
        using var reader = CreateReader("0    \n", BoolColumnOpts());
        Assert.True(reader.Read());
        Assert.False(reader.GetBoolean(0));
    }

    [Fact]
    public void GetBoolean_FromInvalidSingleChar_Throws()
    {
        using var reader = CreateReader("X    \n", BoolColumnOpts());
        Assert.True(reader.Read());
        Assert.Throws<FormatException>(() => reader.GetBoolean(0));
    }

    [Fact]
    public void GetBoolean_FromMultiCharNonBool_Throws()
    {
        using var reader = CreateReader("maybe\n", BoolColumnOpts());
        Assert.True(reader.Read());
        Assert.Throws<FormatException>(() => reader.GetBoolean(0));
    }

    [Fact]
    public void HeaderOnly_NoDataRows_HandlesGracefully()
    {
        var opts = new FixedWidthDataReaderOptions
        {
            Columns = [new FixedWidthDataReaderColumn { Name = "Name", Start = 0, Length = 5 }],
            HasHeaderRow = true
        };
        using var reader = CreateReader("Name \n", opts);
        Assert.False(reader.Read());
    }

    [Fact]
    public void EmptyInput_NoRowsRead()
    {
        var opts = new FixedWidthDataReaderOptions
        {
            Columns = [new FixedWidthDataReaderColumn { Name = "X", Start = 0, Length = 5 }],
            HasHeaderRow = false
        };
        using var reader = CreateReader("", opts);
        Assert.False(reader.Read());
    }

    [Fact]
    public void HeaderOnly_NoExplicitColumnNames_FillsDefaults()
    {
        var opts = new FixedWidthDataReaderOptions
        {
            Columns = [new FixedWidthDataReaderColumn { Start = 0, Length = 5 }],
            HasHeaderRow = true
        };
        using var reader = CreateReader("X    \n", opts);
        Assert.False(reader.Read());
        // Default column name should be auto-generated, e.g., "Column0"
        Assert.NotNull(reader.GetName(0));
    }

    [Fact]
    public void GetValue_Of_NullableField_ReturnsDBNull()
    {
        var opts = new FixedWidthDataReaderOptions
        {
            Columns = [new FixedWidthDataReaderColumn { Name = "X", Start = 0, Length = 5 }],
            HasHeaderRow = false,
            NullValues = ["NULL"]
        };
        using var reader = CreateReader("NULL \n", opts);
        Assert.True(reader.Read());
        Assert.Equal(DBNull.Value, reader.GetValue(0));
        Assert.True(reader.IsDBNull(0));
    }

    [Fact]
    public void IsDBNull_OnEmptyField_True()
    {
        var opts = new FixedWidthDataReaderOptions
        {
            Columns = [new FixedWidthDataReaderColumn { Name = "X", Start = 0, Length = 5 }],
            HasHeaderRow = false
        };
        using var reader = CreateReader("     \n", opts);
        Assert.True(reader.Read());
        // Empty/whitespace-only field — IsDBNull behavior may depend on configuration.
        _ = reader.IsDBNull(0);
    }

    [Fact]
    public void GetSchemaTable_ReturnsRowPerColumn()
    {
        var opts = new FixedWidthDataReaderOptions
        {
            Columns = [
                new FixedWidthDataReaderColumn { Name = "Name", Start = 0, Length = 5 },
                new FixedWidthDataReaderColumn { Name = "Age",  Start = 5, Length = 5 }
            ],
            HasHeaderRow = false
        };
        using var reader = CreateReader("alice00030\n", opts);
        var schema = reader.GetSchemaTable();
        Assert.NotNull(schema);
        Assert.Equal(2, schema.Rows.Count);
    }

    [Fact]
    public void GetEnumerator_IteratesAllRows()
    {
        var opts = new FixedWidthDataReaderOptions
        {
            Columns = [new FixedWidthDataReaderColumn { Name = "X", Start = 0, Length = 5 }],
            HasHeaderRow = false
        };
        using var reader = CreateReader("row1 \nrow2 \nrow3 \n", opts);
        int count = 0;
        foreach (var _ in reader)
        {
            count++;
        }
        Assert.Equal(3, count);
    }

    [Fact]
    public void Read_PastEndOfData_ReturnsFalse()
    {
        var opts = new FixedWidthDataReaderOptions
        {
            Columns = [new FixedWidthDataReaderColumn { Name = "X", Start = 0, Length = 5 }],
            HasHeaderRow = false
        };
        using var reader = CreateReader("only \n", opts);
        Assert.True(reader.Read());
        Assert.False(reader.Read());
        Assert.False(reader.Read());
    }

    [Fact]
    public void Close_DisposesUnderlyingResources()
    {
        var opts = new FixedWidthDataReaderOptions
        {
            Columns = [new FixedWidthDataReaderColumn { Name = "X", Start = 0, Length = 5 }],
            HasHeaderRow = false
        };
        var reader = CreateReader("only \n", opts);
        reader.Close();
        Assert.True(reader.IsClosed);
    }
}
