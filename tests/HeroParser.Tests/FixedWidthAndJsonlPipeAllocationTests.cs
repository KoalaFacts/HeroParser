using System.IO.Pipelines;
using System.Text;
using HeroParser.FixedWidths;
using HeroParser.JsonLines;
using HeroParser.JsonLines.Reading;
using Xunit;

namespace HeroParser.Tests;

/// <summary>
/// Allocation and <c>Clone()</c> regression tests for zero-copy Fixed-Width and JSONL <see cref="PipeReader"/> streaming.
/// </summary>
public class FixedWidthAndJsonlPipeAllocationTests
{
    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task FixedWidth_ReadFromPipeReader_LargeInput_AllocatesConstantMemory()
    {
        var ct = TestContext.Current.CancellationToken;
        var sb = new StringBuilder();
        for (int i = 0; i < 5_000; i++)
        {
            sb.Append((i % 10_000).ToString("D4", System.Globalization.CultureInfo.InvariantCulture))
              .Append("Alice ")
              .Append('\n');
        }

        byte[] payload = Encoding.UTF8.GetBytes(sb.ToString());
        var options = new FixedWidthReadOptions { MaxRecordCount = 10_000 };

        async ValueTask<int> RunPassAsync()
        {
            using var stream = new MemoryStream(payload, writable: false);
            var reader = PipeReader.Create(stream);
            int total = 0;
            await foreach (var row in FixedWidth.ReadFromPipeReaderAsync(reader, options, ct))
            {
                total += row.Length;
            }

            return total;
        }

        Assert.Equal(50_000, await RunPassAsync());

        long before = GC.GetAllocatedBytesForCurrentThread();
        int totalLength = await RunPassAsync();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(50_000, totalLength);
        Assert.True(allocated < 16 * 1024, $"Expected O(1) allocation (< 16 KiB) for 5,000 FixedWidth pipe rows, but allocated {allocated:N0} bytes.");
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task FixedWidth_ReadFromPipeReader_CloneAcrossMultiSegmentChunks_PreservesAllRecords()
    {
        var ct = TestContext.Current.CancellationToken;
        var sb = new StringBuilder();
        var expected = new List<string>();
        for (int i = 0; i < 120; i++)
        {
            string line = $"{i:D4}Record_{i:D3}_Data";
            expected.Add(line);
            sb.Append(line).Append("\r\n");
        }

        byte[] payload = Encoding.UTF8.GetBytes(sb.ToString());
        var pipe = CreateChunkedPipe(payload, chunkSize: 7, ct);
        var cloned = new List<FixedWidthPipeRow>();

        await foreach (var row in FixedWidth.ReadFromPipeReaderAsync(pipe.Reader, cancellationToken: ct))
        {
            Assert.Equal(expected[cloned.Count], row.ToDecodedString());
            cloned.Add(row.Clone());
        }

        Assert.Equal(expected.Count, cloned.Count);
        for (int i = 0; i < cloned.Count; i++)
        {
            Assert.Equal(i + 1, cloned[i].RecordNumber);
            Assert.Equal(expected[i], cloned[i].ToDecodedString());
        }

        Assert.Equal(0, default(FixedWidthPipeRow).Clone().Length);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Jsonl_ReadLinesAsync_LargeInput_AllocatesConstantMemory()
    {
        var ct = TestContext.Current.CancellationToken;
        var sb = new StringBuilder();
        for (int i = 0; i < 5_000; i++)
        {
            sb.Append("{\"id\":").Append(i).Append(",\"name\":\"Item_").Append(i).Append("\"}\n");
        }

        byte[] payload = Encoding.UTF8.GetBytes(sb.ToString());

        async ValueTask<long> RunPassAsync()
        {
            using var stream = new MemoryStream(payload, writable: false);
            var reader = PipeReader.Create(stream);
            long totalBytes = 0;
            await foreach (JsonlLine line in Jsonl.ReadLinesAsync(reader, cancellationToken: ct))
            {
                totalBytes += line.Utf8.Length;
            }

            await reader.CompleteAsync();
            return totalBytes;
        }

        long expectedBytes = await RunPassAsync();
        Assert.True(expectedBytes > 100_000);

        long before = GC.GetAllocatedBytesForCurrentThread();
        long actualBytes = await RunPassAsync();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(expectedBytes, actualBytes);
        Assert.True(allocated < 16 * 1024, $"Expected O(1) allocation (< 16 KiB) for 5,000 JSONL pipe lines, but allocated {allocated:N0} bytes.");
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Jsonl_ReadLinesAsync_CloneAcrossMultiSegmentChunksAndBom_PreservesAllLines()
    {
        var ct = TestContext.Current.CancellationToken;
        var sb = new StringBuilder();
        var expected = new List<string>();
        for (int i = 0; i < 120; i++)
        {
            string line = $"{{\"id\":{i},\"name\":\"User_{i:D3}\"}}";
            expected.Add(line);
            if (i < 119)
            {
                sb.Append(line).Append('\n');
            }
            else
            {
                sb.Append(line); // exercise non-newline-terminated EOF line
            }
        }

        byte[] bom = [0xEF, 0xBB, 0xBF];
        byte[] payload = [.. bom, .. Encoding.UTF8.GetBytes(sb.ToString())];
        var pipe = CreateChunkedPipe(payload, chunkSize: 9, ct);
        var cloned = new List<JsonlLine>();

        try
        {
            await foreach (JsonlLine line in Jsonl.ReadLinesAsync(pipe.Reader, cancellationToken: ct))
            {
                Assert.Equal(expected[cloned.Count], line.ToString());
                cloned.Add(line.Clone());
            }
        }
        finally
        {
            await pipe.Reader.CompleteAsync();
        }

        Assert.Equal(expected.Count, cloned.Count);
        for (int i = 0; i < cloned.Count; i++)
        {
            Assert.Equal(i + 1, cloned[i].LineNumber);
            Assert.Equal(expected[i], cloned[i].ToString());
        }

        var emptyClone = default(JsonlLine).Clone();
        Assert.True(emptyClone.Utf8.IsEmpty);
        Assert.Equal(0, emptyClone.LineNumber);
    }

    private static Pipe CreateChunkedPipe(byte[] data, int chunkSize, CancellationToken ct)
    {
        var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 0, resumeWriterThreshold: 0));
        _ = Task.Run(async () =>
        {
            try
            {
                for (int offset = 0; offset < data.Length; offset += chunkSize)
                {
                    int len = Math.Min(chunkSize, data.Length - offset);
                    await pipe.Writer.WriteAsync(data.AsMemory(offset, len), ct).ConfigureAwait(false);
                    await Task.Yield();
                }

                await pipe.Writer.CompleteAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await pipe.Writer.CompleteAsync(ex).ConfigureAwait(false);
            }
        }, ct);

        return pipe;
    }
}
