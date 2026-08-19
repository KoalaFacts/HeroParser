using System.Collections.Concurrent;
using HeroParser.Cli.AI;
using Xunit;

namespace HeroParser.Tests.Cli;

/// <summary>
/// Covers the provider policy in <see cref="LlmClient"/>: which local CLI each provider
/// maps to, how the model flag is attached, how the prompt is shaped, and how a fenced
/// response is unwrapped.
///
/// The client used to build the process itself, so none of this could be checked without
/// a real agent CLI installed. Injecting an <see cref="ILlmCliRunner"/> separates "which
/// command does this provider need" from "how is a child process run", and only the first
/// of those is policy worth pinning.
/// </summary>
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class LlmClientTests
{
    /// <summary>Records what the client asked for and replays a canned agent response.</summary>
    private sealed class FakeRunner(string response = "ok") : ILlmCliRunner
    {
        public string? Command { get; private set; }
        public string? Arguments { get; private set; }
        public string? Prompt { get; private set; }
        public CancellationToken Token { get; private set; }
        public int Calls { get; private set; }

        public Task<string> RunAsync(string commandName, string arguments, string prompt, CancellationToken cancellationToken)
        {
            Command = commandName;
            Arguments = arguments;
            Prompt = prompt;
            Token = cancellationToken;
            Calls++;
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Drives one ask through a fake runner. The provider is named rather than passed as
    /// the enum because xunit needs public signatures and LlmProvider is internal to the CLI.
    /// </summary>
    private static async Task<FakeRunner> AskAsync(string provider, string? model = null, string prompt = "hello")
    {
        var runner = new FakeRunner();
        await new LlmClient(Enum.Parse<LlmProvider>(provider), model, runner).AskAsync(prompt, TestContext.Current.CancellationToken);
        return runner;
    }

    [Theory]
    [InlineData(nameof(LlmProvider.Google), "agy", "-p - --dangerously-skip-permissions")]
    [InlineData(nameof(LlmProvider.Anthropic), "claude", "-p - --permission-mode dontAsk --no-session-persistence")]
    [InlineData(nameof(LlmProvider.Microsoft), "copilot", "-p - --allow-all -s")]
    [InlineData(nameof(LlmProvider.GitHub), "copilot", "-p - --allow-all -s")]
    public async Task Provider_MapsToItsLocalCliInvocation(string provider, string command, string arguments)
    {
        var runner = await AskAsync(provider);
        Assert.Equal(command, runner.Command);
        Assert.Equal(arguments, runner.Arguments);
    }

    [Fact]
    public async Task OpenAi_UsesWhicheverOfOpenAiOrCodexIsInstalled()
    {
        var runner = await AskAsync(nameof(LlmProvider.OpenAi));

        // Which one wins depends on what is on PATH, so pin the pairing rather than the choice.
        Assert.Contains(runner.Command, new[] { "openai", "codex" });
        Assert.Equal(
            runner.Command == "openai" ? "responses create --input -" : "exec - --ephemeral --skip-git-repo-check -a never -s read-only",
            runner.Arguments);
    }

    [Fact]
    public async Task Ollama_DefaultsToItsBundledModel()
        => Assert.Equal("run qwen3.5:latest", (await AskAsync(nameof(LlmProvider.Ollama))).Arguments);

    [Fact]
    public async Task Ollama_CustomModel_BecomesTheRunTarget()
    {
        // Ollama takes the model as its run target; a second --model flag would be rejected.
        var runner = await AskAsync(nameof(LlmProvider.Ollama), model: "llama4");
        Assert.Equal("run llama4", runner.Arguments);
        Assert.DoesNotContain("--model", runner.Arguments, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(LlmProvider.Google))]
    [InlineData(nameof(LlmProvider.Anthropic))]
    [InlineData(nameof(LlmProvider.Microsoft))]
    public async Task CustomModel_IsAppendedAsAFlag(string provider)
        => Assert.EndsWith(" --model \"my-model\"", (await AskAsync(provider, model: "my-model")).Arguments, StringComparison.Ordinal);

    [Fact]
    public async Task BlankModel_IsIgnored()
        => Assert.DoesNotContain("--model", (await AskAsync(nameof(LlmProvider.Google), model: "   ")).Arguments, StringComparison.Ordinal);

    [Fact]
    public async Task Prompt_GainsAStructuredOutputInstruction()
    {
        var runner = await AskAsync(nameof(LlmProvider.Google), prompt: "list the columns");
        Assert.StartsWith("list the columns", runner.Prompt, StringComparison.Ordinal);
        Assert.Contains("output ONLY the raw requested structured response", runner.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Prompt_ThatAlreadyPinsItsOutputShape_IsSentUnchanged()
    {
        const string prompt = "Do the thing. Output ONLY JSON.";
        Assert.Equal(prompt, (await AskAsync(nameof(LlmProvider.Google), prompt: prompt)).Prompt);
    }

    [Fact]
    public async Task CancellationToken_ReachesTheRunner()
    {
        using var cts = new CancellationTokenSource();
        var runner = new FakeRunner();
        await new LlmClient(LlmProvider.Google, runner: runner).AskAsync("p", cts.Token);
        Assert.Equal(cts.Token, runner.Token);
    }

    [Fact]
    public async Task NullPrompt_Throws()
        => await Assert.ThrowsAsync<ArgumentNullException>(
            () => new LlmClient(LlmProvider.Google, runner: new FakeRunner()).AskAsync(null!, TestContext.Current.CancellationToken));

    [Theory]
    [InlineData("```csharp\nclass X { }\n```", "class X { }")]
    [InlineData("```json\n{\"a\":1}\n```", "{\"a\":1}")]
    [InlineData("```\nbare fence\n```", "bare fence")]
    [InlineData("no fence here", "no fence here")]
    [InlineData("  padded  ", "padded")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("```unterminated", "```unterminated")]
    public async Task Response_HasItsCodeFenceStripped(string raw, string expected)
    {
        var runner = new FakeRunner(raw);
        string actual = await new LlmClient(LlmProvider.Google, runner: runner).AskAsync("p", TestContext.Current.CancellationToken);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Response_KeepsFencesThatAreNestedInsideTheBlock()
    {
        // Only the outermost fence is a wrapper; anything inside is part of the answer.
        var runner = new FakeRunner("```md\ntext\n```inner```\n```");
        string actual = await new LlmClient(LlmProvider.Google, runner: runner).AskAsync("p", TestContext.Current.CancellationToken);
        Assert.Contains("inner", actual, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("google", "agy")]
    [InlineData("gemini", "agy")]
    [InlineData("antigravity", "agy")]
    [InlineData("agy", "agy")]
    [InlineData("GOOGLE", "agy")]
    [InlineData("anthropic", "claude")]
    [InlineData("claude", "claude")]
    [InlineData("microsoft", "copilot")]
    [InlineData("copilot", "copilot")]
    [InlineData("github", "copilot")]
    [InlineData("ollama", "ollama")]
    public async Task CreateFromEnvironment_ResolvesProviderAliases(string alias, string expectedCommand)
    {
        var runner = new FakeRunner();
        await LlmClient.CreateFromEnvironment(alias, runner: runner).AskAsync("p", TestContext.Current.CancellationToken);
        Assert.Equal(expectedCommand, runner.Command);
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("chatgpt")]
    [InlineData("codex")]
    public async Task CreateFromEnvironment_ResolvesOpenAiAliases(string alias)
    {
        var runner = new FakeRunner();
        await LlmClient.CreateFromEnvironment(alias, runner: runner).AskAsync("p", TestContext.Current.CancellationToken);
        Assert.Contains(runner.Command, new[] { "openai", "codex" });
    }

    [Fact]
    public void CreateFromEnvironment_UnknownProvider_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => LlmClient.CreateFromEnvironment("notaprovider"));
        Assert.Contains("notaprovider", ex.Message, StringComparison.Ordinal);
        Assert.Contains("google, openai, anthropic, microsoft, github, ollama", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateFromEnvironment_ApiKeyIsAcceptedButUnused()
    {
        // Local agent CLIs authenticate themselves; the key is kept only for CLI compatibility.
        var runner = new FakeRunner();
        await LlmClient.CreateFromEnvironment("google", "sk-ignored", runner: runner).AskAsync("p", TestContext.Current.CancellationToken);
        Assert.Equal("agy", runner.Command);
    }

    [Fact]
    public async Task CreateFromEnvironment_ModelOverride_ReachesTheCommandLine()
    {
        var runner = new FakeRunner();
        await LlmClient.CreateFromEnvironment("anthropic", null, "opus", runner).AskAsync("p", TestContext.Current.CancellationToken);
        Assert.Contains("--model \"opus\"", runner.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateFromEnvironment_UsesTheProviderEnvironmentVariable()
    {
        const string variable = "HEROPARSER_AI_PROVIDER";
        string? previous = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, "ollama");
            var runner = new FakeRunner();
            await LlmClient.CreateFromEnvironment(runner: runner).AskAsync("p", TestContext.Current.CancellationToken);
            Assert.Equal("ollama", runner.Command);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    [Fact]
    public async Task CreateFromEnvironment_WithNoHints_StillProducesAUsableClient()
    {
        // Nothing configured: the client falls back to whichever agent CLI is installed,
        // so assert it resolves to one of the known commands rather than a specific one.
        var runner = new FakeRunner();
        await LlmClient.CreateFromEnvironment(runner: runner).AskAsync("p", TestContext.Current.CancellationToken);
        Assert.Contains(runner.Command, new[] { "agy", "claude", "copilot", "codex", "openai", "ollama" });
    }

    [Fact]
    public async Task RunnerIsCalledExactlyOncePerAsk()
    {
        var runner = new FakeRunner();
        var client = new LlmClient(LlmProvider.Google, runner: runner);
        await client.AskAsync("a", TestContext.Current.CancellationToken);
        await client.AskAsync("b", TestContext.Current.CancellationToken);
        Assert.Equal(2, runner.Calls);
    }

    [Fact]
    public async Task RunnerFailure_PropagatesToTheCaller()
    {
        var failing = new ThrowingRunner();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new LlmClient(LlmProvider.Google, runner: failing).AskAsync("p", TestContext.Current.CancellationToken));
    }

    private sealed class ThrowingRunner : ILlmCliRunner
    {
        public Task<string> RunAsync(string commandName, string arguments, string prompt, CancellationToken cancellationToken)
            => throw new InvalidOperationException($"Local AI CLI '{commandName}' exited with code 1.");
    }
}
