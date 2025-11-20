namespace HeroParser.Tests;

/// <summary>
/// Test category constants for organizing and filtering tests.
/// Use with [Trait(TestCategories.Category, TestCategories.Unit)] attribute.
/// </summary>
public static class TestCategories
{
    /// <summary>Trait key for test categories.</summary>
    public const string Category = "Category";

    /// <summary>Fast unit tests with no external dependencies (should complete in milliseconds).</summary>
    public const string Unit = "Unit";

    /// <summary>Tests verifying RFC 4180 compliance for CSV parsing.</summary>
    public const string Rfc4180 = "RFC4180";

    /// <summary>Tests verifying README documentation examples compile and work correctly.</summary>
    public const string Documentation = "Documentation";

    /// <summary>Security-focused tests (bounds checking, overflow protection, etc.).</summary>
    public const string Security = "Security";

    /// <summary>Performance-sensitive tests that verify efficiency characteristics.</summary>
    public const string Performance = "Performance";

    /// <summary>Tests for error handling and validation logic.</summary>
    public const string ErrorHandling = "ErrorHandling";
}
