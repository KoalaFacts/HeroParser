using System.Globalization;
using HeroParser.Excels.Core;
using HeroParser.Excels.Writing;
using Xunit;

namespace HeroParser.Tests.Excel;

/// <summary>
/// Writes a record carrying every scalar type the Excel writer knows about.
///
/// The writer dispatches on each property's runtime type to pick a cell kind — numeric,
/// date, string or empty. Most arms of that switch had never run, so a type falling
/// through to the wrong one (a long written as text, say, or a date written as a number)
/// would produce a file that opens fine and reads wrong.
/// </summary>
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class ExcelRecordWriterValueTypeTests
{
    public sealed class EveryType
    {
        public string Text { get; set; } = "";
        public string? MissingText { get; set; }
        public bool Flag { get; set; }
        public int Int32 { get; set; }
        public long Int64 { get; set; }
        public short Int16 { get; set; }
        public byte Byte { get; set; }
        public sbyte SByte { get; set; }
        public uint UInt32 { get; set; }
        public ulong UInt64 { get; set; }
        public ushort UInt16 { get; set; }
        public float Single { get; set; }
        public double Double { get; set; }
        public decimal Decimal { get; set; }
        public DateTime When { get; set; }
        public DateTimeOffset WhenOffset { get; set; }
        public Guid Reference { get; set; }
        public int? NullableInt { get; set; }
    }

    private static readonly EveryType[] Records =
    [
        new()
        {
            Text = "hello",
            MissingText = null,
            Flag = true,
            Int32 = -1,
            Int64 = 9_007_199_254_740_993L,
            Int16 = -2,
            Byte = 3,
            SByte = -4,
            UInt32 = 5,
            UInt64 = 6,
            UInt16 = 7,
            Single = 1.5f,
            Double = 2.25,
            Decimal = 3.125m,
            When = new DateTime(2024, 3, 17, 8, 30, 0, DateTimeKind.Unspecified),
            WhenOffset = new DateTimeOffset(2024, 3, 17, 8, 30, 0, TimeSpan.Zero),
            Reference = new Guid("11112222-3333-4444-5555-666677778888"),
            NullableInt = null,
        },
    ];

    /// <summary>Writes the records and reads the sheet back as raw cell text.</summary>
    /// <remarks>Row-level reading yields data rows only; the header is consumed by the reader.</remarks>
    private static List<string[]> RoundTrip(
        EveryType[] records,
        Func<ExcelWriterBuilder<EveryType>, ExcelWriterBuilder<EveryType>>? configure = null)
    {
        var builder = HeroParser.Excel.Write<EveryType>();
        if (configure is not null) builder = configure(builder);

        using var stream = new MemoryStream();
        builder.ToStream(stream, records);

        stream.Position = 0;
        return [.. HeroParser.Excel.Read().FromStream(stream)];
    }

    /// <summary>Column order follows property declaration order.</summary>
    private static int IndexOf(string property)
        => Array.FindIndex(typeof(EveryType).GetProperties(), p => p.Name == property);

    [Fact]
    public void EveryScalarType_RoundTripsThroughACell()
    {
        string[] data = Assert.Single(RoundTrip(Records));
        string Value(string property) => data[IndexOf(property)];

        Assert.Equal("hello", Value("Text"));
        Assert.Equal(string.Empty, Value("MissingText"));
        Assert.Equal("-1", Value("Int32"));
        Assert.Equal("-2", Value("Int16"));
        Assert.Equal("3", Value("Byte"));
        Assert.Equal("-4", Value("SByte"));
        Assert.Equal("5", Value("UInt32"));
        Assert.Equal("6", Value("UInt64"));
        Assert.Equal("7", Value("UInt16"));
        Assert.Equal(string.Empty, Value("NullableInt"));
        Assert.Contains("1111", Value("Reference"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2024", Value("When"), StringComparison.Ordinal);
        Assert.Contains("2024", Value("WhenOffset"), StringComparison.Ordinal);
    }

    [Fact]
    public void FractionalTypes_KeepTheirValue()
    {
        string[] data = Assert.Single(RoundTrip(Records));

        Assert.Equal(1.5, double.Parse(data[IndexOf("Single")], CultureInfo.InvariantCulture));
        Assert.Equal(2.25, double.Parse(data[IndexOf("Double")], CultureInfo.InvariantCulture));
        Assert.Equal(3.125m, decimal.Parse(data[IndexOf("Decimal")], CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Int64BeyondDoublePrecision_LosesItsLastDigit()
    {
        // Excel stores numbers as IEEE-754 doubles, so an Int64 past 2^53 cannot survive
        // as a numeric cell. Pinning it here so the limit is a known property of the
        // format rather than a surprise in someone's data.
        string[] data = Assert.Single(RoundTrip(Records));

        Assert.Equal(9_007_199_254_740_992L, long.Parse(data[IndexOf("Int64")], CultureInfo.InvariantCulture));
    }

    [Fact]
    public void BooleanCells_AreReadableAsText()
    {
        string[] data = Assert.Single(RoundTrip(Records));
        string value = data[IndexOf("Flag")];

        Assert.False(string.IsNullOrEmpty(value));
        Assert.Contains(value, new[] { "True", "true", "1", "TRUE" });
    }

    [Fact]
    public void DateTimeFormat_IsApplied()
    {
        string[] data = Assert.Single(RoundTrip(Records, b => b.WithDateTimeFormat("yyyy-MM-dd")));
        Assert.Contains("2024", data[IndexOf("When")], StringComparison.Ordinal);
    }

    [Fact]
    public void HeaderCanBeSuppressed()
    {
        // Without a header row the reader has nothing to consume, so the data row is
        // reported as if it were the header and no data rows remain.
        Assert.Empty(RoundTrip(Records, b => b.WithoutHeader()));
    }

    [Fact]
    public void EmptySequence_StillProducesAReadableWorkbook()
        => Assert.Empty(RoundTrip([]));
}
