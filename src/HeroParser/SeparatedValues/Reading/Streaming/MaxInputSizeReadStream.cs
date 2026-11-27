using HeroParser.SeparatedValues.Core;

namespace HeroParser.SeparatedValues.Reading.Streaming;

/// <summary>
/// Enforces <see cref="CsvReadOptions.MaxInputSize"/> over raw decompressed bytes read from an input stream
/// before any character transcoding or BOM stripping occurs.
/// </summary>
internal sealed class MaxInputSizeReadStream(Stream inner, CsvReadOptions options, bool leaveOpen = false) : Stream
{
    private readonly Stream inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly CsvReadOptions options = options ?? throw new ArgumentNullException(nameof(options));
    private long bytesRead;

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        int read = inner.Read(buffer, offset, count);
        RecordBytesRead(read);
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        int read = inner.Read(buffer);
        RecordBytesRead(read);
        return read;
    }

    public override int ReadByte()
    {
        int value = inner.ReadByte();
        if (value >= 0)
            RecordBytesRead(1);
        return value;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ValueTask<int> pending = inner.ReadAsync(buffer, cancellationToken);
        if (pending.IsCompletedSuccessfully)
        {
            int read = pending.Result;
            RecordBytesRead(read);
            return ValueTask.FromResult(read);
        }

        return AwaitAndRecordAsync(pending);
    }

    public override void Flush() { }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !leaveOpen)
            inner.Dispose();

        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync()
    {
        if (leaveOpen)
            return ValueTask.CompletedTask;

        return inner.DisposeAsync();
    }

    private async ValueTask<int> AwaitAndRecordAsync(ValueTask<int> pending)
    {
        int read = await pending.ConfigureAwait(false);
        RecordBytesRead(read);
        return read;
    }

    private void RecordBytesRead(int read)
    {
        if (read > 0)
        {
            bytesRead += read;
            options.ValidateInputSize(bytesRead);
        }
    }
}
