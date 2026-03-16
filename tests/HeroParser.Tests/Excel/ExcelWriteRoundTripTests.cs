using Xunit;

namespace HeroParser.Tests.Excel;

/// <summary>
/// End-to-end round-trip tests for the Excel write and read APIs.
/// Covers all supported CLR types, unicode content, empty collections, and large datasets.
/// </summary>
[Trait("Category", "Integration")]
public class ExcelWriteRoundTripTests
{
    // ──────────────────────────────────────────────
    // All supported primitive types
    // ──────────────────────────────────────────────

    [Fact]
    public void AllSupportedTypes_RoundTrip_ValuesPreserved()
    {
        var now = new DateTime(2025, 6, 15, 14, 30, 0, DateTimeKind.Unspecified);
        var today = new DateOnly(2025, 6, 15);
        var id = Guid.Parse("d3e4f5a6-b7c8-4900-8001-234567890abc");

        var records = new List<AllTypesRecord>
        {
            new()
            {
                StringValue = "Hello",
                IntValue = 42,
                LongValue = 9_876_543_210L,
                DecimalValue = 1234.56m,
                DoubleValue = 3.14159,
                FloatValue = 2.71f,
                BoolValue = true,
                DateTimeValue = now,
                DateOnlyValue = today,
                GuidValue = id,
            },
        };

        // Use string formats for DateTime/DateOnly so values survive the round-trip without
        // OA date serial number parsing discrepancies.
        var bytes = HeroParser.Excel.Write<AllTypesRecord>()
            .WithDateTimeFormat("yyyy-MM-dd HH:mm:ss")
            .WithDateOnlyFormat("yyyy-MM-dd")
            .ToBytes(records);

        using var ms = new MemoryStream(bytes);
        // Read back as raw rows to inspect every cell as a string
        var rows = HeroParser.Excel.Read().FromStream(ms);

        Assert.Single(rows);
        var row = rows[0];
        Assert.Equal("Hello", row[0]);                   // StringValue
        Assert.Equal("42", row[1]);                      // IntValue
        Assert.Equal("9876543210", row[2]);               // LongValue
        Assert.StartsWith("1234", row[3]);                // DecimalValue
        Assert.StartsWith("3.14", row[4]);                // DoubleValue
        Assert.StartsWith("2.7", row[5]);                 // FloatValue
        Assert.Equal("TRUE", row[6]);                     // BoolValue (bool cell → "TRUE" after reader conversion)
        Assert.Equal("2025-06-15 14:30:00", row[7]);      // DateTimeValue
        Assert.Equal("2025-06-15", row[8]);               // DateOnlyValue
        Assert.Equal(id.ToString(), row[9]);              // GuidValue
    }

    [Fact]
    public void BoolValues_TrueAndFalse_RoundTripCorrectly()
    {
        var records = new List<BoolRecord>
        {
            new() { Flag = true },
            new() { Flag = false },
        };

        var bytes = HeroParser.Excel.Write<BoolRecord>().ToBytes(records);

        using var ms = new MemoryStream(bytes);
        var rows = HeroParser.Excel.Read().FromStream(ms);

        Assert.Equal(2, rows.Count);
        // Boolean cells are stored as b-type cells; the reader converts them to "TRUE"/"FALSE"
        Assert.Equal("TRUE", rows[0][0]);
        Assert.Equal("FALSE", rows[1][0]);
    }

    // ──────────────────────────────────────────────
    // Unicode content
    // ──────────────────────────────────────────────

    [Fact]
    public void UnicodeContent_Chinese_RoundTripPreservesCharacters()
    {
        var records = new List<SimpleProduct>
        {
            new() { Name = "中文产品", Price = 10.00m, Quantity = 1 },
        };

        var bytes = HeroParser.Excel.Write<SimpleProduct>().ToBytes(records);

        using var ms = new MemoryStream(bytes);
        var readBack = HeroParser.Excel.Read<SimpleProduct>().FromStream(ms);

        Assert.Single(readBack);
        Assert.Equal("中文产品", readBack[0].Name);
    }

    [Fact]
    public void UnicodeContent_Arabic_RoundTripPreservesCharacters()
    {
        var records = new List<SimpleProduct>
        {
            new() { Name = "منتج عربي", Price = 20.00m, Quantity = 2 },
        };

        var bytes = HeroParser.Excel.Write<SimpleProduct>().ToBytes(records);

        using var ms = new MemoryStream(bytes);
        var readBack = HeroParser.Excel.Read<SimpleProduct>().FromStream(ms);

        Assert.Single(readBack);
        Assert.Equal("منتج عربي", readBack[0].Name);
    }

    [Fact]
    public void UnicodeContent_Emoji_RoundTripPreservesCharacters()
    {
        var records = new List<SimpleProduct>
        {
            new() { Name = "Widget 🚀", Price = 99.99m, Quantity = 3 },
        };

        var bytes = HeroParser.Excel.Write<SimpleProduct>().ToBytes(records);

        using var ms = new MemoryStream(bytes);
        var readBack = HeroParser.Excel.Read<SimpleProduct>().FromStream(ms);

        Assert.Single(readBack);
        Assert.Equal("Widget 🚀", readBack[0].Name);
    }

    [Fact]
    public void UnicodeContent_MixedScripts_RoundTripPreservesCharacters()
    {
        var records = new List<SimpleProduct>
        {
            new() { Name = "Alpha 中文 ™ 🎯", Price = 1.00m, Quantity = 1 },
        };

        var bytes = HeroParser.Excel.Write<SimpleProduct>().ToBytes(records);

        using var ms = new MemoryStream(bytes);
        var readBack = HeroParser.Excel.Read<SimpleProduct>().FromStream(ms);

        Assert.Single(readBack);
        Assert.Equal("Alpha 中文 ™ 🎯", readBack[0].Name);
    }

    // ──────────────────────────────────────────────
    // Empty collection
    // ──────────────────────────────────────────────

    [Fact]
    public void EmptyCollection_WritesHeaderOnly_ReadBackAsEmptyList()
    {
        var bytes = HeroParser.Excel.Write<SimpleProduct>().ToBytes([]);

        using var ms = new MemoryStream(bytes);
        var readBack = HeroParser.Excel.Read<SimpleProduct>().FromStream(ms);

        Assert.Empty(readBack);
    }

    [Fact]
    public void EmptyCollection_WithoutHeader_WritesNoRows_ReadBackAsEmptyList()
    {
        var bytes = HeroParser.Excel.Write<SimpleProduct>()
            .WithoutHeader()
            .ToBytes([]);

        using var ms = new MemoryStream(bytes);
        var rows = HeroParser.Excel.Read().WithoutHeader().FromStream(ms);

        Assert.Empty(rows);
    }

    // ──────────────────────────────────────────────
    // Large dataset
    // ──────────────────────────────────────────────

    [Fact]
    public void LargeDataset_1000Records_RoundTripCountMatches()
    {
        var records = Enumerable.Range(1, 1000)
            .Select(i => new SimpleProduct
            {
                Name = $"Product{i}",
                Price = i * 0.99m,
                Quantity = i,
            })
            .ToList();

        var bytes = HeroParser.Excel.Write<SimpleProduct>().ToBytes(records);

        using var ms = new MemoryStream(bytes);
        var readBack = HeroParser.Excel.Read<SimpleProduct>().FromStream(ms);

        Assert.Equal(1000, readBack.Count);
        Assert.Equal("Product1", readBack[0].Name);
        Assert.Equal("Product1000", readBack[999].Name);
        Assert.Equal(1000, readBack[999].Quantity);
    }

    // ──────────────────────────────────────────────
    // SerializeRecords / DeserializeRecords facade
    // ──────────────────────────────────────────────

    [Fact]
    public void SerializeDeserialize_RoundTripProducesEquivalentRecords()
    {
        var records = new List<SimpleProduct>
        {
            new() { Name = "Serialize1", Price = 1.11m, Quantity = 11 },
            new() { Name = "Serialize2", Price = 2.22m, Quantity = 22 },
        };

        var bytes = HeroParser.Excel.SerializeRecords(records);

        using var ms = new MemoryStream(bytes);
        var readBack = HeroParser.Excel.DeserializeRecords<SimpleProduct>(ms);

        Assert.Equal(2, readBack.Count);
        Assert.Equal("Serialize1", readBack[0].Name);
        Assert.Equal(1.11m, readBack[0].Price);
        Assert.Equal(11, readBack[0].Quantity);
        Assert.Equal("Serialize2", readBack[1].Name);
    }
}

/// <summary>A record covering all CLR types supported by the Excel writer.</summary>
[GenerateBinder]
public class AllTypesRecord
{
    /// <summary>Gets or sets a string value.</summary>
    [TabularMap(Name = "StringValue")]
    public string StringValue { get; set; } = "";

    /// <summary>Gets or sets an int value.</summary>
    [TabularMap(Name = "IntValue")]
    public int IntValue { get; set; }

    /// <summary>Gets or sets a long value.</summary>
    [TabularMap(Name = "LongValue")]
    public long LongValue { get; set; }

    /// <summary>Gets or sets a decimal value.</summary>
    [TabularMap(Name = "DecimalValue")]
    public decimal DecimalValue { get; set; }

    /// <summary>Gets or sets a double value.</summary>
    [TabularMap(Name = "DoubleValue")]
    public double DoubleValue { get; set; }

    /// <summary>Gets or sets a float value.</summary>
    [TabularMap(Name = "FloatValue")]
    public float FloatValue { get; set; }

    /// <summary>Gets or sets a bool value.</summary>
    [TabularMap(Name = "BoolValue")]
    public bool BoolValue { get; set; }

    /// <summary>Gets or sets a DateTime value.</summary>
    [TabularMap(Name = "DateTimeValue")]
    public DateTime DateTimeValue { get; set; }

    /// <summary>Gets or sets a DateOnly value.</summary>
    [TabularMap(Name = "DateOnlyValue")]
    public DateOnly DateOnlyValue { get; set; }

    /// <summary>Gets or sets a Guid value.</summary>
    [TabularMap(Name = "GuidValue")]
    public Guid GuidValue { get; set; }
}

/// <summary>A simple record for bool round-trip testing.</summary>
public class BoolRecord
{
    /// <summary>Gets or sets the flag value.</summary>
    [TabularMap(Name = "Flag")]
    public bool Flag { get; set; }
}
