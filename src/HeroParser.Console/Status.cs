using System;
using System.Threading;
using System.Threading.Tasks;

namespace HeroParser.Console;

/// <summary>
/// Orchestrates the background rendering of loading status spinners.
/// </summary>
public class StatusRunner
{
    private readonly IAnsiConsole? console;

    /// <summary>
    /// Initializes a runner that renders to <see cref="AnsiConsole.Current"/>.
    /// </summary>
    public StatusRunner()
    {
    }

    /// <summary>
    /// Initializes a runner that renders to the supplied console.
    /// </summary>
    /// <param name="console">Console the spinner is drawn on.</param>
    public StatusRunner(IAnsiConsole console)
    {
        ArgumentNullException.ThrowIfNull(console);
        this.console = console;
    }

    /// <summary>
    /// Gets or sets how often the spinner advances to its next frame.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public TimeSpan RefreshInterval
    {
        get;
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The refresh interval must be positive.");
            }
            field = value;
        }
    } = TimeSpan.FromMilliseconds(80);

    /// <summary>
    /// Stubs the spinner configuration to maintain API compatibility.
    /// </summary>
    public StatusRunner Spinner(object spinner)
    {
        _ = spinner;
        return this;
    }

    /// <summary>
    /// Starts the status spinner loop and runs the asynchronous task.
    /// </summary>
    public async Task<T> StartAsync<T>(string message, Func<StatusContext, Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        using var cts = new CancellationTokenSource();
        var context = new StatusContext(message);
        var output = console ?? AnsiConsole.Current;
        var interval = RefreshInterval;

        // Hide terminal cursor
        output.Write("\x1b[?25l");

        var renderTask = Task.Run(async () =>
        {
            char[] spinnerFrames = ['⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏'];
            int frame = 0;

            // Render first frame
            output.Write("\x1b[2K\r");
            output.Markup($"[cyan]{spinnerFrames[frame]}[/] {context.Message}");
            output.Write("\r");

            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(interval, cts.Token).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    break;
                }

                if (cts.Token.IsCancellationRequested) break;

                frame = (frame + 1) % spinnerFrames.Length;

                // Clear current line and rewrite spinner frame
                output.Write("\x1b[2K\r");
                output.Markup($"[cyan]{spinnerFrames[frame]}[/] {context.Message}");
                output.Write("\r");
            }
        });

        try
        {
            T result = await action(context).ConfigureAwait(false);
            return result;
        }
        finally
        {
            cts.Cancel();
            try
            {
                await renderTask.ConfigureAwait(false);
            }
            catch
            {
                // Gracefully ignore cancellation task exceptions
            }

            // Clear the status line
            output.Write("\x1b[2K\r");

            // Restore terminal cursor visibility
            output.Write("\x1b[?25h");
        }
    }
}

/// <summary>
/// Provides status message context for active spinners.
/// </summary>
public class StatusContext
{
    /// <summary>
    /// Gets or sets the active status message text.
    /// </summary>
    public string Message { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="StatusContext"/> class.
    /// </summary>
    /// <param name="message">The initial status message.</param>
    public StatusContext(string message)
    {
        Message = message ?? string.Empty;
    }
}
