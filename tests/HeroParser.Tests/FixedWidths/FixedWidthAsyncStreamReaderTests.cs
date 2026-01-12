using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Streaming;
using System.Text;
using Xunit;

namespace HeroParser.Tests.FixedWidths;

/// <summary>
/// Comprehensive tests for FixedWidthAsyncStreamReader.
/// </summary>
public class FixedWidthAsyncStreamReaderTests
{
    #region Basic Line-Based Reading Tests

    [Fact]
    public async Task MoveNextAsync_SingleLine_ReadsCorrectly()
    {
        var data = "ALICE               00030NYC            ";
        await using var stream = CreateStream(data);
        await using var reader = FixedWidth.CreateAsyncStreamReader(stream);

        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));

        var row = reader.Current;
        Assert.Equal(1, row.RecordNumber);
        Assert.Equal(40, row.Length);

        Assert.False(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MoveNextAsync_MultipleLines_ReadsAllRecords()
    {
        var data = "ALICE               00030NYC            \r\n" +
                   "BOB                 00025LOS ANGELES    \r\n" +
                   "CHARLIE             00035SAN FRANCISCO  ";
        await using var stream = CreateStream(data);
        await using var reader = FixedWidth.CreateAsyncStreamReader(stream);

        var recordCount = 0;
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
        {
            recordCount++;
            Assert.Equal(recordCount, reader.Current.RecordNumber);
        }

        Assert.Equal(3, recordCount);
    }

    [Fact]
    public async Task MoveNextAsync_UnixLineEndings_ReadsCorrectly()
    {
        var data = "LINE1               \n" +
                   "LINE2               \n" +
                   "LINE3               ";
        await using var stream = CreateStream(data);
        await using var reader = FixedWidth.CreateAsyncStreamReader(stream);

        var recordCount = 0;
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
        {
            recordCount++;
        }

        Assert.Equal(3, recordCount);
    }

    [Fact]
    public async Task Current_RawRecord_ReturnsCorrectSpan()
    {
        var data = "TEST DATA HERE     ";
        await using var stream = CreateStream(data);
        await using var reader = FixedWidth.CreateAsyncStreamReader(stream);

        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));

        var rawRecord = reader.Current.RawRecord;
        Assert.Equal(data, new string(rawRecord));
    }

    [Fact]
    public async Task Current_GetField_ExtractsFieldCorrectly()
    {
        var data = "ALICE               00030NYC            ";
        await using var stream = CreateStream(data);
        await using var reader = FixedWidth.CreateAsyncStreamReader(stream);

        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));

        var name = reader.Current.GetField(0, 20);
        var age = reader.Current.GetField(20, 5);
        var city = reader.Current.GetField(25, 15);

        Assert.Equal("ALICE", name.ToString());
        Assert.Equal("00030", age.ToString());
        Assert.Equal("NYC", city.ToString());
    }

    #endregion

    #region Fixed-Length Record Tests

    [Fact]
    public async Task MoveNextAsync_FixedLength_ReadsRecordsWithoutNewlines()
    {
        // Three 20-char records with no newlines
        var data = "RECORD1             " +
                   "RECORD2             " +
                   "RECORD3             ";

        var options = new FixedWidthReadOptions { RecordLength = 20 };
        await using var stream = CreateStream(data);
        await using var reader = FixedWidth.CreateAsyncStreamReader(stream, options);

        var records = new List<string>();
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
        {
            records.Add(new string(reader.Current.RawRecord).Trim());
        }

        Assert.Equal(3, records.Count);
        Assert.Equal("RECORD1", records[0]);
        Assert.Equal("RECORD2", records[1]);
        Assert.Equal("RECORD3", records[2]);
    }

    [Fact]
    public async Task MoveNextAsync_FixedLength_SkipRows_SkipsRecords()
    {
        // Three 20-char records with no newlines
        var data = "RECORD1             " +
                   "RECORD2             " +
                   "RECORD3             ";

        var options = new FixedWidthReadOptions { RecordLength = 20, SkipRows = 1 };
        await using var stream = CreateStream(data);
        await using var reader = FixedWidth.CreateAsyncStreamReader(stream, options);

        var records = new List<string>();
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
        {
            records.Add(new string(reader.Current.RawRecord).Trim());
        }

        Assert.Equal(2, records.Count);
        Assert.Equal("RECORD2", records[0]);
        Assert.Equal("RECORD3", records[1]);
    }

    [Fact]
    public async Task MoveNextAsync_FixedLength_ThrowsOnPartialRecord()
    {
        // Two full records + partial record (only 10 chars)
        var data = "RECORD1             " +
                   "RECORD2             " +
                   "PARTIAL   ";

        var options = new FixedWidthReadOptions { RecordLength = 20 };
        await using var stream = CreateStream(data);
        await using var reader = FixedWidth.CreateAsyncStreamReader(stream, options);

        // Read first two records successfully
        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));

        // Third record should throw due to partial data
        var ex = await Assert.ThrowsAsync<FixedWidthException>(
            () => reader.MoveNextAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(FixedWidthErrorCode.InvalidRecordLength, ex.ErrorCode);
    }

    #endregion

    #region Empty Line Handling Tests

    [Fact]
    public async Task MoveNextAsync_SkipEmptyLines_SkipsEmptyLines()
    {
        var data = "LINE1               \r\n" +
                   "\r\n" +
                   "LINE2               \r\n" +
                   "\r\n" +
                   "\r\n" +
                   "LINE3               ";

        var options = new FixedWidthReadOptions { SkipEmptyLines = true };
        await using var stream = CreateStream(data);
        await using var reader = FixedWidth.CreateAsyncStreamReader(stream, options);

        var records = new List<string>();
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
        {
            records.Add(new string(reader.Current.RawRecord).TrimEnd());
        }

        Assert.Equal(3, records.Count);
        Assert.Equal("LINE1", records[0].Trim());
        Assert.Equal("LINE2", records[1].Trim());
        Assert.Equal("LINE3", records[2].Trim());
    }

    [Fact]
    public async Task MoveNextAsync_IncludeEmptyLines_IncludesEmptyLines()
    {
        var data = "LINE1\r\n\r\nLINE2";

        var options = new FixedWidthReadOptions { SkipEmptyLines = false };
        await using var stream = CreateStream(data);
        await using var reader = FixedWidth.CreateAsyncStreamReader(stream, options);

        var recordCount = 0;
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
        {
            recordCount++;
        }

        Assert.Equal(3, recordCount); // LINE1, empty, LINE2
    }

    #endregion

    #region Comment Character Tests

    [Fact]
    public async Task MoveNextAsync_CommentCharacter_SkipsCommentLines()
    {
        var data = "# This is a comment\r\n" +
                   "DATA LINE 1         \r\n" +
                   "# Another comment\r\n" +
                   "DATA LINE 2         ";

        var options = new FixedWidthReadOptions { CommentCharacter = '#' };
        await using var stream = CreateStream(data);
        await using var reader = FixedWidth.CreateAsyncStreamReader(stream, options);

        var records = new List<string>();
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
        {
            records.Add(new string(reader.Current.RawRecord).Trim());
        }

        Assert.Equal(2, records.Count);
        Assert.StartsWith("DATA LINE 1", records[0]);
        Assert.StartsWith("DATA LINE 2", records[1]);
    }

    #endregion

    #region SkipRows Tests

    [Fact]
    public async Task MoveNextAsync_SkipRows_SkipsInitialRows()
    {
        var data = "HEADER ROW          \r\n" +
                   "HEADER ROW 2        \r\n" +
                   "DATA ROW 1          \r\n" +
                   "DATA ROW 2          ";

        var options = new FixedWidthReadOptions { SkipRows = 2 };
        await using var stream = CreateStream(data);
        await using var reader = FixedWidth.CreateAsyncStreamReader(stream, options);

        var records = new List<string>();
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
        {
            records.Add(new string(reader.Current.RawRecord).Trim());
        }

        Assert.Equal(2, records.Count);
        Assert.StartsWith("DATA ROW 1", records[0]);
        Assert.StartsWith("DATA ROW 2", records[1]);
    }

    [Fact]
    public async Task MoveNextAsync_SkipMoreRowsThanAvailable_ReturnsNoRecords()
    {
        var data = "ROW1\r\nROW2\r\nROW3";

        var options = new FixedWidthReadOptions { SkipRows = 10 };
        await using var stream = CreateStream(data);
        await using var reader = FixedWidth.CreateAsyncStreamReader(stream, options);

        Assert.False(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
    }

    #endregion

    #region MaxRecordCount Tests

    [Fact]
    public async Task MoveNextAsync_MaxRecordCount_ThrowsWhenExceeded()
    {
        var data = "ROW1\r\nROW2\r\nROW3\r\nROW4\r\nROW5";

        var options = new FixedWidthReadOptions { MaxRecordCount = 3 };
        await using var stream = CreateStream(data);
        await using var reader = FixedWidth.CreateAsyncStreamReader(stream, options);

        // Read 3 records successfully
        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));

        // Fourth record should throw
        var ex = await Assert.ThrowsAsync<FixedWidthException>(
            () => reader.MoveNextAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(FixedWidthErrorCode.TooManyRecords, ex.ErrorCode);
    }

    #endregion

    #region BytesRead Tests

    [Fact]
    public async Task BytesRead_TracksApproximateBytesRead()
    {
        var data = "LINE 1\r\nLINE 2\r\nLINE 3";
        await using var stream = CreateStream(data);
        await using var reader = FixedWidth.CreateAsyncStreamReader(stream);

        Assert.Equal(0, reader.BytesRead);

        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
        {
            // BytesRead should increase
        }

        Assert.True(reader.BytesRead > 0, "BytesRead should be greater than 0 after reading");
    }

    #endregion

    #region Source Line Number Tracking Tests

    [Fact]
    public async Task SourceLineNumber_WhenTracked_ReturnsCorrectLineNumber()
    {
        var data = "LINE1\r\nLINE2\r\nLINE3";

        var options = new FixedWidthReadOptions { TrackSourceLineNumbers = true };
        await using var stream = CreateStream(data);
        await using var reader = FixedWidth.CreateAsyncStreamReader(stream, options);

        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, reader.Current.SourceLineNumber);

        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, reader.Current.SourceLineNumber);

        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        Assert.Equal(3, reader.Current.SourceLineNumber);
    }

    [Fact]
    public async Task SourceLineNumber_WithSkippedRows_CorrectlyOffsets()
    {
        var data = "HEADER\r\nDATA1\r\nDATA2";

        var options = new FixedWidthReadOptions
        {
            TrackSourceLineNumbers = true,
            SkipRows = 1
        };
        await using var stream = CreateStream(data);
        await using var reader = FixedWidth.CreateAsyncStreamReader(stream, options);

        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, reader.Current.SourceLineNumber); // Line 2 (after header)
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task MoveNextAsync_CancellationToken_ThrowsWhenCancelled()
    {
        var data = GenerateLargeData(1000);
        await using var stream = CreateStream(data);
        await using var reader = FixedWidth.CreateAsyncStreamReader(stream);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => reader.MoveNextAsync(cts.Token).AsTask());
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public async Task DisposeAsync_CanBeCalledMultipleTimes()
    {
        var data = "TEST";
        await using var stream = CreateStream(data);
        var reader = FixedWidth.CreateAsyncStreamReader(stream);

        await reader.DisposeAsync();
        await reader.DisposeAsync(); // Should not throw
    }

    [Fact]
    public async Task DisposeAsync_LeaveOpenFalse_DisposesUnderlyingStream()
    {
        var data = "TEST";
        var stream = CreateStream(data);
        var reader = FixedWidth.CreateAsyncStreamReader(stream, leaveOpen: false);

        await reader.DisposeAsync();

        Assert.False(stream.CanRead, "Stream should be disposed");
    }

    [Fact]
    public async Task DisposeAsync_LeaveOpenTrue_KeepsStreamOpen()
    {
        var data = "TEST";
        await using var stream = CreateStream(data);
        var reader = FixedWidth.CreateAsyncStreamReader(stream, leaveOpen: true);

        await reader.DisposeAsync();

        Assert.True(stream.CanRead, "Stream should still be readable");
    }

    #endregion

    #region Unicode and Encoding Tests

    [Fact]
    public async Task MoveNextAsync_UnicodeContent_ReadsCorrectly()
    {
        var data = "日本語テスト        \r\n" +
                   "한국어 테스트       \r\n" +
                   "中文测试            ";
        await using var stream = CreateStream(data, Encoding.UTF8);
        await using var reader = FixedWidth.CreateAsyncStreamReader(stream, encoding: Encoding.UTF8);

        var records = new List<string>();
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
        {
            records.Add(new string(reader.Current.RawRecord).Trim());
        }

        Assert.Equal(3, records.Count);
        Assert.Contains("日本語", records[0]);
        Assert.Contains("한국어", records[1]);
        Assert.Contains("中文", records[2]);
    }

    [Fact]
    public async Task MoveNextAsync_UTF8BOM_HandlesCorrectly()
    {
        var data = "TEST DATA";
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(data)).ToArray();
        await using var stream = new MemoryStream(bytes);
        await using var reader = FixedWidth.CreateAsyncStreamReader(stream);

        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));

        var rawRecord = new string(reader.Current.RawRecord);
        Assert.Equal("TEST DATA", rawRecord);
    }

    #endregion

    #region Large File Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task MoveNextAsync_LargeFile_StreamsEfficiently()
    {
        const int recordCount = 10_000;
        var data = GenerateLargeData(recordCount);
        await using var stream = CreateStream(data);
        await using var reader = FixedWidth.CreateAsyncStreamReader(stream);

        var count = 0;
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
        {
            count++;
        }

        Assert.Equal(recordCount, count);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task MoveNextAsync_LargeRecords_HandlesBufferGrowth()
    {
        // Create a record larger than default buffer
        var longLine = new string('X', 50_000) + "\r\n";
        var data = longLine + longLine;

        await using var stream = CreateStream(data);
        await using var reader = FixedWidth.CreateAsyncStreamReader(stream, bufferSize: 1024);

        var count = 0;
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
        {
            count++;
            Assert.Equal(50_000, reader.Current.Length);
        }

        Assert.Equal(2, count);
    }

    #endregion

    #region Builder Integration Tests

    [Fact]
    public async Task Builder_FromFileAsync_CreatesReader()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"fixedwidth_reader_test_{Guid.NewGuid()}.dat");
        var data = "LINE1\r\nLINE2\r\nLINE3";

        try
        {
            await File.WriteAllTextAsync(tempPath, data, TestContext.Current.CancellationToken);

            await using var reader = FixedWidth.Read()
                .SkipEmptyLines()
                .FromFileAsync(tempPath);

            var count = 0;
            while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
            {
                count++;
            }

            Assert.Equal(3, count);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public async Task Builder_FromStreamAsync_CreatesReader()
    {
        var data = "LINE1\r\nLINE2\r\nLINE3";
        await using var stream = CreateStream(data);

        await using var reader = FixedWidth.Read()
            .SkipEmptyLines()
            .FromStreamAsync(stream, leaveOpen: true);

        var count = 0;
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
        {
            count++;
        }

        Assert.Equal(3, count);
    }

    [Fact]
    public async Task Builder_WithAllOptions_CreatesConfiguredReader()
    {
        // SkipRows happens before any processing (including comment handling)
        // So we skip the comment line with SkipRows, then process remaining lines
        var data = "# Skip this\r\n# Comment\r\nDATA1\r\n\r\nDATA2";
        await using var stream = CreateStream(data);

        await using var reader = FixedWidth.Read()
            .WithCommentCharacter('#')
            .SkipRows(1)  // Skip first line (# Skip this)
            .SkipEmptyLines()
            .TrackLineNumbers()
            .FromStreamAsync(stream);

        // After SkipRows=1: # Comment, DATA1, (empty), DATA2
        // Comment handling skips: # Comment
        // Empty line handling skips: (empty)
        // Remaining: DATA1, DATA2
        var records = new List<(string Data, int LineNumber)>();
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
        {
            records.Add((new string(reader.Current.RawRecord).Trim(), reader.Current.SourceLineNumber));
        }

        Assert.Equal(2, records.Count);
        Assert.Equal("DATA1", records[0].Data);
        Assert.Equal("DATA2", records[1].Data);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task MoveNextAsync_EmptyFile_ReturnsFalse()
    {
        await using var stream = CreateStream("");
        await using var reader = FixedWidth.CreateAsyncStreamReader(stream);

        Assert.False(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MoveNextAsync_OnlyNewlines_WithSkipEmpty_ReturnsFalse()
    {
        var data = "\r\n\r\n\r\n";
        var options = new FixedWidthReadOptions { SkipEmptyLines = true };
        await using var stream = CreateStream(data);
        await using var reader = FixedWidth.CreateAsyncStreamReader(stream, options);

        Assert.False(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MoveNextAsync_SingleCharacterLines_ReadsCorrectly()
    {
        var data = "A\r\nB\r\nC";
        await using var stream = CreateStream(data);
        await using var reader = FixedWidth.CreateAsyncStreamReader(stream);

        var chars = new List<char>();
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
        {
            chars.Add(reader.Current.RawRecord[0]);
        }

        Assert.Equal(['A', 'B', 'C'], chars);
    }

    [Fact]
    public async Task MoveNextAsync_NoTrailingNewline_ReadsLastRecord()
    {
        var data = "LINE1\r\nLINE2\r\nLINE3"; // No trailing newline
        await using var stream = CreateStream(data);
        await using var reader = FixedWidth.CreateAsyncStreamReader(stream);

        var count = 0;
        string? lastRecord = null;
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
        {
            count++;
            lastRecord = new string(reader.Current.RawRecord);
        }

        Assert.Equal(3, count);
        Assert.Equal("LINE3", lastRecord);
    }

    #endregion

    #region API Parity Tests (matching CSV async reader patterns)

    [Fact]
    public async Task CreateAsyncStreamReader_FromFile_OpensFileCorrectly()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"fixedwidth_api_test_{Guid.NewGuid()}.dat");
        var data = "TEST DATA";

        try
        {
            await File.WriteAllTextAsync(tempPath, data, TestContext.Current.CancellationToken);

            await using var reader = FixedWidth.CreateAsyncStreamReader(tempPath);

            Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
            Assert.Equal("TEST DATA", new string(reader.Current.RawRecord));
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public async Task CreateAsyncStreamReader_FromStream_ReadsCorrectly()
    {
        var data = "TEST DATA";
        await using var stream = CreateStream(data);
        await using var reader = FixedWidth.CreateAsyncStreamReader(stream);

        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        Assert.Equal("TEST DATA", new string(reader.Current.RawRecord));
    }

    #endregion

    #region Helper Methods

    private static MemoryStream CreateStream(string content, Encoding? encoding = null)
    {
        encoding ??= Encoding.UTF8;
        return new MemoryStream(encoding.GetBytes(content));
    }

    private static string GenerateLargeData(int recordCount)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < recordCount; i++)
        {
            sb.AppendLine($"RECORD{i:D5}          ");
        }
        return sb.ToString();
    }

    #endregion
}

