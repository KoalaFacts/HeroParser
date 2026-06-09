using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace HeroParser.Cli.AI;

internal enum LlmProvider
{
    Gemini,
    OpenAi,
    Anthropic
}

internal sealed class LlmClient
{
    private readonly HttpClient httpClient;
    private readonly LlmProvider provider;
    private readonly string apiKey;
    private readonly string? customModel;

    public LlmClient(LlmProvider provider, string apiKey, string? customModel = null)
    {
        this.provider = provider;
        this.apiKey = apiKey;
        this.customModel = customModel;
        httpClient = new HttpClient();
    }

    public static LlmClient CreateFromEnvironment(string? overrideProvider = null, string? overrideKey = null, string? overrideModel = null)
    {
        LlmProvider resolvedProvider = LlmProvider.Gemini;

        if (!string.IsNullOrWhiteSpace(overrideProvider))
        {
            resolvedProvider = overrideProvider.ToLowerInvariant() switch
            {
                "gemini" => LlmProvider.Gemini,
                "openai" => LlmProvider.OpenAi,
                "anthropic" => LlmProvider.Anthropic,
                _ => throw new ArgumentException($"Unknown AI provider: {overrideProvider}. Valid options: gemini, openai, anthropic")
            };
        }
        else
        {
            // Auto detect from environment keys
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY")))
                resolvedProvider = LlmProvider.Gemini;
            else if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY")))
                resolvedProvider = LlmProvider.OpenAi;
            else if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")) ||
                     !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLAUDE_API_KEY")))
                resolvedProvider = LlmProvider.Anthropic;
        }

        string? resolvedKey = !string.IsNullOrWhiteSpace(overrideKey)
            ? overrideKey
            : resolvedProvider switch
            {
                LlmProvider.Gemini => Environment.GetEnvironmentVariable("GEMINI_API_KEY"),
                LlmProvider.OpenAi => Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
                LlmProvider.Anthropic => Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ??
                                         Environment.GetEnvironmentVariable("CLAUDE_API_KEY"),
                _ => null
            };

        if (string.IsNullOrWhiteSpace(resolvedKey))
        {
            string envVarName = resolvedProvider switch
            {
                LlmProvider.Gemini => "GEMINI_API_KEY",
                LlmProvider.OpenAi => "OPENAI_API_KEY",
                LlmProvider.Anthropic => "ANTHROPIC_API_KEY",
                _ => "API_KEY"
            };
            throw new InvalidOperationException($"API key is missing for {resolvedProvider}. Please set the {envVarName} environment variable or pass the key using the --ai-key parameter.");
        }

        return new LlmClient(resolvedProvider, resolvedKey, overrideModel);
    }

    public async Task<string> AskAsync(string prompt, CancellationToken cancellationToken = default)
    {
        return provider switch
        {
            LlmProvider.Gemini => await CallGeminiAsync(prompt, cancellationToken).ConfigureAwait(false),
            LlmProvider.OpenAi => await CallOpenAiAsync(prompt, cancellationToken).ConfigureAwait(false),
            LlmProvider.Anthropic => await CallAnthropicAsync(prompt, cancellationToken).ConfigureAwait(false),
            _ => throw new NotImplementedException()
        };
    }

    private async Task<string> CallGeminiAsync(string prompt, CancellationToken cancellationToken)
    {
        string model = customModel ?? "gemini-2.5-flash";
        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

        // AOT-safe JSON creation without generic Add<T> methods
        var part = new JsonObject { ["text"] = prompt };
        var parts = new JsonArray
        {
            (JsonNode)part
        };
        var content = new JsonObject { ["parts"] = parts };
        var contents = new JsonArray
        {
            (JsonNode)content
        };
        var requestNode = new JsonObject { ["contents"] = contents };

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(requestNode.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Gemini API error ({response.StatusCode}): {responseText}");
        }

        var doc = JsonNode.Parse(responseText);
        var text = doc?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.GetValue<string>();

        return text ?? throw new InvalidOperationException($"Failed to extract text from Gemini response. Response: {responseText}");
    }

    private async Task<string> CallOpenAiAsync(string prompt, CancellationToken cancellationToken)
    {
        string model = customModel ?? "gpt-4o-mini";
        const string Url = "https://api.openai.com/v1/chat/completions";

        // AOT-safe JSON creation
        var message = new JsonObject { ["role"] = "user", ["content"] = prompt };
        var messages = new JsonArray
        {
            (JsonNode)message
        };
        var requestNode = new JsonObject
        {
            ["model"] = model,
            ["messages"] = messages
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, Url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(requestNode.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"OpenAI API error ({response.StatusCode}): {responseText}");
        }

        var doc = JsonNode.Parse(responseText);
        var text = doc?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();

        return text ?? throw new InvalidOperationException($"Failed to extract text from OpenAI response. Response: {responseText}");
    }

    private async Task<string> CallAnthropicAsync(string prompt, CancellationToken cancellationToken)
    {
        string model = customModel ?? "claude-3-5-haiku-20241022";
        const string Url = "https://api.anthropic.com/v1/messages";

        // AOT-safe JSON creation
        var message = new JsonObject { ["role"] = "user", ["content"] = prompt };
        var messages = new JsonArray
        {
            (JsonNode)message
        };
        var requestNode = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = 4000,
            ["messages"] = messages
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, Url);
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(requestNode.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Anthropic API error ({response.StatusCode}): {responseText}");
        }

        var doc = JsonNode.Parse(responseText);
        var text = doc?["content"]?[0]?["text"]?.GetValue<string>();

        return text ?? throw new InvalidOperationException($"Failed to extract text from Anthropic response. Response: {responseText}");
    }
}
