using System.Text.Json;
using System.Text.Json.Serialization;

namespace HeroParser.Cli;

[JsonSerializable(typeof(JsonElement))]
internal sealed partial class CliJsonElementContext : JsonSerializerContext;
