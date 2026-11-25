using System.Text.RegularExpressions;
using Xunit;

namespace HeroParser.Tests;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed partial class CodeHygieneTests
{
    private static readonly string[] sourceRoots = ["src", "tests", "benchmarks", "examples"];

    [GeneratedRegex(@"^\s*#(?:end)?region\b", RegexOptions.CultureInvariant)]
    private static partial Regex RegionDirectiveRegex();

    [Fact]
    public void SourceAndTestFiles_DoNotContainRegionDirectives()
    {
        string? repoRoot = FindRepoRoot();
        Assert.NotNull(repoRoot);

        List<string> violations = [];
        Regex regex = RegionDirectiveRegex();

        foreach (string root in sourceRoots)
        {
            string rootPath = Path.Combine(repoRoot, root);
            if (!Directory.Exists(rootPath))
                continue;

            foreach (string file in EnumerateCSharpFiles(rootPath))
            {
                string relativePath = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                int lineNumber = 0;
                foreach (string line in File.ReadLines(file))
                {
                    lineNumber++;
                    if (regex.IsMatch(line))
                    {
                        violations.Add($"{relativePath}:{lineNumber}: {line.Trim()}");
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Found forbidden #region/#endregion directives:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    private static readonly string[] consoleWritingEntryPoints = ["new CliCommands(", "Program.Main(", "new InteractiveWizard("];

    /// <summary>
    /// CLI commands write through the process-wide <c>AnsiConsole.Current</c>, which console tests swap.
    /// A test class that runs them outside <c>AnsiConsoleCurrentCollection</c> races with those tests
    /// (it failed <c>Build and Test (windows-latest, net10.0)</c> on #215 through <c>FileCompressionTests</c>).
    /// </summary>
    [Fact]
    public void TestsThatRunCliCommands_AreInTheAnsiConsoleCurrentCollection()
    {
        string? repoRoot = FindRepoRoot();
        Assert.NotNull(repoRoot);

        List<string> violations = [];
        foreach (string file in EnumerateCSharpFiles(Path.Combine(repoRoot, "tests")))
        {
            string text = File.ReadAllText(file);
            bool runsCli = consoleWritingEntryPoints.Any(entry => text.Contains(entry, StringComparison.Ordinal));
            if (runsCli && !text.Contains("[Collection(AnsiConsoleCurrentCollection.NAME)]", StringComparison.Ordinal))
                violations.Add(Path.GetRelativePath(repoRoot, file).Replace('\\', '/'));
        }

        Assert.True(
            violations.Count == 0,
            $"These test files run CLI commands but are not in AnsiConsoleCurrentCollection:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    private static IEnumerable<string> EnumerateCSharpFiles(string rootDirectory)
    {
        var pending = new Stack<string>();
        pending.Push(rootDirectory);

        while (pending.Count > 0)
        {
            string current = pending.Pop();
            foreach (string file in Directory.EnumerateFiles(current, "*.cs"))
            {
                yield return file;
            }

            foreach (string subDir in Directory.EnumerateDirectories(current))
            {
                string name = Path.GetFileName(subDir);
                if (string.Equals(name, "bin", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "obj", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, ".git", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "node_modules", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                pending.Push(subDir);
            }
        }
    }
}
