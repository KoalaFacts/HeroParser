using Xunit;
using HeroParser.Htbs;
using HeroParser.Validation;

namespace HeroParser.Tests.Htb;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class HtbTests
{
    public class HtbTestRecord
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";

        [TabularMap(Index = 2)]
        public double? Score { get; set; }

        public DateTime CreatedAt { get; set; }
        public decimal Balance { get; set; }
        public bool IsActive { get; set; }
        public Guid ReferenceId { get; set; }
        public float[]? Embedding { get; set; }
    }

    public class ValidatedRecord
    {
        public int Id { get; set; }

        [Validate(NotEmpty = true, MinLength = 3)]
        public string Name { get; set; } = "";

        [Validate(RangeMin = 0, RangeMax = 100)]
        public double Score { get; set; }
    }

    [Fact]
    public void TestRoundTripBasicTypes()
    {
        var records = new List<HtbTestRecord>
        {
            new()
            {
                Id = 1,
                Name = "Alice",
                Score = 95.5,
                CreatedAt = new DateTime(2026, 5, 28, 12, 0, 0),
                Balance = 1500.75m,
                IsActive = true,
                ReferenceId = Guid.NewGuid(),
                Embedding = [0.1f, -0.2f, 0.3f]
            },
            new()
            {
                Id = 2,
                Name = "Bob",
                Score = null, // Test nullable
                CreatedAt = new DateTime(2026, 5, 27, 10, 0, 0),
                Balance = -100.50m,
                IsActive = false,
                ReferenceId = Guid.Empty,
                Embedding = null
            }
        };

        using var ms = new MemoryStream();
        HeroParser.Htb.Write<HtbTestRecord>()
            .ToStream(ms, records, leaveOpen: true);

        ms.Position = 0;

        var readRecords = HeroParser.Htb.Read<HtbTestRecord>()
            .FromStream(ms, leaveOpen: true)
            .ToList();

        Assert.Equal(records.Count, readRecords.Count);

        for (int i = 0; i < records.Count; i++)
        {
            Assert.Equal(records[i].Id, readRecords[i].Id);
            Assert.Equal(records[i].Name, readRecords[i].Name);
            Assert.Equal(records[i].Score, readRecords[i].Score);
            Assert.Equal(records[i].CreatedAt, readRecords[i].CreatedAt);
            Assert.Equal(records[i].Balance, readRecords[i].Balance);
            Assert.Equal(records[i].IsActive, readRecords[i].IsActive);
            Assert.Equal(records[i].ReferenceId, readRecords[i].ReferenceId);
            Assert.Equal(records[i].Embedding, readRecords[i].Embedding);
        }
    }

    [Fact]
    public async Task TestRoundTripAsyncParity()
    {
        var records = new List<HtbTestRecord>
        {
            new()
            {
                Id = 100,
                Name = "Async Tester",
                Score = 88.0,
                CreatedAt = DateTime.UtcNow,
                Balance = 9999.99m,
                IsActive = true,
                ReferenceId = Guid.NewGuid(),
                Embedding = [1.0f, 2.0f, 3.0f]
            }
        };

        using var ms = new MemoryStream();
        await HeroParser.Htb.Write<HtbTestRecord>()
            .ToStreamAsync(ms, records, leaveOpen: true);

        ms.Position = 0;

        var readRecords = new List<HtbTestRecord>();
        await foreach (var rec in HeroParser.Htb.Read<HtbTestRecord>().FromStreamAsync(ms, leaveOpen: true))
        {
            readRecords.Add(rec);
        }

        Assert.Single(readRecords);
        Assert.Equal(records[0].Id, readRecords[0].Id);
        Assert.Equal(records[0].Name, readRecords[0].Name);
        Assert.Equal(records[0].Score, readRecords[0].Score);
        Assert.Equal(records[0].Balance, readRecords[0].Balance);
        Assert.Equal(records[0].Embedding, readRecords[0].Embedding);
    }

    [Fact]
    public void TestMaxRowCountLimit()
    {
        var records = new List<HtbTestRecord>
        {
            new() { Id = 1, CreatedAt = DateTime.UtcNow },
            new() { Id = 2, CreatedAt = DateTime.UtcNow },
            new() { Id = 3, CreatedAt = DateTime.UtcNow }
        };

        using var ms = new MemoryStream();

        // 1. Test writer limit
        Assert.Throws<HtbException>(() =>
            HeroParser.Htb.Write<HtbTestRecord>()
                .WithMaxRowCount(2)
                .ToStream(ms, records, leaveOpen: true));

        // Clear and write normally
        ms.SetLength(0);
        HeroParser.Htb.Write<HtbTestRecord>().ToStream(ms, records, leaveOpen: true);
        ms.Position = 0;

        // 2. Test reader limit
        Assert.Throws<HtbException>(() =>
            HeroParser.Htb.Read<HtbTestRecord>()
                .WithMaxRowCount(2)
                .FromStream(ms, leaveOpen: true)
                .ToList());
    }

    [Fact]
    public void TestMaxOutputSizeLimit()
    {
        var records = new List<HtbTestRecord>
        {
            new() { Id = 1, Name = "Large record to exceed size threshold", CreatedAt = DateTime.UtcNow }
        };

        using var ms = new MemoryStream();
        Assert.Throws<HtbException>(() =>
            HeroParser.Htb.Write<HtbTestRecord>()
                .WithMaxOutputSize(10) // 10 bytes is too small for header + 1 record
                .ToStream(ms, records, leaveOpen: true));
    }

    [Fact]
    public void TestValidationStrictValidationMode()
    {
        var invalidRecord = new ValidatedRecord
        {
            Id = 1,
            Name = "X", // Invalid: min length is 3
            Score = 150.0 // Invalid: max is 100
        };

        using var ms = new MemoryStream();
        HeroParser.Htb.Write<ValidatedRecord>().ToStream(ms, [invalidRecord], leaveOpen: true);
        ms.Position = 0;

        // Strict validation mode should throw during parsing
        Assert.Throws<HtbException>(() =>
            HeroParser.Htb.Read<ValidatedRecord>()
                .WithValidationMode(ValidationMode.Strict)
                .FromStream(ms, leaveOpen: true)
                .ToList());
    }

    [Fact]
    public void TestValidationLenientValidationModeWithSkip()
    {
        var records = new List<ValidatedRecord>
        {
            new() { Id = 1, Name = "Invalid", Score = -10.0 }, // Invalid: min is 0
            new() { Id = 2, Name = "Valid User", Score = 85.0 } // Valid
        };

        using var ms = new MemoryStream();
        HeroParser.Htb.Write<ValidatedRecord>().ToStream(ms, records, leaveOpen: true);
        ms.Position = 0;

        // Lenient mode skips invalid and yields valid
        var parsed = HeroParser.Htb.Read<ValidatedRecord>()
            .WithValidationMode(ValidationMode.Lenient)
            .OnError((ctx, ex) => HtbDeserializeErrorAction.SkipRecord)
            .FromStream(ms, leaveOpen: true)
            .ToList();

        Assert.Single(parsed);
        Assert.Equal(2, parsed[0].Id);
        Assert.Equal("Valid User", parsed[0].Name);
    }

    [Fact]
    public void TestProgressReporting()
    {
        var records = new List<HtbTestRecord>();
        for (int i = 0; i < 10; i++)
        {
            records.Add(new HtbTestRecord { Id = i, CreatedAt = DateTime.UtcNow });
        }

        using var ms = new MemoryStream();
        long progressWrittenCount = 0;
        var writeProgress = new SyncProgress<HtbWriteProgress>(p => progressWrittenCount = p.RecordsWritten);

        HeroParser.Htb.Write<HtbTestRecord>()
            .WithProgress(writeProgress, intervalRows: 2)
            .ToStream(ms, records, leaveOpen: true);

        Assert.Equal(10, progressWrittenCount);

        ms.Position = 0;
        long progressReadCount = 0;
        var readProgress = new SyncProgress<HtbProgress>(p => progressReadCount = p.RecordsRead);

        var parsed = HeroParser.Htb.Read<HtbTestRecord>()
            .WithProgress(readProgress, intervalRows: 2)
            .FromStream(ms, leaveOpen: true)
            .ToList();

        Assert.Equal(10, progressReadCount);
        Assert.Equal(10, parsed.Count);
    }

    [Fact]
    public void TestInvalidMagicBytes()
    {
        byte[] badBytes = [0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00];
        using var ms = new MemoryStream(badBytes);

        Assert.Throws<HtbException>(() =>
            HeroParser.Htb.Read<HtbTestRecord>()
                .FromStream(ms)
                .ToList());
    }

    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
