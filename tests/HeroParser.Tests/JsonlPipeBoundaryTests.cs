using System.IO.Pipelines;
using System.Text;
using HeroParser.JsonLines;
using HeroParser.JsonLines.Reading;
using Xunit;

namespace HeroParser.Tests;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class JsonlPipeBoundaryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task ExactLimit_CrLf_SucceedsAtEveryWriteBoundary(int split)
    {
        var pipe = new Pipe();
        byte[] data = Encoding.UTF8.GetBytes("{}\r\n");
        try
        {
            await pipe.Writer.WriteAsync(data.AsMemory(0, split), TestContext.Current.CancellationToken);
            await using var lines = Jsonl.ReadLinesAsync(pipe.Reader,
                new JsonlReadOptions { MaxLineSizeBytes = 2 }, TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);
            Task<bool> next = lines.MoveNextAsync().AsTask();
            await pipe.Writer.WriteAsync(data.AsMemory(split), TestContext.Current.CancellationToken);
            await pipe.Writer.CompleteAsync();

            Assert.True(await next);
            Assert.Equal("{}", lines.Current.ToString());
            Assert.Equal(1, lines.Current.LineNumber);
            Assert.False(await lines.MoveNextAsync());
        }
        finally
        {
            await pipe.Reader.CompleteAsync();
            await pipe.Writer.CompleteAsync();
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("x\n")]
    [InlineData("\r\n")]
    public async Task ExtraCr_WithoutMatchingLf_StillExceedsLimit(string suffix)
    {
        var pipe = new Pipe();
        try
        {
            await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes("{}\r"), TestContext.Current.CancellationToken);
            await using var lines = Jsonl.ReadLinesAsync(pipe.Reader,
                new JsonlReadOptions { MaxLineSizeBytes = 2 }, TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);
            Task<bool> next = lines.MoveNextAsync().AsTask();
            await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(suffix), TestContext.Current.CancellationToken);
            await pipe.Writer.CompleteAsync();

            JsonlException error = await Assert.ThrowsAsync<JsonlException>(async () => await next);
            Assert.Equal(JsonlErrorCode.LineTooLong, error.ErrorCode);
        }
        finally
        {
            await pipe.Reader.CompleteAsync();
            await pipe.Writer.CompleteAsync();
        }
    }
}
