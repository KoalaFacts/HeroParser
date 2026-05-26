using System.Text;
using HeroParser.JsonLines;
using HeroParser.JsonLines.Reading;
using Xunit;

namespace HeroParser.Tests;

public class JsonlReaderBuilderTests
{
    [GenerateBinder]
    public class Person
    {
        public string? Name { get; set; }
        public int Age { get; set; }
    }

    public class ChatMessage
    {
        public string? Role { get; set; }
        public string? Content { get; set; }
    }

    public class ChatExample
    {
        public List<ChatMessage>? Messages { get; set; }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FromText_ReadsFlatRecords()
    {
        string jsonl = """
            {"Name":"Alice","Age":30}
            {"Name":"Bob","Age":25}
            """;

        using var reader = Jsonl.Read<Person>().FromText(jsonl);
        var people = reader.ToList();

        Assert.Equal(2, people.Count);
        Assert.Equal("Alice", people[0].Name);
        Assert.Equal(30, people[0].Age);
        Assert.Equal("Bob", people[1].Name);
        Assert.Equal(25, people[1].Age);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FromText_ReadsNestedChatShape()
    {
        string jsonl = """{"Messages":[{"Role":"user","Content":"hi"},{"Role":"assistant","Content":"hello"}]}""";

        using var reader = Jsonl.Read<ChatExample>().FromText(jsonl);
        var examples = reader.ToList();

        Assert.Single(examples);
        Assert.Equal(2, examples[0].Messages!.Count);
        Assert.Equal("user", examples[0].Messages![0].Role);
        Assert.Equal("hello", examples[0].Messages![1].Content);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FromText_HandlesCrlfLineEndings()
    {
        string jsonl = "{\"Name\":\"Alice\",\"Age\":30}\r\n{\"Name\":\"Bob\",\"Age\":25}\r\n";

        using var reader = Jsonl.Read<Person>().FromText(jsonl);
        var people = reader.ToList();
        Assert.Equal(2, people.Count);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FromStream_StripsBom()
    {
        byte[] bom = [0xEF, 0xBB, 0xBF];
        byte[] body = Encoding.UTF8.GetBytes("""{"Name":"Alice","Age":30}""");
        byte[] combined = [.. bom, .. body];
        using var stream = new MemoryStream(combined);

        using var reader = Jsonl.Read<Person>().FromStream(stream, leaveOpen: false);
        var people = reader.ToList();

        Assert.Single(people);
        Assert.Equal("Alice", people[0].Name);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void SkipEmptyLines_DefaultsToTrue()
    {
        string jsonl = """
            {"Name":"Alice","Age":30}

            {"Name":"Bob","Age":25}

            """;
        using var reader = Jsonl.Read<Person>().FromText(jsonl);
        var people = reader.ToList();
        Assert.Equal(2, people.Count);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void OnError_SkipRecord_SwallowsBadLine()
    {
        string jsonl = """
            {"Name":"Alice","Age":30}
            this is not json
            {"Name":"Bob","Age":25}
            """;

        int errorCount = 0;
        using var reader = Jsonl.Read<Person>()
            .OnError((_, _) => { errorCount++; return JsonlDeserializeErrorAction.SkipRecord; })
            .FromText(jsonl);

        var people = reader.ToList();
        Assert.Equal(2, people.Count);
        Assert.Equal(1, errorCount);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void OnError_Throw_RaisesJsonlException()
    {
        string jsonl = """
            {"Name":"Alice","Age":30}
            this is not json
            """;

        using var reader = Jsonl.Read<Person>()
            .OnError((_, _) => JsonlDeserializeErrorAction.Throw)
            .FromText(jsonl);

        Assert.Throws<JsonlException>(() => reader.ToList());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WithMaxLineSize_RejectsOversizedLine()
    {
        string jsonl = """{"Name":"Alice","Age":30}""";

        using var reader = Jsonl.Read<Person>()
            .WithMaxLineSize(5)
            .FromText(jsonl);

        var ex = Assert.Throws<JsonlException>(() => reader.ToList());
        Assert.Equal(JsonlErrorCode.LineTooLong, ex.ErrorCode);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void SkipRows_SkipsLeadingRecords()
    {
        string jsonl = """
            {"Name":"Alice","Age":30}
            {"Name":"Bob","Age":25}
            {"Name":"Carol","Age":40}
            """;

        using var reader = Jsonl.Read<Person>().SkipRows(2).FromText(jsonl);
        var people = reader.ToList();
        Assert.Single(people);
        Assert.Equal("Carol", people[0].Name);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void EmptyInput_YieldsNoRecords()
    {
        using var reader = Jsonl.Read<Person>().FromText(string.Empty);
        Assert.Empty(reader.ToList());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void NoTrailingNewline_StillReadsLastRecord()
    {
        string jsonl = "{\"Name\":\"Alice\",\"Age\":30}\n{\"Name\":\"Bob\",\"Age\":25}";

        using var reader = Jsonl.Read<Person>().FromText(jsonl);
        Assert.Equal(2, reader.ToList().Count);
    }
}
