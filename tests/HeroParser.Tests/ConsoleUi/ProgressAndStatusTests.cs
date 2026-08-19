using System.Diagnostics;
using HeroParser.Console;
using Xunit;

namespace HeroParser.Tests.ConsoleUi;

/// <summary>
/// Covers the live progress-bar and spinner runners.
///
/// Both drive a background render loop against the console for as long as the caller's
/// work runs, so before the <see cref="IAnsiConsole"/> seam existed neither could be
/// executed off a real terminal. Rendering into a <see cref="RecordingConsole"/> and
/// shortening the refresh interval makes the loop, its cursor bookkeeping and its
/// teardown observable.
/// </summary>
[Trait("Category", "Unit")]
public class ProgressAndStatusTests
{
    /// <summary>Upper bound on how long a test waits for the render loop to produce a frame.</summary>
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Spins until the recorded output contains <paramref name="marker"/>.</summary>
    private static async Task WaitForFrameAsync(RecordingConsole console, string marker)
    {
        var started = Stopwatch.StartNew();
        while (!console.Snapshot().Contains(marker, StringComparison.Ordinal) && started.Elapsed < FrameTimeout)
        {
            await Task.Delay(1, TestContext.Current.CancellationToken).ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task Progress_RendersBarsAndRestoresCursor()
    {
        var console = new RecordingConsole();
        var runner = new ProgressRunner(console) { RefreshInterval = TimeSpan.FromMilliseconds(1) };

        await runner.StartAsync(async context =>
        {
            var task = context.AddTask("Loading", 10);
            task.Increment(10);
            // Wait for the loop to draw at least once, so the final render in the runner's
            // finally block takes the redraw-in-place path rather than the first-draw path.
            await WaitForFrameAsync(console, "█").ConfigureAwait(false);
        });

        string output = console.Snapshot();
        Assert.Contains("\x1b[?25l", output, StringComparison.Ordinal);   // cursor hidden
        Assert.Contains("\x1b[?25h", output, StringComparison.Ordinal);   // and restored
        Assert.Contains("\x1b[1A", output, StringComparison.Ordinal);     // redrawn in place
        Assert.Contains("Loading", output, StringComparison.Ordinal);
        Assert.Contains("100.0%", output, StringComparison.Ordinal);
        Assert.Contains('█', output);
    }

    [Fact]
    public async Task Progress_WithNoTasks_DrawsNothingButStillRestoresCursor()
    {
        var console = new RecordingConsole();
        await new ProgressRunner(console).StartAsync(_ => Task.CompletedTask);

        string output = console.Snapshot();
        Assert.DoesNotContain('█', output);
        Assert.DoesNotContain('░', output);
        Assert.Contains("\x1b[?25h", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Progress_PartialCompletion_DrawsPartialBar()
    {
        var console = new RecordingConsole();
        await new ProgressRunner(console).StartAsync(context =>
        {
            context.AddTask("Half", 4).Increment(2);
            return Task.CompletedTask;
        });

        string output = console.Snapshot();
        Assert.Contains("50.0%", output, StringComparison.Ordinal);
        Assert.Contains('█', output);
        Assert.Contains('░', output);
    }

    [Fact]
    public async Task Progress_ZeroMaxValue_ReportsZeroPercent()
    {
        var console = new RecordingConsole();
        await new ProgressRunner(console).StartAsync(context =>
        {
            context.AddTask("Unknown", 0);
            return Task.CompletedTask;
        });

        Assert.Contains("0.0%", console.Snapshot(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Progress_ActionException_PropagatesAfterRestoringCursor()
    {
        var console = new RecordingConsole();
        var runner = new ProgressRunner(console);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.StartAsync(_ => throw new InvalidOperationException("boom")));

        // The terminal must not be left with a hidden cursor when the work fails.
        Assert.Contains("\x1b[?25h", console.Snapshot(), StringComparison.Ordinal);
    }

    [Fact]
    public void Progress_NullConsole_Throws()
        => Assert.Throws<ArgumentNullException>(() => new ProgressRunner(null!));

    [Fact]
    public async Task Progress_NullAction_Throws()
        => await Assert.ThrowsAsync<ArgumentNullException>(() => new ProgressRunner().StartAsync(null!));

    [Fact]
    public void Progress_Columns_IsFluent()
    {
        var runner = new ProgressRunner();
        Assert.Same(runner, runner.Columns("a", "b"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Progress_NonPositiveRefreshInterval_Throws(int milliseconds)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => new ProgressRunner { RefreshInterval = TimeSpan.FromMilliseconds(milliseconds) });

    [Fact]
    public void ProgressTask_NullDescription_BecomesEmpty()
        => Assert.Equal(string.Empty, new ProgressTask(null!, 1).Description);

    [Fact]
    public void ProgressTask_Increment_ClampsToMaxValue()
    {
        var task = new ProgressTask("t", 5);
        task.Increment(3);
        Assert.Equal(3, task.Value);
        task.Increment(99);
        Assert.Equal(5, task.Value);
    }

    [Fact]
    public void ProgressContext_AddTask_TracksTasks()
    {
        var context = new ProgressContext();
        Assert.False(context.HasRenderedBefore);

        var task = context.AddTask("one", 3);
        Assert.Same(task, Assert.Single(context.Tasks));
        Assert.Equal(3, task.MaxValue);
    }

    [Fact]
    public async Task Status_RendersSpinnerFramesAndReturnsResult()
    {
        var console = new RecordingConsole();
        var runner = new StatusRunner(console) { RefreshInterval = TimeSpan.FromMilliseconds(1) };

        int result = await runner.StartAsync("Working", async context =>
        {
            Assert.Equal("Working", context.Message);
            // '⠙' is the second spinner frame, so seeing it proves the refresh loop ran.
            await WaitForFrameAsync(console, "⠙").ConfigureAwait(false);
            return 42;
        });

        Assert.Equal(42, result);
        string output = console.Snapshot();
        Assert.Contains("Working", output, StringComparison.Ordinal);
        Assert.Contains('⠋', output);
        Assert.Contains('⠙', output);
        Assert.Contains("\x1b[?25l", output, StringComparison.Ordinal);
        Assert.Contains("\x1b[?25h", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_ActionException_PropagatesAfterClearingLine()
    {
        var console = new RecordingConsole();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new StatusRunner(console).StartAsync<int>("w", _ => throw new InvalidOperationException("boom")));

        string output = console.Snapshot();
        Assert.Contains("\x1b[2K\r", output, StringComparison.Ordinal);
        Assert.Contains("\x1b[?25h", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_MessageChangedMidRun_IsPickedUpByTheLoop()
    {
        var console = new RecordingConsole();
        var runner = new StatusRunner(console) { RefreshInterval = TimeSpan.FromMilliseconds(1) };

        await runner.StartAsync("before", async context =>
        {
            await WaitForFrameAsync(console, "before").ConfigureAwait(false);
            context.Message = "after";
            await WaitForFrameAsync(console, "after").ConfigureAwait(false);
            return 0;
        });

        Assert.Contains("after", console.Snapshot(), StringComparison.Ordinal);
    }

    [Fact]
    public void Status_NullConsole_Throws()
        => Assert.Throws<ArgumentNullException>(() => new StatusRunner(null!));

    [Fact]
    public async Task Status_NullAction_Throws()
        => await Assert.ThrowsAsync<ArgumentNullException>(() => new StatusRunner().StartAsync<int>("m", null!));

    [Fact]
    public void Status_Spinner_IsFluent()
    {
        var runner = new StatusRunner();
        Assert.Same(runner, runner.Spinner(Spinner.Known.Dots));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Status_NonPositiveRefreshInterval_Throws(int milliseconds)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => new StatusRunner { RefreshInterval = TimeSpan.FromMilliseconds(milliseconds) });

    [Fact]
    public void StatusContext_NullMessage_BecomesEmpty()
        => Assert.Equal(string.Empty, new StatusContext(null!).Message);

    [Fact]
    public void Spinner_KnownDots_IsAvailable()
        => Assert.NotNull(Spinner.Known.Dots);
}
