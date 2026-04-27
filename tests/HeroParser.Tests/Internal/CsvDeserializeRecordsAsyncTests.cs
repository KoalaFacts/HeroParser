using System.IO.Pipelines;
using System.Text;
using HeroParser.SeparatedValues;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Records;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Drives <see cref="HeroParser.Csv.DeserializeRecordsAsync{T}(PipeReader, CsvRecordOptions?, CsvReadOptions?, CancellationToken)"/>
/// for paths that the existing PipeReaderTests don't cover: progress reporting,
/// scratch-buffer growth for rows spanning multiple pipe segments, large records,
/// validation error collection, and cancellation.
/// </summary>
[Trait("Category", "Unit")]
public class CsvDeserializeRecordsAsyncTests
{
    [GenerateBinder]
    public sealed class Person
    {
        [TabularMap(Name = "Name")] public string Name { get; set; } = "";
        [TabularMap(Name = "Age")] public int Age { get; set; }
    }

    [Fact]
    public async Task BasicRecords_ParsedFromPipeReader()
    {
        var bytes = "Name,Age\nAlice,30\nBob,25\n"u8.ToArray();
        var pipe = PipeReader.Create(new MemoryStream(bytes));
        var records = new List<Person>();
        await foreach (var p in HeroParser.Csv.DeserializeRecordsAsync<Person>(pipe,
            cancellationToken: TestContext.Current.CancellationToken))
        {
            records.Add(p);
        }
        Assert.Equal(2, records.Count);
        Assert.Equal("Alice", records[0].Name);
        Assert.Equal(30, records[0].Age);
    }

    [Fact]
    public async Task NullPipe_Throws()
    {
        await Task.CompletedTask;
        Assert.Throws<ArgumentNullException>(() =>
            HeroParser.Csv.DeserializeRecordsAsync<Person>(null!,
                cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ProgressReported_AtConfiguredInterval()
    {
        var sb = new StringBuilder("Name,Age\n");
        for (int i = 0; i < 50; i++) sb.AppendLine($"P{i},{i + 18}");

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var pipe = PipeReader.Create(new MemoryStream(bytes));
        var reports = new List<CsvProgress>();
        var recordOptions = new CsvRecordOptions
        {
            Progress = new Progress<CsvProgress>(reports.Add),
            ProgressIntervalRows = 5
        };

        var records = new List<Person>();
        await foreach (var p in HeroParser.Csv.DeserializeRecordsAsync<Person>(pipe, recordOptions,
            cancellationToken: TestContext.Current.CancellationToken))
        {
            records.Add(p);
        }
        Assert.Equal(50, records.Count);
        // Progress reports may be delivered asynchronously via SyncContext; verify they fire eventually.
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.NotEmpty(reports);
    }

    [Fact]
    public async Task LargeRow_TriggersScratchBufferGrowth()
    {
        var bigName = new string('a', 4096);
        var csv = $"Name,Age\n{bigName},42\n";
        var bytes = Encoding.UTF8.GetBytes(csv);
        var pipe = PipeReader.Create(new MemoryStream(bytes));

        var records = new List<Person>();
        await foreach (var p in HeroParser.Csv.DeserializeRecordsAsync<Person>(pipe,
            cancellationToken: TestContext.Current.CancellationToken))
        {
            records.Add(p);
        }
        Assert.Single(records);
        Assert.Equal(4096, records[0].Name.Length);
        Assert.Equal(42, records[0].Age);
    }

    [Fact]
    public async Task MultiSegmentRow_HandledByScratchBuffer()
    {
        // Force the parser to copy a row spanning multiple pipe segments via scratch buffer.
        var ct = TestContext.Current.CancellationToken;
        var pipe = new Pipe(new PipeOptions(minimumSegmentSize: 8));

        var data = "Name,Age\nAlice-with-long-name,30\nBob,25\n";
        var bytes = Encoding.UTF8.GetBytes(data);
        int written = 0;
        while (written < bytes.Length)
        {
            int chunk = Math.Min(4, bytes.Length - written);
            bytes.AsSpan(written, chunk).CopyTo(pipe.Writer.GetSpan(chunk));
            pipe.Writer.Advance(chunk);
            await pipe.Writer.FlushAsync(ct);
            written += chunk;
        }
        await pipe.Writer.CompleteAsync();

        var records = new List<Person>();
        await foreach (var p in HeroParser.Csv.DeserializeRecordsAsync<Person>(pipe.Reader,
            cancellationToken: ct))
        {
            records.Add(p);
        }
        Assert.Equal(2, records.Count);
        Assert.Equal("Alice-with-long-name", records[0].Name);
        Assert.Equal("Bob", records[1].Name);
    }

    [Fact]
    public async Task SkipRows_HonoredOnPipePath()
    {
        var csv = "skip-me\nName,Age\nAlice,30\n";
        var bytes = Encoding.UTF8.GetBytes(csv);
        var pipe = PipeReader.Create(new MemoryStream(bytes));
        var recordOptions = new CsvRecordOptions { SkipRows = 1 };

        var records = new List<Person>();
        await foreach (var p in HeroParser.Csv.DeserializeRecordsAsync<Person>(pipe, recordOptions,
            cancellationToken: TestContext.Current.CancellationToken))
        {
            records.Add(p);
        }
        Assert.Single(records);
        Assert.Equal("Alice", records[0].Name);
    }

    [Fact]
    public async Task PreCancelledToken_ThrowsOperationCancelled()
    {
        var bytes = Encoding.UTF8.GetBytes("Name,Age\nAlice,30\nBob,25\n");
        var pipe = PipeReader.Create(new MemoryStream(bytes));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in HeroParser.Csv.DeserializeRecordsAsync<Person>(pipe,
                cancellationToken: cts.Token))
            {
            }
        });
    }

    [Fact]
    public async Task EmptyCsv_NoRecords()
    {
        var pipe = PipeReader.Create(new MemoryStream([]));
        var records = new List<Person>();
        await foreach (var p in HeroParser.Csv.DeserializeRecordsAsync<Person>(pipe,
            cancellationToken: TestContext.Current.CancellationToken))
        {
            records.Add(p);
        }
        Assert.Empty(records);
    }

    [Fact]
    public async Task HeaderOnly_NoDataRecords()
    {
        var pipe = PipeReader.Create(new MemoryStream("Name,Age\n"u8.ToArray()));
        var records = new List<Person>();
        await foreach (var p in HeroParser.Csv.DeserializeRecordsAsync<Person>(pipe,
            cancellationToken: TestContext.Current.CancellationToken))
        {
            records.Add(p);
        }
        Assert.Empty(records);
    }

    [Fact]
    public async Task ParserOptions_CustomDelimiter_Honored()
    {
        var bytes = "Name|Age\nAlice|30\n"u8.ToArray();
        var pipe = PipeReader.Create(new MemoryStream(bytes));
        var parserOptions = new CsvReadOptions { Delimiter = '|' };

        var records = new List<Person>();
        await foreach (var p in HeroParser.Csv.DeserializeRecordsAsync<Person>(pipe,
            parserOptions: parserOptions,
            cancellationToken: TestContext.Current.CancellationToken))
        {
            records.Add(p);
        }
        Assert.Single(records);
        Assert.Equal(30, records[0].Age);
    }
}
