using Xunit;

namespace HeroParser.Tests.ConsoleUi;

/// <summary>
/// Groups every test that swaps <see cref="HeroParser.Console.AnsiConsole.Current"/> —
/// and every test that runs code writing through it.
///
/// That property is process-wide state. A test that reassigns it while another is
/// rendering steals that test's output, and restoring it at the end can leave a
/// concurrent test writing to a disposed writer. xunit parallelises across collections
/// but not within one, so sharing this collection serialises them.
/// </summary>
[CollectionDefinition(NAME)]
public sealed class AnsiConsoleCurrentCollection
{
    /// <summary>The collection name to put on classes that reassign the current console.</summary>
    public const string NAME = "AnsiConsole.Current";
}
