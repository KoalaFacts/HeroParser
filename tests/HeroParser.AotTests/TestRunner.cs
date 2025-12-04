namespace HeroParser.AotTests;

/// <summary>
/// Simple test runner for AOT compatibility tests.
/// </summary>
public sealed class TestRunner
{
    private readonly List<string> failures = [];

    public int FailureCount => failures.Count;

    public IReadOnlyList<string> Failures => failures;

    public void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine($"  [PASS] {name}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [FAIL] {name}: {ex.Message}");
            failures.Add(name);
        }
    }

    public void PrintSection(string name)
    {
        Console.WriteLine($"\n--- {name} ---");
    }

    public int PrintSummary()
    {
        Console.WriteLine();
        Console.WriteLine("===================================");

        if (failures.Count == 0)
        {
            Console.WriteLine("All AOT tests PASSED!");
            return 0;
        }

        Console.WriteLine($"{failures.Count} test(s) FAILED:");
        foreach (var failure in failures)
        {
            Console.WriteLine($"  - {failure}");
        }

        return 1;
    }
}
