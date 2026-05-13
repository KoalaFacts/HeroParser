using System.Text;
using HeroParser.JsonLines;
using HeroParser.JsonLines.Writing;
using Xunit;

namespace HeroParser.Tests;

public class JsonlWriterBuilderTests
{
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
    public void ToText_WritesFlatRecords()
    {
        var people = new List<Person>
        {
            new() { Name = "Alice", Age = 30 },
            new() { Name = "Bob", Age = 25 }
        };

        string jsonl = Jsonl.Write<Person>().ToText(people);

        Assert.Contains("\"Name\":\"Alice\"", jsonl);
        Assert.Contains("\"Age\":30", jsonl);
        Assert.Contains("\n", jsonl);
        Assert.Equal(2, jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void RoundTrip_NestedChatShape()
    {
        var examples = new List<ChatExample>
        {
            new() { Messages =
            [
                new() { Role = "user", Content = "hi" },
                new() { Role = "assistant", Content = "hello" }
            ] }
        };

        string jsonl = Jsonl.Write<ChatExample>().ToText(examples);
        using var reader = Jsonl.Read<ChatExample>().FromText(jsonl);
        var roundtrip = reader.ToList();

        Assert.Single(roundtrip);
        Assert.Equal(2, roundtrip[0].Messages!.Count);
        Assert.Equal("hi", roundtrip[0].Messages![0].Content);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WithFinalNewline_AddsTrailingNewline()
    {
        var people = new List<Person> { new() { Name = "Alice", Age = 30 } };

        string jsonl = Jsonl.Write<Person>().WithFinalNewline().ToText(people);
        Assert.EndsWith("\n", jsonl);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DefaultNoFinalNewline()
    {
        var people = new List<Person> { new() { Name = "Alice", Age = 30 } };

        string jsonl = Jsonl.Write<Person>().ToText(people);
        Assert.False(jsonl.EndsWith('\n'));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WithNewLine_UsesCrlf()
    {
        var people = new List<Person>
        {
            new() { Name = "Alice", Age = 30 },
            new() { Name = "Bob", Age = 25 }
        };

        string jsonl = Jsonl.Write<Person>().WithNewLine("\r\n").ToText(people);
        Assert.Contains("\r\n", jsonl);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WithMaxOutputSize_ThrowsWhenExceeded()
    {
        var people = Enumerable.Range(0, 100).Select(i => new Person { Name = $"P{i}", Age = i }).ToList();

        var ex = Assert.Throws<JsonlException>(() =>
            Jsonl.Write<Person>().WithMaxOutputSize(50).ToText(people));
        Assert.Equal(JsonlErrorCode.OutputSizeExceeded, ex.ErrorCode);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ToStream_WritesUtf8()
    {
        var people = new List<Person> { new() { Name = "Alice", Age = 30 } };
        using var stream = new MemoryStream();
        Jsonl.Write<Person>().ToStream(stream, people, leaveOpen: true);

        string text = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Contains("Alice", text);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void OnError_SkipRecord_ContinuesWriting()
    {
        var people = new List<Person>
        {
            new() { Name = "Alice", Age = 30 },
            new() { Name = "Bob", Age = 25 }
        };

        int errors = 0;
        string jsonl = Jsonl.Write<Person>()
            .OnError((_, _) => { errors++; return JsonlSerializeErrorAction.SkipRecord; })
            .ToText(people);

        Assert.Equal(0, errors); // healthy records produce no errors
        Assert.Contains("Alice", jsonl);
        Assert.Contains("Bob", jsonl);
    }
}
