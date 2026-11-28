using System.IO.Pipelines;
using System.Text;
using HeroParser.SeparatedValues.Core;
using Xunit;

namespace HeroParser.Tests;

/// <summary>
/// Unit tests covering pooled <see cref="CsvPipeRow"/> streaming, <see cref="CsvPipeRow.Clone"/>,
/// O(1) allocation guarantees, <see cref="CsvPipeSequenceReader"/> <see cref="CsvReadOptions.MaxInputSize"/>
/// accounting, and enumerator cancellation.
/// </summary>
public class CsvPipeSlabAndLimitTests
{
    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ReadFromPipeReader_ClonedRowsAcrossHeaderWidths_PreserveAllColumns()
    {
        var ct = TestContext.Current.CancellationToken;
        var expected = new List<(string Col0, string Col1)>();
        var sb = new StringBuilder();

        for (int i = 0; i < 320; i++)
        {
            string col0 = i switch
            {
                50 or 180 => $"  mid_{i}_{new string('a', 320)}  ",          // > 254 B (2-byte header)
                120 or 260 => $"huge_{i}_{new string('b', 1_400)}",          // grows pooled buffer
                290 => $"giant_{i}_{new string('c', 66_000)}",               // > 65534 B (4-byte header)
                _ => $"r{i}_short"
            };
            string col1 = $"val_{i}";
            expected.Add((col0.Trim(), col1));
            sb.Append(col0).Append(',').Append(col1).Append('\n');
        }

        byte[] payload = Encoding.UTF8.GetBytes(sb.ToString());
        var pipe = CreateChunkedPipe(payload, chunkSize: 1_024, ct);
        var options = new CsvReadOptions
        {
            TrimFields = true,
            MaxRowSize = 128 * 1024,
            MaxFieldSize = 128 * 1024
        };

        var cloned = new List<CsvPipeRow>();
        await foreach (var row in Csv.ReadFromPipeReaderAsync(pipe.Reader, options, ct))
        {
            Assert.Equal(expected[cloned.Count].Col0, row[0].ToString());
            Assert.Equal(expected[cloned.Count].Col1, row[1].ToString());
            cloned.Add(row.Clone());
        }

        Assert.Equal(expected.Count, cloned.Count);
        for (int i = 0; i < cloned.Count; i++)
        {
            Assert.Equal(i + 1, cloned[i].RowNumber);
            Assert.Equal(2, cloned[i].ColumnCount);
            Assert.Equal(expected[i].Col0, cloned[i][0].ToString());
            Assert.Equal(expected[i].Col1, cloned[i][1].ToString());
        }

        Assert.Equal(0, default(CsvPipeRow).Clone().ColumnCount);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task PipeSequenceReader_MaxInputSize_ExactAndExceeded_EnforcedCorrectly()
    {
        var ct = TestContext.Current.CancellationToken;
        byte[] payload = Encoding.UTF8.GetBytes("a,b\n1,2\n3,4\n");

        // Exact limit succeeds
        var exactOptions = new CsvReadOptions { MaxInputSize = payload.Length };
        await using var exactReader = Csv.CreatePipeSequenceReader(
            PipeReader.Create(new MemoryStream(payload, writable: false)),
            exactOptions);
        int exactRows = 0;
        while (await exactReader.MoveNextAsync(ct))
        {
            exactRows++;
        }
        Assert.Equal(3, exactRows);

        // One byte over fails
        var overOptions = new CsvReadOptions { MaxInputSize = payload.Length - 1 };
        await using var overReader = Csv.CreatePipeSequenceReader(
            PipeReader.Create(new MemoryStream(payload, writable: false)),
            overOptions);
        var ex = await Assert.ThrowsAsync<CsvException>(async () =>
        {
            while (await overReader.MoveNextAsync(ct))
            {
            }
        });
        Assert.Equal(CsvErrorCode.InvalidOptions, ex.ErrorCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task PipeSequenceReader_MaxInputSize_TracksBomAndMultiChunkRefillsAccurately(bool exceedByOneByte)
    {
        var ct = TestContext.Current.CancellationToken;
        byte[] bom = [0xEF, 0xBB, 0xBF];
        byte[] body = Encoding.UTF8.GetBytes("# comment row\nalpha,beta\n\ngamma,delta\nepsilon,zeta\n");
        byte[] payload = [.. bom, .. body];

        long limit = exceedByOneByte ? payload.Length - 1 : payload.Length;
        var options = new CsvReadOptions
        {
            CommentCharacter = '#',
            MaxInputSize = limit
        };

        // Feed small chunks so BOM, comments, partial rows, and multiple refills are exercised
        var pipe = CreateChunkedPipe(payload, chunkSize: 11, ct);
        await using var reader = Csv.CreatePipeSequenceReader(pipe.Reader, options);

        if (exceedByOneByte)
        {
            var ex = await Assert.ThrowsAsync<CsvException>(async () =>
            {
                while (await reader.MoveNextAsync(ct))
                {
                }
            });
            Assert.Equal(CsvErrorCode.InvalidOptions, ex.ErrorCode);
        }
        else
        {
            var rows = new List<(string A, string B)>();
            while (await reader.MoveNextAsync(ct))
            {
                rows.Add((reader.Current[0].ToString(), reader.Current[1].ToString()));
            }

            Assert.Equal([("alpha", "beta"), ("gamma", "delta"), ("epsilon", "zeta")], rows);
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ReadFromPipeReader_PreCanceledAndAsyncCanceled_ThrowsOperationCanceledException()
    {
        var ct = TestContext.Current.CancellationToken;

        // Synchronous fast-path pre-canceled check
        using var preCanceledCts = new CancellationTokenSource();
        await preCanceledCts.CancelAsync();
        var prePipe = new Pipe();
        await prePipe.Writer.CompleteAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in Csv.ReadFromPipeReaderAsync(prePipe.Reader, cancellationToken: preCanceledCts.Token))
            {
            }
        });

        // Asynchronous slow-path cancellation while awaiting more pipe data
        var slowPipe = new Pipe();
        await slowPipe.Writer.WriteAsync(Encoding.UTF8.GetBytes("a,b\n"), ct);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var enumerator = Csv.ReadFromPipeReaderAsync(slowPipe.Reader, cancellationToken: linkedCts.Token)
            .GetAsyncEnumerator(linkedCts.Token);

        try
        {
            Assert.True(await enumerator.MoveNextAsync());
            Assert.Equal("a", enumerator.Current[0].ToString());

            var pendingNext = enumerator.MoveNextAsync();
            Assert.False(pendingNext.IsCompleted);
            await linkedCts.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pendingNext);
        }
        finally
        {
            await enumerator.DisposeAsync();
            await slowPipe.Writer.CompleteAsync();
        }
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
