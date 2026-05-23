using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Records;
using Xunit;

namespace HeroParser.Tests;

/// <summary>
/// Tests for the three API-parity features added to CSV reading:
/// 1. OnError deserialization error callback
/// 2. WithMaxInputSize DoS protection
/// 3. FromFileAsync / FromStreamAsync convenience methods
/// </summary>
public class CsvBuilderApiParityTests
{
    #region Test Record Types

    [GenerateBinder]
    public class PersonRecord
    {
        public string? Name { get; set; }
        public int Age { get; set; }
    }

    #endregion

    #region CSV Test Data

    private const string VALID_CSV = "Name,Age\r\nAlice,30\r\nBob,25\r\n";
    private const string INVALID_AGE_CSV = "Name,Age\r\nAlice,30\r\nBob,not_a_number\r\nCarol,40\r\n";
    private const string ALL_INVALID_CSV = "Name,Age\r\nAlice,bad\r\nBob,also_bad\r\n";

    #endregion

    // ─────────────────────────────────────────────────────────────
    // Task 1: OnError callback
    // ─────────────────────────────────────────────────────────────

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void OnError_SkipRecord_SkipsBadRowAndContinues()
    {
        // Arrange — second data row has a non-integer Age
        var handlerCalled = false;

        // Act
        using var reader = Csv.Read<PersonRecord>()
            .OnError((ctx, ex) =>
            {
                handlerCalled = true;
                return CsvDeserializeErrorAction.SkipRecord;
            })
            .FromText(INVALID_AGE_CSV);

        var records = reader.ToList();

        // Assert
        Assert.True(handlerCalled);
        Assert.Equal(2, records.Count);
        Assert.Equal("Alice", records[0].Name);
        Assert.Equal(30, records[0].Age);
        Assert.Equal("Carol", records[1].Name);
        Assert.Equal(40, records[1].Age);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void OnError_Throw_RethrowsException()
    {
        // Act & Assert
        Assert.Throws<CsvException>(() =>
        {
            using var reader = Csv.Read<PersonRecord>()
                .OnError((ctx, _) => CsvDeserializeErrorAction.Throw)
                .FromText(INVALID_AGE_CSV);

            reader.ToList();
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void OnError_ContextContainsRowNumber()
    {
        // Arrange
        int capturedRow = 0;

        // Act
        using var reader = Csv.Read<PersonRecord>()
            .OnError((ctx, _) =>
            {
                capturedRow = ctx.Row;
                return CsvDeserializeErrorAction.SkipRecord;
            })
            .FromText(INVALID_AGE_CSV);

        reader.ToList();

        // Assert — "Bob,not_a_number" is a data row so row number must be positive
        Assert.True(capturedRow > 0);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void OnError_AllRowsFail_ReturnsEmptyList()
    {
        // Act
        using var reader = Csv.Read<PersonRecord>()
            .OnError((_, _) => CsvDeserializeErrorAction.SkipRecord)
            .FromText(ALL_INVALID_CSV);

        var records = reader.ToList();

        // Assert
        Assert.Empty(records);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void OnError_WithByteReader_SkipsBadRow()
    {
        // Act — use out-byte[] overload (byte path) to verify OnError works through the byte binder path
        var records = new List<PersonRecord>();
        using var reader = Csv.Read<PersonRecord>()
            .OnError((_, _) => CsvDeserializeErrorAction.SkipRecord)
            .FromText(INVALID_AGE_CSV, out _);

        while (reader.MoveNext())
            records.Add(reader.Current);

        // Assert
        Assert.Equal(2, records.Count);
        Assert.Equal("Alice", records[0].Name);
        Assert.Equal("Carol", records[1].Name);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void OnError_NoHandler_ThrowsOnBadValue()
    {
        // Without OnError, parsing a non-integer Age should throw
        Assert.Throws<CsvException>(() =>
        {
            using var reader = Csv.Read<PersonRecord>()
                .FromText(INVALID_AGE_CSV);
            reader.ToList();
        });
    }

    // ─────────────────────────────────────────────────────────────
    // Task 2: WithMaxInputSize
    // ─────────────────────────────────────────────────────────────

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WithMaxInputSize_FileExceedsLimit_ThrowsCsvException()
    {
        // Arrange — write a temp file and set limit below its size
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, VALID_CSV);
            var fileSize = new FileInfo(tempFile).Length;

            // Act & Assert
            var ex = Assert.Throws<CsvException>(() =>
            {
                _ = Csv.Read<PersonRecord>()
                    .WithMaxInputSize(fileSize - 1)
                    .FromFile(tempFile, out _);
            });

            Assert.Contains("exceeds maximum", ex.Message);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WithMaxInputSize_FileUnderLimit_Succeeds()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, VALID_CSV);
            var fileSize = new FileInfo(tempFile).Length;

            // Act — limit exactly at file size should succeed
            using var reader = Csv.Read<PersonRecord>()
                .WithMaxInputSize(fileSize)
                .FromFile(tempFile, out _);

            var records = new List<PersonRecord>();
            while (reader.MoveNext())
                records.Add(reader.Current);

            // Assert
            Assert.Equal(2, records.Count);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WithMaxInputSize_StreamExceedsLimit_ThrowsCsvException()
    {
        // Arrange
        var bytes = System.Text.Encoding.UTF8.GetBytes(VALID_CSV);
        using var stream = new MemoryStream(bytes);

        // Act & Assert
        var ex = Assert.Throws<CsvException>(() =>
        {
            _ = Csv.Read<PersonRecord>()
                .WithMaxInputSize(bytes.Length - 1)
                .FromStream(stream, out _, leaveOpen: false);
        });

        Assert.Contains("exceeds maximum", ex.Message);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WithMaxInputSize_NullDisablesLimit_Succeeds()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, VALID_CSV);

            // Act — null explicitly disables the limit
            using var reader = Csv.Read<PersonRecord>()
                .WithMaxInputSize(null)
                .FromFile(tempFile, out _);

            var records = new List<PersonRecord>();
            while (reader.MoveNext())
                records.Add(reader.Current);

            // Assert
            Assert.Equal(2, records.Count);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowReaderBuilder_WithMaxInputSize_FileExceedsLimit_ThrowsCsvException()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, VALID_CSV);
            var fileSize = new FileInfo(tempFile).Length;

            // Act & Assert — row builder also enforces the limit
            Assert.Throws<CsvException>(() =>
            {
                _ = Csv.Read()
                    .WithMaxInputSize(fileSize - 1)
                    .FromFile(tempFile, out _);
            });
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Task 3: FromFileAsync / FromStreamAsync
    // ─────────────────────────────────────────────────────────────

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task FromFileAsync_ReadsRecordsFromFile()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, VALID_CSV, TestContext.Current.CancellationToken);

            // Act
            var records = new List<PersonRecord>();
            await foreach (var record in Csv.Read<PersonRecord>()
                .FromFileAsync(tempFile, TestContext.Current.CancellationToken))
            {
                records.Add(record);
            }

            // Assert
            Assert.Equal(2, records.Count);
            Assert.Equal("Alice", records[0].Name);
            Assert.Equal(30, records[0].Age);
            Assert.Equal("Bob", records[1].Name);
            Assert.Equal(25, records[1].Age);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task FromFileAsync_Cancellation_ThrowsOperationCancelledException()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            // Write enough rows to ensure we have data to read
            var sb = new System.Text.StringBuilder("Name,Age\r\n");
            for (int i = 0; i < 100; i++)
                sb.AppendLine($"Person{i},{i}");
            await File.WriteAllTextAsync(tempFile, sb.ToString(), TestContext.Current.CancellationToken);

            using var cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel immediately

            // Act & Assert
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (var _ in Csv.Read<PersonRecord>().FromFileAsync(tempFile, cts.Token))
                {
                    // Should not reach here
                }
            });
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task FromStreamAsync_ReadsRecordsFromStream()
    {
        // Arrange
        var bytes = System.Text.Encoding.UTF8.GetBytes(VALID_CSV);
        using var stream = new MemoryStream(bytes);

        // Act
        var records = new List<PersonRecord>();
        await foreach (var record in Csv.Read<PersonRecord>()
            .FromStreamAsync(stream, cancellationToken: TestContext.Current.CancellationToken))
        {
            records.Add(record);
        }

        // Assert
        Assert.Equal(2, records.Count);
        Assert.Equal("Alice", records[0].Name);
        Assert.Equal(30, records[0].Age);
        Assert.Equal("Bob", records[1].Name);
        Assert.Equal(25, records[1].Age);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task FromStreamAsync_LeaveOpen_True_StreamRemainsReadable()
    {
        // Arrange
        var bytes = System.Text.Encoding.UTF8.GetBytes(VALID_CSV);
        using var stream = new MemoryStream(bytes);

        // Act
        await foreach (var _ in Csv.Read<PersonRecord>()
            .FromStreamAsync(stream, leaveOpen: true, cancellationToken: TestContext.Current.CancellationToken))
        {
            // consume all records
        }

        // Assert — stream should still be readable (MemoryStream stays open)
        Assert.True(stream.CanRead);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task FromFileAsync_WithBuilderOptions_AppliesOptions()
    {
        // Arrange — use tab delimiter
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "Name\tAge\r\nAlice\t30\r\n", TestContext.Current.CancellationToken);

            // Act
            var records = new List<PersonRecord>();
            await foreach (var record in Csv.Read<PersonRecord>()
                .WithDelimiter('\t')
                .FromFileAsync(tempFile, TestContext.Current.CancellationToken))
            {
                records.Add(record);
            }

            // Assert
            Assert.Single(records);
            Assert.Equal("Alice", records[0].Name);
            Assert.Equal(30, records[0].Age);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task FromStreamAsync_Cancellation_ThrowsOperationCancelledException()
    {
        // Arrange
        var sb = new System.Text.StringBuilder("Name,Age\r\n");
        for (int i = 0; i < 100; i++)
            sb.AppendLine($"Person{i},{i}");
        var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        using var stream = new MemoryStream(bytes);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in Csv.Read<PersonRecord>().FromStreamAsync(stream, cancellationToken: cts.Token))
            {
                // Should not reach here
            }
        });
    }
}
