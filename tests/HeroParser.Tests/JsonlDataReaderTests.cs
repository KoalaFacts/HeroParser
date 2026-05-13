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
}
