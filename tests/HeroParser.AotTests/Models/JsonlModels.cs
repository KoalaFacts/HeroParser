using System.Text.Json.Serialization;

namespace HeroParser.AotTests.Models;

public class JsonlPerson
{
    public string Name { get; set; } = "";
    public int Age { get; set; }
}

public class JsonlChatMessage
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
}

public class JsonlChatExample
{
    public List<JsonlChatMessage> Messages { get; set; } = [];
}

[JsonSerializable(typeof(JsonlPerson))]
[JsonSerializable(typeof(JsonlChatExample))]
[JsonSerializable(typeof(JsonlChatMessage))]
internal partial class JsonlAotContext : JsonSerializerContext
{
}
