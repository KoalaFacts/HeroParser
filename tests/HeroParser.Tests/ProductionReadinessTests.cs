using HeroParser.SeparatedValues;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Records;
using HeroParser.SeparatedValues.Reading.Records.Binding;
using System.Text;
using Xunit;

namespace HeroParser.Tests;

/// <summary>
/// Tests for production-critical features added in P1 release.
/// </summary>
public class ProductionReadinessTests
{
    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Utf8BomIsStrippedCorrectly()
    {
        // UTF-8 BOM is 0xEF 0xBB 0xBF
        var csvWithBom = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'a', (byte)',', (byte)'b', (byte)'\n', (byte)'1', (byte)',', (byte)'2' };
        var reader = Csv.ReadFromByteSpan(csvWithBom);

        Assert.True(reader.MoveNext());
        var row1 = reader.Current;
        Assert.Equal(2, row1.ColumnCount);
        Assert.Equal("a", row1[0].ToString());
        Assert.Equal("b", row1[1].ToString());

        Assert.True(reader.MoveNext());
        var row2 = reader.Current;
        Assert.Equal("1", row2[0].ToString());
        Assert.Equal("2", row2[1].ToString());

        Assert.False(reader.MoveNext());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Utf8WithoutBomParsesCorrectly()
    {
        // No BOM, just regular CSV
        var csvWithoutBom = Encoding.UTF8.GetBytes("a,b\n1,2");
        var reader = Csv.ReadFromByteSpan(csvWithoutBom);

        Assert.True(reader.MoveNext());
        var row1 = reader.Current;
        Assert.Equal(2, row1.ColumnCount);
        Assert.Equal("a", row1[0].ToString());
        Assert.Equal("b", row1[1].ToString());

        Assert.True(reader.MoveNext());
        var row2 = reader.Current;
        Assert.Equal("1", row2[0].ToString());
        Assert.Equal("2", row2[1].ToString());

        Assert.False(reader.MoveNext());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void MaxFieldSizeThrowsWhenExceeded()
    {
        var csv = "short,veryverylongfieldthatexceedsthelimit\n1,2";
        var options = new CsvParserOptions
        {
            MaxFieldSize = 10
        };

        CsvException? exception = null;
        try
        {
            var reader = Csv.ReadFromText(csv, options);
            reader.MoveNext();
        }
        catch (CsvException ex)
        {
            exception = ex;
        }

        Assert.NotNull(exception);
        Assert.Contains("exceeds maximum allowed length", exception.Message);
        Assert.Contains("10", exception.Message);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void MaxFieldSizeAllowsFieldsWithinLimit()
    {
        var csv = "short,ok\n1,2";
        var options = new CsvParserOptions
        {
            MaxFieldSize = 10
        };

        var reader = Csv.ReadFromText(csv, options);

        Assert.True(reader.MoveNext());
        var row = reader.Current;
        Assert.Equal(2, row.ColumnCount);
        Assert.Equal("short", row[0].ToString());
        Assert.Equal("ok", row[1].ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void SkipRowsSkipsCorrectNumberOfRows()
    {
        var csv = "skip1,skip2\nskip3,skip4\nName,Age\nAlice,30\nBob,25";
        var recordOptions = new CsvRecordOptions
        {
            SkipRows = 2,
            HasHeaderRow = true
        };

        var records = new List<TestPerson>();
        foreach (var record in Csv.DeserializeRecords<TestPerson>(csv, recordOptions))
        {
            records.Add(record);
        }

        // Should skip 2 rows (skip1 and skip3), then use Name/Age as header, then get two data rows
        Assert.Equal(2, records.Count);
        Assert.Equal("Alice", records[0].Name);
        Assert.Equal(30, records[0].Age);
        Assert.Equal("Bob", records[1].Name);
        Assert.Equal(25, records[1].Age);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void SkipRowsZeroSkipsNoRows()
    {
        var csv = "Name,Age\nCharlie,40";
        var recordOptions = new CsvRecordOptions
        {
            SkipRows = 0,
            HasHeaderRow = true
        };

        var records = new List<TestPerson>();
        foreach (var record in Csv.DeserializeRecords<TestPerson>(csv, recordOptions))
        {
            records.Add(record);
        }

        Assert.Single(records);
        Assert.Equal("Charlie", records[0].Name);
        Assert.Equal(40, records[0].Age);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CloneCreatesIndependentCopyOfCharSpanRow()
    {
        var csv = "a,b,c\n1,2,3";
        var reader = Csv.ReadFromText(csv);

        reader.MoveNext();
        var original = reader.Current;
        var cloned = original.Clone();

        // Move to next row to modify the buffer
        reader.MoveNext();

        // Cloned row should still have original data
        Assert.Equal(3, cloned.ColumnCount);
        Assert.Equal("a", cloned[0].ToString());
        Assert.Equal("b", cloned[1].ToString());
        Assert.Equal("c", cloned[2].ToString());
        Assert.Equal(1, cloned.LineNumber);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ToImmutableCreatesIndependentCopyOfCharSpanRow()
    {
        var csv = "a,b,c\n1,2,3";
        var reader = Csv.ReadFromText(csv);

        reader.MoveNext();
        var original = reader.Current;
        var immutable = original.ToImmutable();

        // Move to next row to modify the buffer
        reader.MoveNext();

        // Immutable row should still have original data
        Assert.Equal(3, immutable.ColumnCount);
        Assert.Equal("a", immutable[0].ToString());
        Assert.Equal("b", immutable[1].ToString());
        Assert.Equal("c", immutable[2].ToString());
        Assert.Equal(1, immutable.LineNumber);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CloneCreatesIndependentCopyOfByteSpanRow()
    {
        var csv = Encoding.UTF8.GetBytes("a,b,c\n1,2,3");
        var reader = Csv.ReadFromByteSpan(csv);

        reader.MoveNext();
        var original = reader.Current;
        var cloned = original.Clone();

        // Move to next row to modify the buffer
        reader.MoveNext();

        // Cloned row should still have original data
        Assert.Equal(3, cloned.ColumnCount);
        Assert.Equal("a", cloned[0].ToString());
        Assert.Equal("b", cloned[1].ToString());
        Assert.Equal("c", cloned[2].ToString());
        Assert.Equal(1, cloned.LineNumber);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ToImmutableCreatesIndependentCopyOfByteSpanRow()
    {
        var csv = Encoding.UTF8.GetBytes("a,b,c\n1,2,3");
        var reader = Csv.ReadFromByteSpan(csv);

        reader.MoveNext();
        var original = reader.Current;
        var immutable = original.ToImmutable();

        // Move to next row to modify the buffer
        reader.MoveNext();

        // Immutable row should still have original data
        Assert.Equal(3, immutable.ColumnCount);
        Assert.Equal("a", immutable[0].ToString());
        Assert.Equal("b", immutable[1].ToString());
        Assert.Equal("c", immutable[2].ToString());
        Assert.Equal(1, immutable.LineNumber);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void LineNumberTrackingIsCorrectForCharSpanReader()
    {
        var csv = "a,b\n1,2\n3,4\n5,6";
        var reader = Csv.ReadFromText(csv);

        Assert.True(reader.MoveNext());
        Assert.Equal(1, reader.Current.LineNumber);
        Assert.Equal("a", reader.Current[0].ToString());

        Assert.True(reader.MoveNext());
        Assert.Equal(2, reader.Current.LineNumber);
        Assert.Equal("1", reader.Current[0].ToString());

        Assert.True(reader.MoveNext());
        Assert.Equal(3, reader.Current.LineNumber);
        Assert.Equal("3", reader.Current[0].ToString());

        Assert.True(reader.MoveNext());
        Assert.Equal(4, reader.Current.LineNumber);
        Assert.Equal("5", reader.Current[0].ToString());

        Assert.False(reader.MoveNext());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void LineNumberTrackingIsCorrectForByteSpanReader()
    {
        var csv = Encoding.UTF8.GetBytes("a,b\n1,2\n3,4\n5,6");
        var reader = Csv.ReadFromByteSpan(csv);

        Assert.True(reader.MoveNext());
        Assert.Equal(1, reader.Current.LineNumber);
        Assert.Equal("a", reader.Current[0].ToString());

        Assert.True(reader.MoveNext());
        Assert.Equal(2, reader.Current.LineNumber);
        Assert.Equal("1", reader.Current[0].ToString());

        Assert.True(reader.MoveNext());
        Assert.Equal(3, reader.Current.LineNumber);
        Assert.Equal("3", reader.Current[0].ToString());

        Assert.True(reader.MoveNext());
        Assert.Equal(4, reader.Current.LineNumber);
        Assert.Equal("5", reader.Current[0].ToString());

        Assert.False(reader.MoveNext());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void LineNumberTrackingWorksWithEmptyLines()
    {
        var csv = "a,b\n\n1,2\n\n3,4";
        var reader = Csv.ReadFromText(csv);

        Assert.True(reader.MoveNext());
        Assert.Equal(1, reader.Current.LineNumber);
        Assert.Equal("a", reader.Current[0].ToString());

        Assert.True(reader.MoveNext());
        Assert.Equal(2, reader.Current.LineNumber);
        Assert.Equal("1", reader.Current[0].ToString());

        Assert.True(reader.MoveNext());
        Assert.Equal(3, reader.Current.LineNumber);
        Assert.Equal("3", reader.Current[0].ToString());

        Assert.False(reader.MoveNext());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void LineNumberTrackingWorksWithStreamReader()
    {
        var csv = "a,b\n1,2\n3,4";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        using var reader = Csv.ReadFromStream(stream);

        Assert.True(reader.MoveNext());
        Assert.Equal(1, reader.Current.LineNumber);
        Assert.Equal("a", reader.Current[0].ToString());

        Assert.True(reader.MoveNext());
        Assert.Equal(2, reader.Current.LineNumber);
        Assert.Equal("1", reader.Current[0].ToString());

        Assert.True(reader.MoveNext());
        Assert.Equal(3, reader.Current.LineNumber);
        Assert.Equal("3", reader.Current[0].ToString());

        Assert.False(reader.MoveNext());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task LineNumberTrackingWorksWithAsyncStreamReader()
    {
        var csv = "a,b\n1,2\n3,4";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        await using var reader = Csv.CreateAsyncStreamReader(stream);

        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, reader.Current.LineNumber);
        Assert.Equal("a", reader.Current[0].ToString());

        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, reader.Current.LineNumber);
        Assert.Equal("1", reader.Current[0].ToString());

        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        Assert.Equal(3, reader.Current.LineNumber);
        Assert.Equal("3", reader.Current[0].ToString());

        Assert.False(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
    }

    // Helper class for record binding tests
    [CsvGenerateBinder]
    internal sealed class TestPerson
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
    }
}
