using System;
using System.Threading;
using System.Threading.Tasks;
using HeroParser.Console;

namespace HeroParser.Cli;

internal readonly struct CliConsole
{
    public void Info(string message)
    {
        string escaped = Markup.Escape(message);
        AnsiConsole.MarkupLine($"[grey][[info]][/] {escaped}");
    }

    public void Success(string message)
    {
        string escaped = Markup.Escape(message);
        AnsiConsole.MarkupLine($"[green]✓[/] [bold green]{escaped}[/]");
    }

    public void Warning(string message)
    {
        string escaped = Markup.Escape(message);
        AnsiConsole.MarkupLine($"[yellow bold]⚠[/] [yellow]{escaped}[/]");
    }

    public void Error(string message)
    {
        string escaped = Markup.Escape(message);
        AnsiConsole.MarkupLine($"[red bold]✗ Error:[/] [red]{escaped}[/]");
    }

    public void Highlight(string title, string content)
    {
        string escTitle = Markup.Escape(title);
        string escContent = Markup.Escape(content);
        AnsiConsole.MarkupLine($"[cyan bold]{escTitle}[/]: {escContent}");
    }

    public void Header(string title)
    {
        var rule = new Rule($"[blue bold]{Markup.Escape(title)}[/]");
        rule.Centered();
        AnsiConsole.Write(rule);
    }

    public async Task<T> RunWithSpinnerAsync<T>(string message, Func<CancellationToken, Task<T>> taskFunc, CancellationToken cancellationToken = default)
    {
        return await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync(message, async ctx =>
            {
                return await taskFunc(cancellationToken).ConfigureAwait(false);
            });
    }
}
