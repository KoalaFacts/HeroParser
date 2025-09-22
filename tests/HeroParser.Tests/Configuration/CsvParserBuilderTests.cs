using HeroParser.Configuration;
using System;

namespace HeroParser.Tests.Configuration;

/// <summary>
/// Tests for the CsvParserBuilder fluent API.
/// </summary>
public class CsvParserBuilderTests
{
    [Fact]
    public void Create_ReturnsNewBuilder()
    {
        // Act
        var builder = CsvParserBuilder.Create();

        // Assert
        Assert.NotNull(builder);
    }

    [Fact]
    public void WithDelimiter_SetsDelimiter()
    {
        // Arrange
        var builder = CsvParserBuilder.Create();

        // Act
        var config = builder.WithDelimiter(';').Build();

        // Assert
        Assert.Equal(';', config.Delimiter);
    }

    [Fact]
    public void WithQuote_SetsQuote()
    {
        // Arrange
        var builder = CsvParserBuilder.Create();

        // Act
        var config = builder.WithQuote('\'').Build();

        // Assert
        Assert.Equal('\'', config.Quote);
    }

    [Fact]
    public void WithEscape_SetsEscape()
    {
        // Arrange
        var builder = CsvParserBuilder.Create();

        // Act
        var config = builder.WithEscape('\\').Build();

        // Assert
        Assert.Equal('\\', config.Escape);
    }

    [Fact]
    public void WithHeaders_SetsHasHeaderRow()
    {
        // Arrange
        var builder = CsvParserBuilder.Create();

        // Act
        var config = builder.WithHeaders(false).Build();

        // Assert
        Assert.False(config.HasHeaderRow);
    }

    [Fact]
    public void IgnoreEmptyLines_SetsIgnoreEmptyLines()
    {
        // Arrange
        var builder = CsvParserBuilder.Create();

        // Act
        var config = builder.IgnoreEmptyLines(false).Build();

        // Assert
        Assert.False(config.IgnoreEmptyLines);
    }

    [Fact]
    public void TrimValues_SetsTrimValues()
    {
        // Arrange
        var builder = CsvParserBuilder.Create();

        // Act
        var config = builder.TrimValues(true).Build();

        // Assert
        Assert.True(config.TrimValues);
    }

    [Fact]
    public void StrictMode_SetsStrictMode()
    {
        // Arrange
        var builder = CsvParserBuilder.Create();

        // Act
        var config = builder.StrictMode(false).Build();

        // Assert
        Assert.False(config.StrictMode);
    }

    [Fact]
    public void WithBufferSize_SetsBufferSize()
    {
        // Arrange
        var builder = CsvParserBuilder.Create();

        // Act
        var config = builder.WithBufferSize(32768).Build();

        // Assert
        Assert.Equal(32768, config.BufferSize);
    }

    [Fact]
    public void WithBufferSize_ZeroOrNegative_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var builder = CsvParserBuilder.Create();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithBufferSize(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.WithBufferSize(-1));
    }

    [Fact]
    public void AllowJaggedRows_SetsAllowJaggedRows()
    {
        // Arrange
        var builder = CsvParserBuilder.Create();

        // Act
        var config = builder.AllowJaggedRows(true).Build();

        // Assert
        Assert.True(config.AllowJaggedRows);
    }

    [Fact]
    public void ForTsv_SetsTabDelimiter()
    {
        // Arrange
        var builder = CsvParserBuilder.Create();

        // Act
        var config = builder.ForTsv().Build();

        // Assert
        Assert.Equal('\t', config.Delimiter);
    }

    [Fact]
    public void ForSsv_SetsSemicolonDelimiter()
    {
        // Arrange
        var builder = CsvParserBuilder.Create();

        // Act
        var config = builder.ForSsv().Build();

        // Assert
        Assert.Equal(';', config.Delimiter);
    }

    [Fact]
    public void ForPsv_SetsPipeDelimiter()
    {
        // Arrange
        var builder = CsvParserBuilder.Create();

        // Act
        var config = builder.ForPsv().Build();

        // Assert
        Assert.Equal('|', config.Delimiter);
    }

    [Fact]
    public void MethodChaining_AllowsFluentAPI()
    {
        // Arrange
        var builder = CsvParserBuilder.Create();

        // Act
        var config = builder
            .WithDelimiter(';')
            .WithQuote('\'')
            .WithHeaders(false)
            .TrimValues(true)
            .StrictMode(false)
            .Build();

        // Assert
        Assert.Equal(';', config.Delimiter);
        Assert.Equal('\'', config.Quote);
        Assert.False(config.HasHeaderRow);
        Assert.True(config.TrimValues);
        Assert.False(config.StrictMode);
    }

    [Fact]
    public void Parse_WithString_ParsesUsingBuiltConfiguration()
    {
        // Arrange
        var csv = "Name;Age\nJohn;25";
        var builder = CsvParserBuilder.Create().WithDelimiter(';');

        // Act
        var result = builder.Parse(csv);

        // Assert
        Assert.Single(result);
        Assert.Equal("John", result[0][0]);
        Assert.Equal("25", result[0][1]);
    }

    [Fact]
    public void Parse_WithTextReader_ParsesUsingBuiltConfiguration()
    {
        // Arrange
        var csv = "Name;Age\nJohn;25";
        using var reader = new System.IO.StringReader(csv);
        var builder = CsvParserBuilder.Create().WithDelimiter(';');

        // Act
        var result = builder.Parse(reader);

        // Assert
        Assert.Single(result);
        Assert.Equal("John", result[0][0]);
        Assert.Equal("25", result[0][1]);
    }

    [Fact]
    public void FromConfiguration_CreatesBuilderFromExistingConfig()
    {
        // Arrange
        var originalConfig = new CsvConfiguration
        {
            Delimiter = ';',
            Quote = '\'',
            HasHeaderRow = false
        };

        // Act
        var builder = CsvParserBuilder.FromConfiguration(originalConfig);
        var config = builder.Build();

        // Assert
        Assert.Equal(';', config.Delimiter);
        Assert.Equal('\'', config.Quote);
        Assert.False(config.HasHeaderRow);
    }

    [Fact]
    public void FromConfiguration_NullConfiguration_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => CsvParserBuilder.FromConfiguration(null));
    }

    [Fact]
    public void Build_CreatesIndependentConfiguration()
    {
        // Arrange
        var builder = CsvParserBuilder.Create();
        var config1 = builder.WithDelimiter(';').Build();

        // Act
        var config2 = builder.WithDelimiter(',').Build();

        // Assert
        Assert.Equal(';', config1.Delimiter);
        Assert.Equal(',', config2.Delimiter);
    }
}