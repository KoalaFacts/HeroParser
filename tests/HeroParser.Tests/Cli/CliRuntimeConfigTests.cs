using System.Text.Json;
using Xunit;

namespace HeroParser.Tests.Cli;

/// <summary>
/// Guards the runtime switches the CLI ships with. The CLI prints exception messages to the
/// user, so a switch that strips the BCL's resource strings turns
/// "The file '…' already exists." into "IO_FileExists_Name, …".
/// </summary>
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed class CliRuntimeConfigTests
{
    [Fact]
    public void RuntimeConfig_KeepsSystemResourceMessages()
    {
        string path = Path.Join(AppContext.BaseDirectory, "HeroParser.Cli.runtimeconfig.json");
        Assert.True(File.Exists(path), $"Expected the CLI runtime config at {path}.");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        bool stripsMessages = document.RootElement.TryGetProperty("runtimeOptions", out JsonElement runtimeOptions)
            && runtimeOptions.TryGetProperty("configProperties", out JsonElement properties)
            && properties.TryGetProperty("System.Resources.UseSystemResourceKeys", out JsonElement value)
            && value.ValueKind == JsonValueKind.True;

        Assert.False(stripsMessages, "UseSystemResourceKeys replaces BCL error messages with resource keys in CLI output.");
    }
}
