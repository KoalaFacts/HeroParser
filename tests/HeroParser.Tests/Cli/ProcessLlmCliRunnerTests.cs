using HeroParser.Cli.AI;
using Xunit;

namespace HeroParser.Tests.Cli;

/// <summary>
/// Covers <see cref="ProcessLlmCliRunner"/> against a real child process.
///
/// This is the one part of the AI stack that cannot be faked away — starting a process,
/// feeding it stdin, draining both its pipes and reaping it is the whole job. It runs the
/// dotnet CLI, which is present wherever these tests are, rather than an agent CLI that
/// may not be installed. Marked Integration because it spawns processes.
/// </summary>
[Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
public class ProcessLlmCliRunnerTests
{
    private static readonly ILlmCliRunner Runner = new ProcessLlmCliRunner();

    [Fact]
    public async Task Success_ReturnsTheProcessStdout()
    {
        string output = await Runner.RunAsync("dotnet", "--version", "ignored prompt", TestContext.Current.CancellationToken);

        Assert.NotEmpty(output);
        // The runner trims, so there is no trailing newline to account for.
        Assert.Equal(output.Trim(), output);
        Assert.Contains('.', output);
    }

    [Fact]
    public async Task NonZeroExit_ThrowsWithTheExitCodeAndStderr()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Runner.RunAsync("dotnet", "--definitely-not-a-flag", "prompt", TestContext.Current.CancellationToken));

        Assert.Contains("dotnet", ex.Message, StringComparison.Ordinal);
        Assert.Contains("exited with code", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingCommand_ThrowsRatherThanHanging()
    {
        // An agent CLI that is not installed must fail fast, not wait for a process that
        // will never start.
        await Assert.ThrowsAnyAsync<Exception>(
            () => Runner.RunAsync("heroparser-no-such-agent-cli", "", "prompt", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CancelledToken_StopsTheProcessAndSurfacesTheCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Runner.RunAsync("dotnet", "--version", "prompt", cts.Token));
    }

    [Fact]
    public async Task Timeout_IsReportedAsATimeout()
    {
        // A hung agent must not block the CLI forever. A zero-length window expires before
        // the child can exit, which is exactly the condition the timeout guards against.
        var runner = new ProcessLlmCliRunner { Timeout = TimeSpan.Zero };

        var ex = await Assert.ThrowsAsync<TimeoutException>(
            () => runner.RunAsync("dotnet", "--version", "prompt", TestContext.Current.CancellationToken));

        Assert.Contains("timed out", ex.Message, StringComparison.Ordinal);
    }
}
