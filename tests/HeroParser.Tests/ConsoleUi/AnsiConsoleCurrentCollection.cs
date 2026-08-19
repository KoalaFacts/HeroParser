using Xunit;

namespace HeroParser.Tests.ConsoleUi;

/// <summary>
/// Groups every test that swaps <see cref="HeroParser.Console.AnsiConsole.Current"/>.
///
/// That property is process-wide state, so two such tests running concurrently would
/// capture each other's output. xunit parallelises across collections but not within one,
/// so sharing this collection serialises them.
/// </summary>
[CollectionDefinition(NAME)]
public sealed class AnsiConsoleCurrentCollection
{
    /// <summary>The collection name to put on classes that reassign the current console.</summary>
    public const string NAME = "AnsiConsole.Current";
}
