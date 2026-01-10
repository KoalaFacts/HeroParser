using HeroParser.SeparatedValues;
using HeroParser.SeparatedValues.Core;
using System.IO;
using System.Text;
using Xunit;

namespace HeroParser.Tests;

public class CsvAsyncStreamReaderTests
{
    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncStreamReader_ParsesRowsAcrossBuffers()
    {
        var csv = "a,b,c\n1,2,3\n4,5,6\n";
        await using var reader = CreateReader(csv, bufferSize: 4);
        var cancellationToken = TestContext.Current.CancellationToken;

        Assert.True(await reader.MoveNextAsync(cancellationToken));
        var row1 = reader.Current;
        Assert.Equal(new[] { "a", "b", "c" }, row1.ToStringArray());

        Assert.True(await reader.MoveNextAsync(cancellationToken));
        var row2 = reader.Current;
        Assert.Equal(new[] { "1", "2", "3" }, row2.ToStringArray());

        Assert.True(await reader.MoveNextAsync(cancellationToken));
        var row3 = reader.Current;
        Assert.Equal(new[] { "4", "5", "6" }, row3.ToStringArray());

        Assert.False(await reader.MoveNextAsync(cancellationToken));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncStreamReader_AllowsNewlinesInsideQuotes()
    {
        var options = new CsvReadOptions { AllowNewlinesInsideQuotes = true };
        var csv = "a,\"b\nc\",d\n1,2,3";

        await using var reader = CreateReader(csv, options, bufferSize: 6);
        var cancellationToken = TestContext.Current.CancellationToken;

        Assert.True(await reader.MoveNextAsync(cancellationToken));
        var row1 = reader.Current;
        Assert.Equal("b\nc", row1[1].UnquoteToString());

        Assert.True(await reader.MoveNextAsync(cancellationToken));
        var row2 = reader.Current;
        Assert.Equal("1", row2[0].ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncStreamReader_HandlesLargeInputWithSmallMaxRowSize()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 1000; i++)
        {
            sb.Append("a,b,c\n");
        }

        var options = new CsvReadOptions { MaxRowSize = 16 };
        await using var reader = CreateReader(sb.ToString(), options, bufferSize: 8);
        var cancellationToken = TestContext.Current.CancellationToken;

        int count = 0;
        while (await reader.MoveNextAsync(cancellationToken))
        {
            count++;
        }

        Assert.Equal(1000, count);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncStreamReader_StreamsInputLargerThanMaxRowSize()
    {
        const string rowValue = "a,b,c";
        byte[] rowBytes = Encoding.UTF8.GetBytes(rowValue + "\n");
        const int rowCount = 50_000;
        long totalBytes = (long)rowBytes.Length * rowCount;
        var options = new CsvReadOptions { MaxRowSize = rowValue.Length };

        Assert.True(totalBytes > options.MaxRowSize);

        await using var reader = Csv.CreateAsyncStreamReader(
            new RepeatingCsvStream(rowBytes, totalBytes),
            options,
            leaveOpen: false,
            bufferSize: 64);
        var cancellationToken = TestContext.Current.CancellationToken;

        int count = 0;
        while (await reader.MoveNextAsync(cancellationToken))
        {
            count++;
        }

        Assert.Equal(rowCount, count);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncStreamReader_AllowsRowAtMaxRowSize()
    {
        var row = "12345678";
        var csv = $"{row}\nnext\n";
        var options = new CsvReadOptions { MaxRowSize = row.Length };
        await using var reader = CreateReader(csv, options, bufferSize: 4);
        var cancellationToken = TestContext.Current.CancellationToken;

        Assert.True(await reader.MoveNextAsync(cancellationToken));
        Assert.Equal(row, reader.Current.GetString(0));

        Assert.True(await reader.MoveNextAsync(cancellationToken));
        Assert.Equal("next", reader.Current.GetString(0));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncStreamReader_UsesBytesForMaxRowSize()
    {
        var csv = "\u20AC\n";
        var options = new CsvReadOptions { MaxRowSize = 2 };
        await using var reader = CreateReader(csv, options, bufferSize: 4);
        var cancellationToken = TestContext.Current.CancellationToken;

        var ex = await Assert.ThrowsAsync<CsvException>(async () =>
            await reader.MoveNextAsync(cancellationToken));
        Assert.Equal(CsvErrorCode.ParseError, ex.ErrorCode);
    }

    private static SeparatedValues.Reading.Streaming.CsvAsyncStreamReader CreateReader(
        string csv,
        CsvReadOptions? options = null,
        int bufferSize = 16 * 1024)
    {
        var bytes = Encoding.UTF8.GetBytes(csv);
        var stream = new MemoryStream(bytes);
        return Csv.CreateAsyncStreamReader(stream, options, leaveOpen: false, bufferSize: bufferSize);
    }

    private sealed class RepeatingCsvStream : Stream
    {
        private readonly byte[] pattern;
        private readonly long totalBytes;
        private long position;
        private int patternOffset;

        public RepeatingCsvStream(byte[] pattern, long totalBytes)
        {
            ArgumentNullException.ThrowIfNull(pattern);
            if (pattern.Length == 0)
                throw new ArgumentException("Pattern must not be empty.", nameof(pattern));
            if (totalBytes < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(totalBytes),
                    totalBytes,
                    "Total bytes must be non-negative.");
            }

            this.pattern = pattern;
            this.totalBytes = totalBytes;
        }

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

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer is null)
                throw new ArgumentNullException(nameof(buffer));

            return Read(buffer.AsSpan(offset, count));
        }

        public override int Read(Span<byte> buffer)
        {
            if (position >= totalBytes)
                return 0;

            long remaining = totalBytes - position;
            int toWrite = (int)Math.Min(remaining, buffer.Length);
            int written = 0;

            while (written < toWrite)
            {
                int available = Math.Min(pattern.Length - patternOffset, toWrite - written);
                pattern.AsSpan(patternOffset, available).CopyTo(buffer.Slice(written, available));
                written += available;
                patternOffset += available;
                if (patternOffset == pattern.Length)
                    patternOffset = 0;
            }

            position += written;
            return written;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return ValueTask.FromCanceled<int>(cancellationToken);

            try
            {
                int read = Read(buffer.Span);
                return ValueTask.FromResult(read);
            }
            catch (Exception ex)
            {
                return ValueTask.FromException<int>(ex);
            }
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<int>(cancellationToken);

            try
            {
                int read = Read(buffer.AsSpan(offset, count));
                return Task.FromResult(read);
            }
            catch (Exception ex)
            {
                return Task.FromException<int>(ex);
            }
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

