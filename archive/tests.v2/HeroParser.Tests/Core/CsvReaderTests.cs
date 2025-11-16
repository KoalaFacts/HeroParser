using HeroParser.Configuration;
using HeroParser.Core;
using System.Text;

namespace HeroParser.Tests.Core;

/// <summary>
/// Tests for the ICsvReader interface focusing on the new Read() method and CurrentRow property.
/// </summary>
public class CsvReaderTests
{
    private const string SimpleCsv = "Name,Age,City\nJohn,25,Boston\nJane,30,Seattle";
    private const string SimpleCsvNoHeader = "John,25,Boston\nJane,30,Seattle";

    #region Read() Method Tests

    [Fact]
    public void Read_WithHeaders_AdvancesToFirstDataRow()
    {
        // Arrange
        using var reader = Csv.OpenContent(SimpleCsv);

        // Act
        var result = reader.Read();

        // Assert
        Assert.True(result);
        Assert.False(reader.EndOfCsv);
        Assert.NotNull(reader.Headers);
        Assert.Equal(3, reader.Headers.Count);
        Assert.Equal("Name", reader.Headers[0]);
        Assert.Equal("Age", reader.Headers[1]);
        Assert.Equal("City", reader.Headers[2]);
    }

    [Fact]
    public void Read_WithoutHeaders_AdvancesToFirstRow()
    {
        // Arrange
        var config = new CsvReadConfiguration { StringContent = SimpleCsvNoHeader, HasHeaderRow = false };
        using var reader = new CsvReader(config);

        // Act
        var result = reader.Read();

        // Assert
        Assert.True(result);
        Assert.False(reader.EndOfCsv);
        Assert.Null(reader.Headers);
    }

    [Fact]
    public void Read_SequentialCalls_AdvancesThroughAllRows()
    {
        // Arrange
        using var reader = Csv.OpenContent(SimpleCsv);

        // Act & Assert
        Assert.True(reader.Read()); // First row: John
        Assert.False(reader.EndOfCsv);

        Assert.True(reader.Read()); // Second row: Jane
        Assert.False(reader.EndOfCsv);

        Assert.False(reader.Read()); // End of data
        Assert.True(reader.EndOfCsv);
    }

    [Fact]
    public void Read_EmptyContent_ReturnsFalse()
    {
        // Arrange
        using var reader = Csv.OpenContent("");

        // Act
        var result = reader.Read();

        // Assert
        Assert.False(result);
        Assert.True(reader.EndOfCsv);
    }

    [Fact]
    public void Read_HeaderOnly_ReturnsFalse()
    {
        // Arrange
        using var reader = Csv.OpenContent("Name,Age,City");

        // Act
        var result = reader.Read();

        // Assert
        Assert.False(result);
        Assert.True(reader.EndOfCsv);
        Assert.NotNull(reader.Headers);
        Assert.Equal(3, reader.Headers.Count);
    }

    #endregion

    #region CurrentRow Property Tests

    [Fact]
    public void CurrentRow_BeforeRead_IsEmpty()
    {
        // Arrange
        using var reader = Csv.OpenContent(SimpleCsv);

        // Act
        var currentRow = reader.CurrentRow;

        // Assert
        Assert.True(currentRow.IsEmpty);
    }

    [Fact]
    public void CurrentRow_AfterSuccessfulRead_ContainsData()
    {
        // Arrange
        using var reader = Csv.OpenContent(SimpleCsv);

        // Act
        reader.Read();
        var currentRow = reader.CurrentRow;

        // Assert
        Assert.False(currentRow.IsEmpty);
        Assert.Equal(3, currentRow.ColumnCount);
        Assert.Equal("John", currentRow[0].ToString());
        Assert.Equal("25", currentRow[1].ToString());
        Assert.Equal("Boston", currentRow[2].ToString());
    }

    [Fact]
    public void CurrentRow_AfterFailedRead_IsEmpty()
    {
        // Arrange
        using var reader = Csv.OpenContent(SimpleCsv);

        // Act
        reader.Read(); // First row
        reader.Read(); // Second row
        reader.Read(); // Should fail - no more data
        var currentRow = reader.CurrentRow;

        // Assert
        Assert.True(currentRow.IsEmpty);
    }

    [Fact]
    public void CurrentRow_MultipleReads_UpdatesCorrectly()
    {
        // Arrange
        using var reader = Csv.OpenContent(SimpleCsv);

        // Act & Assert - First row
        reader.Read();
        var firstRow = reader.CurrentRow;
        Assert.Equal("John", firstRow[0].ToString());
        Assert.Equal("25", firstRow[1].ToString());

        // Act & Assert - Second row
        reader.Read();
        var secondRow = reader.CurrentRow;
        Assert.Equal("Jane", secondRow[0].ToString());
        Assert.Equal("30", secondRow[1].ToString());
    }

    #endregion

    #region Zero-Allocation API Tests

    [Fact]
    public void CurrentRow_ColumnAccess_WorksCorrectly()
    {
        // Arrange
        using var reader = Csv.OpenContent(SimpleCsv);
        reader.Read();

        // Act
        var row = reader.CurrentRow;

        // Assert
        Assert.Equal("John", row[0].ToString());
        Assert.Equal(25, row[1].Parse<int>());
        Assert.Equal("Boston", row[2].ToString());
    }

    [Fact]
    public void CurrentRow_ColumnAccessByName_WorksCorrectly()
    {
        // Arrange
        using var reader = Csv.OpenContent(SimpleCsv);
        reader.Read();

        // Act
        var row = reader.CurrentRow;

        // Assert
        Assert.Equal("John", row["Name"].ToString());
        Assert.Equal(25, row["Age"].Parse<int>());
        Assert.Equal("Boston", row["City"].ToString());
    }

    [Fact]
    public void CurrentRow_TryGetColumn_WorksCorrectly()
    {
        // Arrange
        using var reader = Csv.OpenContent(SimpleCsv);
        reader.Read();

        // Act
        var row = reader.CurrentRow;
        var foundName = row.TryGetColumn("Name", out var nameCol);
        var foundInvalid = row.TryGetColumn("Invalid", out var invalidCol);

        // Assert
        Assert.True(foundName);
        Assert.Equal("John", nameCol.ToString());
        Assert.False(foundInvalid);
        Assert.True(invalidCol.IsEmpty);
    }

    #endregion

    #region Traditional API Tests

    [Fact]
    public void ReadAll_ReturnsAllRows()
    {
        // Arrange
        using var reader = Csv.OpenContent(SimpleCsv);

        // Act
        var allRows = reader.ReadAll().ToArray();

        // Assert
        Assert.Equal(2, allRows.Length);
        Assert.Equal("John", allRows[0][0]);
        Assert.Equal("25", allRows[0][1]);
        Assert.Equal("Jane", allRows[1][0]);
        Assert.Equal("30", allRows[1][1]);
    }

    [Fact]
    public async Task ReadAllAsync_ReturnsAllRows()
    {
        // Arrange
        using var reader = Csv.OpenContent(SimpleCsv);

        // Act
        var allRows = (await reader.ReadAllAsync(TestContext.Current.CancellationToken)).ToArray();

        // Assert
        Assert.Equal(2, allRows.Length);
        Assert.Equal("John", allRows[0][0]);
        Assert.Equal("Boston", allRows[0][2]);
    }

    [Fact]
    public void ReadRecord_ReturnsNextRecord()
    {
        // Arrange
        using var reader = Csv.OpenContent(SimpleCsv);

        // Act
        var firstRecord = reader.ReadRecord();
        var secondRecord = reader.ReadRecord();
        var thirdRecord = reader.ReadRecord();

        // Assert
        Assert.NotNull(firstRecord);
        Assert.Equal("John", firstRecord[0]);
        Assert.Equal("25", firstRecord[1]);

        Assert.NotNull(secondRecord);
        Assert.Equal("Jane", secondRecord[0]);
        Assert.Equal("30", secondRecord[1]);

        Assert.Null(thirdRecord);
    }

    [Fact]
    public async Task ReadRecordAsync_ReturnsNextRecord()
    {
        // Arrange
        using var reader = Csv.OpenContent(SimpleCsv);

        // Act
        var firstRecord = await reader.ReadRecordAsync(TestContext.Current.CancellationToken);
        var secondRecord = await reader.ReadRecordAsync(TestContext.Current.CancellationToken);
        var thirdRecord = await reader.ReadRecordAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(firstRecord);
        Assert.Equal("John", firstRecord[0]);

        Assert.NotNull(secondRecord);
        Assert.Equal("Jane", secondRecord[0]);

        Assert.Null(thirdRecord);
    }

    #endregion

    #region Field Access Tests

    [Fact]
    public void GetField_WithValidColumnName_ReturnsValue()
    {
        // Arrange
        using var reader = Csv.OpenContent(SimpleCsv);
        var record = reader.ReadRecord()!;

        // Act
        var name = reader.GetField(record, "Name");
        var age = reader.GetField(record, "Age");

        // Assert
        Assert.Equal("John", name);
        Assert.Equal("25", age);
    }

    [Fact]
    public void GetField_WithInvalidColumnName_ThrowsException()
    {
        // Arrange
        using var reader = Csv.OpenContent(SimpleCsv);
        var record = reader.ReadRecord()!;

        // Act & Assert
        Assert.Throws<ArgumentException>(() => reader.GetField(record, "Invalid"));
    }

    [Fact]
    public void TryGetField_WithValidColumnName_ReturnsTrue()
    {
        // Arrange
        using var reader = Csv.OpenContent(SimpleCsv);
        var record = reader.ReadRecord()!;

        // Act
        var found = reader.TryGetField(record, "Name", out var value);

        // Assert
        Assert.True(found);
        Assert.Equal("John", value);
    }

    [Fact]
    public void TryGetField_WithInvalidColumnName_ReturnsFalse()
    {
        // Arrange
        using var reader = Csv.OpenContent(SimpleCsv);
        var record = reader.ReadRecord()!;

        // Act
        var found = reader.TryGetField(record, "Invalid", out var value);

        // Assert
        Assert.False(found);
        Assert.Null(value);
    }

    #endregion

    #region Configuration Tests

    [Fact]
    public void Configuration_IsAccessible()
    {
        // Arrange
        var config = new CsvReadConfiguration
        {
            StringContent = SimpleCsv,
            Delimiter = ';',
            HasHeaderRow = false
        };

        // Act
        using var reader = new CsvReader(config);

        // Assert
        Assert.Equal(';', reader.Configuration.Delimiter);
        Assert.False(reader.Configuration.HasHeaderRow);
        Assert.Equal(SimpleCsv, reader.Configuration.StringContent);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public void Read_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var reader = Csv.OpenContent(SimpleCsv);
        reader.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => reader.Read());
    }

    [Fact]
    public void CurrentRow_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var reader = Csv.OpenContent(SimpleCsv);
        reader.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => _ = reader.CurrentRow);
    }

    #endregion

    #region Custom Configuration Tests

    [Fact]
    public void Read_WithCustomDelimiter_ParsesCorrectly()
    {
        // Arrange
        var config = new CsvReadConfiguration
        {
            StringContent = "Name;Age;City\nJohn;25;Boston\nJane;30;Seattle",
            Delimiter = ';'
        };
        using var reader = new CsvReader(config);

        // Act
        reader.Read();
        var row = reader.CurrentRow;

        // Assert
        Assert.Equal("John", row[0].ToString());
        Assert.Equal("25", row[1].ToString());
        Assert.Equal("Boston", row[2].ToString());
    }

    [Fact]
    public void Read_WithTrimValues_TrimsSpaces()
    {
        // Arrange
        var config = new CsvReadConfiguration
        {
            StringContent = "Name,Age,City\n  John  ,  25  ,  Boston  ",
            TrimValues = true
        };
        using var reader = new CsvReader(config);

        // Act
        reader.Read();
        var row = reader.CurrentRow;

        // Assert
        Assert.Equal("John", row[0].ToString());
        Assert.Equal("25", row[1].ToString());
        Assert.Equal("Boston", row[2].ToString());
    }

    #endregion
}