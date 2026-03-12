using System.Text;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Records.Binding;
using Xunit;

namespace HeroParser.Tests.FixedWidths;

public class FixedWidthSecurityTests
{
    [FixedWidthGenerateBinder]
    private sealed class FixedWidthRecord
    {
        [FixedWidthColumn(Start = 0, Length = 4)]
        public string Value { get; set; } = string.Empty;
    }

    [Fact]
    public void ReadFromStream_NonSeekable_EnforcesMaxInputSize()
    {
        using var stream = new NonSeekableMemoryStream(Encoding.UTF8.GetBytes("TOO-LARGE"));
        var options = new FixedWidthReadOptions { MaxInputSize = 4 };

        var ex = Assert.Throws<FixedWidthException>(() => FixedWidth.ReadFromStream(stream, options));
        Assert.Equal(FixedWidthErrorCode.InvalidOptions, ex.ErrorCode);
    }

    [Fact]
    public async Task ReadFromStreamAsync_NonSeekable_EnforcesMaxInputSize()
    {
        await using var stream = new NonSeekableMemoryStream(Encoding.UTF8.GetBytes("TOO-LARGE"));
        var options = new FixedWidthReadOptions { MaxInputSize = 4 };

        var ex = await Assert.ThrowsAsync<FixedWidthException>(
            () => FixedWidth.ReadFromStreamAsync(stream, options, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(FixedWidthErrorCode.InvalidOptions, ex.ErrorCode);
    }

    [Fact]
    public void DeserializeRecords_File_EnforcesMaxInputSizeBeforeReading()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "TOO-LARGE");
            var options = new FixedWidthReadOptions { MaxInputSize = 4 };

            var ex = Assert.Throws<FixedWidthException>(() =>
                FixedWidth.DeserializeRecords<FixedWidthRecord>(tempFile, options, Encoding.UTF8));

            Assert.Equal(FixedWidthErrorCode.InvalidOptions, ex.ErrorCode);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task CreateAsyncStreamReader_EnforcesMaxInputSize()
    {
        await using var stream = new NonSeekableMemoryStream(Encoding.UTF8.GetBytes("TOO-LARGE"));
        var options = new FixedWidthReadOptions { MaxInputSize = 4 };
        await using var reader = FixedWidth.CreateAsyncStreamReader(stream, options);

        var ex = await Assert.ThrowsAsync<FixedWidthException>(
            () => reader.MoveNextAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(FixedWidthErrorCode.InvalidOptions, ex.ErrorCode);
    }

    private sealed class NonSeekableMemoryStream(byte[] buffer) : MemoryStream(buffer)
    {
        public override bool CanSeek => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
    }
}
