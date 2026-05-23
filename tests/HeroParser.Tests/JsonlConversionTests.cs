using System.IO;
using System.Text;
using HeroParser.Conversion;
using Xunit;

namespace HeroParser.Tests;

public class JsonlConversionTests
{
    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvToJsonl_FlatObject_EmitsOneObjectPerRow()
    {
        string csv = "Name,Age\nAlice,30\nBob,25\n";

        string jsonl = CsvToJsonlConverter.Convert(csv, CsvToJsonlShape.FlatObject());

        string[] lines = jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"Name\":\"Alice\"", lines[0]);
        Assert.Contains("\"Age\":\"30\"", lines[0]);
        Assert.Contains("Bob", lines[1]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvToJsonl_OpenAiChat_EmitsMessagesArray()
    {
        string csv = "System,Question,Answer\n\"Be helpful.\",\"What is 2+2?\",\"4\"\n";

        string jsonl = CsvToJsonlConverter.Convert(
            csv,
            CsvToJsonlShape.OpenAiChat(systemColumn: "System", userColumn: "Question", assistantColumn: "Answer"));

        Assert.Contains("\"messages\":", jsonl);
        Assert.Contains("\"role\":\"system\"", jsonl);
        Assert.Contains("\"role\":\"user\"", jsonl);
        Assert.Contains("\"role\":\"assistant\"", jsonl);
        Assert.Contains("Be helpful.", jsonl);
        Assert.Contains("What is 2+2?", jsonl);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvToJsonl_OpenAiChat_WithoutSystemColumn_OmitsSystemRole()
    {
        string csv = "Question,Answer\nhi,hello\n";

        string jsonl = CsvToJsonlConverter.Convert(
            csv,
            CsvToJsonlShape.OpenAiChat(systemColumn: null, userColumn: "Question", assistantColumn: "Answer"));

        Assert.DoesNotContain("\"role\":\"system\"", jsonl);
        Assert.Contains("\"role\":\"user\"", jsonl);
        Assert.Contains("\"role\":\"assistant\"", jsonl);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvToJsonl_AnthropicMessages_EmitsUserAssistantOnly()
    {
        string csv = "Question,Answer\nhi,hello\n";

        string jsonl = CsvToJsonlConverter.Convert(
            csv,
            CsvToJsonlShape.AnthropicMessages(userColumn: "Question", assistantColumn: "Answer"));

        Assert.DoesNotContain("\"role\":\"system\"", jsonl);
        Assert.Contains("\"role\":\"user\"", jsonl);
        Assert.Contains("\"role\":\"assistant\"", jsonl);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void JsonlToCsv_FlatRecords_RoundTrip()
    {
        string jsonl = """
            {"Name":"Alice","Age":30}
            {"Name":"Bob","Age":25}
            """;

        string csv = JsonlToCsvConverter.Convert(jsonl);

        Assert.Contains("Name", csv);
        Assert.Contains("Alice", csv);
        Assert.Contains("Bob", csv);
        Assert.Contains("30", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void JsonlToCsv_NestedValuesAsJsonString()
    {
        string jsonl = """{"name":"Alice","tags":["a","b"]}""";

        string csv = JsonlToCsvConverter.Convert(jsonl);

        Assert.Contains("Alice", csv);
        // Nested JSON arrays are CSV-quoted because they contain a delimiter,
        // and embedded quotes are doubled per RFC 4180.
        Assert.Contains("\"[\"\"a\"\",\"\"b\"\"]\"", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void JsonlToCsv_NonSeekableStream_Sync_Succeeds()
    {
        string jsonl = """
            {"Name":"Alice","Age":30}
            {"Name":"Bob","Age":25}
            {"Name":"Charlie","Age":40}
            """;

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));
        using var nonSeekable = new NonSeekableStream(ms);
        using var output = new StringWriter();

        var options = new JsonlToCsvOptions { SchemaInferencePeekRows = 1 };
        JsonlToCsvConverter.Convert(nonSeekable, output, options);

        string csv = output.ToString();
        Assert.Contains("Name,Age", csv);
        Assert.Contains("Alice,30", csv);
        Assert.Contains("Bob,25", csv);
        Assert.Contains("Charlie,40", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task JsonlToCsv_NonSeekableStream_Async_Succeeds()
    {
        string jsonl = """
            {"Name":"Alice","Age":30}
            {"Name":"Bob","Age":25}
            {"Name":"Charlie","Age":40}
            """;

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));
        using var nonSeekable = new NonSeekableStream(ms);
        using var outMs = new MemoryStream();

        var options = new JsonlToCsvOptions { SchemaInferencePeekRows = 1 };
        await JsonlToCsvConverter.ConvertAsync(nonSeekable, outMs, options, TestContext.Current.CancellationToken);

        string csv = Encoding.UTF8.GetString(outMs.ToArray());
        Assert.Contains("Name,Age", csv);
        Assert.Contains("Alice,30", csv);
        Assert.Contains("Bob,25", csv);
        Assert.Contains("Charlie,40", csv);
    }

    private class NonSeekableStream : Stream
    {
        private readonly Stream inner;
        public NonSeekableStream(Stream inner)
        {
            this.inner = inner;
        }
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override void Write(ReadOnlySpan<byte> buffer) => inner.Write(buffer);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => inner.WriteAsync(buffer, offset, count, cancellationToken);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => inner.WriteAsync(buffer, cancellationToken);
    }
}
