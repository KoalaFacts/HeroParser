using HeroParser.AotTests.Models;
using HeroParser.JsonLines.Writing;

namespace HeroParser.AotTests.Tests;

/// <summary>
/// AOT compatibility tests for JSONL read/write. Uses a generated <c>JsonSerializerContext</c>
/// to prove the typed paths are trim/AOT-safe.
/// </summary>
public static class JsonlAotTests
{
    public static void Run(TestRunner runner)
    {
        runner.PrintSection("JSONL Tests");

        runner.Run("JSONL: Typed read with JsonTypeInfo", TypedRead);
        runner.Run("JSONL: Typed write with JsonTypeInfo", TypedWrite);
        runner.Run("JSONL: Nested round-trip", NestedRoundTrip);
    }

    private static void TypedRead()
    {
        string jsonl = "{\"Name\":\"Alice\",\"Age\":30}\n{\"Name\":\"Bob\",\"Age\":25}\n";
        var people = Jsonl.DeserializeRecords(jsonl, JsonlAotContext.Default.JsonlPerson).ToList();

        if (people.Count != 2)
            throw new Exception($"Expected 2, got {people.Count}");
        if (people[0].Name != "Alice" || people[1].Age != 25)
            throw new Exception("Decoded values mismatch");
    }

    private static void TypedWrite()
    {
        JsonlPerson[] records = [new() { Name = "Charlie", Age = 35 }];
        string jsonl = Jsonl.WriteToText(records, JsonlAotContext.Default.JsonlPerson);

        if (!jsonl.Contains("Charlie") || !jsonl.Contains("35"))
            throw new Exception($"Output missing fields: {jsonl}");
    }

    private static void NestedRoundTrip()
    {
        JsonlChatExample[] examples =
        [
            new()
            {
                Messages =
                [
                    new() { Role = "user", Content = "hi" },
                    new() { Role = "assistant", Content = "hello" }
                ]
            }
        ];

        string jsonl = Jsonl.WriteToText(examples, JsonlAotContext.Default.JsonlChatExample);
        var roundtrip = Jsonl.DeserializeRecords(jsonl, JsonlAotContext.Default.JsonlChatExample).ToList();

        if (roundtrip.Count != 1) throw new Exception($"Expected 1, got {roundtrip.Count}");
        if (roundtrip[0].Messages.Count != 2) throw new Exception("Lost messages");
        if (roundtrip[0].Messages[0].Content != "hi") throw new Exception("Content mismatch");
        // Force a use of JsonlWriterBuilder<T> AOT path
        _ = Jsonl.Write<JsonlPerson>().WithTypeInfo(JsonlAotContext.Default.JsonlPerson);
    }
}
