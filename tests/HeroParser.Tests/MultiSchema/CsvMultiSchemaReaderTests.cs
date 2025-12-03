using HeroParser.SeparatedValues;
using HeroParser.SeparatedValues.Records;
using HeroParser.SeparatedValues.Records.Binding;
using HeroParser.SeparatedValues.Records.MultiSchema;
using Xunit;

namespace HeroParser.Tests.MultiSchema;

public class CsvMultiSchemaReaderTests
{
    #region Test Record Types

    [CsvGenerateBinder]
    public class HeaderRecord
    {
        [CsvColumn(Name = "Type")]
        public string RecordType { get; set; } = "";

        [CsvColumn(Name = "Date")]
        public string Date { get; set; } = "";

        [CsvColumn(Name = "Version")]
        public string Version { get; set; } = "";
    }

    [CsvGenerateBinder]
    public class DetailRecord
    {
        [CsvColumn(Name = "Type")]
        public string RecordType { get; set; } = "";

        [CsvColumn(Name = "Id")]
        public int Id { get; set; }

        [CsvColumn(Name = "Amount")]
        public decimal Amount { get; set; }
    }

    [CsvGenerateBinder]
    public class TrailerRecord
    {
        [CsvColumn(Name = "Type")]
        public string RecordType { get; set; } = "";

        [CsvColumn(Name = "Count")]
        public int RecordCount { get; set; }

        [CsvColumn(Name = "Total")]
        public decimal TotalAmount { get; set; }
    }

    // UnknownRecord is created via custom factory, not deserialized from CSV
    public class UnknownRecord
    {
        public string Type { get; set; } = "";
        public string[] RawData { get; set; } = [];
        public int RowNumber { get; set; }
    }

    #endregion

    [Fact]
    public void FromText_WithMultipleRecordTypes_ParsesAllCorrectly()
    {
        // Arrange
        var csv = """
            Type,Date,Version,Id,Amount,Count,Total
            H,2024-01-01,1.0,,,
            D,,0,100,50.00,
            D,,0,101,75.50,
            T,,,,,2,125.50
            """;

        // Act
        var records = new List<object>();
        foreach (var record in Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .MapRecord<HeaderRecord>("H")
            .MapRecord<DetailRecord>("D")
            .MapRecord<TrailerRecord>("T")
            .AllowMissingColumns()
            .FromText(csv))
        {
            records.Add(record);
        }

        // Assert
        Assert.Equal(4, records.Count);

        Assert.IsType<HeaderRecord>(records[0]);
        var header = (HeaderRecord)records[0];
        Assert.Equal("H", header.RecordType);
        Assert.Equal("2024-01-01", header.Date);
        Assert.Equal("1.0", header.Version);

        Assert.IsType<DetailRecord>(records[1]);
        var detail1 = (DetailRecord)records[1];
        Assert.Equal("D", detail1.RecordType);
        Assert.Equal(100, detail1.Id);
        Assert.Equal(50.00m, detail1.Amount);

        Assert.IsType<DetailRecord>(records[2]);
        var detail2 = (DetailRecord)records[2];
        Assert.Equal(101, detail2.Id);
        Assert.Equal(75.50m, detail2.Amount);

        Assert.IsType<TrailerRecord>(records[3]);
        var trailer = (TrailerRecord)records[3];
        Assert.Equal("T", trailer.RecordType);
        Assert.Equal(2, trailer.RecordCount);
        Assert.Equal(125.50m, trailer.TotalAmount);
    }

    [Fact]
    public void FromText_WithDiscriminatorByIndex_ParsesCorrectly()
    {
        // Arrange - no header row, discriminator at index 0
        var csv = """
            H,2024-01-01,1.0
            D,100,50.00
            D,101,75.50
            T,2,125.50
            """;

        // Act
        var records = new List<object>();
        foreach (var record in Csv.Read()
            .WithMultiSchema()
            .WithoutHeader()
            .WithDiscriminator(0)
            .MapRecord<SimpleHeader>("H")
            .MapRecord<SimpleDetail>("D")
            .MapRecord<SimpleTrailer>("T")
            .FromText(csv))
        {
            records.Add(record);
        }

        // Assert
        Assert.Equal(4, records.Count);
        Assert.IsType<SimpleHeader>(records[0]);
        Assert.IsType<SimpleDetail>(records[1]);
        Assert.IsType<SimpleDetail>(records[2]);
        Assert.IsType<SimpleTrailer>(records[3]);
    }

    [Fact]
    public void FromText_WithUnmatchedRowSkip_SkipsUnknownTypes()
    {
        // Arrange
        var csv = """
            Type,Value
            H,Header
            X,Unknown
            D,Detail
            Y,AlsoUnknown
            T,Trailer
            """;

        // Act
        var records = new List<object>();
        foreach (var record in Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .MapRecord<TypeValueRecord>("H")
            .MapRecord<TypeValueRecord>("D")
            .MapRecord<TypeValueRecord>("T")
            .OnUnmatchedRow(UnmatchedRowBehavior.Skip)
            .FromText(csv))
        {
            records.Add(record);
        }

        // Assert
        Assert.Equal(3, records.Count);
        Assert.All(records, r => Assert.IsType<TypeValueRecord>(r));
    }

    [Fact]
    public void FromText_WithFallbackFactory_CreatesCustomRecords()
    {
        // Arrange
        var csv = """
            Type,Value,Extra
            H,Header,1
            X,Unknown,2
            D,Detail,3
            """;

        // Act
        var records = new List<object>();
        foreach (var record in Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .MapRecord<TypeValueRecord>("H")
            .MapRecord<TypeValueRecord>("D")
            .MapRecord((discriminator, columns, rowNum) => new UnknownRecord
            {
                Type = discriminator,
                RawData = columns,
                RowNumber = rowNum
            })
            .FromText(csv))
        {
            records.Add(record);
        }

        // Assert
        Assert.Equal(3, records.Count);
        Assert.IsType<TypeValueRecord>(records[0]);
        Assert.IsType<UnknownRecord>(records[1]);
        Assert.IsType<TypeValueRecord>(records[2]);

        var unknown = (UnknownRecord)records[1];
        Assert.Equal("X", unknown.Type);
        Assert.Equal(["X", "Unknown", "2"], unknown.RawData);
        Assert.Equal(3, unknown.RowNumber); // Row 1 = header, Row 2 = H, Row 3 = X
    }

    [Fact]
    public void FromText_WithUnmatchedRowThrow_ThrowsOnUnknownType()
    {
        // Arrange
        var csv = """
            Type,Value
            H,Header
            X,Unknown
            """;

        // Act & Assert
        var ex = Assert.Throws<CsvException>(() =>
        {
            foreach (var _ in Csv.Read()
                .WithMultiSchema()
                .WithDiscriminator("Type")
                .MapRecord<TypeValueRecord>("H")
                .OnUnmatchedRow(UnmatchedRowBehavior.Throw)
                .FromText(csv))
            {
                // Iterate to trigger parsing
            }
        });

        Assert.Contains("X", ex.Message);
    }

    [Fact]
    public void FromText_WithCaseInsensitiveDiscriminator_MatchesIgnoringCase()
    {
        // Arrange
        var csv = """
            Type,Value
            h,Lower
            H,Upper
            HEADER,AllCaps
            """;

        // Act
        var records = new List<object>();
        foreach (var record in Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .MapRecord<TypeValueRecord>("H")
            .MapRecord<TypeValueRecord>("header")
            .CaseSensitiveDiscriminator(false)
            .OnUnmatchedRow(UnmatchedRowBehavior.Skip)
            .FromText(csv))
        {
            records.Add(record);
        }

        // Assert - all should match due to case-insensitive comparison
        Assert.Equal(3, records.Count);
    }

    [Fact]
    public void FromText_MissingDiscriminatorColumn_ThrowsException()
    {
        // Arrange
        var csv = """
            Name,Value
            Test,123
            """;

        // Act & Assert
        var ex = Assert.Throws<CsvException>(() =>
        {
            foreach (var _ in Csv.Read()
                .WithMultiSchema()
                .WithDiscriminator("Type") // Column doesn't exist
                .MapRecord<TypeValueRecord>("H")
                .FromText(csv))
            {
                // Iterate to trigger parsing
            }
        });

        Assert.Contains("Type", ex.Message);
        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public void FromText_WithParserOptions_InheritsFromBuilder()
    {
        // Arrange - semicolon-delimited
        var csv = """
            Type;Value
            H;Header
            D;Detail
            """;

        // Act
        var records = new List<object>();
        foreach (var record in Csv.Read()
            .WithDelimiter(';')
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .MapRecord<TypeValueRecord>("H")
            .MapRecord<TypeValueRecord>("D")
            .FromText(csv))
        {
            records.Add(record);
        }

        // Assert
        Assert.Equal(2, records.Count);
    }

    [Fact]
    public void WithMultiSchema_NoDiscriminator_ThrowsOnFromText()
    {
        // Arrange
        var csv = "Type,Value\nH,Header";

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
        {
            Csv.Read()
                .WithMultiSchema()
                .MapRecord<TypeValueRecord>("H")
                .FromText(csv);
        });
    }

    [Fact]
    public void WithMultiSchema_NoMappings_ThrowsOnFromText()
    {
        // Arrange
        var csv = "Type,Value\nH,Header";

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
        {
            Csv.Read()
                .WithMultiSchema()
                .WithDiscriminator("Type")
                .FromText(csv);
        });
    }

    [Fact]
    public void WithMultiSchema_DiscriminatorByNameWithoutHeader_ThrowsOnFromText()
    {
        // Arrange
        var csv = "H,Header";

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
        {
            Csv.Read()
                .WithMultiSchema()
                .WithoutHeader()
                .WithDiscriminator("Type") // By name, but no header
                .MapRecord<TypeValueRecord>("H")
                .FromText(csv);
        });
    }

    #region Streaming Tests

    [Fact]
    public void FromStream_WithMultipleRecordTypes_ParsesAllCorrectly()
    {
        // Arrange
        var csv = """
            Type,Value
            H,Header
            D,Detail1
            D,Detail2
            T,Trailer
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));

        // Act
        var records = new List<object>();
        foreach (var record in Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .MapRecord<TypeValueRecord>("H")
            .MapRecord<TypeValueRecord>("D")
            .MapRecord<TypeValueRecord>("T")
            .FromStream(stream))
        {
            records.Add(record);
        }

        // Assert
        Assert.Equal(4, records.Count);
    }

    [Fact]
    public void FromFile_WithMultipleRecordTypes_ParsesAllCorrectly()
    {
        // Arrange
        var csv = """
            Type,Value
            H,Header
            D,Detail
            T,Trailer
            """;
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, csv);

            // Act
            var records = new List<object>();
            foreach (var record in Csv.Read()
                .WithMultiSchema()
                .WithDiscriminator("Type")
                .MapRecord<TypeValueRecord>("H")
                .MapRecord<TypeValueRecord>("D")
                .MapRecord<TypeValueRecord>("T")
                .FromFile(tempFile))
            {
                records.Add(record);
            }

            // Assert
            Assert.Equal(3, records.Count);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    #endregion

    #region Async Tests

    [Fact]
    public async Task FromStreamAsync_WithMultipleRecordTypes_ParsesAllCorrectly()
    {
        // Arrange
        var csv = """
            Type,Value
            H,Header
            D,Detail1
            D,Detail2
            T,Trailer
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));

        // Act
        var records = new List<object>();
        await foreach (var record in Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .MapRecord<TypeValueRecord>("H")
            .MapRecord<TypeValueRecord>("D")
            .MapRecord<TypeValueRecord>("T")
            .FromStreamAsync(stream, cancellationToken: TestContext.Current.CancellationToken))
        {
            records.Add(record);
        }

        // Assert
        Assert.Equal(4, records.Count);
    }

    [Fact]
    public async Task FromFileAsync_WithMultipleRecordTypes_ParsesAllCorrectly()
    {
        // Arrange
        var csv = """
            Type,Value
            H,Header
            D,Detail
            T,Trailer
            """;
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, csv, TestContext.Current.CancellationToken);

            // Act
            var records = new List<object>();
            await foreach (var record in Csv.Read()
                .WithMultiSchema()
                .WithDiscriminator("Type")
                .MapRecord<TypeValueRecord>("H")
                .MapRecord<TypeValueRecord>("D")
                .MapRecord<TypeValueRecord>("T")
                .FromFileAsync(tempFile, TestContext.Current.CancellationToken))
            {
                records.Add(record);
            }

            // Assert
            Assert.Equal(3, records.Count);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task FromStreamAsync_WithCancellation_StopsProcessing()
    {
        // Arrange - large file that would take time
        var lines = new List<string> { "Type,Value" };
        for (int i = 0; i < 10000; i++)
        {
            lines.Add($"D,Detail{i}");
        }
        var csv = string.Join("\n", lines);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        using var cts = new CancellationTokenSource();

        // Act
        var records = new List<object>();
        var cancelled = false;

        try
        {
            await foreach (var record in Csv.Read()
                .WithMultiSchema()
                .WithDiscriminator("Type")
                .MapRecord<TypeValueRecord>("D")
                .FromStreamAsync(stream, cancellationToken: cts.Token))
            {
                records.Add(record);
                if (records.Count >= 100)
                {
                    cts.Cancel();
                }
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        // Assert
        Assert.True(cancelled || records.Count < 10000);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void FromText_EmptyFile_ReturnsNoRecords()
    {
        // Arrange
        var csv = "";

        // Act
        var records = new List<object>();
        foreach (var record in Csv.Read()
            .WithMultiSchema()
            .WithoutHeader()
            .WithDiscriminator(0)
            .MapRecord<TypeValueRecord>("H")
            .FromText(csv))
        {
            records.Add(record);
        }

        // Assert
        Assert.Empty(records);
    }

    [Fact]
    public void FromText_HeaderOnly_ReturnsNoRecords()
    {
        // Arrange
        var csv = "Type,Value";

        // Act
        var records = new List<object>();
        foreach (var record in Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .MapRecord<TypeValueRecord>("H")
            .FromText(csv))
        {
            records.Add(record);
        }

        // Assert
        Assert.Empty(records);
    }

    [Fact]
    public void FromText_SingleRecordType_WorksCorrectly()
    {
        // Arrange - all same type
        var csv = """
            Type,Value
            D,Detail1
            D,Detail2
            D,Detail3
            """;

        // Act
        var records = new List<object>();
        foreach (var record in Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .MapRecord<TypeValueRecord>("D")
            .FromText(csv))
        {
            records.Add(record);
        }

        // Assert
        Assert.Equal(3, records.Count);
        Assert.All(records, r => Assert.IsType<TypeValueRecord>(r));
    }

    [Fact]
    public void FromText_EmptyDiscriminatorValue_MatchesEmptyString()
    {
        // Arrange
        var csv = """
            Type,Value
            ,EmptyType
            H,Header
            """;

        // Act
        var records = new List<object>();
        foreach (var record in Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .MapRecord<TypeValueRecord>("") // Map empty string
            .MapRecord<TypeValueRecord>("H")
            .FromText(csv))
        {
            records.Add(record);
        }

        // Assert
        Assert.Equal(2, records.Count);
    }

    [Fact]
    public void FromText_WithSkipRows_SkipsMetadataLines()
    {
        // Arrange - first 2 rows are metadata
        var csv = """
            Generated: 2024-01-01
            Version: 1.0
            Type,Value
            H,Header
            D,Detail
            """;

        // Act
        var records = new List<object>();
        foreach (var record in Csv.Read()
            .WithMultiSchema()
            .SkipRows(2)
            .WithDiscriminator("Type")
            .MapRecord<TypeValueRecord>("H")
            .MapRecord<TypeValueRecord>("D")
            .FromText(csv))
        {
            records.Add(record);
        }

        // Assert
        Assert.Equal(2, records.Count);
    }

    [Fact]
    public void FromText_LargeDiscriminatorValues_WorksCorrectly()
    {
        // Arrange - very long discriminator values
        var longType = new string('A', 1000);
        var csv = $"""
            Type,Value
            {longType},LongTypeRecord
            H,Header
            """;

        // Act
        var records = new List<object>();
        foreach (var record in Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .MapRecord<TypeValueRecord>(longType)
            .MapRecord<TypeValueRecord>("H")
            .FromText(csv))
        {
            records.Add(record);
        }

        // Assert
        Assert.Equal(2, records.Count);
    }

    [Fact]
    public void FromText_ManyRecordTypes_AllMapped()
    {
        // Arrange - many different types
        var csv = """
            Type,Value
            A,TypeA
            B,TypeB
            C,TypeC
            D,TypeD
            E,TypeE
            F,TypeF
            G,TypeG
            H,TypeH
            I,TypeI
            J,TypeJ
            """;

        // Act
        var builder = Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type");

        // Map all 10 types
        foreach (var type in new[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J" })
        {
            builder = builder.MapRecord<TypeValueRecord>(type);
        }

        var records = new List<object>();
        foreach (var record in builder.FromText(csv))
        {
            records.Add(record);
        }

        // Assert
        Assert.Equal(10, records.Count);
    }

    #endregion

    #region Simple Test Record Types (for index-based tests)

    [CsvGenerateBinder]
    public class SimpleHeader
    {
        [CsvColumn(Index = 0)]
        public string Type { get; set; } = "";

        [CsvColumn(Index = 1)]
        public string Date { get; set; } = "";

        [CsvColumn(Index = 2)]
        public string Version { get; set; } = "";
    }

    [CsvGenerateBinder]
    public class SimpleDetail
    {
        [CsvColumn(Index = 0)]
        public string Type { get; set; } = "";

        [CsvColumn(Index = 1)]
        public int Id { get; set; }

        [CsvColumn(Index = 2)]
        public decimal Amount { get; set; }
    }

    [CsvGenerateBinder]
    public class SimpleTrailer
    {
        [CsvColumn(Index = 0)]
        public string Type { get; set; } = "";

        [CsvColumn(Index = 1)]
        public int Count { get; set; }

        [CsvColumn(Index = 2)]
        public decimal Total { get; set; }
    }

    [CsvGenerateBinder]
    public class TypeValueRecord
    {
        [CsvColumn(Name = "Type")]
        public string Type { get; set; } = "";

        [CsvColumn(Name = "Value")]
        public string Value { get; set; } = "";
    }

    #endregion
}
