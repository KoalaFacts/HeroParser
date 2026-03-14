using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using HeroParser.SeparatedValues.Core;
using Xunit;

namespace HeroParser.Tests;

/// <summary>
/// Tests for PipeReader-based CSV reading support.
/// </summary>
public class PipeReaderTests
{
    [GenerateBinder]
    public sealed class PipePersonRecord
    {
        public string Name { get; set; } = string.Empty;

        public int Age { get; set; }
    }

    #region Basic Reading

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ReadFromPipeReader_SimpleData_ParsesCorrectly()
    {
        var csv = "Name,Age,City\r\nAlice,30,NYC\r\nBob,25,LA\r\n";
        var pipe = CreatePipeFromString(csv);

        var rows = new List<(string Name, string Age, string City)>();
        await foreach (var row in Csv.ReadFromPipeReaderAsync(pipe.Reader, cancellationToken: TestContext.Current.CancellationToken))
        {
            rows.Add((
                row[0].ToString(),
                row[1].ToString(),
                row[2].ToString()));
        }

        Assert.Equal(3, rows.Count);
        Assert.Equal("Name", rows[0].Name);
        Assert.Equal("Age", rows[0].Age);
        Assert.Equal("City", rows[0].City);
        Assert.Equal("Alice", rows[1].Name);
        Assert.Equal("30", rows[1].Age);
        Assert.Equal("NYC", rows[1].City);
        Assert.Equal("Bob", rows[2].Name);
        Assert.Equal("25", rows[2].Age);
        Assert.Equal("LA", rows[2].City);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ReadFromPipeReader_EmptyInput_ReturnsNoRows()
    {
        var pipe = CreatePipeFromString("");

        var count = 0;
        await foreach (var _ in Csv.ReadFromPipeReaderAsync(pipe.Reader, cancellationToken: TestContext.Current.CancellationToken))
        {
            count++;
        }

        Assert.Equal(0, count);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ReadFromPipeReader_SingleRow_ParsesCorrectly()
    {
        var csv = "a,b,c\r\n";
        var pipe = CreatePipeFromString(csv);

        var rows = new List<int>();
        await foreach (var row in Csv.ReadFromPipeReaderAsync(pipe.Reader, cancellationToken: TestContext.Current.CancellationToken))
        {
            rows.Add(row.ColumnCount);
        }

        Assert.Single(rows);
        Assert.Equal(3, rows[0]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ReadFromPipeReader_QuotedFields_HandlesCorrectly()
    {
        var csv = "Name,Description\r\n\"Alice\",\"Has a, comma\"\r\n";
        var pipe = CreatePipeFromString(csv);

        var descriptions = new List<string>();
        var isFirst = true;
        await foreach (var row in Csv.ReadFromPipeReaderAsync(pipe.Reader, cancellationToken: TestContext.Current.CancellationToken))
        {
            if (isFirst) { isFirst = false; continue; } // skip header
            descriptions.Add(row[1].ToUnquotedString());
        }

        Assert.Single(descriptions);
        Assert.Equal("Has a, comma", descriptions[0]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ReadFromPipeReader_CommentLines_AreSkipped()
    {
        var csv = "# ignore me\r\nName,Age\r\nAlice,30\r\n";
        var pipe = CreatePipeFromString(csv);
        var options = new CsvReadOptions { CommentCharacter = '#' };

        var names = new List<string>();
        await foreach (var row in Csv.ReadFromPipeReaderAsync(pipe.Reader, options, TestContext.Current.CancellationToken))
        {
            names.Add(row[0].ToString());
        }

        Assert.Equal(["Name", "Alice"], names);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ReadFromPipeReader_ChunkedCommentLines_AreSkipped()
    {
        var pipe = new Pipe();
        var csv = "# ignore me\r\nName,Age\r\nAlice,30\r\n";
        var bytes = Encoding.UTF8.GetBytes(csv);
        var options = new CsvReadOptions { CommentCharacter = '#' };
        var ct = TestContext.Current.CancellationToken;

        _ = Task.Run(async () =>
        {
            for (int i = 0; i < bytes.Length; i += 2)
            {
                var chunk = bytes.AsMemory(i, Math.Min(2, bytes.Length - i));
                await pipe.Writer.WriteAsync(chunk, ct);
                await Task.Delay(1, ct);
            }

            await pipe.Writer.CompleteAsync();
        }, ct);

        var names = new List<string>();
        await foreach (var row in Csv.ReadFromPipeReaderAsync(pipe.Reader, options, ct))
        {
            names.Add(row[0].ToString());
        }

        Assert.Equal(["Name", "Alice"], names);
    }

    #endregion

    #region Borrowed Reader

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task CreatePipeSequenceReader_SimpleData_ParsesCorrectly()
    {
        var csv = "Name,Age,City\r\nAlice,30,NYC\r\nBob,25,LA\r\n";
        var pipe = CreatePipeFromString(csv);

        var rows = new List<(string Name, string Age, string City)>();
        await using var reader = Csv.CreatePipeSequenceReader(pipe.Reader);

        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
        {
            var row = reader.Current;
            rows.Add((
                row[0].ToString(),
                row[1].ToString(),
                row[2].ToString()));
        }

        Assert.Equal(3, rows.Count);
        Assert.Equal("Name", rows[0].Name);
        Assert.Equal("Alice", rows[1].Name);
        Assert.Equal("Bob", rows[2].Name);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task CreatePipeSequenceReader_TrackSourceLineNumbers_TracksCorrectly()
    {
        var csv = "Id,Notes\r\n1,\"line1\r\nline2\"\r\n2,done\r\n";
        var pipe = CreatePipeFromString(csv);
        var options = new CsvReadOptions
        {
            AllowNewlinesInsideQuotes = true,
            TrackSourceLineNumbers = true
        };

        var sourceLines = new List<int>();
        var notes = new List<string>();
        await using var reader = Csv.CreatePipeSequenceReader(pipe.Reader, options);

        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
        {
            var row = reader.Current;
            sourceLines.Add(row.SourceLineNumber);
            notes.Add(row[1].ToString());
        }

        Assert.Equal([1, 2, 4], sourceLines);
        Assert.Equal("Notes", notes[0]);
        Assert.Equal("line1\r\nline2", notes[1]);
        Assert.Equal("done", notes[2]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task CreatePipeSequenceReader_TrackSourceLineNumbers_TracksChunkedQuotedRows()
    {
        var pipe = new Pipe();
        var csv = "Id,Notes\r\n1,\"line1\r\nline2\"\r\n2,done\r\n";
        var bytes = Encoding.UTF8.GetBytes(csv);
        var options = new CsvReadOptions
        {
            AllowNewlinesInsideQuotes = true,
            TrackSourceLineNumbers = true
        };
        var ct = TestContext.Current.CancellationToken;

        _ = Task.Run(async () =>
        {
            for (int i = 0; i < bytes.Length; i += 2)
            {
                var chunk = bytes.AsMemory(i, Math.Min(2, bytes.Length - i));
                await pipe.Writer.WriteAsync(chunk, ct);
                await Task.Delay(1, ct);
            }

            await pipe.Writer.CompleteAsync();
        }, ct);

        var sourceLines = new List<int>();
        await using var reader = Csv.CreatePipeSequenceReader(pipe.Reader, options);

        while (await reader.MoveNextAsync(ct))
        {
            sourceLines.Add(reader.Current.SourceLineNumber);
        }

        Assert.Equal([1, 2, 4], sourceLines);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task CreatePipeSequenceReader_CommentLines_AreSkipped()
    {
        var csv = "# ignore me\r\nName,Age\r\nAlice,30\r\n";
        var pipe = CreatePipeFromString(csv);
        var options = new CsvReadOptions { CommentCharacter = '#' };

        var names = new List<string>();
        await using var reader = Csv.CreatePipeSequenceReader(pipe.Reader, options);

        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
        {
            names.Add(reader.Current[0].ToString());
        }

        Assert.Equal(["Name", "Alice"], names);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task CreatePipeSequenceReader_ChunkedCommentLines_AreSkipped()
    {
        var pipe = new Pipe();
        var csv = "# ignore me\r\nName,Age\r\nAlice,30\r\n";
        var bytes = Encoding.UTF8.GetBytes(csv);
        var options = new CsvReadOptions { CommentCharacter = '#' };
        var ct = TestContext.Current.CancellationToken;

        _ = Task.Run(async () =>
        {
            for (int i = 0; i < bytes.Length; i += 2)
            {
                var chunk = bytes.AsMemory(i, Math.Min(2, bytes.Length - i));
                await pipe.Writer.WriteAsync(chunk, ct);
                await Task.Delay(1, ct);
            }

            await pipe.Writer.CompleteAsync();
        }, ct);

        var names = new List<string>();
        await using var reader = Csv.CreatePipeSequenceReader(pipe.Reader, options);

        while (await reader.MoveNextAsync(ct))
        {
            names.Add(reader.Current[0].ToString());
        }

        Assert.Equal(["Name", "Alice"], names);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Builder_FromPipeReaderAsync_SkipRows_AppliesSkipRows()
    {
        var csv = "skip,me\r\nName,Age\r\nAlice,30\r\n";
        var pipe = CreatePipeFromString(csv);

        var names = new List<string>();
        await using var reader = Csv.Read()
            .SkipRows(1)
            .FromPipeReaderAsync(pipe.Reader);

        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
        {
            names.Add(reader.Current[0].ToString());
        }

        Assert.Equal(["Name", "Alice"], names);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task DeserializeRecordsAsync_FromPipeReader_BindsGeneratedRecords()
    {
        var csv = "Name,Age\r\nAlice,30\r\nBob,25\r\n";
        var pipe = CreatePipeFromString(csv);

        var records = new List<PipePersonRecord>();
        await foreach (var record in Csv.DeserializeRecordsAsync<PipePersonRecord>(
            pipe.Reader,
            cancellationToken: TestContext.Current.CancellationToken))
        {
            records.Add(record);
        }

        Assert.Equal(2, records.Count);
        Assert.Equal("Alice", records[0].Name);
        Assert.Equal(30, records[0].Age);
        Assert.Equal("Bob", records[1].Name);
        Assert.Equal(25, records[1].Age);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task DeserializeRecordsAsync_FromPipeReader_BindsChunkedRows()
    {
        var pipe = new Pipe();
        var csv = "Name,Age\r\nAlice,30\r\nBob,25\r\n";
        var bytes = Encoding.UTF8.GetBytes(csv);
        var ct = TestContext.Current.CancellationToken;

        _ = Task.Run(async () =>
        {
            for (int i = 0; i < bytes.Length; i += 3)
            {
                var chunk = bytes.AsMemory(i, Math.Min(3, bytes.Length - i));
                await pipe.Writer.WriteAsync(chunk, ct);
                await Task.Delay(1, ct);
            }

            await pipe.Writer.CompleteAsync();
        }, ct);

        var records = new List<PipePersonRecord>();
        await foreach (var record in Csv.DeserializeRecordsAsync<PipePersonRecord>(
            pipe.Reader,
            cancellationToken: ct))
        {
            records.Add(record);
        }

        Assert.Equal(2, records.Count);
        Assert.Equal("Alice", records[0].Name);
        Assert.Equal(30, records[0].Age);
        Assert.Equal("Bob", records[1].Name);
        Assert.Equal(25, records[1].Age);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task DeserializeRecordsAsync_FromPipeReader_SkipsChunkedCommentLines()
    {
        var pipe = new Pipe();
        var csv = "# ignore me\r\nName,Age\r\nAlice,30\r\nBob,25\r\n";
        var bytes = Encoding.UTF8.GetBytes(csv);
        var options = new CsvReadOptions { CommentCharacter = '#' };
        var ct = TestContext.Current.CancellationToken;

        _ = Task.Run(async () =>
        {
            for (int i = 0; i < bytes.Length; i += 2)
            {
                var chunk = bytes.AsMemory(i, Math.Min(2, bytes.Length - i));
                await pipe.Writer.WriteAsync(chunk, ct);
                await Task.Delay(1, ct);
            }

            await pipe.Writer.CompleteAsync();
        }, ct);

        var records = new List<PipePersonRecord>();
        await foreach (var record in Csv.DeserializeRecordsAsync<PipePersonRecord>(
            pipe.Reader,
            parserOptions: options,
            cancellationToken: ct))
        {
            records.Add(record);
        }

        Assert.Equal(2, records.Count);
        Assert.Equal("Alice", records[0].Name);
        Assert.Equal(30, records[0].Age);
        Assert.Equal("Bob", records[1].Name);
        Assert.Equal(25, records[1].Age);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Builder_FromPipeReaderAsync_BindsGeneratedRecords()
    {
        var csv = "Name,Age\r\nAlice,30\r\nBob,25\r\n";
        var pipe = CreatePipeFromString(csv);

        var records = new List<PipePersonRecord>();
        await foreach (var record in Csv.Read<PipePersonRecord>()
            .FromPipeReaderAsync(pipe.Reader, TestContext.Current.CancellationToken))
        {
            records.Add(record);
        }

        Assert.Equal(2, records.Count);
        Assert.Equal("Alice", records[0].Name);
        Assert.Equal(30, records[0].Age);
        Assert.Equal("Bob", records[1].Name);
        Assert.Equal(25, records[1].Age);
    }

    #endregion

    #region Custom Options

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ReadFromPipeReader_CustomDelimiter_UsesDelimiter()
    {
        var csv = "Name;Age;City\r\nAlice;30;NYC\r\n";
        var pipe = CreatePipeFromString(csv);
        var options = new CsvReadOptions { Delimiter = ';' };

        var rows = new List<int>();
        await foreach (var row in Csv.ReadFromPipeReaderAsync(pipe.Reader, options, TestContext.Current.CancellationToken))
        {
            rows.Add(row.ColumnCount);
        }

        Assert.Equal(2, rows.Count);
        Assert.Equal(3, rows[0]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ReadFromPipeReader_TabDelimited_ParsesCorrectly()
    {
        var csv = "Name\tAge\r\nAlice\t30\r\n";
        var pipe = CreatePipeFromString(csv);
        var options = new CsvReadOptions { Delimiter = '\t' };

        var names = new List<string>();
        await foreach (var row in Csv.ReadFromPipeReaderAsync(pipe.Reader, options, TestContext.Current.CancellationToken))
        {
            names.Add(Encoding.UTF8.GetString(row[0].Span));
        }

        Assert.Equal(2, names.Count);
        Assert.Equal("Name", names[0]);
        Assert.Equal("Alice", names[1]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ReadFromPipeReader_MediumRow_UsesPackedHeaderWithoutCorruptingColumns()
    {
        string largeValue = new('a', 300);
        var csv = $"{largeValue},tail\r\n";
        var pipe = CreatePipeFromString(csv);

        var rows = new List<(int FirstLength, string SecondValue)>();
        await foreach (var row in Csv.ReadFromPipeReaderAsync(pipe.Reader, cancellationToken: TestContext.Current.CancellationToken))
        {
            rows.Add((row[0].Span.Length, row[1].ToString()));
        }

        Assert.Single(rows);
        Assert.Equal(300, rows[0].FirstLength);
        Assert.Equal("tail", rows[0].SecondValue);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ReadFromPipeReader_VeryLargeRow_FallsBackToIntHeaderWithoutCorruptingColumns()
    {
        string largeValue = new('b', 70_000);
        var csv = $"{largeValue},tail\r\n";
        var pipe = CreatePipeFromString(csv);
        var options = new CsvReadOptions { MaxRowSize = 100_000 };

        var rows = new List<(int FirstLength, string SecondValue)>();
        await foreach (var row in Csv.ReadFromPipeReaderAsync(pipe.Reader, options, TestContext.Current.CancellationToken))
        {
            rows.Add((row[0].Span.Length, row[1].ToString()));
        }

        Assert.Single(rows);
        Assert.Equal(70_000, rows[0].FirstLength);
        Assert.Equal("tail", rows[0].SecondValue);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ReadFromPipeReader_StripsUtf8Bom()
    {
        var bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes("Name,Age\r\nAlice,30\r\n"))
            .ToArray();

        var pipe = new Pipe();
        pipe.Writer.Write(bytes);
        pipe.Writer.Complete();

        var names = new List<string>();
        await foreach (var row in Csv.ReadFromPipeReaderAsync(pipe.Reader, cancellationToken: TestContext.Current.CancellationToken))
        {
            names.Add(row[0].ToString());
        }

        Assert.Equal(["Name", "Alice"], names);
    }

    #endregion

    #region Security Limits

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ReadFromPipeReader_MaxRowCount_ThrowsWhenExceeded()
    {
        var pipe = CreatePipeFromString("a\r\nb\r\n");
        var options = new CsvReadOptions { MaxRowCount = 1 };

        var count = 0;
        var ex = await Assert.ThrowsAsync<CsvException>(async () =>
        {
            await foreach (var _ in Csv.ReadFromPipeReaderAsync(pipe.Reader, options, TestContext.Current.CancellationToken))
            {
                count++;
            }
        });

        Assert.Equal(1, count);
        Assert.Equal(CsvErrorCode.TooManyRows, ex.ErrorCode);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ReadFromPipeReader_MaxFieldSize_ThrowsWhenExceeded()
    {
        var pipe = CreatePipeFromString("short,toolong\r\n");
        var options = new CsvReadOptions { MaxFieldSize = 4 };

        var ex = await Assert.ThrowsAsync<CsvException>(async () =>
        {
            await foreach (var _ in Csv.ReadFromPipeReaderAsync(pipe.Reader, options, TestContext.Current.CancellationToken))
            {
            }
        });

        Assert.Equal(CsvErrorCode.ParseError, ex.ErrorCode);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ReadFromPipeReader_MaxColumnCount_ThrowsWhenExceeded()
    {
        var pipe = CreatePipeFromString("a,b,c\r\n");
        var options = new CsvReadOptions { MaxColumnCount = 2 };

        var ex = await Assert.ThrowsAsync<CsvException>(async () =>
        {
            await foreach (var _ in Csv.ReadFromPipeReaderAsync(pipe.Reader, options, TestContext.Current.CancellationToken))
            {
            }
        });

        Assert.Equal(CsvErrorCode.TooManyColumns, ex.ErrorCode);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ReadFromPipeReader_MaxRowSize_ThrowsWhenExceeded()
    {
        var pipe = CreatePipeFromString("abcdef");
        var options = new CsvReadOptions { MaxRowSize = 3 };

        var ex = await Assert.ThrowsAsync<CsvException>(async () =>
        {
            await foreach (var _ in Csv.ReadFromPipeReaderAsync(pipe.Reader, options, TestContext.Current.CancellationToken))
            {
            }
        });

        Assert.Equal(CsvErrorCode.ParseError, ex.ErrorCode);
    }

    #endregion

    #region Cancellation

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ReadFromPipeReader_CancelledToken_StopsReading()
    {
        var csv = "a,b\r\n1,2\r\n3,4\r\n5,6\r\n";
        var pipe = CreatePipeFromString(csv);
        using var cts = new CancellationTokenSource();

        var count = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in Csv.ReadFromPipeReaderAsync(pipe.Reader, cancellationToken: cts.Token))
            {
                count++;
                if (count == 1)
                    cts.Cancel();
            }
        });

        Assert.Equal(1, count);
    }

    #endregion

    #region Large Data / Chunked Reading

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task ReadFromPipeReader_LargeData_StreamsWithoutFullBuffering()
    {
        // Generate a large CSV that would be expensive to fully buffer
        var sb = new StringBuilder();
        sb.AppendLine("Id,Value");
        for (int i = 0; i < 10_000; i++)
            sb.AppendLine($"{i},value_{i}");

        var pipe = CreatePipeFromString(sb.ToString());

        var count = 0;
        await foreach (var _ in Csv.ReadFromPipeReaderAsync(pipe.Reader, cancellationToken: TestContext.Current.CancellationToken))
        {
            count++;
        }

        // 10,000 data rows + 1 header
        Assert.Equal(10_001, count);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ReadFromPipeReader_SlowProducer_HandlesChunkedInput()
    {
        // Simulate a slow producer writing data in small chunks
        var pipe = new Pipe();
        var csv = "Name,Age\r\nAlice,30\r\nBob,25\r\n";
        var bytes = Encoding.UTF8.GetBytes(csv);

        // Write in small chunks to simulate network I/O
        var ct = TestContext.Current.CancellationToken;
        _ = Task.Run(async () =>
        {
            for (int i = 0; i < bytes.Length; i += 5)
            {
                var chunk = bytes.AsMemory(i, Math.Min(5, bytes.Length - i));
                await pipe.Writer.WriteAsync(chunk, ct);
                await Task.Delay(1, ct);
            }
            await pipe.Writer.CompleteAsync();
        }, ct);

        var rows = new List<string>();
        await foreach (var row in Csv.ReadFromPipeReaderAsync(pipe.Reader, cancellationToken: TestContext.Current.CancellationToken))
        {
            rows.Add(Encoding.UTF8.GetString(row[0].Span));
        }

        Assert.Equal(3, rows.Count);
        Assert.Equal("Name", rows[0]);
        Assert.Equal("Alice", rows[1]);
        Assert.Equal("Bob", rows[2]);
    }

    #endregion

    #region Null Argument Handling

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ReadFromPipeReader_NullReader_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
        {
            // Force enumeration to trigger the null check
            return Csv.ReadFromPipeReaderAsync(null!, cancellationToken: TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken).MoveNextAsync().AsTask();
        });
    }

    #endregion

    #region Helpers

    private static Pipe CreatePipeFromString(string data)
    {
        var pipe = new Pipe();
        var bytes = Encoding.UTF8.GetBytes(data);
        pipe.Writer.Write(bytes);
        pipe.Writer.Complete();
        return pipe;
    }

    #endregion
}
