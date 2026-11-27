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

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void InferredSchema_PreservesLiteralDottedKeys_WhileExplicitColumnNavigatesPath()
    {
        const string jsonl = """{"a.b":7,"a":{"b":99}}""";

        using (var inferredStream = new MemoryStream(Encoding.UTF8.GetBytes(jsonl)))
        using (var inferredReader = Jsonl.CreateDataReader(inferredStream))
        {
            Assert.Equal("a.b", inferredReader.GetName(0));
            Assert.True(inferredReader.Read());
            Assert.Equal("7", inferredReader.GetString(0));
        }

        using var explicitStream = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));
        var explicitOptions = new JsonlDataReaderOptions
        {
            Columns = [new JsonlColumnDefinition("Nested", "a.b", typeof(int))]
        };
        using var explicitReader = Jsonl.CreateDataReader(explicitStream, readerOptions: explicitOptions);
        Assert.True(explicitReader.Read());
        Assert.Equal(99, explicitReader.GetInt32(0));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ReadAsync_PreCanceledAfterHasRows_ThrowsWithoutConsumingPeekedRow()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""{"id":1,"name":"first"}""" + "\n" + """{"id":2,"name":"second"}"""));
        await using var reader = Jsonl.CreateDataReader(stream);

        Assert.True(reader.HasRows);

        using var canceledCts = new CancellationTokenSource();
        canceledCts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await reader.ReadAsync(canceledCts.Token));

        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal("1", reader.GetString(0));
        Assert.Equal("first", reader.GetString(1));

        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal("2", reader.GetString(0));
        Assert.False(await reader.ReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ExplicitColumns_GetValueReturnsDeclaredClrTypes_AndRejectsUnsupportedTypesUpfront()
    {
        using (var badStream = new MemoryStream(Encoding.UTF8.GetBytes("""{"u":"https://example.com"}""")))
        {
            var badOptions = new JsonlDataReaderOptions
            {
                Columns = [new JsonlColumnDefinition("u", "u", typeof(Uri))]
            };
            Assert.Throws<ArgumentException>(() => Jsonl.CreateDataReader(badStream, readerOptions: badOptions));
        }

        string jsonl = """{"f":1.25,"s":12,"b":7,"c":"Z","d":"2026-11-27"}""";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));
        var options = new JsonlDataReaderOptions
        {
            Columns =
            [
                new JsonlColumnDefinition("f", "f", typeof(float)),
                new JsonlColumnDefinition("s", "s", typeof(short)),
                new JsonlColumnDefinition("b", "b", typeof(byte)),
                new JsonlColumnDefinition("c", "c", typeof(char)),
                new JsonlColumnDefinition("d", "d", typeof(DateOnly))
            ]
        };

        using var reader = Jsonl.CreateDataReader(stream, readerOptions: options);
        Assert.True(reader.Read());
        Assert.Equal(1.25f, Assert.IsType<float>(reader.GetValue(0)));
        Assert.Equal((short)12, Assert.IsType<short>(reader.GetValue(1)));
        Assert.Equal((byte)7, Assert.IsType<byte>(reader.GetValue(2)));
        Assert.Equal('Z', Assert.IsType<char>(reader.GetValue(3)));
        Assert.Equal(new DateOnly(2026, 11, 27), Assert.IsType<DateOnly>(reader.GetValue(4)));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ExtractionFailure_DisposesDocumentCleanlyOnSyncAndAsyncAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        byte[] payload = Encoding.UTF8.GetBytes("""{"s":"\uD800"}""" + "\n" + """{"s":"\uD800"}""");

        using (var syncStream = new MemoryStream(payload))
        using (var reader = Jsonl.CreateDataReader(syncStream))
        {
            Assert.ThrowsAny<Exception>(() => reader.Read());
            Assert.ThrowsAny<Exception>(() => reader.Read());
            reader.Close();
        }

        await using var asyncStream = new MemoryStream(payload);
        await using var asyncReader = Jsonl.CreateDataReader(asyncStream);
        await Assert.ThrowsAnyAsync<Exception>(() => asyncReader.ReadAsync(ct));
        await Assert.ThrowsAnyAsync<Exception>(() => asyncReader.ReadAsync(ct));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ExplicitBinaryColumn_SupportsGetBytesAndGetStream()
    {
        string jsonl = """{"payload":"AQIDBAUG","text":"hello"}""" + "\n" + """{"payload":null,"text":"world"}""";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));
        var options = new JsonlDataReaderOptions
        {
            Columns =
            [
                new JsonlColumnDefinition("payload", "payload", typeof(byte[])),
                new JsonlColumnDefinition("text", "text", typeof(string))
            ]
        };

        using var reader = Jsonl.CreateDataReader(stream, readerOptions: options);
        Assert.True(reader.Read());

        Assert.Equal(6L, reader.GetBytes(0, 0, null, 0, 0));

        byte[] buf = new byte[6];
        Assert.Equal(2L, reader.GetBytes(0, 0, buf, 0, 2));
        Assert.Equal(4L, reader.GetBytes(0, 2, buf, 2, 4));
        Assert.Equal([1, 2, 3, 4, 5, 6], buf);

        Assert.Equal(0L, reader.GetBytes(0, 6, buf, 0, 2));
        Assert.Equal(0L, reader.GetBytes(0, 0, buf, 0, 0));

        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetBytes(0, -1, buf, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetBytes(0, 0, buf, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetBytes(0, 0, buf, 0, -1));
        Assert.Throws<ArgumentException>(() => reader.GetBytes(0, 0, buf, 4, 4));

        using Stream binaryStream = reader.GetStream(0);
        using var copy = new MemoryStream();
        binaryStream.CopyTo(copy);
        Assert.Equal([1, 2, 3, 4, 5, 6], copy.ToArray());

        Assert.Throws<InvalidCastException>(() => reader.GetBytes(1, 0, null, 0, 0));

        Assert.True(reader.Read());
        Assert.True(reader.IsDBNull(0));
        Assert.Throws<InvalidCastException>(() => reader.GetBytes(0, 0, null, 0, 0));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ObjectColumn_PreservesExactNumbersWithoutSilentRounding()
    {
        string jsonl = string.Join(
            '\n',
            """{"v":42}""",
            """{"v":1.25}""",
            """{"v":79228162514264337593543950335}""",
            """{"v":100000000000000000000000000000000000001}""",
            """{"v":6e-29}""",
            """{"v":0.12345678901234567890123456789}""",
            """{"v":1e400}""",
            """{"v":1.000000000000000000000000000000000}""");

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));
        var options = new JsonlDataReaderOptions
        {
            Columns = [new JsonlColumnDefinition("v", "v", typeof(object))]
        };
        using var reader = Jsonl.CreateDataReader(stream, readerOptions: options);

        Assert.True(reader.Read());
        Assert.Equal(42L, Assert.IsType<long>(reader.GetValue(0)));

        Assert.True(reader.Read());
        Assert.Equal(1.25m, Assert.IsType<decimal>(reader.GetValue(0)));

        Assert.True(reader.Read());
        Assert.Equal(decimal.MaxValue, Assert.IsType<decimal>(reader.GetValue(0)));

        Assert.True(reader.Read());
        Assert.Equal("100000000000000000000000000000000000001", Assert.IsType<string>(reader.GetValue(0)));

        Assert.True(reader.Read());
        Assert.Equal("6e-29", Assert.IsType<string>(reader.GetValue(0)));

        Assert.True(reader.Read());
        Assert.Equal("0.12345678901234567890123456789", Assert.IsType<string>(reader.GetValue(0)));

        Assert.True(reader.Read());
        Assert.Equal("1e400", Assert.IsType<string>(reader.GetValue(0)));

        Assert.True(reader.Read());
        Assert.Equal(1m, Assert.IsType<decimal>(reader.GetValue(0)));

        Assert.False(reader.Read());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void NullableColumns_ReportUnderlyingTypesAndSupportDataTableLoad()
    {
        string jsonl = """{"id":1,"date":"2026-11-27","amount":12.5}""" + "\n"
            + """{"id":null,"date":null,"amount":null}""";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));
        var options = new JsonlDataReaderOptions
        {
            Columns =
            [
                new JsonlColumnDefinition("id", "id", typeof(int?)),
                new JsonlColumnDefinition("date", "date", typeof(DateOnly?)),
                new JsonlColumnDefinition("amount", "amount", typeof(decimal?))
            ]
        };

        using var reader = Jsonl.CreateDataReader(stream, readerOptions: options);

        Assert.Equal(typeof(int), reader.GetFieldType(0));
        Assert.Equal(typeof(DateOnly), reader.GetFieldType(1));
        Assert.Equal(typeof(decimal), reader.GetFieldType(2));
        Assert.Equal("Int32", reader.GetDataTypeName(0));
        Assert.Equal("DateOnly", reader.GetDataTypeName(1));
        Assert.Equal("Decimal", reader.GetDataTypeName(2));

        System.Data.DataTable schemaTable = reader.GetSchemaTable();
        Assert.Equal(typeof(int), schemaTable.Rows[0]["DataType"]);
        Assert.Equal(typeof(DateOnly), schemaTable.Rows[1]["DataType"]);
        Assert.Equal(typeof(decimal), schemaTable.Rows[2]["DataType"]);
        Assert.True((bool)schemaTable.Rows[0]["AllowDBNull"]);
        Assert.True((bool)schemaTable.Rows[1]["AllowDBNull"]);
        Assert.True((bool)schemaTable.Rows[2]["AllowDBNull"]);

        using var loaded = new System.Data.DataTable();
        loaded.Load(reader);
        Assert.Equal(2, loaded.Rows.Count);
        Assert.Equal(1, loaded.Rows[0]["id"]);
        Assert.Equal(new DateOnly(2026, 11, 27), loaded.Rows[0]["date"]);
        Assert.Equal(12.5m, loaded.Rows[0]["amount"]);
        Assert.True(loaded.Rows[1].IsNull("id"));
        Assert.True(loaded.Rows[1].IsNull("date"));
        Assert.True(loaded.Rows[1].IsNull("amount"));
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
