using System;
using System.Threading;
using System.Threading.Tasks;
using HeroParser.Console;

namespace HeroParser.Cli;

internal static class ConsoleUtils
{
    public static void Info(string message)
    {
        string escaped = Markup.Escape(message);
        AnsiConsole.MarkupLine($"[grey][[info]][/] {escaped}");
    }

    public static void Success(string message)
    {
        string escaped = Markup.Escape(message);
        AnsiConsole.MarkupLine($"[green]✓[/] [bold green]{escaped}[/]");
    }

    public static void Warning(string message)
    {
        string escaped = Markup.Escape(message);
        AnsiConsole.MarkupLine($"[yellow bold]⚠[/] [yellow]{escaped}[/]");
    }

    public static void Error(string message)
    {
        string escaped = Markup.Escape(message);
        AnsiConsole.MarkupLine($"[red bold]✗ Error:[/] [red]{escaped}[/]");
    }

    public static void Highlight(string title, string content)
    {
        string escTitle = Markup.Escape(title);
        string escContent = Markup.Escape(content);
        AnsiConsole.MarkupLine($"[cyan bold]{escTitle}[/]: {escContent}");
    }

    public static void Header(string title)
    {
        var rule = new Rule($"[blue bold]{Markup.Escape(title)}[/]");
        rule.Centered();
        AnsiConsole.Write(rule);
    }

    public static async Task<T> RunWithSpinnerAsync<T>(string message, Func<CancellationToken, Task<T>> taskFunc, CancellationToken cancellationToken = default)
    {
        return await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync(message, async ctx =>
            {
                return await taskFunc(cancellationToken).ConfigureAwait(false);
            });
    }
}
