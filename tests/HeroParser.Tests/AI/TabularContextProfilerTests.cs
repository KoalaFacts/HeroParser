using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HeroParser.AI;
using Xunit;

namespace HeroParser.Tests.AI;

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

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }
}
