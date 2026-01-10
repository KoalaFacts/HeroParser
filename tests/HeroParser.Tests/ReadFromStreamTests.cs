using HeroParser.SeparatedValues;
using System.Text;
using Xunit;

namespace HeroParser.Tests;

public class ReadFromStreamTests
{
    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ReadFromStream_NonSeekableStream_ParsesCorrectly()
    {
        var csv = "a,b\n1,2";
        var bytes = Encoding.UTF8.GetBytes(csv);
        using var stream = new NonSeekableReadStream(new MemoryStream(bytes));

        var reader = Csv.ReadFromStream(stream, out _, leaveOpen: false);
        try
        {
            Assert.True(reader.MoveNext());
            Assert.Equal(new[] { "a", "b" }, reader.Current.ToStringArray());

            Assert.True(reader.MoveNext());
            Assert.Equal(new[] { "1", "2" }, reader.Current.ToStringArray());

            Assert.False(reader.MoveNext());
        }
        finally
        {
            reader.Dispose();
        }

        Assert.True(stream.Disposed);
    }

    private sealed class NonSeekableReadStream : Stream
    {
        private readonly Stream inner;

        public NonSeekableReadStream(Stream inner)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public bool Disposed { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => inner.Read(buffer);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            return inner.ReadAsync(buffer, cancellationToken);
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            return inner.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Disposed = true;
                inner.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
