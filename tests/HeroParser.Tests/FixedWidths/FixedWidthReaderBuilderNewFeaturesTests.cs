using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Mapping;
using Xunit;

namespace HeroParser.Tests.FixedWidths;

/// <summary>
/// Tests for FixedWidthReaderBuilder inline Map&lt;TProperty&gt;() and CaseSensitiveHeaders() features.
/// </summary>
public class FixedWidthReaderBuilderNewFeaturesTests
{
    // "Alice     030Chicago   "
    //  Name[0,10] Age[10,3] City[13,10]
    private const string TWO_ROW_DATA =
        "Alice     030Chicago   \n" +
        "Bob       025New York  ";

    private const string HEADER_AND_TWO_ROWS =
        "Name      AgeCity      \n" +
        "Alice     030Chicago   \n" +
        "Bob       025New York  ";

    #region Map<TProperty> — Start + Length

    [Fact]
    public void Map_StartAndLength_ParsesCorrectly()
    {
        var result = FixedWidth.Read<PersonRecord>()
            .Map(p => p.Name, f => f.Start(0).Length(10))
            .Map(p => p.Age, f => f.Start(10).Length(3))
            .Map(p => p.City, f => f.Start(13).Length(10))
            .FromText(TWO_ROW_DATA);

        Assert.Equal(2, result.Records.Count);
        Assert.Equal("Alice", result.Records[0].Name);
        Assert.Equal(30, result.Records[0].Age);
        Assert.Equal("Chicago", result.Records[0].City);
        Assert.Equal("Bob", result.Records[1].Name);
        Assert.Equal(25, result.Records[1].Age);
        Assert.Equal("New York", result.Records[1].City);
    }

    [Fact]
    public void Map_EndInsteadOfLength_ParsesCorrectly()
    {
        var result = FixedWidth.Read<PersonRecord>()
            .Map(p => p.Name, f => f.Start(0).End(10))
            .Map(p => p.Age, f => f.Start(10).End(13))
            .Map(p => p.City, f => f.Start(13).End(23))
            .FromText(TWO_ROW_DATA);

        Assert.Equal(2, result.Records.Count);
        Assert.Equal("Alice", result.Records[0].Name);
        Assert.Equal(30, result.Records[0].Age);
        Assert.Equal("Chicago", result.Records[0].City);
    }

    [Fact]
    public void Map_EndTakesPrecedenceOverLength_UsesEndValue()
    {
        // Length(99) is ignored because End(10) is also set
        var result = FixedWidth.Read<PersonRecord>()
            .Map(p => p.Name, f => f.Start(0).Length(99).End(10))
            .Map(p => p.Age, f => f.Start(10).End(13))
            .Map(p => p.City, f => f.Start(13).End(23))
            .FromText(TWO_ROW_DATA);

        Assert.Equal("Alice", result.Records[0].Name);
    }

    [Fact]
    public void Map_PadCharOverride_TrimsCustomPadding()
    {
        // Name field padded with '.' instead of space
        const string data = "Alice.....030Chicago   ";
        var result = FixedWidth.Read<PersonRecord>()
            .Map(p => p.Name, f => f.Start(0).Length(10).PadChar('.'))
            .Map(p => p.Age, f => f.Start(10).Length(3))
            .Map(p => p.City, f => f.Start(13).Length(10))
            .FromText(data);

        Assert.Equal("Alice", result.Records[0].Name);
    }

    [Fact]
    public void Map_AlignmentOverride_TrimsFromCorrectSide()
    {
        // Right-aligned name: "     Alice" — leading spaces are padding
        const string data = "     Alice030Chicago   ";
        var result = FixedWidth.Read<PersonRecord>()
            .Map(p => p.Name, f => f.Start(0).Length(10).Alignment(FieldAlignment.Right))
            .Map(p => p.Age, f => f.Start(10).Length(3))
            .Map(p => p.City, f => f.Start(13).Length(10))
            .FromText(data);

        Assert.Equal("Alice", result.Records[0].Name);
    }

    [Fact]
    public void Map_MultipleRows_ParsesAllRows()
    {
        const string data =
            "Row1value 001\n" +
            "Row2value 002\n" +
            "Row3value 003";

        var result = FixedWidth.Read<SimpleRecord>()
            .Map(r => r.Value, f => f.Start(0).Length(13))
            .FromText(data);

        Assert.Equal(3, result.Records.Count);
        Assert.Equal("Row1value 001", result.Records[0].Value);
        Assert.Equal("Row2value 002", result.Records[1].Value);
        Assert.Equal("Row3value 003", result.Records[2].Value);
    }

    [Fact]
    public void Map_MissingStart_ThrowsInvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            FixedWidth.Read<PersonRecord>()
                .Map(p => p.Name, f => f.Length(10))  // No Start!
                .FromText(TWO_ROW_DATA));

        Assert.Contains("Start", ex.Message);
        Assert.Contains("Name", ex.Message);
    }

    [Fact]
    public void Map_MissingLength_ThrowsInvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            FixedWidth.Read<PersonRecord>()
                .Map(p => p.Name, f => f.Start(0))  // No Length or End!
                .FromText(TWO_ROW_DATA));

        Assert.Contains("Length", ex.Message);
        Assert.Contains("Name", ex.Message);
    }

    [Fact]
    public void Map_DuplicateProperty_ThrowsInvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            FixedWidth.Read<PersonRecord>()
                .Map(p => p.Name, f => f.Start(0).Length(10))
                .Map(p => p.Name, f => f.Start(0).Length(10)));  // Duplicate!

        Assert.Contains("Name", ex.Message);
        Assert.Contains("already been mapped", ex.Message);
    }

    [Fact]
    public void Map_AfterWithMap_ThrowsInvalidOperationException()
    {
        var existingMap = new PersonFixedWidthMap();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            FixedWidth.Read<PersonRecord>()
                .WithMap(existingMap)
                .Map(p => p.Name, f => f.Start(0).Length(10)));

        Assert.Contains("WithMap", ex.Message);
    }

    [Fact]
    public void Map_WithNullConfigure_UsesDefaults()
    {
        // Passing null configure should not throw — field will use defaults
        var ex = Assert.Throws<InvalidOperationException>(() =>
            FixedWidth.Read<PersonRecord>()
                .Map(p => p.Name, configure: null)  // null configure is valid
                .FromText(TWO_ROW_DATA));

        // Should fail with missing Start, not a NullReferenceException
        Assert.Contains("Start", ex.Message);
    }

    #endregion

    #region WithHeader skips header row

    [Fact]
    public void Map_WithHeader_SkipsHeaderRow()
    {
        var result = FixedWidth.Read<PersonRecord>()
            .WithHeader()
            .Map(p => p.Name, f => f.Start(0).Length(10))
            .Map(p => p.Age, f => f.Start(10).Length(3))
            .Map(p => p.City, f => f.Start(13).Length(10))
            .FromText(HEADER_AND_TWO_ROWS);

        Assert.Equal(2, result.Records.Count);
        Assert.Equal("Alice", result.Records[0].Name);
        Assert.Equal(30, result.Records[0].Age);
        Assert.Equal("Bob", result.Records[1].Name);
    }

    #endregion

    #region CaseSensitiveHeaders — default is case-insensitive

    [Fact]
    public void WithHeaderName_DefaultCaseInsensitive_MatchesLowercase()
    {
        // Header row has uppercase "NAME", WithHeaderName uses lowercase "name"
        const string data =
            "NAME      AGECITY      \n" +
            "Alice     030Chicago   ";

        var result = FixedWidth.Read<PersonRecord>()
            .WithHeader()
            .Map(p => p.Name, f => f.Start(0).Length(10).WithHeaderName("name"))
            .Map(p => p.Age, f => f.Start(10).Length(3).WithHeaderName("age"))
            .Map(p => p.City, f => f.Start(13).Length(10).WithHeaderName("city"))
            .FromText(data);

        var record = Assert.Single(result.Records);
        Assert.Equal("Alice", record.Name);
    }

    [Fact]
    public void WithHeaderName_ExactMatch_Succeeds()
    {
        var result = FixedWidth.Read<PersonRecord>()
            .WithHeader()
            .Map(p => p.Name, f => f.Start(0).Length(10).WithHeaderName("Name"))
            .Map(p => p.Age, f => f.Start(10).Length(3).WithHeaderName("Age"))
            .Map(p => p.City, f => f.Start(13).Length(10).WithHeaderName("City"))
            .FromText(HEADER_AND_TWO_ROWS);

        Assert.Equal(2, result.Records.Count);
        Assert.Equal("Alice", result.Records[0].Name);
    }

    [Fact]
    public void WithHeaderName_CaseMismatch_CaseSensitive_ThrowsInvalidOperationException()
    {
        const string data =
            "Name      AgeCity      \n" +
            "Alice     030Chicago   ";

        var ex = Assert.Throws<InvalidOperationException>(() =>
            FixedWidth.Read<PersonRecord>()
                .WithHeader()
                .CaseSensitiveHeaders()
                .Map(p => p.Name, f => f.Start(0).Length(10).WithHeaderName("name"))  // wrong case!
                .Map(p => p.Age, f => f.Start(10).Length(3))
                .Map(p => p.City, f => f.Start(13).Length(10))
                .FromText(data));

        Assert.Contains("name", ex.Message);
        Assert.Contains("Name", ex.Message);
        Assert.Contains("mismatch", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WithHeaderName_NoWithHeaderName_DoesNotValidate()
    {
        // Map without WithHeaderName should work even if headers don't match
        var result = FixedWidth.Read<PersonRecord>()
            .WithHeader()
            .Map(p => p.Name, f => f.Start(0).Length(10))  // no WithHeaderName
            .Map(p => p.Age, f => f.Start(10).Length(3))
            .Map(p => p.City, f => f.Start(13).Length(10))
            .FromText(HEADER_AND_TWO_ROWS);

        Assert.Equal(2, result.Records.Count);
    }

    [Fact]
    public void CaseSensitiveHeaders_WithoutWithHeader_HasNoEffect()
    {
        // CaseSensitiveHeaders is a no-op when there's no header row
        var result = FixedWidth.Read<PersonRecord>()
            .CaseSensitiveHeaders()  // no .WithHeader(), so this is a no-op
            .Map(p => p.Name, f => f.Start(0).Length(10))
            .Map(p => p.Age, f => f.Start(10).Length(3))
            .Map(p => p.City, f => f.Start(13).Length(10))
            .FromText(TWO_ROW_DATA);

        Assert.Equal(2, result.Records.Count);
        Assert.Equal("Alice", result.Records[0].Name);
    }

    [Fact]
    public void WithHeaderName_AllCombinations_DefaultInsensitive()
    {
        const string data =
            "nAmE      aGeCiTy      \n" +
            "Alice     030Chicago   ";

        // All sorts of mixed-case headers — default insensitive matching handles them all
        var result = FixedWidth.Read<PersonRecord>()
            .WithHeader()
            .Map(p => p.Name, f => f.Start(0).Length(10).WithHeaderName("NAME"))
            .Map(p => p.Age, f => f.Start(10).Length(3).WithHeaderName("AGE"))
            .Map(p => p.City, f => f.Start(13).Length(10).WithHeaderName("CITY"))
            .FromText(data);

        var record = Assert.Single(result.Records);
        Assert.Equal("Alice", record.Name);
    }

    #endregion

    #region Test record types

    private sealed class PersonRecord
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
        public string City { get; set; } = string.Empty;
    }

    private sealed class SimpleRecord
    {
        public string Value { get; set; } = string.Empty;
    }

    /// <summary>
    /// A minimal IFixedWidthReadMapSource for the WithMap test.
    /// </summary>
    private sealed class PersonFixedWidthMap : IFixedWidthReadMapSource<PersonRecord>
    {
        public HeroParser.FixedWidths.Records.Binding.FixedWidthRecordDescriptor<PersonRecord> BuildReadDescriptor()
            => throw new NotSupportedException("Not needed for this test.");
    }

    #endregion
}
