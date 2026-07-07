using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HeroParser.Cli.AI;

internal enum LlmProvider
{
    Google,
    OpenAi,
    Anthropic,
    Microsoft,
    GitHub
}

internal sealed class LlmClient
{
    private readonly LlmProvider provider;
    private readonly string? customModel;

    public LlmClient(LlmProvider provider, string? customModel = null)
    {
        this.provider = provider;
        this.customModel = customModel;
    }

    public static LlmClient CreateFromEnvironment(string? overrideProvider = null, string? overrideKey = null, string? overrideModel = null)
    {
        _ = overrideKey; // Retained for API compatibility but unused since we call local CLI processes directly
        LlmProvider resolvedProvider = LlmProvider.Google;

        if (!string.IsNullOrWhiteSpace(overrideProvider))
        {
            resolvedProvider = overrideProvider.ToLowerInvariant() switch
            {
                "google" or "gemini" or "antigravity" or "agy" => LlmProvider.Google,
                "openai" or "chatgpt" or "codex" => LlmProvider.OpenAi,
                "anthropic" or "claude" => LlmProvider.Anthropic,
                "microsoft" or "copilot" => LlmProvider.Microsoft,
                "github" => LlmProvider.GitHub,
                _ => throw new ArgumentException($"Unknown AI provider: {overrideProvider}. Valid options: google, openai, anthropic, microsoft, github")
            };
        }
        else
        {
            // Auto detect from environment variables or local paths
            var envProvider = Environment.GetEnvironmentVariable("HEROPARSER_AI_PROVIDER");
            if (!string.IsNullOrWhiteSpace(envProvider))
            {
                resolvedProvider = envProvider.ToLowerInvariant() switch
                {
                    "google" or "gemini" or "antigravity" or "agy" => LlmProvider.Google,
                    "openai" or "chatgpt" or "codex" => LlmProvider.OpenAi,
                    "anthropic" or "claude" => LlmProvider.Anthropic,
                    "microsoft" or "copilot" => LlmProvider.Microsoft,
                    "github" => LlmProvider.GitHub,
                    _ => resolvedProvider
                };
            }
            else
            {
                // Auto detect by checking command availability in order: agy -> claude -> copilot -> codex -> openai
                if (IsCommandAvailable("agy"))
                    resolvedProvider = LlmProvider.Google;
                else if (IsCommandAvailable("claude"))
                    resolvedProvider = LlmProvider.Anthropic;
                else if (IsCommandAvailable("copilot"))
                    resolvedProvider = LlmProvider.Microsoft;
                else if (IsCommandAvailable("codex") || IsCommandAvailable("openai"))
                    resolvedProvider = LlmProvider.OpenAi;
            }
        }

        return new LlmClient(resolvedProvider, overrideModel);
    }

    public async Task<string> AskAsync(string prompt, CancellationToken cancellationToken = default)
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
                if (IsCommandAvailable("openai") || ResolveCommandPath("openai") != "openai")
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

            default:
                throw new NotImplementedException();
        }

        if (!string.IsNullOrWhiteSpace(customModel))
        {
            args += $" --model \"{customModel}\"";
        }

        // Standardize structuring instructions to ensure deterministic response shape from the local agent
        string structuredPrompt = prompt;
        if (!prompt.Contains("Output ONLY", StringComparison.OrdinalIgnoreCase))
        {
            structuredPrompt += "\n\nIMPORTANT: You must output ONLY the raw requested structured response. Do not include any conversational prefix, suffix, explanation, or chat formatting. Return the raw data directly.";
        }

        string rawResponse = await RunLocalCliAsync(cmd, args, structuredPrompt, cancellationToken).ConfigureAwait(false);
        return ExtractStructuredContent(rawResponse);
    }

    private async Task<string> RunLocalCliAsync(string commandName, string arguments, string prompt, CancellationToken cancellationToken)
    {
        string resolvedCommand = ResolveCommandPath(commandName);
        string finalFileName = resolvedCommand;
        string finalArguments = arguments;

        // If running a .cmd or .bat script on Windows, wrap it via cmd.exe
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
            (resolvedCommand.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
             resolvedCommand.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)))
        {
            finalFileName = "cmd.exe";
            finalArguments = $"/c \"\"{resolvedCommand}\" {arguments}\"";
        }

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = finalFileName,
            Arguments = finalArguments,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        using var outputWaitHandle = new SemaphoreSlim(0);
        using var errorWaitHandle = new SemaphoreSlim(0);

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data == null)
            {
                outputWaitHandle.Release();
            }
            else
            {
                outputBuilder.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data == null)
            {
                errorWaitHandle.Release();
            }
            else
            {
                errorBuilder.AppendLine(e.Data);
            }
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start local AI CLI process for {commandName}.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Set up cancellation token registration to kill process tree on cancellation
            using var registration = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Ignore errors during cancellation cleanup
                }
            });

            // Write the prompt to the stdin of the process
            try
            {
                await process.StandardInput.WriteAsync(prompt).ConfigureAwait(false);
                await process.StandardInput.FlushAsync().ConfigureAwait(false);
                process.StandardInput.Close();
            }
            catch (Exception ex)
            {
                errorBuilder.AppendLine($"[StdIn Write Error] {ex.Message}");
            }

            // Set a hard timeout of 3 minutes per invocation
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(3));

            try
            {
                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw; // Cancelled by user
                }
                throw new TimeoutException($"The local AI CLI process for {commandName} timed out (limit: 3 minutes).");
            }

            // Wait briefly for stdout/stderr to drain completely
            await Task.WhenAll(outputWaitHandle.WaitAsync(TimeSpan.FromSeconds(5)), errorWaitHandle.WaitAsync(TimeSpan.FromSeconds(5))).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Local AI CLI '{commandName}' exited with code {process.ExitCode}.\nError: {errorBuilder}");
            }

            return outputBuilder.ToString().Trim();
        }
        finally
        {
            // Safeguard: explicitly kill the process and its tree if it is still running
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Ignore errors on cleanup
            }
        }
    }

    private static bool IsCommandAvailable(string command)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return false;

        var extensions = new[] { "", ".exe", ".cmd", ".bat", ".lnk" };
        var paths = pathEnv.Split(Path.PathSeparator);
        foreach (var path in paths)
        {
            try
            {
                foreach (var ext in extensions)
                {
                    var fullPath = Path.Combine(path.Trim(), command + ext);
                    if (File.Exists(fullPath)) return true;
                }
            }
            catch
            {
                // Skip invalid paths
            }
        }
        return false;
    }

    private static string ResolveCommandPath(string command)
    {
        if (IsCommandAvailable(command))
        {
            return command;
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        var candidatePaths = new List<string>();

        if (command.Equals("agy", StringComparison.OrdinalIgnoreCase))
        {
            candidatePaths.Add(Path.Combine(localAppData, "agy", "bin", "agy.exe"));
        }

        else if (command.Equals("claude", StringComparison.OrdinalIgnoreCase))
        {
            candidatePaths.Add(Path.Combine(userProfile, ".local", "bin", "claude.exe"));
        }
        else if (command.Equals("copilot", StringComparison.OrdinalIgnoreCase))
        {
            candidatePaths.Add(Path.Combine(appData, "npm", "copilot.cmd"));
            candidatePaths.Add(Path.Combine(appData, "npm", "copilot.ps1"));
            candidatePaths.Add(Path.Combine(localAppData, "Microsoft", "WindowsApps", "copilot.exe"));
        }
        else if (command.Equals("codex", StringComparison.OrdinalIgnoreCase))
        {
            candidatePaths.Add(Path.Combine(localAppData, "Programs", "codex.exe"));
            candidatePaths.Add(Path.Combine(userProfile, ".local", "bin", "codex.exe"));
        }
        else if (command.Equals("openai", StringComparison.OrdinalIgnoreCase))
        {
            candidatePaths.Add(Path.Combine(localAppData, "Programs", "openai.exe"));
            candidatePaths.Add(Path.Combine(userProfile, ".local", "bin", "openai.exe"));
        }

        foreach (var path in candidatePaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return command;
    }

    private static string ExtractStructuredContent(string rawOutput)
    {
        if (string.IsNullOrWhiteSpace(rawOutput)) return string.Empty;

        string trimmed = rawOutput.Trim();

        if (trimmed.StartsWith("```"))
        {
            int firstNewLine = trimmed.IndexOf('\n');
            if (firstNewLine >= 0)
            {
                int lastBlock = trimmed.LastIndexOf("```");
                if (lastBlock > firstNewLine)
                {
                    return trimmed.Substring(firstNewLine + 1, lastBlock - firstNewLine - 1).Trim();
                }
            }
        }

        return trimmed;
    }
}
