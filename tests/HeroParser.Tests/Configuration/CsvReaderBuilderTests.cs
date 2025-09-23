using HeroParser.Configuration;

namespace HeroParser.Tests.Configuration;

/// <summary>
/// Tests for the CsvReaderBuilder fluent API.
/// </summary>
public class CsvReaderBuilderTests
{
    [Fact]
    public void WithDelimiter_SetsDelimiter()
    {
        // Arrange
        // Act
        using var parser = CsvReaderBuilder.ForContent("test").WithDelimiter(';').Build();

        // Assert
        Assert.Equal(';', parser.Configuration.Delimiter);
    }

    [Fact]
    public void WithQuote_SetsQuote()
    {
        // Arrange
        // Act
        using var parser = CsvReaderBuilder.ForContent("test").WithQuote('\'').Build();

        // Assert
        Assert.Equal('\'', parser.Configuration.Quote);
    }

    [Fact]
    public void WithEscape_SetsEscape()
    {
        // Arrange
        // Act
        using var parser = CsvReaderBuilder.ForContent("test").WithEscape('\\').Build();

        // Assert
        Assert.Equal('\\', parser.Configuration.Escape);
    }

    [Fact]
    public void WithHeaders_SetsHasHeaderRow()
    {
        // Arrange
        // Act
        using var parser = CsvReaderBuilder.ForContent("test").WithHeaders(false).Build();

        // Assert
        Assert.False(parser.Configuration.HasHeaderRow);
    }

    [Fact]
    public void IgnoreEmptyLines_SetsIgnoreEmptyLines()
    {
        // Arrange
        // Act
        using var parser = CsvReaderBuilder.ForContent("test").IgnoreEmptyLines(false).Build();

        // Assert
        Assert.False(parser.Configuration.IgnoreEmptyLines);
    }

    [Fact]
    public void TrimValues_SetsTrimValues()
    {
        // Arrange
        // Act
        using var parser = CsvReaderBuilder.ForContent("test").TrimValues(true).Build();

        // Assert
        Assert.True(parser.Configuration.TrimValues);
    }

    [Fact]
    public void StrictMode_SetsStrictMode()
    {
        // Arrange
        // Act
        using var parser = CsvReaderBuilder.ForContent("test").StrictMode(false).Build();

        // Assert
        Assert.False(parser.Configuration.StrictMode);
    }

    [Fact]
    public void WithBufferSize_SetsBufferSize()
    {
        // Arrange
        // Act
        using var parser = CsvReaderBuilder.ForContent("test").WithBufferSize(32768).Build();

        // Assert
        Assert.Equal(32768, parser.Configuration.BufferSize);
    }

    [Fact]
    public void WithBufferSize_ZeroOrNegative_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var builder = CsvReaderBuilder.Create();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithBufferSize(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithBufferSize(-1));
    }

    [Fact]
    public void AllowJaggedRows_SetsAllowJaggedRows()
    {
        // Arrange
        // Act
        using var parser = CsvReaderBuilder.ForContent("test").AllowJaggedRows(true).Build();

        // Assert
        Assert.True(parser.Configuration.AllowJaggedRows);
    }

    [Fact]
    public void ForTsv_SetsTabDelimiter()
    {
        // Arrange
        // Act
        using var parser = CsvReaderBuilder.ForContent("test").ForTsv().Build();

        // Assert
        Assert.Equal('\t', parser.Configuration.Delimiter);
    }

    [Fact]
    public void ForSsv_SetsSemicolonDelimiter()
    {
        // Arrange
        // Act
        using var parser = CsvReaderBuilder.ForContent("test").ForSsv().Build();

        // Assert
        Assert.Equal(';', parser.Configuration.Delimiter);
    }

    [Fact]
    public void ForPsv_SetsPipeDelimiter()
    {
        // Arrange
        // Act
        using var parser = CsvReaderBuilder.ForContent("test").ForPsv().Build();

        // Assert
        Assert.Equal('|', parser.Configuration.Delimiter);
    }

    [Fact]
    public void MethodChaining_AllowsFluentAPI()
    {
        // Arrange
        var builder = CsvReaderBuilder.Create();

        // Act
        using var parser = CsvReaderBuilder.ForContent("test")
            .WithDelimiter(';')
            .WithQuote('\'')
            .WithHeaders(false)
            .TrimValues(true)
            .StrictMode(false)
            .Build();

        // Assert
        Assert.Equal(';', parser.Configuration.Delimiter);
        Assert.Equal('\'', parser.Configuration.Quote);
        Assert.False(parser.Configuration.HasHeaderRow);
        Assert.True(parser.Configuration.TrimValues);
        Assert.False(parser.Configuration.StrictMode);
    }

    [Fact]
    public void Build_WithString_CreatesParserUsingBuiltConfiguration()
    {
        // Arrange
        var csv = "Name;Age\nJohn;25";

        // Act
        using var parser = CsvReaderBuilder.ForContent(csv).WithDelimiter(';').Build();
        var result = parser.ReadAll().ToArray();

        // Assert
        Assert.Single(result);
        Assert.Equal("John", result[0][0]);
        Assert.Equal("25", result[0][1]);
    }

    [Fact]
    public void Build_WithTextReader_CreatesParserUsingBuiltConfiguration()
    {
        // Arrange
        var csv = "Name;Age\nJohn;25";
        using var reader = new StringReader(csv);

        // Act
        using var parser = CsvReaderBuilder.ForReader(reader).WithDelimiter(';').Build();
        var result = parser.ReadAll().ToArray();

        // Assert
        Assert.Single(result);
        Assert.Equal("John", result[0][0]);
        Assert.Equal("25", result[0][1]);
    }

    [Fact]
    public void FromConfiguration_CreatesBuilderFromExistingConfig()
    {
        // Arrange
        var originalConfig = new CsvReadConfiguration
        {
            Delimiter = ';',
            Quote = '\'',
            HasHeaderRow = false
        };

        // Act
        var builder = CsvReaderBuilder.FromConfiguration(originalConfig);
        using var parser = builder.WithContent("test").Build();

        // Assert
        Assert.Equal(';', parser.Configuration.Delimiter);
        Assert.Equal('\'', parser.Configuration.Quote);
        Assert.False(parser.Configuration.HasHeaderRow);
    }


    [Fact]
    public void Build_CreatesIndependentConfiguration()
    {
        // Arrange
        var builder = CsvReaderBuilder.Create();
        using var parser1 = builder.WithDelimiter(';').WithContent("test").Build();

        // Act
        using var parser2 = builder.WithDelimiter(',').WithContent("test").Build();

        // Assert
        Assert.Equal(';', parser1.Configuration.Delimiter);
        Assert.Equal(',', parser2.Configuration.Delimiter);
    }
}