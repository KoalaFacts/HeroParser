using HeroParser.Configuration;
using HeroParser.Exceptions;

namespace HeroParser.ComplianceTests;

/// <summary>
/// RFC 4180 compliance tests for CSV reading functionality.
/// </summary>
public class CsvReadingComplianceTests
{
    [Fact]
    public void RFC4180_Example1_BasicCSV()
    {
        // Arrange - Example from RFC 4180
        var csv = "aaa,bbb,ccc\nzzz,yyy,xxx";

        // Act
        var result = Csv.ParseString(csv);

        // Assert
        Assert.Single(result);
        Assert.Equal(3, result[0].Length);
        Assert.Equal("zzz", result[0][0]);
        Assert.Equal("yyy", result[0][1]);
        Assert.Equal("xxx", result[0][2]);
    }

    [Fact]
    public void RFC4180_Example2_QuotedFields()
    {
        // Arrange - Example from RFC 4180
        var csv = "\"aaa\",\"bbb\",\"ccc\"\n\"zzz\",\"yyy\",\"xxx\"";

        // Act
        var result = Csv.ParseString(csv);

        // Assert
        Assert.Single(result);
        Assert.Equal("zzz", result[0][0]);
        Assert.Equal("yyy", result[0][1]);
        Assert.Equal("xxx", result[0][2]);
    }

    [Fact]
    public void RFC4180_Example3_MixedQuotedAndUnquoted()
    {
        // Arrange - Example from RFC 4180
        var csv = "\"aaa\",bbb,\"ccc\"\nzzz,\"yyy\",xxx";

        // Act
        var result = Csv.ParseString(csv);

        // Assert
        Assert.Single(result);
        Assert.Equal("zzz", result[0][0]);
        Assert.Equal("yyy", result[0][1]);
        Assert.Equal("xxx", result[0][2]);
    }

    [Fact]
    public void RFC4180_Example4_QuotedFieldWithComma()
    {
        // Arrange - Example from RFC 4180
        var csv = "\"aaa\",\"b,bb\",\"ccc\"";

        // Act
        var result = Csv.ParseString(csv);

        // Assert
        Assert.Empty(result); // Header only, no data rows
    }

    [Fact]
    public void RFC4180_Example4_WithData_QuotedFieldWithComma()
    {
        // Arrange - RFC 4180 example with data row
        var csv = "\"aaa\",\"b,bb\",\"ccc\"\n\"zzz\",\"y,yy\",\"xxx\"";

        // Act
        var result = Csv.ParseString(csv);

        // Assert
        Assert.Single(result);
        Assert.Equal("zzz", result[0][0]);
        Assert.Equal("y,yy", result[0][1]);
        Assert.Equal("xxx", result[0][2]);
    }

    [Fact]
    public void RFC4180_Example5_QuotedFieldWithNewline()
    {
        // Arrange - Example from RFC 4180
        var csv = "\"aaa\",\"b\nbb\",\"ccc\"";

        // Act
        var result = Csv.ParseString(csv);

        // Assert
        Assert.Empty(result); // Header only, no data rows
    }

    [Fact]
    public void RFC4180_Example5_WithData_QuotedFieldWithNewline()
    {
        // Arrange - RFC 4180 example with data row
        var csv = "\"aaa\",\"b\nbb\",\"ccc\"\n\"zzz\",\"y\nyy\",\"xxx\"";

        // Act
        var result = Csv.ParseString(csv);

        // Assert
        Assert.Single(result);
        Assert.Equal("zzz", result[0][0]);
        Assert.Equal("y\nyy", result[0][1]);
        Assert.Equal("xxx", result[0][2]);
    }

    [Fact]
    public void RFC4180_Example6_QuotedFieldWithQuotes()
    {
        // Arrange - Example from RFC 4180
        var csv = "\"aaa\",\"b\"\"bb\",\"ccc\"";

        // Act
        var result = Csv.ParseString(csv);

        // Assert
        Assert.Empty(result); // Header only, no data rows
    }

    [Fact]
    public void RFC4180_Example6_WithData_QuotedFieldWithQuotes()
    {
        // Arrange - RFC 4180 example with data row
        var csv = "\"aaa\",\"b\"\"bb\",\"ccc\"\n\"zzz\",\"y\"\"yy\",\"xxx\"";

        // Act
        var result = Csv.ParseString(csv);

        // Assert
        Assert.Single(result);
        Assert.Equal("zzz", result[0][0]);
        Assert.Equal("y\"yy", result[0][1]);
        Assert.Equal("xxx", result[0][2]);
    }

    [Fact]
    public void RFC4180_EmptyFields()
    {
        // Arrange
        var csv = "aaa,,ccc\n,yyy,";

        // Act
        var result = Csv.ParseString(csv);

        // Assert
        Assert.Single(result);
        Assert.Equal("", result[0][0]);
        Assert.Equal("yyy", result[0][1]);
        Assert.Equal("", result[0][2]);
    }

    [Fact]
    public void RFC4180_EmptyQuotedFields()
    {
        // Arrange
        var csv = "\"aaa\",\"\",\"ccc\"\n\"\",\"yyy\",\"\"";

        // Act
        var result = Csv.ParseString(csv);

        // Assert
        Assert.Single(result);
        Assert.Equal("", result[0][0]);
        Assert.Equal("yyy", result[0][1]);
        Assert.Equal("", result[0][2]);
    }

    [Fact]
    public void RFC4180_OnlyCommas()
    {
        // Arrange
        var csv = "header1,header2,header3\n,,";

        // Act
        var result = Csv.ParseString(csv);

        // Assert
        Assert.Single(result);
        Assert.Equal(3, result[0].Length);
        Assert.Equal("", result[0][0]);
        Assert.Equal("", result[0][1]);
        Assert.Equal("", result[0][2]);
    }

    [Fact]
    public void RFC4180_CRLF_LineEndings()
    {
        // Arrange - Windows-style line endings
        var csv = "aaa,bbb,ccc\r\nzzz,yyy,xxx";

        // Act
        var result = Csv.ParseString(csv);

        // Assert
        Assert.Single(result);
        Assert.Equal("zzz", result[0][0]);
        Assert.Equal("yyy", result[0][1]);
        Assert.Equal("xxx", result[0][2]);
    }

    [Fact]
    public void RFC4180_LF_LineEndings()
    {
        // Arrange - Unix-style line endings
        var csv = "aaa,bbb,ccc\nzzz,yyy,xxx";

        // Act
        var result = Csv.ParseString(csv);

        // Assert
        Assert.Single(result);
        Assert.Equal("zzz", result[0][0]);
        Assert.Equal("yyy", result[0][1]);
        Assert.Equal("xxx", result[0][2]);
    }

    [Fact]
    public void RFC4180_TrailingNewline()
    {
        // Arrange
        var csv = "aaa,bbb,ccc\nzzz,yyy,xxx\n";

        // Act
        var result = Csv.ParseString(csv);

        // Assert
        Assert.Single(result);
        Assert.Equal("zzz", result[0][0]);
        Assert.Equal("yyy", result[0][1]);
        Assert.Equal("xxx", result[0][2]);
    }

    [Fact]
    public void RFC4180_NoTrailingNewline()
    {
        // Arrange
        var csv = "aaa,bbb,ccc\nzzz,yyy,xxx";

        // Act
        var result = Csv.ParseString(csv);

        // Assert
        Assert.Single(result);
        Assert.Equal("zzz", result[0][0]);
        Assert.Equal("yyy", result[0][1]);
        Assert.Equal("xxx", result[0][2]);
    }

    [Fact]
    public void RFC4180_StrictMode_UnterminatedQuote_ThrowsException()
    {
        // Arrange
        var csv = "\"aaa,bbb,ccc";
        var config = new CsvReadConfiguration { StrictMode = true };

        // Act & Assert
        Assert.Throws<CsvParseException>(() => Csv.ParseString(csv, config));
    }

    [Fact]
    public void RFC4180_StrictMode_UnescapedQuoteInField_ThrowsException()
    {
        // Arrange
        var csv = "aaa,b\"bb,ccc";
        var config = new CsvReadConfiguration { StrictMode = true };

        // Act & Assert
        Assert.Throws<CsvParseException>(() => Csv.ParseString(csv, config));
    }

    [Fact]
    public void RFC4180_WhitespacePreservation()
    {
        // Arrange - RFC 4180 states that whitespace is significant
        var csv = "aaa , bbb , ccc \n zzz , yyy , xxx ";

        // Act
        var result = Csv.ParseString(csv);

        // Assert
        Assert.Single(result);
        Assert.Equal(" zzz ", result[0][0]);
        Assert.Equal(" yyy ", result[0][1]);
        Assert.Equal(" xxx ", result[0][2]);
    }

    [Fact]
    public void RFC4180_QuotedWhitespace()
    {
        // Arrange
        var csv = "\"aaa\",\" bbb \",\"ccc\"\n\"zzz\",\" yyy \",\"xxx\"";

        // Act
        var result = Csv.ParseString(csv);

        // Assert
        Assert.Single(result);
        Assert.Equal("zzz", result[0][0]);
        Assert.Equal(" yyy ", result[0][1]);
        Assert.Equal("xxx", result[0][2]);
    }
}