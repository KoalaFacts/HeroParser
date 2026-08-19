using System.Text;
using System.Text.Json;
using HeroParser.Conversion;
using HeroParser.SeparatedValues.Core;
using Xunit;

namespace HeroParser.Tests.Conversion;

/// <summary>
/// Covers <see cref="CsvToJsonlConverter"/> across all three output shapes and all
/// three entry points (string, file, stream).
///
/// Only the flat-object string path had been exercised, so the chat shapes — the ones
/// that exist to produce fine-tuning corpora, where a malformed line silently poisons
/// a training run — were entirely untested, as were the file and stream overloads.
/// </summary>
[Trait("Category", "Unit")]
public class CsvToJsonlConverterTests
{
    private static string[] Lines(string jsonl) =>
        jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Every emitted line must stand alone as valid JSON — that is the format's whole contract.</summary>
    private static JsonDocument ParseLine(string jsonl, int index)
    {
        string[] lines = Lines(jsonl);
        Assert.InRange(index, 0, lines.Length - 1);
        return JsonDocument.Parse(lines[index]);
    }

    [Fact]
    public void FlatObject_EmitsOneObjectPerRow()
    {
        const string csv = "name,age\nalice,30\nbob,41\n";
        string jsonl = CsvToJsonlConverter.Convert(csv, CsvToJsonlShape.FlatObject());

        Assert.Equal(2, Lines(jsonl).Length);
        using var first = ParseLine(jsonl, 0);
        Assert.Equal("alice", first.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public void FlatObject_QuotedFieldWithDelimiterAndQuotes_RoundTrips()
    {
        const string csv = "note\n\"has, comma and \"\"quotes\"\"\"\n";
        string jsonl = CsvToJsonlConverter.Convert(csv, CsvToJsonlShape.FlatObject());

        using var doc = ParseLine(jsonl, 0);
        Assert.Equal("has, comma and \"quotes\"", doc.RootElement.GetProperty("note").GetString());
    }

    [Fact]
    public void FlatObject_NewlineInsideQuotedField_ThrowsRatherThanSplittingTheRecord()
    {
        // RFC 4180 permits a newline inside a quoted field, but the parser requires
        // AllowNewlinesInsideQuotes to be opted into and CsvToJsonlOptions exposes no
        // way to set it, so such a file cannot be converted at all. Refusing is at
        // least safe — silently splitting would corrupt one record into two.
        const string csv = "note\n\"line1\nline2\"\n";

        var ex = Assert.Throws<CsvException>(
            () => CsvToJsonlConverter.Convert(csv, CsvToJsonlShape.FlatObject()));
        Assert.Contains("AllowNewlinesInsideQuotes", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FlatObject_HeaderOnly_ProducesNoRows()
        => Assert.Empty(Lines(CsvToJsonlConverter.Convert("a,b\n", CsvToJsonlShape.FlatObject())));

    [Fact]
    public void FlatObject_CustomDelimiter_IsHonoured()
    {
        string jsonl = CsvToJsonlConverter.Convert(
            "a;b\n1;2\n",
            CsvToJsonlShape.FlatObject(),
            new CsvToJsonlOptions { Delimiter = ';' });

        using var doc = ParseLine(jsonl, 0);
        Assert.Equal("1", doc.RootElement.GetProperty("a").GetString());
    }

    [Fact]
    public void FlatObject_CustomNewLine_SeparatesRecords()
    {
        string jsonl = CsvToJsonlConverter.Convert(
            "a\n1\n2\n",
            CsvToJsonlShape.FlatObject(),
            new CsvToJsonlOptions { NewLine = "\r\n" });

        Assert.Contains("\r\n", jsonl, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenAiChat_WithSystemColumn_EmitsThreeRoles()
    {
        const string csv = "sys,q,a\nbe terse,hello,hi\n";
        string jsonl = CsvToJsonlConverter.Convert(csv, CsvToJsonlShape.OpenAiChat("sys", "q", "a"));

        using var doc = ParseLine(jsonl, 0);
        var messages = doc.RootElement.GetProperty("messages");
        Assert.Equal(3, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("be terse", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("assistant", messages[2].GetProperty("role").GetString());
        Assert.Equal("hi", messages[2].GetProperty("content").GetString());
    }

    [Fact]
    public void OpenAiChat_WithoutSystemColumn_EmitsTwoRoles()
    {
        string jsonl = CsvToJsonlConverter.Convert(
            "q,a\nhello,hi\n",
            CsvToJsonlShape.OpenAiChat(null, "q", "a"));

        using var doc = ParseLine(jsonl, 0);
        var messages = doc.RootElement.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("user", messages[0].GetProperty("role").GetString());
    }

    [Fact]
    public void AnthropicMessages_EmitsUserAndAssistantOnly()
    {
        string jsonl = CsvToJsonlConverter.Convert(
            "q,a\nhello,hi\n",
            CsvToJsonlShape.AnthropicMessages("q", "a"));

        using var doc = ParseLine(jsonl, 0);
        var messages = doc.RootElement.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("user", messages[0].GetProperty("role").GetString());
        Assert.Equal("assistant", messages[1].GetProperty("role").GetString());
        // The Anthropic shape carries no system role at all.
        Assert.False(doc.RootElement.TryGetProperty("system", out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void OpenAiChat_BlankRequiredColumn_Throws(string? blank)
    {
        Assert.ThrowsAny<ArgumentException>(() => CsvToJsonlShape.OpenAiChat("s", blank!, "a"));
        Assert.ThrowsAny<ArgumentException>(() => CsvToJsonlShape.OpenAiChat("s", "u", blank!));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void AnthropicMessages_BlankRequiredColumn_Throws(string? blank)
    {
        Assert.ThrowsAny<ArgumentException>(() => CsvToJsonlShape.AnthropicMessages(blank!, "a"));
        Assert.ThrowsAny<ArgumentException>(() => CsvToJsonlShape.AnthropicMessages("u", blank!));
    }

    [Fact]
    public void Convert_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => CsvToJsonlConverter.Convert(null!, CsvToJsonlShape.FlatObject()));
        Assert.Throws<ArgumentNullException>(() => CsvToJsonlConverter.Convert("a\n1\n", null!));
    }

    [Fact]
    public async Task ConvertAsync_NullStreams_Throw()
    {
        var ct = TestContext.Current.CancellationToken;
        using var ms = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await CsvToJsonlConverter.ConvertAsync(null!, ms, CsvToJsonlShape.FlatObject(), cancellationToken: ct));
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await CsvToJsonlConverter.ConvertAsync(ms, null!, CsvToJsonlShape.FlatObject(), cancellationToken: ct));
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await CsvToJsonlConverter.ConvertAsync(ms, ms, null!, cancellationToken: ct));
    }

    [Fact]
    public async Task ConvertAsync_StreamToStream_WritesJsonl()
    {
        var ct = TestContext.Current.CancellationToken;
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("a,b\n1,2\n3,4\n"));
        using var output = new MemoryStream();

        await CsvToJsonlConverter.ConvertAsync(input, output, CsvToJsonlShape.FlatObject(), cancellationToken: ct);

        string jsonl = Encoding.UTF8.GetString(output.ToArray());
        Assert.Equal(2, Lines(jsonl).Length);
        using var second = ParseLine(jsonl, 1);
        Assert.Equal("3", second.RootElement.GetProperty("a").GetString());
    }

    [Fact]
    public async Task ConvertAsync_LargeInput_CrossesBufferBoundaries()
    {
        var ct = TestContext.Current.CancellationToken;
        var csv = new StringBuilder("id,payload\n");
        for (int i = 0; i < 500; i++) csv.Append(i).Append(',').Append(new string('p', 200)).Append('\n');

        using var input = new MemoryStream(Encoding.UTF8.GetBytes(csv.ToString()));
        using var output = new MemoryStream();
        await CsvToJsonlConverter.ConvertAsync(input, output, CsvToJsonlShape.FlatObject(), cancellationToken: ct);

        Assert.Equal(500, Lines(Encoding.UTF8.GetString(output.ToArray())).Length);
    }

    [Fact]
    public void Convert_FileToFile_WritesJsonlFile()
    {
        string csvPath = Path.Combine(Path.GetTempPath(), $"hero-c2j-{Guid.NewGuid():N}.csv");
        string jsonlPath = Path.ChangeExtension(csvPath, ".jsonl");
        try
        {
            File.WriteAllText(csvPath, "a,b\n1,2\n");
            CsvToJsonlConverter.Convert(csvPath, jsonlPath, CsvToJsonlShape.FlatObject());

            string jsonl = File.ReadAllText(jsonlPath);
            using var doc = ParseLine(jsonl, 0);
            Assert.Equal("2", doc.RootElement.GetProperty("b").GetString());
        }
        finally
        {
            if (File.Exists(csvPath)) File.Delete(csvPath);
            if (File.Exists(jsonlPath)) File.Delete(jsonlPath);
        }
    }

    [Fact]
    public void Convert_FileOverload_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => CsvToJsonlConverter.Convert(null!, "o.jsonl", CsvToJsonlShape.FlatObject()));
        Assert.Throws<ArgumentNullException>(() => CsvToJsonlConverter.Convert("i.csv", null!, CsvToJsonlShape.FlatObject()));
        Assert.Throws<ArgumentNullException>(() => CsvToJsonlConverter.Convert("i.csv", "o.jsonl", null!));
    }

    [Fact]
    public void Shapes_AreValueEqual()
    {
        // Records, so two shapes describing the same projection must compare equal.
        Assert.Equal(CsvToJsonlShape.FlatObject(), CsvToJsonlShape.FlatObject());
        Assert.Equal(CsvToJsonlShape.OpenAiChat("s", "u", "a"), CsvToJsonlShape.OpenAiChat("s", "u", "a"));
        Assert.NotEqual(CsvToJsonlShape.OpenAiChat(null, "u", "a"), CsvToJsonlShape.OpenAiChat("s", "u", "a"));
        Assert.NotEqual<CsvToJsonlShape>(CsvToJsonlShape.FlatObject(), CsvToJsonlShape.AnthropicMessages("u", "a"));
    }

    // The async path carries its own copy of the shape switch and emitters, so the
    // chat shapes must be driven through ConvertAsync as well as Convert to reach it.
    private static async Task<string> ConvertViaStreamAsync(string csv, CsvToJsonlShape shape, CsvToJsonlOptions? options = null)
    {
        var ct = TestContext.Current.CancellationToken;
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        using var output = new MemoryStream();
        await CsvToJsonlConverter.ConvertAsync(input, output, shape, options, ct);
        return Encoding.UTF8.GetString(output.ToArray());
    }

    [Fact]
    public async Task ConvertAsync_OpenAiChat_EmitsThreeRoles()
    {
        string jsonl = await ConvertViaStreamAsync("sys,q,a\nbe terse,hello,hi\n", CsvToJsonlShape.OpenAiChat("sys", "q", "a"));
        using var doc = ParseLine(jsonl, 0);
        var messages = doc.RootElement.GetProperty("messages");
        Assert.Equal(3, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
    }

    [Fact]
    public async Task ConvertAsync_OpenAiChat_WithoutSystemColumn_EmitsTwoRoles()
    {
        string jsonl = await ConvertViaStreamAsync("q,a\nhello,hi\n", CsvToJsonlShape.OpenAiChat(null, "q", "a"));
        using var doc = ParseLine(jsonl, 0);
        Assert.Equal(2, doc.RootElement.GetProperty("messages").GetArrayLength());
    }

    [Fact]
    public async Task ConvertAsync_AnthropicMessages_EmitsUserAndAssistant()
    {
        string jsonl = await ConvertViaStreamAsync("q,a\nhello,hi\n", CsvToJsonlShape.AnthropicMessages("q", "a"));
        using var doc = ParseLine(jsonl, 0);
        var messages = doc.RootElement.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("assistant", messages[1].GetProperty("role").GetString());
    }

    [Fact]
    public void FlatObject_WithoutHeaderRow_NamesColumnsPositionally()
    {
        // With no header the emitter synthesises column1..columnN.
        string jsonl = CsvToJsonlConverter.Convert(
            "x,y\n",
            CsvToJsonlShape.FlatObject(),
            new CsvToJsonlOptions { HasHeaderRow = false });

        using var doc = ParseLine(jsonl, 0);
        Assert.Equal("x", doc.RootElement.GetProperty("column1").GetString());
        Assert.Equal("y", doc.RootElement.GetProperty("column2").GetString());
    }

    [Fact]
    public async Task ConvertAsync_FlatObject_WithoutHeaderRow_NamesColumnsPositionally()
    {
        string jsonl = await ConvertViaStreamAsync(
            "x,y\n",
            CsvToJsonlShape.FlatObject(),
            new CsvToJsonlOptions { HasHeaderRow = false });

        using var doc = ParseLine(jsonl, 0);
        Assert.Equal("y", doc.RootElement.GetProperty("column2").GetString());
    }

    [Fact]
    public void FlatObject_WithoutHeaderRow_ManyColumns_StillNamesThemAll()
    {
        // Column names are formatted into a 32-byte stack buffer; a wide row exercises
        // the fallback for indices that will not fit.
        string row = string.Join(',', Enumerable.Range(0, 60).Select(i => $"v{i}"));
        string jsonl = CsvToJsonlConverter.Convert(
            row + "\n",
            CsvToJsonlShape.FlatObject(),
            new CsvToJsonlOptions { HasHeaderRow = false });

        using var doc = ParseLine(jsonl, 0);
        Assert.Equal("v59", doc.RootElement.GetProperty("column60").GetString());
    }

    [Fact]
    public void ChatShape_WithoutHeaderRow_Throws()
    {
        // The chat shapes address columns by name, so they cannot work headerless.
        var ex = Assert.Throws<InvalidOperationException>(() => CsvToJsonlConverter.Convert(
            "hello,hi\n",
            CsvToJsonlShape.OpenAiChat(null, "q", "a"),
            new CsvToJsonlOptions { HasHeaderRow = false }));
        Assert.Contains("HasHeaderRow", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConvertAsync_ChatShape_WithoutHeaderRow_Throws()
        => await Assert.ThrowsAsync<InvalidOperationException>(async () => await ConvertViaStreamAsync(
            "hello,hi\n",
            CsvToJsonlShape.AnthropicMessages("q", "a"),
            new CsvToJsonlOptions { HasHeaderRow = false }));

    [Fact]
    public void ChatShape_MissingNamedColumn_Throws()
        => Assert.ThrowsAny<Exception>(() => CsvToJsonlConverter.Convert(
            "q,other\nhello,hi\n",
            CsvToJsonlShape.OpenAiChat(null, "q", "absent")));
}
