using System.Text;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Reading.Data;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Covers the fixed-width data reader's option validation and its null/empty answers.
///
/// A layout is described entirely by numbers, so a mistyped start or length produces a
/// reader that silently slices the wrong bytes. These guards are what turn that into an
/// error at construction instead — and none of them had run.
/// </summary>
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class FixedWidthDataReaderGuardTests
{
    private static MemoryStream Data(string text = "Alice30   \nBob  25   \n")
        => new(Encoding.UTF8.GetBytes(text));

    private static FixedWidthDataReader Create(FixedWidthDataReaderOptions options, MemoryStream? stream = null)
        => FixedWidth.CreateDataReader(stream ?? Data(), readerOptions: options);

    private static FixedWidthDataReaderOptions WithColumns(params FixedWidthDataReaderColumn[] columns)
        => new() { Columns = columns, HasHeaderRow = false };

    [Fact]
    public void NoColumns_IsRejected()
    {
        var ex = Assert.Throws<FixedWidthException>(() => Create(new FixedWidthDataReaderOptions()));
        Assert.Equal(FixedWidthErrorCode.InvalidOptions, ex.ErrorCode);
        Assert.Contains("at least one column", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ColumnNamesOfADifferentLength_AreRejected()
    {
        var options = new FixedWidthDataReaderOptions
        {
            Columns = [new FixedWidthDataReaderColumn { Name = "A", Start = 0, Length = 5 }],
            ColumnNames = ["A", "B"],
        };

        var ex = Assert.Throws<FixedWidthException>(() => Create(options));
        Assert.Contains("2 columns but 1", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NullColumnDefinition_IsRejected()
    {
        var options = new FixedWidthDataReaderOptions { Columns = [null!] };

        var ex = Assert.Throws<FixedWidthException>(() => Create(options));
        Assert.Contains("cannot be null", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NegativeStart_IsRejected()
    {
        var ex = Assert.Throws<FixedWidthException>(
            () => Create(WithColumns(new FixedWidthDataReaderColumn { Name = "A", Start = -1, Length = 5 })));

        Assert.Contains("start must be non-negative", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void NonPositiveLength_IsRejected(int length)
    {
        var ex = Assert.Throws<FixedWidthException>(
            () => Create(WithColumns(new FixedWidthDataReaderColumn { Name = "A", Start = 0, Length = length })));

        Assert.Contains("length must be positive", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StartAndLengthThatOverflow_AreRejected()
    {
        var ex = Assert.Throws<FixedWidthException>(
            () => Create(WithColumns(new FixedWidthDataReaderColumn { Name = "A", Start = int.MaxValue, Length = 2 })));

        Assert.Contains("overflowed", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NullEntryInColumnNames_IsRejected()
    {
        var options = new FixedWidthDataReaderOptions
        {
            Columns = [new FixedWidthDataReaderColumn { Name = "A", Start = 0, Length = 5 }],
            ColumnNames = [null!],
        };

        var ex = Assert.Throws<FixedWidthException>(() => Create(options));
        Assert.Contains("cannot contain null entries", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnnamedColumn_GetsAGeneratedName()
    {
        // A column with no name still has to be addressable by name, so the reader
        // substitutes a positional one rather than leaving it blank.
        using var reader = Create(WithColumns(new FixedWidthDataReaderColumn { Start = 0, Length = 5 }));
        Assert.Equal("Column1", reader.GetName(0));
    }

    [Fact]
    public void ColumnNames_OverrideTheDefinitionNames()
    {
        var options = new FixedWidthDataReaderOptions
        {
            Columns = [new FixedWidthDataReaderColumn { Name = "Original", Start = 0, Length = 5 }],
            ColumnNames = ["Renamed"],
            HasHeaderRow = false,
        };

        using var reader = Create(options);
        Assert.Equal("Renamed", reader.GetName(0));
    }

    [Fact]
    public void ReadingBeforeRead_IsRejected()
    {
        using var reader = Create(WithColumns(new FixedWidthDataReaderColumn { Name = "Name", Start = 0, Length = 5 }));
        Assert.Throws<InvalidOperationException>(() => reader.GetString(0));
    }

    [Fact]
    public void ConfiguredNullValue_IsReportedAsDbNull()
    {
        var options = new FixedWidthDataReaderOptions
        {
            Columns = [new FixedWidthDataReaderColumn { Name = "Name", Start = 0, Length = 5 }],
            NullValues = ["NULL"],
            HasHeaderRow = false,
        };

        using var reader = Create(options, Data("NULL \nAlice\n"));

        Assert.True(reader.Read());
        Assert.True(reader.IsDBNull(0));
        Assert.Equal(DBNull.Value, reader.GetValue(0));
        Assert.Throws<InvalidCastException>(() => reader.GetString(0));

        Assert.True(reader.Read());
        Assert.False(reader.IsDBNull(0));
        Assert.Equal("Alice", reader.GetString(0));
    }

    [Fact]
    public void EmptyColumn_CannotBeReadAsANumber()
    {
        using var reader = Create(
            WithColumns(new FixedWidthDataReaderColumn { Name = "Age", Start = 0, Length = 5 }),
            Data("     \n"));

        Assert.True(reader.Read());
        Assert.Throws<FormatException>(() => reader.GetInt32(0));
    }

    [Fact]
    public void GetChars_RejectsANegativeOffsetAndStopsPastTheEnd()
    {
        using var reader = Create(
            WithColumns(new FixedWidthDataReaderColumn { Name = "Name", Start = 0, Length = 5 }),
            Data("Alice\n"));

        Assert.True(reader.Read());
        char[] buffer = new char[8];

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetChars(0, -1, buffer, 0, 4));
        Assert.Equal(0, reader.GetChars(0, 100, buffer, 0, 4));
    }
}
