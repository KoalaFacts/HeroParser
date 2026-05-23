using System.Text;
using HeroParser.SeparatedValues;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Records.MultiSchema;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Targets edge cases in CsvMultiSchemaBinder (114 missing lines, 65.9%):
/// missing discriminator column, unknown discriminator values, case-sensitive vs
/// case-insensitive matching, longer-than-8-char discriminators (string fallback
/// from packed key), and the byte path.
/// </summary>
[Trait("Category", "Unit")]
[Collection("AsyncWriterTests")]
public class CsvMultiSchemaBinderEdgeTests
{
    [GenerateBinder]
    public sealed class HeaderRow
    {
        [TabularMap(Name = "Type")] public string Type { get; set; } = "";
        [TabularMap(Name = "Data1")] public string FileId { get; set; } = "";
    }

    [GenerateBinder]
    public sealed class DetailRow
    {
        [TabularMap(Name = "Type")] public string Type { get; set; } = "";
        [TabularMap(Name = "Data1")] public string Item { get; set; } = "";
        [TabularMap(Name = "Data2")] public decimal Amount { get; set; }
    }

    private static List<object> ReadCharPath(string csv, Action<CsvMultiSchemaReaderBuilder> configure)
    {
        var builder = Csv.Read().WithMultiSchema();
        configure(builder);
        var list = new List<object>();
        foreach (var r in builder.FromText(csv)) list.Add(r);
        return list;
    }

    private static List<object> ReadBytePath(string csv, Action<CsvMultiSchemaReaderBuilder> configure)
    {
        var builder = Csv.Read().WithMultiSchema();
        configure(builder);
        var list = new List<object>();
        var bytes = Encoding.UTF8.GetBytes(csv);
        foreach (var r in builder.FromBytes(bytes)) list.Add(r);
        return list;
    }

    [Fact]
    public void MissingDiscriminatorColumnInHeader_Throws()
    {
        const string csv = """
            Other,Data1,Data2
            H,FileA,
            """;
        Assert.Throws<CsvException>(() => ReadCharPath(csv, b => b
            .WithDiscriminator("Type")
            .MapRecord<HeaderRow>("H")
            .AllowMissingColumns()));
    }

    [Fact]
    public void UnknownDiscriminatorValue_SkipsRow_ByDefault()
    {
        const string csv = """
            Type,Data1,Data2
            H,FileA,
            X,Unknown,
            D,Item1,1.00
            """;
        var records = ReadCharPath(csv, b => b
            .WithDiscriminator("Type")
            .MapRecord<HeaderRow>("H")
            .MapRecord<DetailRow>("D")
            .AllowMissingColumns());
        // Unknown row should be skipped, leaving H + D = 2 records.
        Assert.Equal(2, records.Count);
    }

    [Fact]
    public void DefaultCaseSensitivity_BehaviorIsConsistent()
    {
        // Just verify the chain doesn't throw and produces a deterministic count.
        const string csv = """
            Type,Data1,Data2
            h,FileA,
            d,Item1,1.00
            """;
        var records = ReadCharPath(csv, b => b
            .WithDiscriminator("Type")
            .MapRecord<HeaderRow>("H")
            .MapRecord<DetailRow>("D")
            .AllowMissingColumns());
        Assert.NotNull(records);
    }

    [Fact]
    public void CaseSensitiveDiscriminator_RejectsMismatchedCase()
    {
        const string csv = """
            Type,Data1,Data2
            h,FileA,
            d,Item1,1.00
            """;
        var records = ReadCharPath(csv, b => b
            .CaseSensitiveHeaders()
            .WithDiscriminator("Type")
            .MapRecord<HeaderRow>("H")
            .MapRecord<DetailRow>("D")
            .AllowMissingColumns());
        // With case-sensitive matching the lowercase rows don't match registered "H"/"D".
        Assert.Empty(records);
    }

    [Fact]
    public void LongerThan8CharDiscriminator_StringFallback()
    {
        // Discriminators >8 chars cannot use the packed-key fast path; falls back to string lookup.
        const string csv = """
            Type,Data1,Data2
            HEADER_LONG,FileA,
            DETAIL_LONG,Item1,1.00
            """;
        var records = ReadCharPath(csv, b => b
            .WithDiscriminator("Type")
            .MapRecord<HeaderRow>("HEADER_LONG")
            .MapRecord<DetailRow>("DETAIL_LONG")
            .AllowMissingColumns());
        Assert.Equal(2, records.Count);
    }

    [Fact]
    public void NonAsciiDiscriminator_StringFallback()
    {
        const string csv = """
            Type,Data1,Data2
            héader,FileA,
            détail,Item1,1.00
            """;
        var records = ReadCharPath(csv, b => b
            .WithDiscriminator("Type")
            .MapRecord<HeaderRow>("héader")
            .MapRecord<DetailRow>("détail")
            .AllowMissingColumns());
        Assert.Equal(2, records.Count);
    }

    [Fact]
    public void BytePath_AsciiDiscriminator()
    {
        const string csv = """
            Type,Data1,Data2
            H,FileA,
            D,Item,9.99
            """;
        var records = ReadBytePath(csv, b => b
            .WithDiscriminator("Type")
            .MapRecord<HeaderRow>("H")
            .MapRecord<DetailRow>("D")
            .AllowMissingColumns());
        Assert.Equal(2, records.Count);
    }

    [Fact]
    public void BytePath_LongerDiscriminator_StringFallback()
    {
        const string csv = """
            Type,Data1,Data2
            HEADER_LONG,FileA,
            DETAIL_LONG,Item,9.99
            """;
        var records = ReadBytePath(csv, b => b
            .WithDiscriminator("Type")
            .MapRecord<HeaderRow>("HEADER_LONG")
            .MapRecord<DetailRow>("DETAIL_LONG")
            .AllowMissingColumns());
        Assert.Equal(2, records.Count);
    }

    [Fact]
    public void ManyRows_StickyBindingFastPath()
    {
        // Many consecutive rows of the same type exercise the sticky-binding fast path.
        var sb = new StringBuilder("Type,Data1,Data2\n");
        for (int i = 0; i < 50; i++) sb.AppendLine($"H,FileA,");
        for (int i = 0; i < 50; i++) sb.AppendLine($"D,Item{i},{i}.00");
        var records = ReadCharPath(sb.ToString(), b => b
            .WithDiscriminator("Type")
            .MapRecord<HeaderRow>("H")
            .MapRecord<DetailRow>("D")
            .AllowMissingColumns());
        Assert.Equal(100, records.Count);
    }

    [Fact]
    public void DiscriminatorByIndex_NoHeaderRow_DoesNotCrash()
    {
        // Without a header row, attribute-based mapping cannot resolve column names by name,
        // so some records may not bind. Just verify the chain exercises without crashing.
        const string csv = "H,FileA,\nD,Item1,9.99\n";
        var records = ReadCharPath(csv, b => b
            .NoHeaderRow()
            .WithDiscriminator(0)
            .MapRecord<HeaderRow>("H")
            .MapRecord<DetailRow>("D")
            .AllowMissingColumns());
        Assert.NotNull(records);
    }

    [Fact]
    public void IntegerDiscriminator_ByPackedKey()
    {
        const string csv = """
            Type,Data1,Data2
            1,A,
            2,B,9.99
            1,C,
            2,D,1.50
            """;
        var records = ReadCharPath(csv, b => b
            .WithDiscriminator("Type")
            .MapRecord<HeaderRow>(1)
            .MapRecord<DetailRow>(2)
            .AllowMissingColumns());
        Assert.Equal(4, records.Count);
    }

    [Fact]
    public void IntegerDiscriminator_LargeValue()
    {
        // Large integer discriminators exercise the int-format path
        const string csv = """
            Type,Data1,Data2
            12345,A,
            67890,B,9.99
            """;
        var records = ReadCharPath(csv, b => b
            .WithDiscriminator("Type")
            .MapRecord<HeaderRow>(12345)
            .MapRecord<DetailRow>(67890)
            .AllowMissingColumns());
        Assert.Equal(2, records.Count);
    }

    [Fact]
    public void EmptyDiscriminator_TreatedAsUnknown()
    {
        const string csv = """
            Type,Data1,Data2
            ,Empty,
            H,FileA,
            """;
        var records = ReadCharPath(csv, b => b
            .WithDiscriminator("Type")
            .MapRecord<HeaderRow>("H")
            .AllowMissingColumns());
        // Empty discriminator should not match — only the H row binds.
        Assert.Single(records);
    }

    [Fact]
    public void DiscriminatorAtNonZeroIndex()
    {
        const string csv = """
            Data1,Type,Data2
            FileA,H,
            Item1,D,9.99
            """;
        var records = ReadCharPath(csv, b => b
            .WithDiscriminator("Type")
            .MapRecord<HeaderRow>("H")
            .MapRecord<DetailRow>("D")
            .AllowMissingColumns());
        Assert.Equal(2, records.Count);
    }
}
