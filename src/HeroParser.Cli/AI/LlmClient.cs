using System;
using System.Threading;
using System.Threading.Tasks;

namespace HeroParser.Cli.AI;

internal enum LlmProvider
{
    Google,
    OpenAi,
    Anthropic,
    Microsoft,
    GitHub,
    Ollama
}

internal sealed class LlmClient
{
    /// <summary>Default model used when talking to Ollama and none was requested.</summary>
    private const string DEFAULT_OLLAMA_MODEL = "qwen3.5:latest";

    /// <summary>
    /// Appended unless the caller's prompt already pins the output shape, so a chatty
    /// agent does not wrap the structured answer in prose the caller has to strip.
    /// </summary>
    private const string STRUCTURED_OUTPUT_INSTRUCTION =
        "\n\nIMPORTANT: You must output ONLY the raw requested structured response. Do not include any conversational prefix, suffix, explanation, or chat formatting. Return the raw data directly.";

    private readonly LlmProvider provider;
    private readonly string? customModel;
    private readonly ILlmCliRunner runner;

    public LlmClient(LlmProvider provider, string? customModel = null, ILlmCliRunner? runner = null)
    {
        this.provider = provider;
        this.customModel = customModel;
        this.runner = runner ?? new ProcessLlmCliRunner();
    }

    public static LlmClient CreateFromEnvironment(
        string? overrideProvider = null,
        string? overrideKey = null,
        string? overrideModel = null,
        ILlmCliRunner? runner = null)
    {
        _ = overrideKey; // Retained for API compatibility but unused since we call local CLI processes directly

        if (!string.IsNullOrWhiteSpace(overrideProvider))
        {
            if (!TryParseProvider(overrideProvider, out var requested))
            {
                throw new ArgumentException($"Unknown AI provider: {overrideProvider}. Valid options: google, openai, anthropic, microsoft, github, ollama");
            }

            return new LlmClient(requested, overrideModel, runner);
        }

        return new LlmClient(DetectProvider(), overrideModel, runner);
    }

    /// <summary>
    /// Maps a provider name — or one of its common aliases — onto a <see cref="LlmProvider"/>.
    /// </summary>
    private static bool TryParseProvider(string name, out LlmProvider provider)
    {
        switch (name.ToLowerInvariant())
        {
            case "google" or "gemini" or "antigravity" or "agy": provider = LlmProvider.Google; return true;
            case "openai" or "chatgpt" or "codex": provider = LlmProvider.OpenAi; return true;
            case "anthropic" or "claude": provider = LlmProvider.Anthropic; return true;
            case "microsoft" or "copilot": provider = LlmProvider.Microsoft; return true;
            case "github": provider = LlmProvider.GitHub; return true;
            case "ollama": provider = LlmProvider.Ollama; return true;
            default: provider = LlmProvider.Google; return false;
        }
    }

    /// <summary>
    /// Picks a provider from the environment variable when set, otherwise from whichever
    /// agent CLI is actually installed.
    /// </summary>
    private static LlmProvider DetectProvider()
    {
        var envProvider = Environment.GetEnvironmentVariable("HEROPARSER_AI_PROVIDER");
        if (!string.IsNullOrWhiteSpace(envProvider) && TryParseProvider(envProvider, out var fromEnv))
        {
            return fromEnv;
        }

        // Auto detect by checking command availability in order: agy -> claude -> copilot -> codex -> openai -> ollama
        if (LocalCliLocator.IsCommandAvailable("agy")) return LlmProvider.Google;
        if (LocalCliLocator.IsCommandAvailable("claude")) return LlmProvider.Anthropic;
        if (LocalCliLocator.IsCommandAvailable("copilot")) return LlmProvider.Microsoft;
        if (LocalCliLocator.IsCommandAvailable("codex") || LocalCliLocator.IsCommandAvailable("openai")) return LlmProvider.OpenAi;
        if (LocalCliLocator.IsCommandAvailable("ollama")) return LlmProvider.Ollama;

        return LlmProvider.Google;
    }

    public async Task<string> AskAsync(string prompt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        (string cmd, string args) = ResolveCommandLine();

        // Standardize structuring instructions to ensure deterministic response shape from the local agent
        string structuredPrompt = prompt.Contains("Output ONLY", StringComparison.OrdinalIgnoreCase)
            ? prompt
            : prompt + STRUCTURED_OUTPUT_INSTRUCTION;

        string rawResponse = await runner.RunAsync(cmd, args, structuredPrompt, cancellationToken).ConfigureAwait(false);
        return ExtractStructuredContent(rawResponse);
    }

    /// <summary>
    /// Returns the command and arguments that drive this provider's CLI in headless mode.
    /// </summary>
    private (string Command, string Arguments) ResolveCommandLine()
    {
        string cmd;
        string args;

        switch (provider)
        {
            case LlmProvider.Google:
                cmd = "agy";
                args = "-p - --dangerously-skip-permissions";
                break;

            case LlmProvider.OpenAi:
                if (LocalCliLocator.IsCommandAvailable("openai") || LocalCliLocator.ResolveCommandPath("openai") != "openai")
                {
                    cmd = "openai";
                    args = "responses create --input -";
                }
                else
                {
                    cmd = "codex";
                    args = "exec - --ephemeral --skip-git-repo-check -a never -s read-only";
                }
                break;

            case LlmProvider.Anthropic:
                cmd = "claude";
                args = "-p - --permission-mode dontAsk --no-session-persistence";
                break;

            case LlmProvider.Microsoft:
            case LlmProvider.GitHub:
                cmd = "copilot";
                args = "-p - --allow-all -s";
                break;

            case LlmProvider.Ollama:
                cmd = "ollama";
                var ollamaModel = !string.IsNullOrWhiteSpace(customModel) ? customModel : DEFAULT_OLLAMA_MODEL;
                args = $"run {ollamaModel}";
                break;

            default:
                throw new NotSupportedException($"Provider {provider} has no local CLI mapping.");
        }

        // Ollama already carries the model as its run target, so a second flag would be rejected.
        if (!string.IsNullOrWhiteSpace(customModel) && provider != LlmProvider.Ollama)
        {
            args += $" --model \"{customModel}\"";
        }

        return (cmd, args);
    }

    /// <summary>
    /// Strips a fenced code block, which agents wrap structured output in even when told not to.
    /// </summary>
    private static string ExtractStructuredContent(string rawOutput)
    {
        if (string.IsNullOrWhiteSpace(rawOutput)) return string.Empty;

        string trimmed = rawOutput.Trim();

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            int firstNewLine = trimmed.IndexOf('\n');
            if (firstNewLine >= 0)
            {
                int lastBlock = trimmed.LastIndexOf("```", StringComparison.Ordinal);
                if (lastBlock > firstNewLine)
                {
                    return trimmed.Substring(firstNewLine + 1, lastBlock - firstNewLine - 1).Trim();
                }
            }
        }

        return trimmed;
    }
}
