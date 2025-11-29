using HeroParser.FixedWidths;
using Xunit;

namespace HeroParser.Tests.FixedWidth;

public class FixedWidthReaderTests
{
    [Fact]
    public void ReadFromText_LineBasedParsing_ReadsAllRecords()
    {
        // Arrange - Fixed-width data where each line is a record
        // Format: ID (10 chars) | Name (20 chars) | Amount (10 chars)
        var data =
            "0000000001John Doe            0000012345\n" +
            "0000000002Jane Smith          0000067890\n" +
            "0000000003Bob Wilson          0000099999";

        // Act
        var records = new List<(string Id, string Name, string Amount)>();
        foreach (var row in HeroParser.FixedWidth.ReadFromText(data))
        {
            // GetField trims by default (Left alignment trims trailing spaces)
            var id = row.GetField(0, 10).ToString();
            var name = row.GetField(10, 20).ToString();
            var amount = row.GetField(30, 10).ToString();
            records.Add((id, name, amount));
        }

        // Assert
        Assert.Equal(3, records.Count);
        Assert.Equal("0000000001", records[0].Id);
        Assert.Equal("John Doe", records[0].Name);  // Trimmed by default
        Assert.Equal("0000012345", records[0].Amount);
    }

    [Fact]
    public void ReadFromText_WithTrimming_TrimsFields()
    {
        // Arrange
        var data = "0000000001John Doe            0000012345";

        // Act
        string name = "";
        string amount = "";
        foreach (var row in HeroParser.FixedWidth.ReadFromText(data))
        {
            // Left-aligned field (text) - trim trailing spaces
            name = row.GetField(10, 20, ' ', FieldAlignment.Left).ToString();
            // Right-aligned field (number) - trim leading zeros
            amount = row.GetField(30, 10, '0', FieldAlignment.Right).ToString();
            break;
        }

        // Assert
        Assert.Equal("John Doe", name);
        Assert.Equal("12345", amount);
    }

    [Fact]
    public void GetRawField_ReturnsUntrimmedData()
    {
        // Arrange
        var data = "0000000001John Doe            0000012345";

        // Act
        string rawName = "";
        foreach (var row in HeroParser.FixedWidth.ReadFromText(data))
        {
            // GetRawField returns the field without any trimming
            rawName = row.GetRawField(10, 20).ToString();
            break;
        }

        // Assert
        Assert.Equal("John Doe            ", rawName); // 8 chars + 12 spaces = 20 chars
        Assert.Equal(20, rawName.Length);
    }

    [Fact]
    public void ReadFromText_FixedRecordLength_ParsesCorrectly()
    {
        // Arrange - No newlines, fixed 40-character records
        var data = "0000000001John Doe            00000123450000000002Jane Smith          0000067890";

        var options = new FixedWidthParserOptions { RecordLength = 40 };

        // Act
        var records = new List<string>();
        foreach (var row in HeroParser.FixedWidth.ReadFromText(data, options))
        {
            records.Add(row.RawRecord.ToString());
        }

        // Assert
        Assert.Equal(2, records.Count);
        Assert.Equal("0000000001John Doe            0000012345", records[0]);
        Assert.Equal("0000000002Jane Smith          0000067890", records[1]);
    }

    [Fact]
    public void ReadFromText_EmptyLines_SkippedByDefault()
    {
        // Arrange
        var data = """
            0000000001John Doe            0000012345

            0000000002Jane Smith          0000067890
            """;

        // Act
        var count = 0;
        foreach (var _ in HeroParser.FixedWidth.ReadFromText(data))
        {
            count++;
        }

        // Assert
        Assert.Equal(2, count);
    }

    [Fact]
    public void ReadFromText_TrackSourceLineNumbers_TracksCorrectly()
    {
        // Arrange
        var data = """
            Record1
            Record2
            Record3
            """;

        var options = new FixedWidthParserOptions { TrackSourceLineNumbers = true };

        // Act
        var lineNumbers = new List<int>();
        foreach (var row in HeroParser.FixedWidth.ReadFromText(data, options))
        {
            lineNumbers.Add(row.SourceLineNumber);
        }

        // Assert
        Assert.Equal([1, 2, 3], lineNumbers);
    }

    [Fact]
    public void GetField_OutOfBounds_ReturnsEmpty()
    {
        // Arrange
        var data = "Short";

        // Act
        FixedWidthCharSpanColumn field = default;
        foreach (var row in HeroParser.FixedWidth.ReadFromText(data))
        {
            field = row.GetField(100, 10); // Beyond the record length
            break;
        }

        // Assert
        Assert.True(field.IsEmpty);
        Assert.Equal("", field.ToString());
    }

    [Fact]
    public void GetField_PartialField_ReturnsAvailableData()
    {
        // Arrange
        var data = "Short";

        // Act
        string fieldValue = "";
        foreach (var row in HeroParser.FixedWidth.ReadFromText(data))
        {
            fieldValue = row.GetField(0, 100).ToString(); // Request more than available
            break;
        }

        // Assert
        Assert.Equal("Short", fieldValue);
    }

    [Fact]
    public void Column_TryParseInt32_ParsesCorrectly()
    {
        // Arrange
        var data = "0000012345";

        // Act
        bool success = false;
        int result = 0;
        foreach (var row in HeroParser.FixedWidth.ReadFromText(data))
        {
            var column = row.GetField(0, 10, '0', FieldAlignment.Right);
            success = column.TryParseInt32(out result);
            break;
        }

        // Assert
        Assert.True(success);
        Assert.Equal(12345, result);
    }

    [Fact]
    public void Column_TryParseDecimal_ParsesCorrectly()
    {
        // Arrange
        var data = "00001234.56";

        // Act
        bool success = false;
        decimal result = 0;
        foreach (var row in HeroParser.FixedWidth.ReadFromText(data))
        {
            var column = row.GetField(0, 11, '0', FieldAlignment.Right);
            success = column.TryParseDecimal(out result);
            break;
        }

        // Assert
        Assert.True(success);
        Assert.Equal(1234.56m, result);
    }

    [Fact]
    public void Column_TryParseDateTime_WithFormat_ParsesCorrectly()
    {
        // Arrange
        var data = "20231225";

        // Act
        bool success = false;
        DateTime result = default;
        foreach (var row in HeroParser.FixedWidth.ReadFromText(data))
        {
            var column = row.GetField(0, 8);
            success = column.TryParseDateTime(out result, "yyyyMMdd");
            break;
        }

        // Assert
        Assert.True(success);
        Assert.Equal(new DateTime(2023, 12, 25), result);
    }

    [Fact]
    public void ToImmutable_CreatesHeapCopy()
    {
        // Arrange
        var data = "TestRecord";

        // Act
        ImmutableFixedWidthRow? immutableRow = null;
        foreach (var row in HeroParser.FixedWidth.ReadFromText(data))
        {
            immutableRow = row.ToImmutable();
        }

        // Assert - Can use immutable row outside the foreach
        Assert.NotNull(immutableRow);
        Assert.Equal("TestRecord", immutableRow.RawRecord.ToString());
        Assert.Equal(1, immutableRow.RecordNumber);
    }

    [Fact]
    public void ReadFromText_MaxRecordCount_ThrowsWhenExceeded()
    {
        // Arrange
        var data = """
            Record1
            Record2
            Record3
            """;

        var options = new FixedWidthParserOptions { MaxRecordCount = 2 };

        // Act & Assert
        var ex = Assert.Throws<FixedWidthException>(() =>
        {
            foreach (var _ in HeroParser.FixedWidth.ReadFromText(data, options))
            {
                // Iterate through all
            }
        });

        Assert.Equal(FixedWidthErrorCode.TooManyRecords, ex.ErrorCode);
    }

    [Fact]
    public void ReadFromText_CommentCharacter_SkipsCommentLines()
    {
        // Arrange
        var data = """
            # This is a comment
            Record1
            # Another comment
            Record2
            Record3
            """;

        var options = new FixedWidthParserOptions { CommentCharacter = '#' };

        // Act
        var records = new List<string>();
        foreach (var row in HeroParser.FixedWidth.ReadFromText(data, options))
        {
            records.Add(row.RawRecord.ToString());
        }

        // Assert
        Assert.Equal(3, records.Count);
        Assert.Equal("Record1", records[0]);
        Assert.Equal("Record2", records[1]);
        Assert.Equal("Record3", records[2]);
    }

    [Fact]
    public void ReadFromText_CommentCharacter_TracksLineNumbersCorrectly()
    {
        // Arrange
        var data = """
            # Comment on line 1
            Record1
            # Comment on line 3
            Record2
            """;

        var options = new FixedWidthParserOptions
        {
            CommentCharacter = '#',
            TrackSourceLineNumbers = true
        };

        // Act
        var lineNumbers = new List<int>();
        foreach (var row in HeroParser.FixedWidth.ReadFromText(data, options))
        {
            lineNumbers.Add(row.SourceLineNumber);
        }

        // Assert
        Assert.Equal([2, 4], lineNumbers);
    }

    [Fact]
    public void ReadFromText_SkipRows_SkipsFirstNRows()
    {
        // Arrange
        var data = """
            Header Row
            Skip this too
            Record1
            Record2
            Record3
            """;

        var options = new FixedWidthParserOptions { SkipRows = 2 };

        // Act
        var records = new List<string>();
        foreach (var row in HeroParser.FixedWidth.ReadFromText(data, options))
        {
            records.Add(row.RawRecord.ToString());
        }

        // Assert
        Assert.Equal(3, records.Count);
        Assert.Equal("Record1", records[0]);
        Assert.Equal("Record2", records[1]);
        Assert.Equal("Record3", records[2]);
    }

    [Fact]
    public void ReadFromText_SkipRows_TracksLineNumbersCorrectly()
    {
        // Arrange
        var data = """
            Header Row
            Skip this too
            Record1
            Record2
            """;

        var options = new FixedWidthParserOptions
        {
            SkipRows = 2,
            TrackSourceLineNumbers = true
        };

        // Act
        var lineNumbers = new List<int>();
        foreach (var row in HeroParser.FixedWidth.ReadFromText(data, options))
        {
            lineNumbers.Add(row.SourceLineNumber);
        }

        // Assert
        Assert.Equal([3, 4], lineNumbers);
    }

    [Fact]
    public void ReadFromText_SkipRows_WithCommentCharacter_CombinesBoth()
    {
        // Arrange - Skip first row, then parse with comment character
        var data = """
            # File header comment
            Header Row
            # Data section comment
            Record1
            Record2
            """;

        var options = new FixedWidthParserOptions
        {
            SkipRows = 2,
            CommentCharacter = '#'
        };

        // Act
        var records = new List<string>();
        foreach (var row in HeroParser.FixedWidth.ReadFromText(data, options))
        {
            records.Add(row.RawRecord.ToString());
        }

        // Assert
        Assert.Equal(2, records.Count);
        Assert.Equal("Record1", records[0]);
        Assert.Equal("Record2", records[1]);
    }

    [Fact]
    public void Builder_WithCommentCharacter_SkipsCommentLines()
    {
        // Arrange
        var data = """
            # Comment
            Record1
            Record2
            """;

        // Act
        var records = new List<string>();
        foreach (var row in HeroParser.FixedWidth.Read()
            .WithCommentCharacter('#')
            .FromText(data))
        {
            records.Add(row.RawRecord.ToString());
        }

        // Assert
        Assert.Equal(2, records.Count);
        Assert.Equal("Record1", records[0]);
        Assert.Equal("Record2", records[1]);
    }

    [Fact]
    public void Builder_SkipRows_SkipsFirstNRows()
    {
        // Arrange
        var data = """
            Header
            Record1
            Record2
            """;

        // Act
        var records = new List<string>();
        foreach (var row in HeroParser.FixedWidth.Read()
            .SkipRows(1)
            .FromText(data))
        {
            records.Add(row.RawRecord.ToString());
        }

        // Assert
        Assert.Equal(2, records.Count);
        Assert.Equal("Record1", records[0]);
        Assert.Equal("Record2", records[1]);
    }
}
