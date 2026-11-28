using System.IO.Pipelines;
using System.Text;
using HeroParser.FixedWidths;
using HeroParser.JsonLines;
using HeroParser.JsonLines.Reading;
using Xunit;

namespace HeroParser.Tests;

/// <summary>
/// <c>Clone()</c> and multi-segment chunk tests for zero-copy Fixed-Width and JSONL <see cref="PipeReader"/> streaming.
/// </summary>
public class FixedWidthAndJsonlPipeAllocationTests
{
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
