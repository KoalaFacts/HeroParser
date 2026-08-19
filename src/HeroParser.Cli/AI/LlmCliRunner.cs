using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HeroParser.Cli.AI;

/// <summary>
/// Runs a locally installed AI CLI and returns everything it wrote to stdout.
/// </summary>
/// <remarks>
/// Separating this from <see cref="LlmClient"/> keeps two unrelated concerns apart: which
/// command and arguments a provider needs, and how a child process is started, fed and
/// reaped. It also means the provider mapping can be exercised without spawning anything.
/// </remarks>
internal interface ILlmCliRunner
{
    /// <summary>
    /// Runs <paramref name="commandName"/>, writes <paramref name="prompt"/> to its stdin,
    /// and returns its trimmed stdout.
    /// </summary>
    Task<string> RunAsync(string commandName, string arguments, string prompt, CancellationToken cancellationToken);
}

/// <summary>
/// The default <see cref="ILlmCliRunner"/>, which starts a real child process.
/// </summary>
internal sealed class ProcessLlmCliRunner : ILlmCliRunner
{
    /// <summary>Hard cap on a single invocation, after which the process tree is killed.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(3);

    /// <inheritdoc />
    public async Task<string> RunAsync(string commandName, string arguments, string prompt, CancellationToken cancellationToken)
    {
        string resolvedCommand = LocalCliLocator.ResolveCommandPath(commandName);
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
            using var registration = cancellationToken.Register(() => TryKill(process));

            // Write the prompt to the stdin of the process
            try
            {
                await process.StandardInput.WriteAsync(prompt).ConfigureAwait(false);
                await process.StandardInput.FlushAsync().ConfigureAwait(false);
                process.StandardInput.Close();
            }
            catch (IOException ex)
            {
                // The child closed stdin early (or died); its stderr explains why, so record
                // the write failure there rather than losing the real error.
                errorBuilder.AppendLine($"[StdIn Write Error] {ex.Message}");
            }
            catch (ObjectDisposedException ex)
            {
                errorBuilder.AppendLine($"[StdIn Write Error] {ex.Message}");
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(Timeout);

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
                throw new TimeoutException($"The local AI CLI process for {commandName} timed out (limit: {Timeout.TotalMinutes:0.##} minutes).");
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
            TryKill(process);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Already exited between the check and the kill: nothing to clean up.
        }
        catch (NotSupportedException)
        {
            // Killing a process tree is unsupported on this host.
        }
    }
}
