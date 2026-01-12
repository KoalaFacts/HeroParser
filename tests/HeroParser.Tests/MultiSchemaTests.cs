using HeroParser.SeparatedValues;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Records.MultiSchema;
using HeroParser.SeparatedValues.Reading.Rows;
using HeroParser.SeparatedValues.Reading.Shared;
using System.Text;
using Xunit;

namespace HeroParser.Tests;

/// <summary>
/// Tests for multi-schema CSV parsing where different rows map to different record types.
/// </summary>
[Collection("AsyncWriterTests")]
public class MultiSchemaTests
{
    #region Basic Functionality

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void StringDiscriminator_ParsesDifferentRecordTypes()
    {
        var csv = """
            Type,Data1,Data2,Data3
            H,FileId1,2024-01-01,
            D,Item1,100.50,Description1
            D,Item2,200.75,Description2
            T,2,301.25,
            """;

        var results = new List<object>();
        foreach (var record in Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .MapRecord<HeaderRecord>("H")
            .MapRecord<DetailRecord>("D")
            .MapRecord<TrailerRecord>("T")
            .AllowMissingColumns()
            .FromText(csv))
        {
            results.Add(record);
        }

        Assert.Equal(4, results.Count);

        var header = Assert.IsType<HeaderRecord>(results[0]);
        Assert.Equal("FileId1", header.FileId);
        Assert.Equal(new DateOnly(2024, 1, 1), header.Date);

        var detail1 = Assert.IsType<DetailRecord>(results[1]);
        Assert.Equal("Item1", detail1.ItemId);
        Assert.Equal(100.50m, detail1.Amount);
        Assert.Equal("Description1", detail1.Description);

        var detail2 = Assert.IsType<DetailRecord>(results[2]);
        Assert.Equal("Item2", detail2.ItemId);
        Assert.Equal(200.75m, detail2.Amount);

        var trailer = Assert.IsType<TrailerRecord>(results[3]);
        Assert.Equal(2, trailer.RecordCount);
        Assert.Equal(301.25m, trailer.TotalAmount);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void StringDiscriminator_ParsesStructRecordTypes()
    {
        var csv = """
            Type,Value
            H,HeaderValue
            D,DetailValue
            """;

        var results = new List<object>();
        foreach (var record in Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .MapRecord<StructHeader>("H")
            .MapRecord<StructDetail>("D")
            .AllowMissingColumns()
            .FromText(csv))
        {
            results.Add(record);
        }

        Assert.Equal(2, results.Count);

        var header = Assert.IsType<StructHeader>(results[0]);
        Assert.Equal("HeaderValue", header.Value);

        var detail = Assert.IsType<StructDetail>(results[1]);
        Assert.Equal("DetailValue", detail.Value);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void IntegerDiscriminator_ParsesRecordTypes()
    {
        var csv = """
            RecordType,Value1,Value2
            1,FileHeader,V1
            5,BatchItem,V2
            9,FileControl,V3
            """;

        var results = new List<object>();
        foreach (var record in Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("RecordType")
            .MapRecord<FileHeader>(1)
            .MapRecord<BatchItem>(5)
            .MapRecord<FileControl>(9)
            .AllowMissingColumns()
            .FromText(csv))
        {
            results.Add(record);
        }

        Assert.Equal(3, results.Count);
        Assert.IsType<FileHeader>(results[0]);
        Assert.IsType<BatchItem>(results[1]);
        Assert.IsType<FileControl>(results[2]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DiscriminatorByIndex_WorksWithoutHeaderRow()
    {
        var csv = """
            H,FileId1,2024-01-01
            D,Item1,100.50
            T,1,100.50
            """;

        var results = new List<object>();
        foreach (var record in Csv.Read()
            .WithMultiSchema()
            .NoHeaderRow()
            .WithDiscriminator(0)
            .MapRecord<NoHeaderRecord>("H")
            .MapRecord<NoHeaderDetail>("D")
            .MapRecord<NoHeaderTrailer>("T")
            .FromText(csv))
        {
            results.Add(record);
        }

        Assert.Equal(3, results.Count);
        Assert.IsType<NoHeaderRecord>(results[0]);
        Assert.IsType<NoHeaderDetail>(results[1]);
        Assert.IsType<NoHeaderTrailer>(results[2]);
    }

    #endregion

    #region Case Sensitivity

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CaseInsensitiveDiscriminator_MatchesIgnoringCase()
    {
        var csv = """
            Type,Data
            h,Header1
            H,Header2
            d,Detail1
            D,Detail2
            """;

        var results = new List<object>();
        foreach (var record in Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .CaseInsensitiveDiscriminator()
            .MapRecord<SimpleHeader>("H")
            .MapRecord<SimpleDetail>("D")
            .FromText(csv))
        {
            results.Add(record);
        }

        Assert.Equal(4, results.Count);
        Assert.Equal(2, results.OfType<SimpleHeader>().Count());
        Assert.Equal(2, results.OfType<SimpleDetail>().Count());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CaseSensitiveDiscriminator_SkipsNonMatching()
    {
        var csv = """
            Type,Data
            H,Header1
            h,ShouldBeSkipped
            D,Detail1
            """;

        var results = new List<object>();
        foreach (var record in Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .MapRecord<SimpleHeader>("H")
            .MapRecord<SimpleDetail>("D")
            .FromText(csv))
        {
            results.Add(record);
        }

        Assert.Equal(2, results.Count);
        Assert.IsType<SimpleHeader>(results[0]);
        Assert.IsType<SimpleDetail>(results[1]);
    }

    #endregion

    #region Unmatched Row Behavior

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void UnmatchedRow_Skip_IgnoresUnknownTypes()
    {
        var csv = """
            Type,Data
            H,Header1
            X,Unknown1
            D,Detail1
            Y,Unknown2
            """;

        var results = new List<object>();
        foreach (var record in Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .MapRecord<SimpleHeader>("H")
            .MapRecord<SimpleDetail>("D")
            .OnUnmatchedRow(UnmatchedRowBehavior.Skip)
            .FromText(csv))
        {
            results.Add(record);
        }

        Assert.Equal(2, results.Count);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void UnmatchedRow_Throw_ThrowsOnUnknownType()
    {
        var csv = """
            Type,Data
            H,Header1
            X,Unknown1
            """;

        var ex = Assert.Throws<CsvException>(() =>
        {
            foreach (var record in Csv.Read()
                .WithMultiSchema()
                .WithDiscriminator("Type")
                .MapRecord<SimpleHeader>("H")
                .OnUnmatchedRow(UnmatchedRowBehavior.Throw)
                .FromText(csv))
            {
                // Process
            }
        });

        Assert.Contains("X", ex.Message);
    }

    #endregion

    #region Edge Cases

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void EmptyDiscriminator_IsSkippedByDefault()
    {
        var csv = """
            Type,Data
            H,Header1
            ,Empty
            D,Detail1
            """;

        var results = new List<object>();
        foreach (var record in Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .MapRecord<SimpleHeader>("H")
            .MapRecord<SimpleDetail>("D")
            .FromText(csv))
        {
            results.Add(record);
        }

        Assert.Equal(2, results.Count);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void MultiCharDiscriminator_WorksWithLongerCodes()
    {
        var csv = """
            RecordType,Data
            HDR,HeaderData
            DTL,DetailData
            TRL,TrailerData
            """;

        var results = new List<object>();
        foreach (var record in Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("RecordType")
            .MapRecord<SimpleHeader>("HDR")
            .MapRecord<SimpleDetail>("DTL")
            .MapRecord<SimpleTrailer>("TRL")
            .FromText(csv))
        {
            results.Add(record);
        }

        Assert.Equal(3, results.Count);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void SkipRows_SkipsInitialRows()
    {
        var csv = """
            Comment line 1
            Comment line 2
            Type,Data
            H,Header1
            D,Detail1
            """;

        var results = new List<object>();
        foreach (var record in Csv.Read()
            .WithMultiSchema()
            .SkipRows(2)
            .WithDiscriminator("Type")
            .MapRecord<SimpleHeader>("H")
            .MapRecord<SimpleDetail>("D")
            .FromText(csv))
        {
            results.Add(record);
        }

        Assert.Equal(2, results.Count);
    }

    #endregion

    #region Configuration Validation

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void MissingDiscriminator_ThrowsOnBuild()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            Csv.Read()
                .WithMultiSchema()
                .MapRecord<SimpleHeader>("H")
                .FromText("Type,Data\nH,Data");
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void NoMappings_ThrowsOnBuild()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            Csv.Read()
                .WithMultiSchema()
                .WithDiscriminator("Type")
                .FromText("Type,Data\nH,Data");
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ColumnNameWithoutHeader_ThrowsOnBuild()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            Csv.Read()
                .WithMultiSchema()
                .NoHeaderRow()
                .WithDiscriminator("Type")
                .MapRecord<SimpleHeader>("H")
                .FromText("H,Data");
        });
    }

    #endregion

    #region Streaming

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task FromStreamAsync_ReadsRecordsAsynchronously()
    {
        var csv = """
            Type,Data
            H,Header1
            D,Detail1
            D,Detail2
            T,Trailer1
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var results = new List<object>();
        await foreach (var record in Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .MapRecord<SimpleHeader>("H")
            .MapRecord<SimpleDetail>("D")
            .MapRecord<SimpleTrailer>("T")
            .FromStreamAsync(stream, cancellationToken: TestContext.Current.CancellationToken))
        {
            results.Add(record);
        }

        Assert.Equal(4, results.Count);
        Assert.IsType<SimpleHeader>(results[0]);
        Assert.Equal(2, results.OfType<SimpleDetail>().Count());
        Assert.IsType<SimpleTrailer>(results[3]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task FromStream_MaxRowSize_ThrowsForOversizeRow()
    {
        const int maxRowSize = 20;
        var data = new string('x', maxRowSize + 5);
        var csv = $"Type,Data\nH,{data}\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        await using var reader = Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .MapRecord<SimpleHeader>("H")
            .WithMaxRowSize(maxRowSize)
            .FromStream(stream);

        var ex = await Assert.ThrowsAsync<CsvException>(async () =>
            await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        Assert.Equal(CsvErrorCode.ParseError, ex.ErrorCode);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task FromStream_BytesRead_TracksBytes()
    {
        var csv = "Type,Data\nH,Header1\n";
        var bytes = Encoding.UTF8.GetBytes(csv);
        using var stream = new MemoryStream(bytes);

        await using var reader = Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .MapRecord<SimpleHeader>("H")
            .FromStream(stream);

        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
        {
        }

        Assert.Equal(bytes.Length, reader.BytesRead);
    }

    #endregion

    #region Test Record Classes

    [CsvGenerateBinder]
    public class HeaderRecord
    {
        [CsvColumn(Name = "Data1")]
        public string FileId { get; set; } = string.Empty;

        [CsvColumn(Name = "Data2")]
        public DateOnly Date { get; set; }
    }

    [CsvGenerateBinder]
    public class DetailRecord
    {
        [CsvColumn(Name = "Data1")]
        public string ItemId { get; set; } = string.Empty;

        [CsvColumn(Name = "Data2")]
        public decimal Amount { get; set; }

        [CsvColumn(Name = "Data3")]
        public string Description { get; set; } = string.Empty;
    }

    [CsvGenerateBinder]
    public class TrailerRecord
    {
        [CsvColumn(Name = "Data1")]
        public int RecordCount { get; set; }

        [CsvColumn(Name = "Data2")]
        public decimal TotalAmount { get; set; }
    }

    [CsvGenerateBinder]
    public class FileHeader
    {
        public string Value1 { get; set; } = string.Empty;
    }

    [CsvGenerateBinder]
    public class BatchItem
    {
        public string Value1 { get; set; } = string.Empty;
    }

    [CsvGenerateBinder]
    public class FileControl
    {
        public string Value1 { get; set; } = string.Empty;
    }

    [CsvGenerateBinder]
    public class NoHeaderRecord
    {
        [CsvColumn(Index = 0)]
        public string Type { get; set; } = string.Empty;

        [CsvColumn(Index = 1)]
        public string FileId { get; set; } = string.Empty;
    }

    [CsvGenerateBinder]
    public class NoHeaderDetail
    {
        [CsvColumn(Index = 0)]
        public string Type { get; set; } = string.Empty;

        [CsvColumn(Index = 1)]
        public string ItemId { get; set; } = string.Empty;

        [CsvColumn(Index = 2)]
        public decimal Amount { get; set; }
    }

    [CsvGenerateBinder]
    public class NoHeaderTrailer
    {
        [CsvColumn(Index = 0)]
        public string Type { get; set; } = string.Empty;

        [CsvColumn(Index = 1)]
        public int RecordCount { get; set; }
    }

    [CsvGenerateBinder]
    public class SimpleHeader
    {
        public string Data { get; set; } = string.Empty;
    }

    [CsvGenerateBinder]
    public class SimpleDetail
    {
        public string Data { get; set; } = string.Empty;
    }

    [CsvGenerateBinder]
    public class SimpleTrailer
    {
        public string Data { get; set; } = string.Empty;
    }

    [CsvGenerateBinder]
    public struct StructHeader
    {
        public string? Value { get; set; }
    }

    [CsvGenerateBinder]
    public struct StructDetail
    {
        public string? Value { get; set; }
    }

    #endregion
}
