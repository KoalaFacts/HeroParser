using System;
using System.Threading;
using System.Threading.Tasks;

namespace HeroParser.Console;

/// <summary>
/// Orchestrates the background rendering of loading status spinners.
/// </summary>
public class StatusRunner
{
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
        using var cts = new CancellationTokenSource();
        var context = new StatusContext(message);

        // Hide terminal cursor
        System.Console.Write("\x1b[?25l");

        var renderTask = Task.Run(async () =>
        {
            char[] spinnerFrames = ['⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏'];
            int frame = 0;

            // Render first frame
            System.Console.Write("\x1b[2K\r");
            AnsiConsole.Markup($"[cyan]{spinnerFrames[frame]}[/] {context.Message}");
            System.Console.Write("\r");

            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(80, cts.Token).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    break;
                }

                if (cts.Token.IsCancellationRequested) break;

                frame = (frame + 1) % spinnerFrames.Length;

                // Clear current line and rewrite spinner frame
                System.Console.Write("\x1b[2K\r");
                AnsiConsole.Markup($"[cyan]{spinnerFrames[frame]}[/] {context.Message}");
                System.Console.Write("\r");
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
            System.Console.Write("\x1b[2K\r");

            // Restore terminal cursor visibility
            System.Console.Write("\x1b[?25h");
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
