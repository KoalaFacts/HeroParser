using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HeroParser.UnitTests.Contracts;

/// <summary>
/// API Contract Tests for Fixed-Length Parser - defines the exact interface we must implement.
/// These tests will fail initially (TDD approach) and guide implementation.
/// Reference: contracts/fixed-length-parser-api.md:6-55
/// </summary>
public partial class FixedLengthParserApiTests
{
    #region Simple Copybook-Based Parsing APIs - contracts/fixed-length-parser-api.md:6-14

    [Fact]
    public void Parse_String_CobolCopybook_ReturnsFixedLengthRecordEnumerable()
    {
        // Arrange
        const string content = "1234567890JOHN DOE           00000123456789012022";
        var copybook = CreateTestCopybook();

        // Act
        var result = FixedLengthParser.Parse(content, copybook);

        // Assert
        Assert.NotNull(result);
        var records = new List<FixedLengthRecord>(result);
        Assert.Single(records);
        Assert.Equal("1234567890", records[0].GetField("CustomerId"));
        Assert.Equal("JOHN DOE", records[0].GetField("CustomerName").Trim());
    }

    [Fact]
    public void Parse_Generic_String_CobolCopybook_ReturnsTypedEnumerable()
    {
        // Arrange
        const string content = "1234567890JOHN DOE           00000123456789012022";
        var copybook = CreateTestCopybook();

        // Act
        var result = FixedLengthParser.Parse<CustomerRecord>(content, copybook);

        // Assert
        Assert.NotNull(result);
        var customers = new List<CustomerRecord>(result);
        Assert.Single(customers);
        Assert.Equal("1234567890", customers[0].CustomerId);
        Assert.Equal("JOHN DOE", customers[0].CustomerName.Trim());
        Assert.Equal(1234567890, customers[0].AccountBalance);
    }

    [Fact]
    public void Parse_String_FieldLayout_ReturnsFixedLengthRecordEnumerable()
    {
        // Arrange
        const string content = "1234567890JOHN DOE           00000123456789012022";
        var layout = CreateTestFieldLayout();

        // Act
        var result = FixedLengthParser.Parse(content, layout);

        // Assert
        Assert.NotNull(result);
        var records = new List<FixedLengthRecord>(result);
        Assert.Single(records);
        Assert.Equal("1234567890", records[0].GetField("CustomerId"));
    }

    [Fact]
    public void Parse_Generic_String_FieldLayout_ReturnsTypedEnumerable()
    {
        // Arrange
        const string content = "1234567890JOHN DOE           00000123456789012022";
        var layout = CreateTestFieldLayout();

        // Act
        var result = FixedLengthParser.Parse<CustomerRecord>(content, layout);

        // Assert
        Assert.NotNull(result);
        var customers = new List<CustomerRecord>(result);
        Assert.Single(customers);
        Assert.Equal("1234567890", customers[0].CustomerId);
    }

    #endregion

    #region Schema-Based Parsing APIs - contracts/fixed-length-parser-api.md:16-26

    [Fact]
    public void Parse_WithProgrammaticSchema_ReturnsTypedEnumerable()
    {
        // Arrange
        const string content = "1234567890JOHN DOE                   00001234.5620220315";
        var schema = FixedLengthSchema.Create()
            .Field("CustomerId", 0, 10, FieldType.Numeric)
            .Field("CustomerName", 10, 30, FieldType.Text)
            .Field("AccountBalance", 40, 12, FieldType.Decimal, 2)
            .Field("LastTransactionDate", 52, 8, FieldType.Date, "yyyyMMdd");

        // Act
        var result = FixedLengthParser.Parse<CustomerRecord>(content, schema);

        // Assert
        Assert.NotNull(result);
        var customers = new List<CustomerRecord>(result);
        Assert.Single(customers);
        Assert.Equal("1234567890", customers[0].CustomerId);
        Assert.Equal("JOHN DOE", customers[0].CustomerName.Trim());
        Assert.Equal(1234.56m, customers[0].AccountBalance);
        Assert.Equal(new DateTime(2022, 3, 15), customers[0].LastTransactionDate);
    }

    #endregion

    #region Fluent Configuration API - contracts/fixed-length-parser-api.md:31-40

    [Fact]
    public void Configure_FluentAPI_ReturnsConfiguredParser()
    {
        // Arrange & Act
        var parser = FixedLengthParser.Configure()
            .WithRecordLength(120)
            .WithEncoding(Encoding.ASCII)
            .WithPadding(PaddingMode.Space)
            .WithTrimming(TrimmingMode.Both)
            .EnableParallelProcessing()
            .WithErrorHandling(ErrorMode.Tolerant)
            .Build();

        // Assert
        Assert.NotNull(parser);

        // Test with configured parser
        const string content = "1234567890JOHN DOE                                                                                                    ";
        var schema = CreateSimpleSchema();
        var result = parser.Parse<SimpleCustomer>(content, schema);

        var customers = new List<SimpleCustomer>(result);
        Assert.Single(customers);
        Assert.Equal("1234567890", customers[0].CustomerId);
        Assert.Equal("JOHN DOE", customers[0].CustomerName);
    }

    #endregion

    #region COBOL Copybook Integration - contracts/fixed-length-parser-api.md:42-55

    [Fact]
    public void Configure_WithCopybook_ReturnsConfiguredParser()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, CreateTestCopybookContent());
            var copybook = CobolCopybook.LoadFromFile(tempFile);

            // Act
            var parser = FixedLengthParser.Configure()
                .WithCopybook(copybook)
                .EnableEBCDICSupport()
                .WithSignedNumberFormat(SignFormat.Trailing)
                .Build();

            // Assert
            Assert.NotNull(parser);

            const string mainframeData = "1234567890JOHN DOE           0000012345{"; // { = positive sign in EBCDIC
            var result = parser.Parse<CustomerRecord>(mainframeData);
            var customers = new List<CustomerRecord>(result);
            Assert.Single(customers);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void CobolCopybook_LoadFromFile_ReturnsValidCopybook()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, CreateTestCopybookContent());

            // Act
            var copybook = CobolCopybook.LoadFromFile(tempFile);

            // Assert
            Assert.NotNull(copybook);
            Assert.True(copybook.Fields.Count > 0);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    #endregion

    #region Error Handling Contract - contracts/fixed-length-parser-api.md:100-108

    [Fact]
    public void Parse_RecordTooShort_ThrowsFixedLengthParseException()
    {
        // Arrange
        const string shortRecord = "12345"; // Too short for expected format
        var schema = CreateSimpleSchema();

        // Act & Assert
        var exception = Assert.Throws<FixedLengthParseException>(() =>
        {
            var result = FixedLengthParser.Parse<SimpleCustomer>(shortRecord, schema);
            // Force enumeration to trigger parsing
            var _ = new List<SimpleCustomer>(result);
        });

        Assert.Equal(1, exception.RecordNumber);
        Assert.Equal(FixedLengthError.RecordTooShort, exception.ErrorType);
        Assert.NotNull(exception.FieldName);
    }

    [Fact]
    public void Parse_InvalidNumericFormat_ThrowsFixedLengthParseException()
    {
        // Arrange
        const string invalidNumeric = "ABCDEFGHIJJOHN DOE                   NOTANUMBER20220315";
        var schema = FixedLengthSchema.Create()
            .Field("CustomerId", 0, 10, FieldType.Numeric)
            .Field("CustomerName", 10, 30, FieldType.Text)
            .Field("AccountBalance", 40, 12, FieldType.Decimal, 2)
            .Field("LastTransactionDate", 52, 8, FieldType.Date, "yyyyMMdd");

        // Act & Assert
        var exception = Assert.Throws<FixedLengthParseException>(() =>
        {
            var result = FixedLengthParser.Parse<CustomerRecord>(invalidNumeric, schema);
            // Force enumeration to trigger parsing
            var _ = new List<CustomerRecord>(result);
        });

        Assert.Equal(FixedLengthError.InvalidNumericFormat, exception.ErrorType);
        Assert.NotNull(exception.FieldName);
    }

    #endregion

    #region Performance Contract Validation - contracts/fixed-length-parser-api.md:78-96

    [Fact]
    public void Parse_StartupTime_CompletesWithinLatencyRequirement()
    {
        // Arrange
        const string content = "1234567890JOHN DOE                   00001234.5620220315";
        var schema = CreateSimpleSchema();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        var result = FixedLengthParser.Parse<CustomerRecord>(content, schema);
        stopwatch.Stop();

        // Assert - Schema compilation requirement: <5ms
        Assert.True(stopwatch.ElapsedMilliseconds < 5,
            $"Startup time {stopwatch.ElapsedMilliseconds}ms exceeds 5ms requirement");

        // Ensure result is valid
        var customers = new List<CustomerRecord>(result);
        Assert.Single(customers);
    }

    #endregion

    #region Asynchronous APIs

    [Fact]
    public async Task ParseAsync_Stream_ReturnsFixedLengthRecordAsyncEnumerable()
    {
        // Arrange
        const string content = "1234567890JOHN DOE                   00001234.5620220315";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var schema = CreateSimpleSchema();

        // Act
        var result = FixedLengthParser.ParseAsync(stream, schema);

        // Assert
        Assert.NotNull(result);
        var records = new List<FixedLengthRecord>();
        await foreach (var record in result)
        {
            records.Add(record);
        }
        Assert.Single(records);
    }

    [Fact]
    public async Task ParseAsync_Generic_Stream_ReturnsTypedAsyncEnumerable()
    {
        // Arrange
        const string content = "1234567890JOHN DOE                   00001234.5620220315";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var schema = CreateSimpleSchema();

        // Act
        var result = FixedLengthParser.ParseAsync<CustomerRecord>(stream, schema);

        // Assert
        Assert.NotNull(result);
        var customers = new List<CustomerRecord>();
        await foreach (var customer in result)
        {
            customers.Add(customer);
        }
        Assert.Single(customers);
        Assert.Equal("1234567890", customers[0].CustomerId);
    }

    [Fact]
    public async Task ParseAsync_WithCancellationToken_RespectsCancel()
    {
        // Arrange
        const string content = "1234567890JOHN DOE                   00001234.5620220315";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var schema = CreateSimpleSchema();
        using var cts = new CancellationTokenSource();

        // Act & Assert
        var result = FixedLengthParser.ParseAsync<CustomerRecord>(stream, schema, cts.Token);
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var customer in result)
            {
                // Should be cancelled
            }
        });
    }

    #endregion
}

#region Test Data Models

public class CustomerRecord
{
    public string CustomerId { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public decimal AccountBalance { get; set; }
    public DateTime LastTransactionDate { get; set; }
}

public class SimpleCustomer
{
    public string CustomerId { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
}

#endregion

#region Expected Types and Enums - contracts/fixed-length-parser-api.md

/// <summary>
/// Base record type for fixed-length parsing
/// </summary>
public class FixedLengthRecord
{
    private readonly Dictionary<string, string> _fields = new();

    public string GetField(string fieldName)
    {
        return _fields.TryGetValue(fieldName, out var value) ? value : string.Empty;
    }

    public void SetField(string fieldName, string value)
    {
        _fields[fieldName] = value;
    }
}

/// <summary>
/// COBOL copybook definition
/// </summary>
public class CobolCopybook
{
    public List<CobolField> Fields { get; set; } = new();

    public static CobolCopybook LoadFromFile(string filePath)
        => throw new NotImplementedException("CobolCopybook.LoadFromFile not yet implemented - implement in Phase 3.5");
}

public class CobolField
{
    public string Name { get; set; } = string.Empty;
    public int Position { get; set; }
    public int Length { get; set; }
    public string PictureClause { get; set; } = string.Empty;
}

/// <summary>
/// Field layout definition for programmatic schema creation
/// </summary>
public class FieldLayout
{
    public List<FieldDefinition> Fields { get; set; } = new();
}

public class FieldDefinition
{
    public string Name { get; set; } = string.Empty;
    public int Position { get; set; }
    public int Length { get; set; }
    public FieldType Type { get; set; }
    public object? Format { get; set; }
}

/// <summary>
/// Fluent schema builder for fixed-length formats
/// </summary>
public class FixedLengthSchema
{
    private readonly List<FieldDefinition> _fields = new();

    public static FixedLengthSchema Create() => new();

    public FixedLengthSchema Field(string name, int position, int length, FieldType type, object? format = null)
    {
        _fields.Add(new FieldDefinition
        {
            Name = name,
            Position = position,
            Length = length,
            Type = type,
            Format = format
        });
        return this;
    }

    public List<FieldDefinition> Fields => _fields;
}

/// <summary>
/// Field type enumeration
/// </summary>
public enum FieldType
{
    Text,
    Numeric,
    Decimal,
    Date,
    Binary,
    PackedDecimal
}

/// <summary>
/// Padding mode for fixed-length records
/// </summary>
public enum PaddingMode
{
    None,
    Space,
    Zero
}

/// <summary>
/// Trimming mode for text fields
/// </summary>
public enum TrimmingMode
{
    None,
    Left,
    Right,
    Both
}

/// <summary>
/// Error handling mode
/// </summary>
public enum ErrorMode
{
    Strict,
    Tolerant
}

/// <summary>
/// Signed number format for COBOL
/// </summary>
public enum SignFormat
{
    Leading,
    Trailing,
    Separate,
    Embedded
}

/// <summary>
/// Fixed-length parsing exception
/// </summary>
public class FixedLengthParseException : Exception
{
    public int RecordNumber { get; }
    public int FieldPosition { get; }
    public string FieldName { get; }
    public FixedLengthError ErrorType { get; }

    public FixedLengthParseException(int recordNumber, int fieldPosition, string fieldName,
        FixedLengthError errorType, string message)
        : base(message)
    {
        RecordNumber = recordNumber;
        FieldPosition = fieldPosition;
        FieldName = fieldName;
        ErrorType = errorType;
    }
}

/// <summary>
/// Fixed-length parsing error types
/// </summary>
public enum FixedLengthError
{
    RecordTooShort,
    RecordTooLong,
    InvalidNumericFormat,
    InvalidDateFormat,
    InvalidPackedDecimal,
    UnexpectedEndOfFile
}

#endregion

#region Placeholder Implementation for Contract Testing

/// <summary>
/// Placeholder FixedLengthParser implementation for contract testing.
/// All methods throw NotImplementedException to ensure TDD approach.
/// This will be replaced by the actual high-performance implementation.
/// </summary>
public static class FixedLengthParser
{
    // Simple APIs with copybook
    public static IEnumerable<FixedLengthRecord> Parse(string content, CobolCopybook copybook)
        => throw new NotImplementedException("FixedLengthParser.Parse(string, CobolCopybook) not yet implemented - implement in Phase 3.5");

    public static IEnumerable<T> Parse<T>(string content, CobolCopybook copybook)
        => throw new NotImplementedException("FixedLengthParser.Parse<T>(string, CobolCopybook) not yet implemented - implement in Phase 3.5");

    // Simple APIs with field layout
    public static IEnumerable<FixedLengthRecord> Parse(string content, FieldLayout layout)
        => throw new NotImplementedException("FixedLengthParser.Parse(string, FieldLayout) not yet implemented - implement in Phase 3.5");

    public static IEnumerable<T> Parse<T>(string content, FieldLayout layout)
        => throw new NotImplementedException("FixedLengthParser.Parse<T>(string, FieldLayout) not yet implemented - implement in Phase 3.5");

    // Schema-based APIs
    public static IEnumerable<T> Parse<T>(string content, FixedLengthSchema schema)
        => throw new NotImplementedException("FixedLengthParser.Parse<T>(string, FixedLengthSchema) not yet implemented - implement in Phase 3.5");

    // Asynchronous APIs
    public static IAsyncEnumerable<FixedLengthRecord> ParseAsync(Stream stream, FixedLengthSchema schema, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("FixedLengthParser.ParseAsync(Stream, FixedLengthSchema) not yet implemented - implement in Phase 3.5");

    public static IAsyncEnumerable<T> ParseAsync<T>(Stream stream, FixedLengthSchema schema, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("FixedLengthParser.ParseAsync<T>(Stream, FixedLengthSchema) not yet implemented - implement in Phase 3.5");

    // Configuration API
    public static IFixedLengthParserBuilder Configure()
        => throw new NotImplementedException("FixedLengthParser.Configure() not yet implemented - implement in Phase 3.6");
}

/// <summary>
/// Placeholder fluent builder interface for fixed-length parser configuration.
/// </summary>
public interface IFixedLengthParserBuilder
{
    IFixedLengthParserBuilder WithRecordLength(int length);
    IFixedLengthParserBuilder WithEncoding(Encoding encoding);
    IFixedLengthParserBuilder WithPadding(PaddingMode padding);
    IFixedLengthParserBuilder WithTrimming(TrimmingMode trimming);
    IFixedLengthParserBuilder EnableParallelProcessing();
    IFixedLengthParserBuilder WithErrorHandling(ErrorMode errorMode);
    IFixedLengthParserBuilder WithCopybook(CobolCopybook copybook);
    IFixedLengthParserBuilder EnableEBCDICSupport();
    IFixedLengthParserBuilder WithSignedNumberFormat(SignFormat signFormat);
    IFixedLengthParser Build();
}

/// <summary>
/// Placeholder configured fixed-length parser interface.
/// </summary>
public interface IFixedLengthParser
{
    IEnumerable<FixedLengthRecord> Parse(string content, FixedLengthSchema schema);
    IEnumerable<T> Parse<T>(string content, FixedLengthSchema schema);
    IEnumerable<T> Parse<T>(string content);
}

#endregion

#region Test Helper Methods

public partial class FixedLengthParserApiTests
{
    private static CobolCopybook CreateTestCopybook()
    {
        return new CobolCopybook
        {
            Fields = new List<CobolField>
            {
                new() { Name = "CustomerId", Position = 0, Length = 10, PictureClause = "X(10)" },
                new() { Name = "CustomerName", Position = 10, Length = 20, PictureClause = "X(20)" },
                new() { Name = "AccountBalance", Position = 30, Length = 15, PictureClause = "9(13)V99" },
                new() { Name = "LastTransactionDate", Position = 45, Length = 8, PictureClause = "9(8)" }
            }
        };
    }

    private static FieldLayout CreateTestFieldLayout()
    {
        return new FieldLayout
        {
            Fields = new List<FieldDefinition>
            {
                new() { Name = "CustomerId", Position = 0, Length = 10, Type = FieldType.Text },
                new() { Name = "CustomerName", Position = 10, Length = 20, Type = FieldType.Text },
                new() { Name = "AccountBalance", Position = 30, Length = 15, Type = FieldType.Decimal },
                new() { Name = "LastTransactionDate", Position = 45, Length = 8, Type = FieldType.Date }
            }
        };
    }

    private static FixedLengthSchema CreateSimpleSchema()
    {
        return FixedLengthSchema.Create()
            .Field("CustomerId", 0, 10, FieldType.Text)
            .Field("CustomerName", 10, 30, FieldType.Text)
            .Field("AccountBalance", 40, 12, FieldType.Decimal, 2)
            .Field("LastTransactionDate", 52, 8, FieldType.Date, "yyyyMMdd");
    }

    private static string CreateTestCopybookContent()
    {
        return @"       01  CUSTOMER-RECORD.
           05  CUSTOMER-ID          PIC X(10).
           05  CUSTOMER-NAME        PIC X(20).
           05  ACCOUNT-BALANCE      PIC 9(13)V99.
           05  LAST-TRANSACTION     PIC 9(8).";
    }
}

#endregion