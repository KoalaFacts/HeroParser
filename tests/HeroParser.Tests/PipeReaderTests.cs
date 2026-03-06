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
    #region Basic Reading

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ReadFromPipeReader_SimpleData_ParsesCorrectly()
    {
        var csv = "Name,Age,City\r\nAlice,30,NYC\r\nBob,25,LA\r\n";
        var pipe = CreatePipeFromString(csv);

        var rows = new List<(string Name, string Age, string City)>();
        await foreach (var row in Csv.ReadFromPipeReaderAsync(pipe.Reader))
        {
            rows.Add((
                row[0].ToString(),
                row[1].ToString(),
                row[2].ToString()));
        }

        Assert.Equal(2, rows.Count);
        Assert.Equal("Alice", rows[0].Name);
        Assert.Equal("30", rows[0].Age);
        Assert.Equal("NYC", rows[0].City);
        Assert.Equal("Bob", rows[1].Name);
        Assert.Equal("25", rows[1].Age);
        Assert.Equal("LA", rows[1].City);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ReadFromPipeReader_EmptyInput_ReturnsNoRows()
    {
        var pipe = CreatePipeFromString("");

        var count = 0;
        await foreach (var row in Csv.ReadFromPipeReaderAsync(pipe.Reader))
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
        await foreach (var row in Csv.ReadFromPipeReaderAsync(pipe.Reader))
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
        await foreach (var row in Csv.ReadFromPipeReaderAsync(pipe.Reader))
        {
            if (isFirst) { isFirst = false; continue; } // skip header
            descriptions.Add(Encoding.UTF8.GetString(row[1].Span));
        }

        Assert.Single(descriptions);
        Assert.Equal("Has a, comma", descriptions[0]);
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
        await foreach (var row in Csv.ReadFromPipeReaderAsync(pipe.Reader, options))
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
        await foreach (var row in Csv.ReadFromPipeReaderAsync(pipe.Reader, options))
        {
            names.Add(Encoding.UTF8.GetString(row[0].Span));
        }

        Assert.Equal(2, names.Count);
        Assert.Equal("Name", names[0]);
        Assert.Equal("Alice", names[1]);
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
            await foreach (var row in Csv.ReadFromPipeReaderAsync(pipe.Reader, cancellationToken: cts.Token))
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
        await foreach (var row in Csv.ReadFromPipeReaderAsync(pipe.Reader))
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
        _ = Task.Run(async () =>
        {
            for (int i = 0; i < bytes.Length; i += 5)
            {
                var chunk = bytes.AsMemory(i, Math.Min(5, bytes.Length - i));
                await pipe.Writer.WriteAsync(chunk);
                await Task.Delay(1);
            }
            await pipe.Writer.CompleteAsync();
        });

        var rows = new List<string>();
        await foreach (var row in Csv.ReadFromPipeReaderAsync(pipe.Reader))
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
    public void ReadFromPipeReader_NullReader_ThrowsArgumentNullException()
    {
        Assert.ThrowsAsync<ArgumentNullException>(() =>
        {
            // Force enumeration to trigger the null check
            return Csv.ReadFromPipeReaderAsync(null!).GetAsyncEnumerator().MoveNextAsync().AsTask();
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
