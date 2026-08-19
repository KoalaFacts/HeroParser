using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HeroParser.Console;

/// <summary>
/// Orchestrates the display and refresh loop of live progress bars.
/// </summary>
public class ProgressRunner
{
    private readonly IAnsiConsole? console;
    private readonly List<object> columns = [];

    /// <summary>
    /// Initializes a runner that renders to <see cref="AnsiConsole.Current"/>.
    /// </summary>
    public ProgressRunner()
    {
    }

    /// <summary>
    /// Initializes a runner that renders to the supplied console.
    /// </summary>
    /// <param name="console">Console the progress bars are drawn on.</param>
    public ProgressRunner(IAnsiConsole console)
    {
        ArgumentNullException.ThrowIfNull(console);
        this.console = console;
    }

    /// <summary>
    /// Gets or sets how often the bars are redrawn while the work runs.
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
    } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Stubs the columns configuration to support API compatibility with Spectre.Console.
    /// </summary>
    public ProgressRunner Columns(params object[] cols)
    {
        columns.AddRange(cols);
        return this;
    }

    /// <summary>
    /// Starts the progress rendering loop and executes the given action.
    /// </summary>
    public async Task StartAsync(Func<ProgressContext, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        using var cts = new CancellationTokenSource();
        var context = new ProgressContext();
        var output = console ?? AnsiConsole.Current;
        var interval = RefreshInterval;

        // Hide terminal cursor
        output.Write("\x1b[?25l");

        var renderTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                RenderProgress(output, context);
                try
                {
                    await Task.Delay(interval, cts.Token).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        });

        try
        {
            await action(context).ConfigureAwait(false);
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

            RenderProgress(output, context);

            // Restore terminal cursor visibility
            output.Write("\x1b[?25h");
            output.WriteLine(string.Empty);
        }
    }

    private static void RenderProgress(IAnsiConsole output, ProgressContext context)
    {
        lock (context.Tasks)
        {
            if (context.Tasks.Count == 0) return;

            if (context.HasRenderedBefore)
            {
                // Move cursor up by the number of tasks to rewrite them in place
                output.Write($"\x1b[{context.Tasks.Count}A");
            }
            else
            {
                context.HasRenderedBefore = true;
            }

            foreach (var task in context.Tasks)
            {
                // Clear the current console line
                output.Write("\x1b[2K\r");

                double percent = task.MaxValue > 0 ? (task.Value / task.MaxValue) : 0;
                percent = Math.Clamp(percent, 0.0, 1.0);

                int barWidth = 30;
                int filledWidth = (int)(percent * barWidth);
                int emptyWidth = barWidth - filledWidth;

                string filled = new('█', filledWidth);
                string empty = new('░', emptyWidth);

                output.Markup($"{task.Description} ");

                // The surrounding brackets have to be written as plain text: the markup
                // parser reads '[' as the start of a style tag, so embedding the bar in
                // "[green][...][/]" made the whole bar parse as a tag name and vanish.
                output.Write("[");
                output.Markup($"[green]{filled}{empty}[/]");
                output.Write("] ");
                output.MarkupLine($"[grey]{percent * 100:0.0}%[/]");
            }
        }
    }
}

/// <summary>
/// Manages the state of active progress tasks.
/// </summary>
public class ProgressContext
{
    /// <summary>
    /// Gets the list of active progress tasks.
    /// </summary>
    public List<ProgressTask> Tasks { get; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether this context has been rendered previously.
    /// </summary>
    public bool HasRenderedBefore { get; set; }

    /// <summary>
    /// Adds a new progress task.
    /// </summary>
    public ProgressTask AddTask(string description, double maxValue)
    {
        lock (Tasks)
        {
            var task = new ProgressTask(description, maxValue);
            Tasks.Add(task);
            return task;
        }
    }
}

/// <summary>
/// Represents a single progress task.
/// </summary>
public class ProgressTask
{
    /// <summary>
    /// Gets or sets the text description of this task.
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets the current progress value.
    /// </summary>
    public double Value { get; set; }

    /// <summary>
    /// Gets the maximum progress value.
    /// </summary>
    public double MaxValue { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProgressTask"/> class.
    /// </summary>
    /// <param name="description">The description of the progress task.</param>
    /// <param name="maxValue">The maximum value of the progress task.</param>
    public ProgressTask(string description, double maxValue)
    {
        Description = description ?? string.Empty;
        MaxValue = maxValue;
        Value = 0;
    }

    /// <summary>
    /// Increments the task progress.
    /// </summary>
    public void Increment(double amount)
    {
        Value = Math.Min(MaxValue, Value + amount);
    }
}
