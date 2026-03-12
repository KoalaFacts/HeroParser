using HeroParser.SeparatedValues.Core;
using Xunit;

namespace HeroParser.Tests;

public class CsvReaderSecurityTests
{
    private sealed class CsvRecord
    {
        public string Value { get; set; } = string.Empty;
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Builder_FromStream_EnforcesBufferedSizeLimit()
    {
        using var stream = new LargeSeekableStream((128L * 1024 * 1024) + 1);

        var ex = Assert.Throws<CsvException>(() => Csv.Read<CsvRecord>().FromStream(stream, out _));
        Assert.Equal(CsvErrorCode.ParseError, ex.ErrorCode);
    }

    private sealed class LargeSeekableStream(long length) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get; set; }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => 0;

        public override int Read(Span<byte> buffer) => 0;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(0);

        public override long Seek(long offset, SeekOrigin origin)
        {
            Position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => Position + offset,
                SeekOrigin.End => length + offset,
                _ => Position
            };
            return Position;
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
