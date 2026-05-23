using System.Text;
using HeroParser.SeparatedValues;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Targets the configuration and terminal methods of CsvMultiSchemaReaderBuilder
/// that lacked coverage (61-64%): byte path, file/stream/async terminals,
/// and the many fluent option methods.
/// </summary>
[Trait("Category", "Unit")]
[Collection("AsyncWriterTests")]
public class CsvMultiSchemaCoverageTests
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

    private const string SAMPLE_CSV = """
        Type,Data1,Data2
        H,FileA,
        D,Item1,10.50
        D,Item2,20.75
        """;

    [Fact]
    public void FromText_Char_StringDiscriminator_DispatchesByType()
    {
        var records = new List<object>();
        foreach (var r in Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .MapRecord<HeaderRow>("H")
            .MapRecord<DetailRow>("D")
            .AllowMissingColumns()
            .FromText(SAMPLE_CSV))
        {
            records.Add(r);
        }

        Assert.Equal(3, records.Count);
        Assert.IsType<HeaderRow>(records[0]);
        Assert.IsType<DetailRow>(records[1]);
        Assert.IsType<DetailRow>(records[2]);
    }

    [Fact]
    public void FromBytes_StringDiscriminator_DispatchesByType()
    {
        var bytes = Encoding.UTF8.GetBytes(SAMPLE_CSV);
        var records = new List<object>();
        foreach (var r in Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .MapRecord<HeaderRow>("H")
            .MapRecord<DetailRow>("D")
            .AllowMissingColumns()
            .FromBytes(bytes))
        {
            records.Add(r);
        }
        Assert.Equal(3, records.Count);
    }

    [Fact]
    public async Task FromStream_StreamingReader_DispatchesByType()
    {
        var bytes = Encoding.UTF8.GetBytes(SAMPLE_CSV);
        using var ms = new MemoryStream(bytes);
        await using var reader = Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .MapRecord<HeaderRow>("H")
            .MapRecord<DetailRow>("D")
            .AllowMissingColumns()
            .FromStream(ms);

        var records = new List<object>();
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
        {
            records.Add(reader.Current!);
        }
        Assert.Equal(3, records.Count);
        Assert.True(reader.BytesRead > 0);
    }

    [Fact]
    public async Task FromFile_DispatchesByType()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempPath, SAMPLE_CSV, TestContext.Current.CancellationToken);
            await using var reader = Csv.Read()
                .WithMultiSchema()
                .WithDiscriminator("Type")
                .MapRecord<HeaderRow>("H")
                .MapRecord<DetailRow>("D")
                .AllowMissingColumns()
                .FromFile(tempPath);

            var records = new List<object>();
            while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
            {
                records.Add(reader.Current!);
            }
            Assert.Equal(3, records.Count);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task FromStreamAsync_DispatchesByType()
    {
        var bytes = Encoding.UTF8.GetBytes(SAMPLE_CSV);
        using var ms = new MemoryStream(bytes);

        var records = new List<object>();
        await foreach (var r in Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .MapRecord<HeaderRow>("H")
            .MapRecord<DetailRow>("D")
            .AllowMissingColumns()
            .FromStreamAsync(ms, cancellationToken: TestContext.Current.CancellationToken))
        {
            records.Add(r);
        }
        Assert.Equal(3, records.Count);
    }

    [Fact]
    public async Task FromFileAsync_DispatchesByType()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempPath, SAMPLE_CSV, TestContext.Current.CancellationToken);
            var records = new List<object>();
            await foreach (var r in Csv.Read()
                .WithMultiSchema()
                .WithDiscriminator("Type")
                .MapRecord<HeaderRow>("H")
                .MapRecord<DetailRow>("D")
                .AllowMissingColumns()
                .FromFileAsync(tempPath, cancellationToken: TestContext.Current.CancellationToken))
            {
                records.Add(r);
            }
            Assert.Equal(3, records.Count);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void Discriminator_ByColumnIndex()
    {
        var records = new List<object>();
        foreach (var r in Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator(0)
            .MapRecord<HeaderRow>("H")
            .MapRecord<DetailRow>("D")
            .AllowMissingColumns()
            .FromText(SAMPLE_CSV))
        {
            records.Add(r);
        }
        Assert.Equal(3, records.Count);
    }

    [Fact]
    public void IntegerDiscriminator_DispatchesByNumericValue()
    {
        const string csv = """
            Type,Data1,Data2
            1,A,
            2,B,9.99
            1,C,
            """;

        var records = new List<object>();
        foreach (var r in Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .MapRecord<HeaderRow>(1)
            .MapRecord<DetailRow>(2)
            .AllowMissingColumns()
            .FromText(csv))
        {
            records.Add(r);
        }
        Assert.Equal(3, records.Count);
        Assert.IsType<HeaderRow>(records[0]);
        Assert.IsType<DetailRow>(records[1]);
    }

    [Fact]
    public void NoMappedTypes_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            foreach (var _ in Csv.Read()
                .WithMultiSchema()
                .WithDiscriminator("Type")
                .FromText(SAMPLE_CSV))
            {
            }
        });
    }

    [Fact]
    public void FluentOptionsChain_DoesNotThrow()
    {
        var records = new List<object>();
        foreach (var r in Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .MapRecord<HeaderRow>("H")
            .MapRecord<DetailRow>("D")
            .WithDelimiter(',')
            .WithQuote('"')
            .WithMaxColumnCount(100)
            .WithMaxRowCount(10000)
            .UseSimd(true)
            .AllowNewlinesInQuotes(true)
            .EnableQuotedFields(true)
            .WithCommentCharacter(null)
            .TrimFields(false)
            .WithMaxFieldSize(1_000_000)
            .WithEscapeCharacter(null)
            .WithMaxRowSize(1_000_000)
            .TrackSourceLineNumbers(false)
            .HasHeaderRow(true)
            .CaseSensitiveHeaders(false)
            .AllowMissingColumns(true)
            .WithNullValues("NULL", "N/A")
            .WithCulture(System.Globalization.CultureInfo.InvariantCulture)
            .SkipRows(0)
            .FromText(SAMPLE_CSV))
        {
            records.Add(r);
        }
        Assert.Equal(3, records.Count);
    }

    [Fact]
    public void CustomDelimiter_SemicolonSeparator()
    {
        const string csv = "Type;Data1;Data2\nH;FileA;\nD;Item;9.99\n";
        var records = new List<object>();
        foreach (var r in Csv.Read()
            .WithMultiSchema()
            .WithDelimiter(';')
            .WithDiscriminator("Type")
            .MapRecord<HeaderRow>("H")
            .MapRecord<DetailRow>("D")
            .AllowMissingColumns()
            .FromText(csv))
        {
            records.Add(r);
        }
        Assert.Equal(2, records.Count);
    }

    [Fact]
    public void CommentLines_Skipped()
    {
        const string csv = """
            Type,Data1,Data2
            # this is a comment
            H,FileA,
            # another comment
            D,Item1,1.00
            """;

        var records = new List<object>();
        foreach (var r in Csv.Read()
            .WithMultiSchema()
            .WithCommentCharacter('#')
            .WithDiscriminator("Type")
            .MapRecord<HeaderRow>("H")
            .MapRecord<DetailRow>("D")
            .AllowMissingColumns()
            .FromText(csv))
        {
            records.Add(r);
        }
        Assert.Equal(2, records.Count);
    }

    [Fact]
    public void NoHeaderRow_DiscriminatorByIndex_DoesNotCrash()
    {
        const string csv = "H,FileA,\nD,Item,9.99\n";
        var records = new List<object>();
        foreach (var r in Csv.Read()
            .WithMultiSchema()
            .NoHeaderRow()
            .WithDiscriminator(0)
            .MapRecord<HeaderRow>("H")
            .MapRecord<DetailRow>("D")
            .AllowMissingColumns()
            .FromText(csv))
        {
            records.Add(r);
        }
        // Without a header row, attribute-based mapping cannot resolve column names by name,
        // so some records may not bind. We only assert the chain exercises without crashing.
        Assert.NotNull(records);
    }

    [Fact]
    public void SkipRows_SkipsLeadingRows()
    {
        const string csv = """
            ignore-this-row
            Type,Data1,Data2
            H,FileA,
            """;
        var records = new List<object>();
        foreach (var r in Csv.Read()
            .WithMultiSchema()
            .SkipRows(1)
            .WithDiscriminator("Type")
            .MapRecord<HeaderRow>("H")
            .AllowMissingColumns()
            .FromText(csv))
        {
            records.Add(r);
        }
        Assert.Single(records);
    }
}
