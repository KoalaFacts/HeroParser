using HeroParser.Configuration;

namespace HeroParser.Tests.Core;

/// <summary>
/// Tests for the CsvReader implementation via CsvReader.
/// </summary>
public class CsvReaderTests
{
    [Fact]
    public void Parse_SimpleCSV_ReturnsCorrectData()
    {
        // Arrange
        var csv = "Name,Age,City\nJohn,25,\"New York\"\nJane,30,Boston";

        // Act
        var result = Csv.ParseString(csv);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal(3, result[0].Length);
        Assert.Equal("John", result[0][0]);
        Assert.Equal("25", result[0][1]);
        Assert.Equal("New York", result[0][2]);
        Assert.Equal("Jane", result[1][0]);
        Assert.Equal("30", result[1][1]);
        Assert.Equal("Boston", result[1][2]);
    }

    [Fact]
    public void Parse_WithQuotedFields_HandlesQuotesCorrectly()
    {
        // Arrange
        var csv = "\"First Name\",\"Last, Name\",\"Age\"\n\"John\",\"Doe, Jr.\",\"25\"";

        // Act
        var result = Csv.ParseString(csv);

        // Assert
        Assert.Single(result);
        Assert.Equal("John", result[0][0]);
        Assert.Equal("Doe, Jr.", result[0][1]);
        Assert.Equal("25", result[0][2]);
    }

    [Fact]
    public void Parse_WithEscapedQuotes_HandlesEscapesCorrectly()
    {
        // Arrange
        var csv = "Name,Quote\n\"John\",\"He said \"\"Hello\"\"\"";

        // Act
        var result = Csv.ParseString(csv);

        // Assert
        Assert.Single(result);
        Assert.Equal("John", result[0][0]);
        Assert.Equal("He said \"Hello\"", result[0][1]);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsEmptyArray()
    {
        // Arrange
        var csv = "";

        // Act
        var result = Csv.ParseString(csv);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_OnlyHeaders_ReturnsEmptyArray()
    {
        // Arrange
        var csv = "Name,Age,City";

        // Act
        var result = Csv.ParseString(csv);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_WithCustomConfiguration_UsesConfiguration()
    {
        // Arrange
        var csv = "Name;Age;City\nJohn;25;Boston";
        var config = new CsvReadConfiguration { Delimiter = ';' };

        // Act
        var result = Csv.ParseString(csv, config);

        // Assert
        Assert.Single(result);
        Assert.Equal("John", result[0][0]);
        Assert.Equal("25", result[0][1]);
        Assert.Equal("Boston", result[0][2]);
    }

    [Fact]
    public void Parse_NullInput_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => Csv.ParseString((string)null));
    }


    [Fact]
    public void Parse_WithTextReader_ParsesCorrectly()
    {
        // Arrange
        var csv = "Name,Age\nJohn,25\nJane,30";
        using var reader = new StringReader(csv);

        // Act
        var result = Csv.ParseReader(reader);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("John", result[0][0]);
        Assert.Equal("25", result[0][1]);
        Assert.Equal("Jane", result[1][0]);
        Assert.Equal("30", result[1][1]);
    }

    [Fact]
    public void Parse_NullReader_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => Csv.ParseReader((TextReader)null));
    }

    [Fact]
    public async Task ParseAsync_WithTextReader_ParsesCorrectly()
    {
        // Arrange
        var csv = "Name,Age\nJohn,25\nJane,30";
        using var reader = new StringReader(csv);

        // Act
        var result = await Csv.ParseReaderAsync(reader);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("John", result[0][0]);
        Assert.Equal("25", result[0][1]);
        Assert.Equal("Jane", result[1][0]);
        Assert.Equal("30", result[1][1]);
    }

    [Fact]
    public void Parse_WithNewlineInQuotedField_HandlesCorrectly()
    {
        // Arrange
        var csv = "Name,Description\n\"John\",\"Line 1\nLine 2\"";

        // Act
        var result = Csv.ParseString(csv);

        // Assert
        Assert.Single(result);
        Assert.Equal("John", result[0][0]);
        Assert.Equal("Line 1\nLine 2", result[0][1]);
    }

    [Fact]
    public void Parse_WithTrimValuesEnabled_TrimsWhitespace()
    {
        // Arrange
        var csv = "Name,Age\n John , 25 \n Jane , 30 ";
        var config = new CsvReadConfiguration { TrimValues = true };

        // Act
        var result = Csv.ParseString(csv, config);

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal("John", result[0][0]);
        Assert.Equal("25", result[0][1]);
        Assert.Equal("Jane", result[1][0]);
        Assert.Equal("30", result[1][1]);
    }

    [Fact]
    public void Parse_WithIgnoreEmptyLinesDisabled_IncludesEmptyLines()
    {
        // Arrange
        var csv = "Name,Age\nJohn,25\n\nJane,30";
        var config = new CsvReadConfiguration { IgnoreEmptyLines = false };

        // Act
        var result = Csv.ParseString(csv, config);

        // Assert
        Assert.Equal(3, result.Length);
        Assert.Equal("John", result[0][0]);
        Assert.Equal("25", result[0][1]);
        Assert.Equal("", result[1][0]); // Empty line
        Assert.Equal("Jane", result[2][0]);
        Assert.Equal("30", result[2][1]);
    }
}