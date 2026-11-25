using System.Text;
using HeroParser.JsonLines.Reading.Data;
using Xunit;

namespace HeroParser.Tests;

public class JsonlDataReaderTests
{
    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void InfersSchemaFromFirstLine()
    {
        string jsonl = """
            {"name":"Alice","age":30}
            {"name":"Bob","age":25}
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));
        using var reader = Jsonl.CreateDataReader(stream);

        Assert.True(reader.Read());
        Assert.Equal(2, reader.FieldCount);
        Assert.Equal("name", reader.GetName(0));
        Assert.Equal("Alice", reader.GetString(0));
        Assert.Equal("30", reader.GetString(1));

        Assert.True(reader.Read());
        Assert.Equal("Bob", reader.GetString(0));
        Assert.False(reader.Read());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ExplicitColumns_WithJsonPath_ExtractsNestedValue()
    {
        string jsonl = """{"messages":[{"role":"user","content":"hi"},{"role":"assistant","content":"hello"}]}""";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));
        var readerOptions = new JsonlDataReaderOptions
        {
            Columns =
            [
                new JsonlColumnDefinition("UserMessage", "messages[0].content", typeof(string)),
                new JsonlColumnDefinition("AssistantMessage", "messages[1].content", typeof(string)),
            ]
        };

        using var reader = Jsonl.CreateDataReader(stream, readerOptions: readerOptions);
        Assert.True(reader.Read());
        Assert.Equal("hi", reader.GetString(0));
        Assert.Equal("hello", reader.GetString(1));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void MissingKey_SurfacesAsDBNull()
    {
        string jsonl = """{"a":1}""";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));
        var readerOptions = new JsonlDataReaderOptions
        {
            Columns =
            [
                new JsonlColumnDefinition("a", "a", typeof(int)),
                new JsonlColumnDefinition("b", "b", typeof(int)),
            ]
        };

        using var reader = Jsonl.CreateDataReader(stream, readerOptions: readerOptions);
        Assert.True(reader.Read());
        Assert.False(reader.IsDBNull(0));
        Assert.True(reader.IsDBNull(1));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void EmptyAndWhitespaceStreams_HaveHasRowsFalse()
    {
        using var emptyStream = new MemoryStream([]);
        using var emptyReader = Jsonl.CreateDataReader(emptyStream);
        Assert.False(emptyReader.HasRows);
        Assert.Equal(0, emptyReader.FieldCount);
        Assert.False(emptyReader.Read());

        using var wsStream = new MemoryStream(Encoding.UTF8.GetBytes("   \n\t\r\n"));
        using var wsReader = Jsonl.CreateDataReader(wsStream);
        Assert.False(wsReader.HasRows);
        Assert.False(wsReader.Read());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void AllRowsSkipped_HasRowsIsFalse()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"a\":1}\n{\"a\":2}\n"));
        using var reader = Jsonl.CreateDataReader(stream, readerOptions: new JsonlDataReaderOptions { SkipRows = 2 });
        Assert.False(reader.HasRows);
        Assert.False(reader.Read());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FieldCountAndHasRows_WorkBeforeRead_AndPreserveFirstRow()
    {
        string jsonl = """
            {"name":"Alice","age":30}
            {"name":"Bob","age":25}
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));
        using var reader = Jsonl.CreateDataReader(stream);

        Assert.True(reader.HasRows);
        Assert.Equal(2, reader.FieldCount);
        Assert.Equal("name", reader.GetName(0));
        Assert.Equal(1, reader.GetOrdinal("age"));
        Assert.Throws<InvalidOperationException>(() => reader.GetString(0));

        Assert.True(reader.Read());
        Assert.Equal("Alice", reader.GetString(0));
        Assert.Equal("30", reader.GetString(1));

        Assert.True(reader.Read());
        Assert.Equal("Bob", reader.GetString(0));
        Assert.False(reader.Read());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ReadAsync_UsesAsyncStreamWithoutCallingSyncRead()
    {
        string jsonl = """
            {"id":1,"name":"Alpha"}
            {"id":2,"name":"Beta"}
            """;

        await using var stream = new AsyncOnlyMemoryStream(Encoding.UTF8.GetBytes(jsonl));
        using var reader = Jsonl.CreateDataReader(stream);

        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.True(reader.HasRows);
        Assert.Equal("1", reader.GetString(0));
        Assert.Equal("Alpha", reader.GetString(1));

        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal("2", reader.GetString(0));
        Assert.Equal("Beta", reader.GetString(1));

        Assert.False(await reader.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ClosedReader_AndInvalidOrdinal_ThrowExpectedExceptions()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""{"a":1}"""));
        var reader = Jsonl.CreateDataReader(stream);

        Assert.True(reader.Read());
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetValue(-1));
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetValue(5));
        Assert.Throws<IndexOutOfRangeException>(() => reader.GetName(5));

        reader.Close();
        Assert.Throws<InvalidOperationException>(() => reader.Read());
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Throws<InvalidOperationException>(() => _ = reader.HasRows);
        Assert.Throws<InvalidOperationException>(() => _ = reader.FieldCount);
        Assert.Throws<InvalidOperationException>(() => reader.GetValue(0));
    }

    private sealed class AsyncOnlyMemoryStream(byte[] data) : Stream
    {
        private int position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Sync Read is disabled.");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int remaining = data.Length - position;
            int toCopy = Math.Min(remaining, Math.Min(buffer.Length, 16));
            if (toCopy <= 0)
                return ValueTask.FromResult(0);
            data.AsSpan(position, toCopy).CopyTo(buffer.Span);
            position += toCopy;
            return ValueTask.FromResult(toCopy);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }
}
