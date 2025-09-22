using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HeroParser.UnitTests.Contracts;

/// <summary>
/// API Contract Tests for CSV Parser - defines the exact interface we must implement.
/// These tests will fail initially (TDD approach) and guide implementation.
/// Reference: contracts/csv-parser-api.md:6-27
/// </summary>
public class CsvParserApiTests
{
    #region Simple Synchronous APIs - contracts/csv-parser-api.md:8-15

    [Fact]
    public void Parse_String_ReturnsStringArrayEnumerable()
    {
        // Arrange
        const string csvContent = "Name,Age,Email\nJohn,25,john@example.com\nJane,30,jane@example.com";

        // Act
        var result = CsvParser.Parse(csvContent);

        // Assert
        Assert.NotNull(result);
        var rows = new List<string[]>(result);
        Assert.Equal(3, rows.Count); // Header + 2 data rows
        Assert.Equal(new[] { "Name", "Age", "Email" }, rows[0]);
        Assert.Equal(new[] { "John", "25", "john@example.com" }, rows[1]);
        Assert.Equal(new[] { "Jane", "30", "jane@example.com" }, rows[2]);
    }

    [Fact]
    public void Parse_ReadOnlySpan_ReturnsStringArrayEnumerable()
    {
        // Arrange
        ReadOnlySpan<char> csvContent = "Name,Age\nJohn,25".AsSpan();

        // Act
        var result = CsvParser.Parse(csvContent);

        // Assert
        Assert.NotNull(result);
        var rows = new List<string[]>(result);
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "Name", "Age" }, rows[0]);
        Assert.Equal(new[] { "John", "25" }, rows[1]);
    }

    [Fact]
    public void ParseFile_FilePath_ReturnsStringArrayEnumerable()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "Name,Age\nJohn,25\nJane,30");

            // Act
            var result = CsvParser.ParseFile(tempFile);

            // Assert
            Assert.NotNull(result);
            var rows = new List<string[]>(result);
            Assert.Equal(3, rows.Count);
            Assert.Equal(new[] { "Name", "Age" }, rows[0]);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Parse_Generic_String_ReturnsTypedEnumerable()
    {
        // Arrange
        const string csvContent = "Name,Age,Email\nJohn,25,john@example.com\nJane,30,jane@example.com";

        // Act
        var result = CsvParser.Parse<Person>(csvContent);

        // Assert
        Assert.NotNull(result);
        var people = new List<Person>(result);
        Assert.Equal(2, people.Count); // Excluding header
        Assert.Equal("John", people[0].Name);
        Assert.Equal(25, people[0].Age);
        Assert.Equal("john@example.com", people[0].Email);
    }

    [Fact]
    public void Parse_Generic_ReadOnlySpan_ReturnsTypedEnumerable()
    {
        // Arrange
        ReadOnlySpan<char> csvContent = "Name,Age\nJohn,25\nJane,30".AsSpan();

        // Act
        var result = CsvParser.Parse<SimplePerson>(csvContent);

        // Assert
        Assert.NotNull(result);
        var people = new List<SimplePerson>(result);
        Assert.Equal(2, people.Count);
        Assert.Equal("John", people[0].Name);
        Assert.Equal(25, people[0].Age);
    }

    [Fact]
    public void ParseFile_Generic_FilePath_ReturnsTypedEnumerable()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "Name,Age\nJohn,25\nJane,30");

            // Act
            var result = CsvParser.ParseFile<SimplePerson>(tempFile);

            // Assert
            Assert.NotNull(result);
            var people = new List<SimplePerson>(result);
            Assert.Equal(2, people.Count);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    #endregion

    #region Asynchronous APIs - contracts/csv-parser-api.md:20-26

    [Fact]
    public async Task ParseAsync_Stream_ReturnsStringArrayAsyncEnumerable()
    {
        // Arrange
        var csvContent = "Name,Age\nJohn,25\nJane,30";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csvContent));

        // Act
        var result = CsvParser.ParseAsync(stream);

        // Assert
        Assert.NotNull(result);
        var rows = new List<string[]>();
        await foreach (var row in result)
        {
            rows.Add(row);
        }
        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { "Name", "Age" }, rows[0]);
    }

    [Fact]
    public async Task ParseAsync_Generic_Stream_ReturnsTypedAsyncEnumerable()
    {
        // Arrange
        var csvContent = "Name,Age\nJohn,25\nJane,30";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csvContent));

        // Act
        var result = CsvParser.ParseAsync<SimplePerson>(stream);

        // Assert
        Assert.NotNull(result);
        var people = new List<SimplePerson>();
        await foreach (var person in result)
        {
            people.Add(person);
        }
        Assert.Equal(2, people.Count);
        Assert.Equal("John", people[0].Name);
    }

    [Fact]
    public async Task ParseFileAsync_FilePath_ReturnsStringArrayAsyncEnumerable()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "Name,Age\nJohn,25\nJane,30");

            // Act
            var result = CsvParser.ParseFileAsync(tempFile);

            // Assert
            Assert.NotNull(result);
            var rows = new List<string[]>();
            await foreach (var row in result)
            {
                rows.Add(row);
            }
            Assert.Equal(3, rows.Count);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ParseFileAsync_Generic_FilePath_ReturnsTypedAsyncEnumerable()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "Name,Age\nJohn,25\nJane,30");

            // Act
            var result = CsvParser.ParseFileAsync<SimplePerson>(tempFile);

            // Assert
            Assert.NotNull(result);
            var people = new List<SimplePerson>();
            await foreach (var person in result)
            {
                people.Add(person);
            }
            Assert.Equal(2, people.Count);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ParseAsync_WithCancellationToken_RespectsCancel()
    {
        // Arrange
        var csvContent = "Name,Age\nJohn,25\nJane,30";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csvContent));
        using var cts = new CancellationTokenSource();

        // Act & Assert
        var result = CsvParser.ParseAsync(stream, cts.Token);
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var row in result)
            {
                // Should be cancelled
            }
        });
    }

    #endregion

    #region Fluent Configuration API - contracts/csv-parser-api.md:32-44

    [Fact]
    public void Configure_FluentAPI_ReturnsConfiguredParser()
    {
        // Arrange & Act
        var parser = CsvParser.Configure()
            .WithDelimiter(';')
            .WithQuoteChar('"')
            .AllowComments()
            .TrimWhitespace()
            .EnableParallelProcessing()
            .EnableSIMDOptimizations()
            .Build();

        // Assert
        Assert.NotNull(parser);

        // Test with semicolon-delimited data
        var result = parser.Parse("Name;Age\nJohn;25");
        var rows = new List<string[]>(result);
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "Name", "Age" }, rows[0]);
        Assert.Equal(new[] { "John", "25" }, rows[1]);
    }

    [Fact]
    public void Configure_CustomMapping_ReturnsConfiguredParser()
    {
        // Arrange & Act
        var parser = CsvParser.Configure()
            .MapField<Person>(p => p.Name, 0)
            .MapField<Person>(p => p.Age, 1)
            .MapField<Person>(p => p.Email, 2)
            .Build();

        // Assert
        Assert.NotNull(parser);

        var result = parser.Parse<Person>("John,25,john@example.com");
        var people = new List<Person>(result);
        Assert.Single(people);
        Assert.Equal("John", people[0].Name);
        Assert.Equal(25, people[0].Age);
        Assert.Equal("john@example.com", people[0].Email);
    }

    #endregion

    #region Error Handling Contract - contracts/csv-parser-api.md:82-98

    [Fact]
    public void Parse_MalformedCsv_ThrowsCsvParseException()
    {
        // Arrange
        const string malformedCsv = "Name,Age\n\"Unclosed quote,25";

        // Act & Assert
        var exception = Assert.Throws<CsvParseException>(() =>
        {
            var result = CsvParser.Parse(malformedCsv);
            // Force enumeration to trigger parsing
            var _ = new List<string[]>(result);
        });

        Assert.True(exception.LineNumber > 0);
        Assert.True(exception.ColumnNumber > 0);
        Assert.NotNull(exception.FieldValue);
    }

    [Fact]
    public void Parse_InvalidTypeMapping_ThrowsCsvMappingException()
    {
        // Arrange
        const string csvContent = "Name,Age\nJohn,NotANumber";

        // Act & Assert
        var exception = Assert.Throws<CsvMappingException>(() =>
        {
            var result = CsvParser.Parse<SimplePerson>(csvContent);
            // Force enumeration to trigger parsing
            var _ = new List<SimplePerson>(result);
        });

        Assert.Equal(typeof(SimplePerson), exception.TargetType);
        Assert.NotNull(exception.FieldName);
    }

    #endregion

    #region Performance Contract Validation - contracts/csv-parser-api.md:72-76

    [Fact]
    public void Parse_StartupTime_CompletesWithinLatencyRequirement()
    {
        // Arrange
        const string csvContent = "Name,Age\nJohn,25";
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        var result = CsvParser.Parse(csvContent);
        stopwatch.Stop();

        // Assert - Startup time requirement: <1ms
        Assert.True(stopwatch.ElapsedMilliseconds < 1,
            $"Startup time {stopwatch.ElapsedMilliseconds}ms exceeds 1ms requirement");

        // Ensure result is valid
        var rows = new List<string[]>(result);
        Assert.Equal(2, rows.Count);
    }

    #endregion
}

#region Test Data Models

public class Person
{
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
    public string Email { get; set; } = string.Empty;
}

public class SimplePerson
{
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
}

#endregion

#region Expected Exception Types - contracts/csv-parser-api.md:82-98

/// <summary>
/// Base exception for CSV parsing errors
/// </summary>
public class CsvParseException : Exception
{
    public int LineNumber { get; }
    public int ColumnNumber { get; }
    public string FieldValue { get; }

    public CsvParseException(int lineNumber, int columnNumber, string fieldValue, string message)
        : base(message)
    {
        LineNumber = lineNumber;
        ColumnNumber = columnNumber;
        FieldValue = fieldValue;
    }
}

/// <summary>
/// Exception for type mapping errors during CSV parsing
/// </summary>
public class CsvMappingException : CsvParseException
{
    public Type TargetType { get; }
    public string FieldName { get; }

    public CsvMappingException(Type targetType, string fieldName, int lineNumber, int columnNumber,
        string fieldValue, string message)
        : base(lineNumber, columnNumber, fieldValue, message)
    {
        TargetType = targetType;
        FieldName = fieldName;
    }
}

#endregion

#region Placeholder Implementation for Contract Testing

/// <summary>
/// Placeholder CsvParser implementation for contract testing.
/// All methods throw NotImplementedException to ensure TDD approach.
/// This will be replaced by the actual high-performance implementation.
/// </summary>
public static class CsvParser
{
    // Simple synchronous APIs
    public static IEnumerable<string[]> Parse(string csvContent)
        => throw new NotImplementedException("CsvParser.Parse(string) not yet implemented - implement in Phase 3.5");

    public static IEnumerable<string[]> Parse(ReadOnlySpan<char> csvContent)
        => throw new NotImplementedException("CsvParser.Parse(ReadOnlySpan<char>) not yet implemented - implement in Phase 3.5");

    public static IEnumerable<string[]> ParseFile(string filePath)
        => throw new NotImplementedException("CsvParser.ParseFile(string) not yet implemented - implement in Phase 3.5");

    // Generic synchronous APIs
    public static IEnumerable<T> Parse<T>(string csvContent)
        => throw new NotImplementedException("CsvParser.Parse<T>(string) not yet implemented - implement in Phase 3.5");

    public static IEnumerable<T> Parse<T>(ReadOnlySpan<char> csvContent)
        => throw new NotImplementedException("CsvParser.Parse<T>(ReadOnlySpan<char>) not yet implemented - implement in Phase 3.5");

    public static IEnumerable<T> ParseFile<T>(string filePath)
        => throw new NotImplementedException("CsvParser.ParseFile<T>(string) not yet implemented - implement in Phase 3.5");

    // Asynchronous APIs
    public static IAsyncEnumerable<string[]> ParseAsync(Stream csvStream, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("CsvParser.ParseAsync(Stream) not yet implemented - implement in Phase 3.5");

    public static IAsyncEnumerable<T> ParseAsync<T>(Stream csvStream, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("CsvParser.ParseAsync<T>(Stream) not yet implemented - implement in Phase 3.5");

    public static IAsyncEnumerable<string[]> ParseFileAsync(string filePath, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("CsvParser.ParseFileAsync(string) not yet implemented - implement in Phase 3.5");

    public static IAsyncEnumerable<T> ParseFileAsync<T>(string filePath, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("CsvParser.ParseFileAsync<T>(string) not yet implemented - implement in Phase 3.5");

    // Configuration API
    public static ICsvParserBuilder Configure()
        => throw new NotImplementedException("CsvParser.Configure() not yet implemented - implement in Phase 3.6");
}

/// <summary>
/// Placeholder fluent builder interface for CSV parser configuration.
/// </summary>
public interface ICsvParserBuilder
{
    ICsvParserBuilder WithDelimiter(char delimiter);
    ICsvParserBuilder WithQuoteChar(char quoteChar);
    ICsvParserBuilder WithEscapeChar(char escapeChar);
    ICsvParserBuilder AllowComments();
    ICsvParserBuilder TrimWhitespace();
    ICsvParserBuilder EnableParallelProcessing();
    ICsvParserBuilder EnableSIMDOptimizations();
    ICsvParserBuilder WithBufferSize(int bufferSize);
    ICsvParserBuilder MapField<T>(System.Linq.Expressions.Expression<Func<T, object>> propertySelector, int fieldIndex);
    ICsvParserBuilder WithCustomConverter<T>(Func<string, T> converter);
    ICsvParser Build();
}

/// <summary>
/// Placeholder configured CSV parser interface.
/// </summary>
public interface ICsvParser
{
    IEnumerable<string[]> Parse(string csvContent);
    IEnumerable<T> Parse<T>(string csvContent);
}

#endregion