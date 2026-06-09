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
    private readonly List<object> columns = [];

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
        using var cts = new CancellationTokenSource();
        var context = new ProgressContext();

        // Hide terminal cursor
        System.Console.Write("\x1b[?25l");

        var renderTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                RenderProgress(context);
                try
                {
                    await Task.Delay(100, cts.Token).ConfigureAwait(false);
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

            RenderProgress(context);

            // Restore terminal cursor visibility
            System.Console.Write("\x1b[?25h");
            System.Console.WriteLine();
        }
    }

    private static void RenderProgress(ProgressContext context)
    {
        lock (context.Tasks)
        {
            if (context.Tasks.Count == 0) return;

            if (context.HasRenderedBefore)
            {
                // Move cursor up by the number of tasks to rewrite them in place
                System.Console.Write($"\x1b[{context.Tasks.Count}A");
            }
            else
            {
                context.HasRenderedBefore = true;
            }

            foreach (var task in context.Tasks)
            {
                // Clear the current console line
                System.Console.Write("\x1b[2K\r");

                double percent = task.MaxValue > 0 ? (task.Value / task.MaxValue) : 0;
                percent = Math.Clamp(percent, 0.0, 1.0);

                int barWidth = 30;
                int filledWidth = (int)(percent * barWidth);
                int emptyWidth = barWidth - filledWidth;

                string filled = new('█', filledWidth);
                string empty = new('░', emptyWidth);

                AnsiConsole.Markup($"{task.Description} ");
                AnsiConsole.Markup($"[green][{filled}{empty}][/] ");
                AnsiConsole.MarkupLine($"[grey]{percent * 100:0.0}%[/]");
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
