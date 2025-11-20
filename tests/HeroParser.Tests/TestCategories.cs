namespace HeroParser.Tests;

/// <summary>
/// Test category constants for organizing and filtering tests.
/// Use with [Trait(TestCategories.CATEGORY, TestCategories.UNIT)] attribute.
/// </summary>
public static class TestCategories
{
    /// <summary>Trait key for test categories.</summary>
    public const string CATEGORY = "Category";

    /// <summary>
    /// Fast unit tests with no external dependencies (should complete in milliseconds).
    /// Tests isolated functionality of individual components.
    /// </summary>
    public const string UNIT = "Unit";

    /// <summary>
    /// Integration tests involving multiple components working together,
    /// complex scenarios, or end-to-end workflows.
    /// </summary>
    public const string INTEGRATION = "Integration";
}
