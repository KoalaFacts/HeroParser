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
}
