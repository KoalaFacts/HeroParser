using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using HeroParser.AI;
using Xunit;

namespace HeroParser.Tests.AI;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class TabularContextProfilerTests
{
    private enum TestEnum
    {
        OptionA,
        OptionB
    }

    private sealed class ProfileRecord
    {
        [TabularMap(Name = "Custom Name")]
        public string Name { get; set; } = string.Empty;

        public int Age { get; set; }
        public double? Score { get; set; }
        public bool IsActive { get; set; }
        public TestEnum Category { get; set; }
        public char Symbol { get; set; }
        public DateTime Created { get; set; }
    }

    [Fact]
    public void GenerateContextCard_WithEmptyList_ReturnsNoData()
    {
        var records = new List<ProfileRecord>();
        var card = records.GenerateContextCard("EmptySet");

        Assert.Contains("Dataset Profile: EmptySet (0 rows)", card);
        Assert.Contains("No data available.", card);
    }

    [Fact]
    public async Task GenerateContextCardAsync_WithValidData_ReturnsStats()
    {
        var records = new List<ProfileRecord>
        {
            new() { Name = "Alice", Age = 30, Score = 95.5, IsActive = true, Category = TestEnum.OptionA, Symbol = 'A', Created = DateTime.UtcNow },
            new() { Name = "Bob", Age = 25, Score = null, IsActive = false, Category = TestEnum.OptionA, Symbol = 'B', Created = DateTime.UtcNow },
            new() { Name = "Charlie", Age = 35, Score = 80.0, IsActive = true, Category = TestEnum.OptionB, Symbol = 'A', Created = DateTime.UtcNow }
        };

        var source = ToAsyncEnumerable(records);
        var card = await source.GenerateContextCardAsync("TestSet", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("Dataset Profile: TestSet (3 rows)", card);
        // Custom Name check
        Assert.Contains("**Custom Name** (String, 0% Null)", card);
        Assert.Contains("3 distinct categories", card);
        Assert.Contains("Top values", card);

        // Numeric checks
        Assert.Contains("**Age** (Integer, 0% Null)", card);
        Assert.Contains("Numeric range [25.00 to 35.00], Avg: 30.00.", card);

        // Nullable numeric checks
        Assert.Contains("**Score** (Decimal?, 33.3% Null)", card);
        Assert.Contains("Numeric range [80.00 to 95.50], Avg: 87.75.", card);

        // Boolean checks
        Assert.Contains("**IsActive** (Boolean, 0% Null)", card);
        Assert.Contains("Boolean. True: 2 (66.7%), False: 1 (33.3%).", card);

        // Enum and char checks
        Assert.Contains("**Category** (TestEnum, 0% Null)", card);
        Assert.Contains("**Symbol** (Char, 0% Null)", card);

        // Complex type checks (DateTime)
        Assert.Contains("**Created** (DateTime, 0% Null)", card);
        Assert.Contains("Complex or other type.", card);
    }

    [Fact]
    public async Task GenerateContextCard_ThrowsOnNullSource()
    {
        IEnumerable<ProfileRecord> nullEnum = null!;
        IAsyncEnumerable<ProfileRecord> nullAsync = null!;

        Assert.Throws<ArgumentNullException>(() => nullEnum.GenerateContextCard());
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await nullAsync.GenerateContextCardAsync(cancellationToken: TestContext.Current.CancellationToken));
    }

    private sealed class AmountRecord
    {
        public double Amount { get; set; }
        public double? Rate { get; set; }
        public bool Flag { get; set; }
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("en-US")]
    public void GenerateContextCard_FormatsNumbersWithInvariantCulture(string cultureName)
    {
        // The card is machine-facing text for an LLM prompt: it must not change with the
        // server's culture ("1.500,00" under de-DE, narrow no-break spaces under fr-FR).
        var records = new List<AmountRecord>
        {
            new() { Amount = 1500.25, Rate = 0.5, Flag = true },
            new() { Amount = 2500.75, Rate = null, Flag = false },
            new() { Amount = 2000.5, Rate = 1.25, Flag = true }
        };

        var original = CultureInfo.CurrentCulture;
        string card;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            card = records.GenerateContextCard("Amounts");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        Assert.Contains("Numeric range [1,500.25 to 2,500.75], Avg: 2,000.50.", card);
        Assert.Contains("**Rate** (Decimal?, 33.3% Null)", card);
        Assert.Contains("Numeric range [0.50 to 1.25], Avg: 0.88.", card);
        Assert.Contains("Boolean. True: 2 (66.7%), False: 1 (33.3%).", card);
    }

    [Fact]
    public void GenerateContextCard_FormatsRowCountWithInvariantCulture()
    {
        var records = new List<AmountRecord>();
        for (int i = 0; i < 1234; i++)
        {
            records.Add(new AmountRecord { Amount = i, Rate = i, Flag = i % 2 == 0 });
        }

        var original = CultureInfo.CurrentCulture;
        string card;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            card = records.GenerateContextCard("Many");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        Assert.Contains("Dataset Profile: Many (1,234 rows)", card);
        Assert.Contains("Boolean. True: 617 (50.0%), False: 617 (50.0%).", card);
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }
}
