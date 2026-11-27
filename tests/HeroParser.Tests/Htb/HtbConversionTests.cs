using System.Globalization;
using System.IO.Compression;
using System.Text;
using Xunit;
using HeroParser.Htbs;
using HeroParser.Htbs.Reading;
using HeroParser.Htbs.Records;
using HeroParser.Conversion;
using HeroParser.SeparatedValues.Reading.Records;

namespace HeroParser.Tests.Htb;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class HtbConversionTests
{
    private static HtbSchema CreateTestSchema()
    {
        return new HtbSchema([
            new HtbColumn("Id", HtbDataType.Int32, isNullable: true),
            new HtbColumn("Name", HtbDataType.String, isNullable: true),
            new HtbColumn("Score", HtbDataType.Double, isNullable: true),
            new HtbColumn("IsActive", HtbDataType.Boolean, isNullable: true),
            new HtbColumn("CreatedAt", HtbDataType.DateTime, isNullable: true),
            new HtbColumn("Balance", HtbDataType.Decimal, isNullable: true),
            new HtbColumn("ReferenceId", HtbDataType.Guid, isNullable: true),
            new HtbColumn("Embedding", HtbDataType.FloatArray, isNullable: true)
        ]);
    }

    public class ConversionRecord
    {
        public int? Id { get; set; }
        public string? Name { get; set; }
        public double? Score { get; set; }
        public bool? IsActive { get; set; }
        public DateTime? CreatedAt { get; set; }
        public decimal? Balance { get; set; }
        public Guid? ReferenceId { get; set; }
        public float[]? Embedding { get; set; }
    }

    [Fact]
    public void TestRoundTripParitySync()
    {
        var schema = CreateTestSchema();
        string csvData = "Id,Name,Score,IsActive,CreatedAt,Balance,ReferenceId,Embedding\r\n" +
                         "1,Alice,95.5,True,2026-05-29T12:00:00.0000000Z,1500.75,d2b960a5-866b-4e89-9a2c-a070e17621c5,\"[0.1,0.2,-0.3]\"\r\n" +
                         "2,,,False,2026-05-27T10:00:00.0000000Z,-100.5,00000000-0000-0000-0000-000000000000,[]\r\n" +
                         ",Bob,88.0,True,2026-05-28T00:00:00.0000000Z,0.0,,";

        using var htbStream = new MemoryStream();
        CsvToHtbConverter.Convert(csvData, htbStream, schema);

        htbStream.Position = 0;

        using var csvWriter = new StringWriter();
        HtbToCsvConverter.Convert(htbStream, csvWriter);

        string roundTrippedCsv = csvWriter.ToString();

        using var reader1 = HeroParser.Csv.ReadFromText(csvData);
        using var reader2 = HeroParser.Csv.ReadFromText(roundTrippedCsv);

        bool isHeaderRow = true;
        while (true)
        {
            bool hasNext1 = reader1.MoveNext();
            bool hasNext2 = reader2.MoveNext();
            Assert.Equal(hasNext1, hasNext2);
            if (!hasNext1) break;

            var row1 = reader1.Current;
            var row2 = reader2.Current;

            Assert.Equal(row1.ColumnCount, row2.ColumnCount);
            for (int i = 0; i < row1.ColumnCount; i++)
            {
                string s1 = row1[i].UnquoteToString();
                string s2 = row2[i].UnquoteToString();

                if (isHeaderRow)
                {
                    Assert.Equal(s1, s2);
                    continue;
                }

                if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
                {
                    Assert.Equal(s1, s2);
                    continue;
                }

                switch (i)
                {
                    case 0: // Id (Int32)
                        Assert.Equal(int.Parse(s1, CultureInfo.InvariantCulture), int.Parse(s2, CultureInfo.InvariantCulture));
                        break;
                    case 1: // Name (String)
                        Assert.Equal(s1, s2);
                        break;
                    case 2: // Score (Double)
                        Assert.Equal(double.Parse(s1, CultureInfo.InvariantCulture), double.Parse(s2, CultureInfo.InvariantCulture));
                        break;
                    case 3: // IsActive (Boolean)
                        Assert.Equal(bool.Parse(s1), bool.Parse(s2));
                        break;
                    case 4: // CreatedAt (DateTime)
                        Assert.Equal(DateTime.Parse(s1, CultureInfo.InvariantCulture).ToUniversalTime(), DateTime.Parse(s2, CultureInfo.InvariantCulture).ToUniversalTime());
                        break;
                    case 5: // Balance (Decimal)
                        Assert.Equal(decimal.Parse(s1, CultureInfo.InvariantCulture), decimal.Parse(s2, CultureInfo.InvariantCulture));
                        break;
                    case 6: // ReferenceId (Guid)
                        Assert.Equal(Guid.Parse(s1), Guid.Parse(s2));
                        break;
                    case 7: // Embedding (FloatArray)
                        Assert.Equal(HeroParser.Vectors.VectorParser.ParseFloats(s1), HeroParser.Vectors.VectorParser.ParseFloats(s2));
                        break;
                    default:
                        Assert.Equal(s1, s2);
                        break;
                }
            }
            isHeaderRow = false;
        }
    }

    [Fact]
    public async Task TestRoundTripParityAsync()
    {
        var schema = CreateTestSchema();
        string csvData = "Id,Name,Score,IsActive,CreatedAt,Balance,ReferenceId,Embedding\r\n" +
                         "100,Async Tester,88.5,True,2026-05-29T12:00:00.0000000Z,999.99,d2b960a5-866b-4e89-9a2c-a070e17621c5,\"[1.0,2.0,3.0]\"\r\n" +
                         ",Bob,,False,2026-05-28T00:00:00.0000000Z,0.0,,";

        using var csvStream = new MemoryStream(Encoding.UTF8.GetBytes(csvData));
        using var htbStream = new MemoryStream();

        await CsvToHtbConverter.ConvertAsync(csvStream, htbStream, schema, options: null, TestContext.Current.CancellationToken);

        htbStream.Position = 0;

        using var csvWriter = new StringWriter();
        await HtbToCsvConverter.ConvertAsync(htbStream, csvWriter, options: null, TestContext.Current.CancellationToken);

        string roundTrippedCsv = csvWriter.ToString();

        using var reader1 = HeroParser.Csv.ReadFromText(csvData);
        using var reader2 = HeroParser.Csv.ReadFromText(roundTrippedCsv);

        bool isHeaderRow = true;
        while (true)
        {
            bool hasNext1 = reader1.MoveNext();
            bool hasNext2 = reader2.MoveNext();
            Assert.Equal(hasNext1, hasNext2);
            if (!hasNext1) break;

            var row1 = reader1.Current;
            var row2 = reader2.Current;

            Assert.Equal(row1.ColumnCount, row2.ColumnCount);
            for (int i = 0; i < row1.ColumnCount; i++)
            {
                string s1 = row1[i].UnquoteToString();
                string s2 = row2[i].UnquoteToString();

                if (isHeaderRow)
                {
                    Assert.Equal(s1, s2);
                    continue;
                }

                if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
                {
                    Assert.Equal(s1, s2);
                    continue;
                }

                switch (i)
                {
                    case 0: // Id (Int32)
                        Assert.Equal(int.Parse(s1, CultureInfo.InvariantCulture), int.Parse(s2, CultureInfo.InvariantCulture));
                        break;
                    case 1: // Name (String)
                        Assert.Equal(s1, s2);
                        break;
                    case 2: // Score (Double)
                        Assert.Equal(double.Parse(s1, CultureInfo.InvariantCulture), double.Parse(s2, CultureInfo.InvariantCulture));
                        break;
                    case 3: // IsActive (Boolean)
                        Assert.Equal(bool.Parse(s1), bool.Parse(s2));
                        break;
                    case 4: // CreatedAt (DateTime)
                        Assert.Equal(DateTime.Parse(s1, CultureInfo.InvariantCulture).ToUniversalTime(), DateTime.Parse(s2, CultureInfo.InvariantCulture).ToUniversalTime());
                        break;
                    case 5: // Balance (Decimal)
                        Assert.Equal(decimal.Parse(s1, CultureInfo.InvariantCulture), decimal.Parse(s2, CultureInfo.InvariantCulture));
                        break;
                    case 6: // ReferenceId (Guid)
                        Assert.Equal(Guid.Parse(s1), Guid.Parse(s2));
                        break;
                    case 7: // Embedding (FloatArray)
                        Assert.Equal(HeroParser.Vectors.VectorParser.ParseFloats(s1), HeroParser.Vectors.VectorParser.ParseFloats(s2));
                        break;
                    default:
                        Assert.Equal(s1, s2);
                        break;
                }
            }
            isHeaderRow = false;
        }
    }

    [Fact]
    public void TestMissingOrUnmappedColumns()
    {
        var schema = CreateTestSchema();
        string csvData = "Name,Id,IsActive,CreatedAt,Balance,Embedding\r\n" +
                         "Alice,1,true,2026-05-29T12:00:00.0000000Z,1500.75,\"[0.1,0.2,-0.3]\"";

        using var htbStream = new MemoryStream();
        CsvToHtbConverter.Convert(csvData, htbStream, schema);

        htbStream.Position = 0;

        var htbRecords = HeroParser.Htb.Read<ConversionRecord>()
            .FromStream(htbStream)
            .ToList();

        Assert.Single(htbRecords);
        var rec = htbRecords[0];
        Assert.Equal(1, rec.Id);
        Assert.Equal("Alice", rec.Name);
        Assert.Null(rec.Score);
        Assert.True(rec.IsActive);
        Assert.Equal(new DateTime(2026, 5, 29, 12, 0, 0, DateTimeKind.Utc), rec.CreatedAt?.ToUniversalTime());
        Assert.Equal(1500.75m, rec.Balance);
        Assert.Null(rec.ReferenceId);
        Assert.Equal(new float[] { 0.1f, 0.2f, -0.3f }, rec.Embedding);
    }

    [Fact]
    public void TestCustomDelimiterAndHeaderOptions()
    {
        var schema = new HtbSchema([
            new HtbColumn("ColA", HtbDataType.Int32, isNullable: true),
            new HtbColumn("ColB", HtbDataType.String, isNullable: true)
        ]);

        string csvData = "ColA;ColB\r\n100;Hello\r\n200;World";
        using var htbStream = new MemoryStream();
        var options = new CsvToHtbOptions
        {
            Delimiter = ';',
            HasHeaderRow = true
        };
        CsvToHtbConverter.Convert(csvData, htbStream, schema, options);

        htbStream.Position = 0;

        var reader = new HtbStreamReader(htbStream);
        var parsedSchema = reader.ParseHeader();
        Assert.Equal(2, parsedSchema.Columns.Count);

        Assert.False(reader.IsEndOfStream());
        reader.IncrementRecordCount();
        Span<byte> mask = stackalloc byte[1];
        reader.ReadMask(mask);
        Assert.Equal(100, reader.ReadInt32());
        Assert.Equal("Hello", reader.ReadString());

        Assert.False(reader.IsEndOfStream());
        reader.IncrementRecordCount();
        reader.ReadMask(mask);
        Assert.Equal(200, reader.ReadInt32());
        Assert.Equal("World", reader.ReadString());
    }

    [Fact]
    public void TestNoHeaderRowOption()
    {
        var schema = new HtbSchema([
            new HtbColumn("ColA", HtbDataType.Int32, isNullable: true),
            new HtbColumn("ColB", HtbDataType.String, isNullable: true)
        ]);

        string csvData = "100,Hello\r\n200,World";
        using var htbStream = new MemoryStream();
        var options = new CsvToHtbOptions
        {
            HasHeaderRow = false
        };
        CsvToHtbConverter.Convert(csvData, htbStream, schema, options);

        htbStream.Position = 0;

        using var csvWriter = new StringWriter();
        var toCsvOptions = new HtbToCsvOptions
        {
            IncludeHeaderRow = false
        };
        HtbToCsvConverter.Convert(htbStream, csvWriter, toCsvOptions);

        Assert.Equal("100,Hello\r\n200,World\r\n", csvWriter.ToString());
    }

    [Fact]
    public void TestMaxRowCountLimitEnforced()
    {
        var schema = CreateTestSchema();
        string csvData = "Id,Name\r\n" +
                         "1,Alice\r\n" +
                         "2,Bob\r\n" +
                         "3,Charlie";

        using var htbStream = new MemoryStream();
        var options = new CsvToHtbOptions
        {
            MaxRowCount = 2
        };

        Assert.Throws<HtbException>(() => CsvToHtbConverter.Convert(csvData, htbStream, schema, options));
    }

    [Fact]
    public void TestProgressReporting()
    {
        var schema = CreateTestSchema();
        string csvData = "Id,Name\r\n" +
                         "1,Alice\r\n" +
                         "2,Bob\r\n" +
                         "3,Charlie\r\n" +
                         "4,David";

        using var htbStream = new MemoryStream();
        long progressWrittenCount = 0;
        var progress = new SyncProgress<HtbWriteProgress>(p => progressWrittenCount = p.RecordsWritten);
        var options = new CsvToHtbOptions
        {
            Progress = progress,
            ProgressIntervalRows = 2
        };

        CsvToHtbConverter.Convert(csvData, htbStream, schema, options);

        Assert.Equal(4, progressWrittenCount);

        htbStream.Position = 0;
        long progressReadCount = 0;
        var readProgress = new SyncProgress<HtbProgress>(p => progressReadCount = p.RecordsRead);
        var readOptions = new HtbToCsvOptions
        {
            Progress = readProgress,
            ProgressIntervalRows = 2
        };

        using var csvWriter = new StringWriter();
        HtbToCsvConverter.Convert(htbStream, csvWriter, readOptions);

        Assert.Equal(4, progressReadCount);
    }

    public class QuotedRecord
    {
        public int? Id { get; set; }
        public string? Name { get; set; }
    }

    private static readonly HtbSchema quotedSchema = new([
        new HtbColumn("Id", HtbDataType.Int32, isNullable: true),
        new HtbColumn("Name", HtbDataType.String, isNullable: true)
    ]);

    public static TheoryData<string, char, char?, string> QuotedCsvCases => new()
    {
        // csv, quote, escape, expected Name of the single record (Id is always 7)
        { "\"Id\",\"Name\"\r\n7,\"Smith, \"\"J\"\"\"", '"', null, "Smith, \"J\"" },
        { "'Id','Name'\r\n7,'O''Brien'", '\'', null, "O'Brien" },
        { "\"Id\",\"Name\"\r\n7,\"A\\\"B\"", '"', '\\', "A\"B" },
    };

    [Theory]
    [MemberData(nameof(QuotedCsvCases))]
    public void Convert_QuotedHeadersAndValues_FollowTheConfiguredQuoting(string csv, char quote, char? escape, string expectedName)
    {
        var options = new CsvToHtbOptions { Quote = quote, EscapeCharacter = escape };

        using var htbStream = new MemoryStream();
        CsvToHtbConverter.Convert(csv, htbStream, quotedSchema, options);
        var (id, name) = ReadSingleRow(htbStream);

        Assert.Equal(7, id);
        Assert.Equal(expectedName, name);
    }

    [Theory]
    [MemberData(nameof(QuotedCsvCases))]
    public async Task ConvertAsync_QuotedHeadersAndValues_FollowTheConfiguredQuoting(string csv, char quote, char? escape, string expectedName)
    {
        var options = new CsvToHtbOptions { Quote = quote, EscapeCharacter = escape };

        using var csvStream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        using var htbStream = new MemoryStream();
        await CsvToHtbConverter.ConvertAsync(csvStream, htbStream, quotedSchema, options, TestContext.Current.CancellationToken);
        var (id, name) = ReadSingleRow(htbStream);

        Assert.Equal(7, id);
        Assert.Equal(expectedName, name);
    }

    [Fact]
    public void Convert_QuotedNumericValue_IsParsedWithoutItsQuotes()
    {
        using var htbStream = new MemoryStream();
        CsvToHtbConverter.Convert("Id,Name\r\n\"42\",x", htbStream, quotedSchema);
        var (id, name) = ReadSingleRow(htbStream);

        Assert.Equal(42, id);
        Assert.Equal("x", name);
    }

    [Fact]
    public void Convert_QuotedEmptyValueInNullableTypedColumn_IsNull()
    {
        using var htbStream = new MemoryStream();
        CsvToHtbConverter.Convert("Id,Name\r\n\"\",\"\"", htbStream, quotedSchema);
        var (id, name) = ReadSingleRow(htbStream);

        Assert.Null(id);
        Assert.Equal("", name);
    }

    [Fact]
    public async Task ConvertAsync_QuotedEmptyValueInNullableTypedColumn_IsNull()
    {
        using var csvStream = new MemoryStream(Encoding.UTF8.GetBytes("Id,Name\r\n\"\",\"\""));
        using var htbStream = new MemoryStream();
        await CsvToHtbConverter.ConvertAsync(csvStream, htbStream, quotedSchema, cancellationToken: TestContext.Current.CancellationToken);
        var (id, name) = ReadSingleRow(htbStream);

        Assert.Null(id);
        Assert.Equal("", name);
    }

    [Fact]
    public void Convert_QuotedEmptyValueInNonNullableTypedColumn_ReportsAnEmptyValue()
    {
        var schema = new HtbSchema([new HtbColumn("Id", HtbDataType.Int32, isNullable: false)]);
        using var htbStream = new MemoryStream();

        var ex = Assert.Throws<HtbException>(() => CsvToHtbConverter.Convert("Id\r\n\"\"", htbStream, schema));

        Assert.Contains("is not nullable but has an empty value", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Convert_And_ConvertAsync_PreserveFullTemporalPrecision()
    {
        var schema = new HtbSchema([
            new HtbColumn("Timestamp", HtbDataType.DateTime, isNullable: false),
            new HtbColumn("OffsetTime", HtbDataType.DateTimeOffset, isNullable: false),
            new HtbColumn("Date", HtbDataType.DateOnly, isNullable: false),
            new HtbColumn("Clock", HtbDataType.TimeOnly, isNullable: false),
            new HtbColumn("Duration", HtbDataType.TimeSpan, isNullable: false)
        ]);

        var expectedDt = new DateTime(2026, 11, 24, 14, 30, 45, 789, DateTimeKind.Unspecified).AddTicks(1234);
        var expectedDto = new DateTimeOffset(2026, 11, 24, 14, 30, 45, 789, TimeSpan.FromHours(5)).AddTicks(1234);
        var expectedDate = new DateOnly(2026, 11, 24);
        var expectedTime = new TimeOnly(14, 30, 45, 789).Add(TimeSpan.FromTicks(1234));
        var expectedSpan = new TimeSpan(2, 14, 30, 45, 789).Add(TimeSpan.FromTicks(1234));

        string sourceCsv =
            "Timestamp,OffsetTime,Date,Clock,Duration\r\n" +
            $"{expectedDt:O},{expectedDto:O},{expectedDate:O},{expectedTime:O},{expectedSpan:c}\r\n";

        using var htbStream = new MemoryStream();
        CsvToHtbConverter.Convert(sourceCsv, htbStream, schema);

        htbStream.Position = 0;
        using var csvWriter = new StringWriter();
        HtbToCsvConverter.Convert(htbStream, csvWriter);
        string csvText = csvWriter.ToString();

        Assert.Contains("14:30:45.7891234", csvText, StringComparison.Ordinal);

        using var roundHtb = new MemoryStream();
        CsvToHtbConverter.Convert(csvText, roundHtb, schema);
        roundHtb.Position = 0;
        using var drSync = HeroParser.Htb.CreateDataReader(roundHtb);
        Assert.True(drSync.Read());
        Assert.Equal(expectedDt, drSync.GetDateTime(0));
        Assert.Equal(expectedDto, Assert.IsType<DateTimeOffset>(drSync.GetValue(1)));
        Assert.Equal(expectedDate, Assert.IsType<DateOnly>(drSync.GetValue(2)));
        Assert.Equal(expectedTime, Assert.IsType<TimeOnly>(drSync.GetValue(3)));
        Assert.Equal(expectedSpan, Assert.IsType<TimeSpan>(drSync.GetValue(4)));
    }

    [Fact]
    public async Task ConvertAsync_PreservesFullTemporalPrecision()
    {
        var ct = TestContext.Current.CancellationToken;
        var schema = new HtbSchema([
            new HtbColumn("Timestamp", HtbDataType.DateTime, isNullable: false),
            new HtbColumn("OffsetTime", HtbDataType.DateTimeOffset, isNullable: false),
            new HtbColumn("Date", HtbDataType.DateOnly, isNullable: false),
            new HtbColumn("Clock", HtbDataType.TimeOnly, isNullable: false),
            new HtbColumn("Duration", HtbDataType.TimeSpan, isNullable: false)
        ]);

        var expectedDt = new DateTime(2026, 11, 24, 14, 30, 45, 789, DateTimeKind.Unspecified).AddTicks(1234);
        var expectedDto = new DateTimeOffset(2026, 11, 24, 14, 30, 45, 789, TimeSpan.FromHours(-7)).AddTicks(5678);
        var expectedDate = new DateOnly(2026, 11, 24);
        var expectedTime = new TimeOnly(14, 30, 45, 789).Add(TimeSpan.FromTicks(5678));
        var expectedSpan = new TimeSpan(1, 2, 3, 4, 567).Add(TimeSpan.FromTicks(8901));

        string sourceCsv =
            "Timestamp,OffsetTime,Date,Clock,Duration\r\n" +
            $"{expectedDt:O},{expectedDto:O},{expectedDate:O},{expectedTime:O},{expectedSpan:c}\r\n";

        using var csvStream = new MemoryStream(Encoding.UTF8.GetBytes(sourceCsv));
        using var htbStream = new MemoryStream();
        await CsvToHtbConverter.ConvertAsync(csvStream, htbStream, schema, cancellationToken: ct);

        htbStream.Position = 0;
        using var csvWriter = new StringWriter();
        await HtbToCsvConverter.ConvertAsync(htbStream, csvWriter, cancellationToken: ct);
        string csvText = csvWriter.ToString();

        using var csvRoundStream = new MemoryStream(Encoding.UTF8.GetBytes(csvText));
        using var roundHtb = new MemoryStream();
        await CsvToHtbConverter.ConvertAsync(csvRoundStream, roundHtb, schema, cancellationToken: ct);
        roundHtb.Position = 0;

        using var drAsync = HeroParser.Htb.CreateDataReader(roundHtb);
        Assert.True(await drAsync.ReadAsync(ct));
        Assert.Equal(expectedDt, drAsync.GetDateTime(0));
        Assert.Equal(expectedDto, Assert.IsType<DateTimeOffset>(drAsync.GetValue(1)));
        Assert.Equal(expectedDate, Assert.IsType<DateOnly>(drAsync.GetValue(2)));
        Assert.Equal(expectedTime, Assert.IsType<TimeOnly>(drAsync.GetValue(3)));
        Assert.Equal(expectedSpan, Assert.IsType<TimeSpan>(drAsync.GetValue(4)));
    }

    [Fact]
    public void Convert_MapByOrdinal_AllowsDuplicateAndBlankHeaders_AndMultilineQuotes()
    {
        const string csv = "Id,Id,,Name\r\n1,2,mid,\"line1\nline2\"\r\n";
        var schema = new HtbSchema([
            new HtbColumn("Id", HtbDataType.Int32, isNullable: false),
            new HtbColumn("Id", HtbDataType.Int32, isNullable: false),
            new HtbColumn("Col3", HtbDataType.String, isNullable: true),
            new HtbColumn("Name", HtbDataType.String, isNullable: false)
        ]);

        using var htbStream = new MemoryStream();
        CsvToHtbConverter.Convert(csv, htbStream, schema, new CsvToHtbOptions
        {
            MapByOrdinal = true,
            AllowNewlinesInsideQuotes = true
        });

        htbStream.Position = 0;
        using var dr = HeroParser.Htb.CreateDataReader(htbStream, leaveOpen: true);
        Assert.True(dr.Read());
        Assert.Equal(1, dr.GetInt32(0));
        Assert.Equal(2, dr.GetInt32(1));
        Assert.Equal("mid", dr.GetString(2));
        Assert.Equal("line1\nline2", dr.GetString(3));
        Assert.False(dr.Read());
    }

    [Fact]
    public async Task ConvertAsync_MapByOrdinal_AllowsDuplicateAndBlankHeaders_AndMultilineQuotes()
    {
        var ct = TestContext.Current.CancellationToken;
        const string csv = "Id,Id,,Name\r\n1,2,mid,\"line1\nline2\"\r\n";
        var schema = new HtbSchema([
            new HtbColumn("Id", HtbDataType.Int32, isNullable: false),
            new HtbColumn("Id", HtbDataType.Int32, isNullable: false),
            new HtbColumn("Col3", HtbDataType.String, isNullable: true),
            new HtbColumn("Name", HtbDataType.String, isNullable: false)
        ]);

        using var csvStream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        using var htbStream = new MemoryStream();
        await CsvToHtbConverter.ConvertAsync(csvStream, htbStream, schema, new CsvToHtbOptions
        {
            MapByOrdinal = true,
            AllowNewlinesInsideQuotes = true
        }, ct);

        htbStream.Position = 0;
        using var dr = HeroParser.Htb.CreateDataReader(htbStream, leaveOpen: true);
        Assert.True(await dr.ReadAsync(ct));
        Assert.Equal(1, dr.GetInt32(0));
        Assert.Equal(2, dr.GetInt32(1));
        Assert.Equal("mid", dr.GetString(2));
        Assert.Equal("line1\nline2", dr.GetString(3));
        Assert.False(await dr.ReadAsync(ct));
    }

    [Fact]
    public async Task Convert_RejectExtraColumns_And_ConvertAsync_Utf16BomStreams()
    {
        var ct = TestContext.Current.CancellationToken;
        var schema = new HtbSchema([
            new HtbColumn("Id", HtbDataType.Int32, isNullable: false),
            new HtbColumn("Name", HtbDataType.String, isNullable: false)
        ]);

        const string wideCsv = "Id,Name\n1,Alice,Extra\n";
        using (var outMs = new MemoryStream())
        {
            CsvToHtbConverter.Convert(wideCsv, outMs, schema, new CsvToHtbOptions { MapByOrdinal = true });
            outMs.Position = 0;
            using var dr = HeroParser.Htb.CreateDataReader(outMs, leaveOpen: true);
            Assert.True(dr.Read());
            Assert.Equal("Alice", dr.GetString(1));
        }

        using (var outMs = new MemoryStream())
        {
            var ex = Assert.Throws<HtbException>(() => CsvToHtbConverter.Convert(wideCsv, outMs, schema, new CsvToHtbOptions
            {
                MapByOrdinal = true,
                RejectExtraColumns = true
            }));
            Assert.Contains("row 2", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        using (var inMs = new MemoryStream(Encoding.UTF8.GetBytes(wideCsv)))
        using (var outMs = new MemoryStream())
        {
            var ex = await Assert.ThrowsAsync<HtbException>(() => CsvToHtbConverter.ConvertAsync(
                inMs,
                outMs,
                schema,
                new CsvToHtbOptions { MapByOrdinal = true, RejectExtraColumns = true },
                ct));
            Assert.Contains("row 2", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        foreach (Encoding utf16 in (Encoding[])[Encoding.Unicode, Encoding.BigEndianUnicode])
        {
            byte[] payload = [.. utf16.GetPreamble(), .. utf16.GetBytes("Id,Name\n7,Café\n")];
            using var inMs = new MemoryStream(payload);
            using var outMs = new MemoryStream();
            await CsvToHtbConverter.ConvertAsync(inMs, outMs, schema, new CsvToHtbOptions { MapByOrdinal = true }, ct);
            Assert.True(inMs.CanRead);

            outMs.Position = 0;
            using var dr = HeroParser.Htb.CreateDataReader(outMs, leaveOpen: true);
            Assert.True(await dr.ReadAsync(ct));
            Assert.Equal(7, dr.GetInt32(0));
            Assert.Equal("Café", dr.GetString(1));
            Assert.False(await dr.ReadAsync(ct));
        }
    }

    [Fact]
    public async Task Convert_RejectLossyNumericValues_RejectsLeadingZerosAndRoundedDecimalsAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var schema = new HtbSchema([
            new HtbColumn("Code", HtbDataType.Int32, isNullable: false),
            new HtbColumn("Amount", HtbDataType.Decimal, isNullable: false)
        ]);

        var opts = new CsvToHtbOptions { RejectLossyNumericValues = true };

        using (var okMs = new MemoryStream())
        {
            CsvToHtbConverter.Convert("Code,Amount\n123,2.50\n", okMs, schema, opts);
            Assert.True(okMs.Length > 0);
        }

        using (var badZipMs = new MemoryStream())
        {
            Assert.Throws<HtbException>(() => CsvToHtbConverter.Convert("Code,Amount\n00123,2.50\n", badZipMs, schema, opts));
        }

        using var badDecIn = new MemoryStream(Encoding.UTF8.GetBytes("Code,Amount\n123,1.000000000000000000000000000001\n"));
        using var badDecOut = new MemoryStream();
        await Assert.ThrowsAsync<HtbException>(() => CsvToHtbConverter.ConvertAsync(badDecIn, badDecOut, schema, opts, ct));
    }

    private static (int? Id, string? Name) ReadSingleRow(MemoryStream htbStream)
    {
        htbStream.Position = 0;
        var records = HeroParser.Htb.Read<QuotedRecord>().FromStream(htbStream).ToList();
        var record = Assert.Single(records);
        return (record.Id, record.Name);
    }

    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    [Fact]
    public async Task Convert_MapByOrdinalWithShortHeader_MapsSchemaOrdinalsSyncAndAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var schema = new HtbSchema([
            new HtbColumn("First", HtbDataType.String, isNullable: false),
            new HtbColumn("Second", HtbDataType.String, isNullable: false)
        ]);
        const string csv = "A\nfirst,second\n";
        var opts = new CsvToHtbOptions { MapByOrdinal = true, RejectExtraColumns = true };

        using (var syncOut = new MemoryStream())
        {
            CsvToHtbConverter.Convert(csv, syncOut, schema, opts);
            syncOut.Position = 0;
            using var dr = HeroParser.Htb.CreateDataReader(syncOut, leaveOpen: true);
            Assert.True(dr.Read());
            Assert.Equal("first", dr.GetString(0));
            Assert.Equal("second", dr.GetString(1));
        }

        using var inMs = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        using var asyncOut = new MemoryStream();
        await CsvToHtbConverter.ConvertAsync(inMs, asyncOut, schema, opts, ct);
        asyncOut.Position = 0;
        using var asyncDr = HeroParser.Htb.CreateDataReader(asyncOut, leaveOpen: true);
        Assert.True(await asyncDr.ReadAsync(ct));
        Assert.Equal("first", asyncDr.GetString(0));
        Assert.Equal("second", asyncDr.GetString(1));
    }

    [Fact]
    public async Task Convert_RejectLossyNumericValues_RejectsLossyFloatDoubleAndHalfSyncAndAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var schema = new HtbSchema([
            new HtbColumn("H", HtbDataType.Half, isNullable: false),
            new HtbColumn("F", HtbDataType.Float, isNullable: false),
            new HtbColumn("D", HtbDataType.Double, isNullable: false)
        ]);
        var opts = new CsvToHtbOptions { RejectLossyNumericValues = true };

        using (var okOut = new MemoryStream())
        {
            CsvToHtbConverter.Convert("H,F,D\n1.5,1.5,1.5\n", okOut, schema, opts);
            Assert.True(okOut.Length > 0);
        }

        foreach (string lossyCsv in (string[])[
            "H,F,D\n2049,1.5,1.5\n",
            "H,F,D\n1.5,16777217,1.5\n",
            "H,F,D\n1.5,1.5,9007199254740993\n"
        ])
        {
            using var syncOut = new MemoryStream();
            Assert.Throws<HtbException>(() => CsvToHtbConverter.Convert(lossyCsv, syncOut, schema, opts));

            using var inMs = new MemoryStream(Encoding.UTF8.GetBytes(lossyCsv));
            using var asyncOut = new MemoryStream();
            await Assert.ThrowsAsync<HtbException>(() => CsvToHtbConverter.ConvertAsync(inMs, asyncOut, schema, opts, ct));
        }
    }

    [Fact]
    public void HtbSchema_RejectsColumnNameExceeding2048Utf8Bytes()
    {
        string overlongName = new('中', 683); // 2,049 UTF-8 bytes
        var ex = Assert.Throws<HtbException>(() =>
            new HtbSchema([new HtbColumn(overlongName, HtbDataType.Int32, isNullable: true)]));
        Assert.Equal(HtbErrorCode.SerializationError, ex.ErrorCode);
    }

    [Fact]
    public async Task Convert_HtbToCsvToHtb_PreservesNullAndEmptyByteArraySyncAndAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var schema = new HtbSchema([
            new HtbColumn("NullableBytes", HtbDataType.ByteArray, isNullable: true),
            new HtbColumn("RequiredBytes", HtbDataType.ByteArray, isNullable: false)
        ]);

        using var initialHtb = new MemoryStream();
        using (var writer = new HeroParser.Htbs.Writing.HtbStreamWriter(initialHtb, leaveOpen: true))
        {
            writer.WriteHeader(schema);

            writer.IncrementRecordCount();
            writer.WriteMask([0b0000_0001]); // NullableBytes = null, RequiredBytes = []
            writer.WriteByteArray([]);

            writer.IncrementRecordCount();
            writer.WriteMask([0]); // NullableBytes = [], RequiredBytes = [1, 2, 3]
            writer.WriteByteArray([]);
            writer.WriteByteArray([1, 2, 3]);

            writer.IncrementRecordCount();
            writer.WriteMask([0]); // NullableBytes = [9, 8], RequiredBytes = []
            writer.WriteByteArray([9, 8]);
            writer.WriteByteArray([]);

            writer.Flush();
        }

        foreach (bool quoteAll in (bool[])[false, true])
        {
            initialHtb.Position = 0;
            using var syncCsv = new StringWriter();
            HtbToCsvConverter.Convert(initialHtb, syncCsv, new HtbToCsvOptions { QuoteAll = quoteAll });

            using var syncRoundTrip = new MemoryStream();
            CsvToHtbConverter.Convert(syncCsv.ToString(), syncRoundTrip, schema);
            AssertRoundTrip(syncRoundTrip);

            initialHtb.Position = 0;
            using var asyncCsv = new StringWriter();
            await HtbToCsvConverter.ConvertAsync(initialHtb, asyncCsv, new HtbToCsvOptions { QuoteAll = quoteAll }, ct);

            using var csvBytes = new MemoryStream(Encoding.UTF8.GetBytes(asyncCsv.ToString()));
            using var asyncRoundTrip = new MemoryStream();
            await CsvToHtbConverter.ConvertAsync(csvBytes, asyncRoundTrip, schema, cancellationToken: ct);
            AssertRoundTrip(asyncRoundTrip);
        }

        // Preserve L5l-e: quoted empty string ("") in a nullable typed column remains DBNull.
        using var quotedEmptyOut = new MemoryStream();
        CsvToHtbConverter.Convert("NullableBytes,RequiredBytes\n\"\",\"base64:\"\n", quotedEmptyOut, schema);
        quotedEmptyOut.Position = 0;
        using var quotedDr = HeroParser.Htb.CreateDataReader(quotedEmptyOut, leaveOpen: true);
        Assert.True(quotedDr.Read());
        Assert.True(quotedDr.IsDBNull(0));
        Assert.Equal([], Assert.IsType<byte[]>(quotedDr.GetValue(1)));

        static void AssertRoundTrip(MemoryStream stream)
        {
            stream.Position = 0;
            using var dr = HeroParser.Htb.CreateDataReader(stream, leaveOpen: true);

            Assert.True(dr.Read());
            Assert.True(dr.IsDBNull(0));
            Assert.Equal([], Assert.IsType<byte[]>(dr.GetValue(1)));

            Assert.True(dr.Read());
            Assert.False(dr.IsDBNull(0));
            Assert.Equal([], Assert.IsType<byte[]>(dr.GetValue(0)));
            Assert.Equal([1, 2, 3], Assert.IsType<byte[]>(dr.GetValue(1)));

            Assert.True(dr.Read());
            Assert.False(dr.IsDBNull(0));
            Assert.Equal([9, 8], Assert.IsType<byte[]>(dr.GetValue(0)));
            Assert.Equal([], Assert.IsType<byte[]>(dr.GetValue(1)));

            Assert.False(dr.Read());
        }
    }

    [Fact]
    public async Task Convert_RejectLossyTemporalValues_RejectsOffsetMismatchesSyncAndAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var dtSchema = new HtbSchema([new HtbColumn("CreatedAt", HtbDataType.DateTime, isNullable: true)]);
        var dtoSchema = new HtbSchema([new HtbColumn("OccurredAt", HtbDataType.DateTimeOffset, isNullable: true)]);
        var opts = new CsvToHtbOptions { RejectLossyNumericValues = true };

        using (var okDtSync = new MemoryStream())
        {
            CsvToHtbConverter.Convert("CreatedAt\n2026-11-21T12:34:56\n", okDtSync, dtSchema, opts);
        }
        using (var okDtoSync = new MemoryStream())
        {
            CsvToHtbConverter.Convert("OccurredAt\n2026-11-21T12:34:56+05:00\n2026-11-21T12:34:56Z\n", okDtoSync, dtoSchema, opts);
        }

        foreach (string badDtCsv in (string[])[
            "CreatedAt\n2026-11-21T12:34:56+05:00\n",
            "CreatedAt\n2026-11-21T12:34:56Z\n",
            "CreatedAt\n2026-11-21T12:34:56-04:00\n"
        ])
        {
            using var syncOut = new MemoryStream();
            var syncEx = Assert.Throws<HtbException>(() => CsvToHtbConverter.Convert(badDtCsv, syncOut, dtSchema, opts));
            Assert.Equal(HtbErrorCode.SerializationError, syncEx.ErrorCode);

            using var inMs = new MemoryStream(Encoding.UTF8.GetBytes(badDtCsv));
            using var asyncOut = new MemoryStream();
            var asyncEx = await Assert.ThrowsAsync<HtbException>(() => CsvToHtbConverter.ConvertAsync(inMs, asyncOut, dtSchema, opts, ct));
            Assert.Equal(HtbErrorCode.SerializationError, asyncEx.ErrorCode);
        }

        const string badDtoCsv = "OccurredAt\n2026-11-21T12:34:56\n";
        using (var syncOut = new MemoryStream())
        {
            var syncEx = Assert.Throws<HtbException>(() => CsvToHtbConverter.Convert(badDtoCsv, syncOut, dtoSchema, opts));
            Assert.Equal(HtbErrorCode.SerializationError, syncEx.ErrorCode);
        }
        using (var inMs = new MemoryStream(Encoding.UTF8.GetBytes(badDtoCsv)))
        using (var asyncOut = new MemoryStream())
        {
            var asyncEx = await Assert.ThrowsAsync<HtbException>(() => CsvToHtbConverter.ConvertAsync(inMs, asyncOut, dtoSchema, opts, ct));
            Assert.Equal(HtbErrorCode.SerializationError, asyncEx.ErrorCode);
        }
    }

    [Fact]
    public void ConvertFile_CompressedUtf16Csv_StreamsAndEnforcesMaxRowCount()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"hp-htb-file-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string csvGzPath = Path.Combine(dir, "input.csv.gz");
            byte[] utf16Bytes = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("Name,Age\nAlice,30\nBob,25\nCarol,40\n")];
            using (var fs = File.Create(csvGzPath))
            using (var gz = new GZipStream(fs, CompressionMode.Compress))
            {
                gz.Write(utf16Bytes);
            }

            var schema = new HtbSchema([
                new HtbColumn("Name", HtbDataType.String, isNullable: false),
                new HtbColumn("Age", HtbDataType.Int32, isNullable: false)
            ]);

            string okHtb = Path.Combine(dir, "ok.htb");
            CsvToHtbConverter.ConvertFile(csvGzPath, okHtb, schema);

            using (var reader = HeroParser.Htb.CreateDataReader(okHtb))
            {
                Assert.True(reader.Read());
                Assert.Equal("Alice", reader.GetString(0));
                Assert.Equal(30, reader.GetInt32(1));
                Assert.True(reader.Read());
                Assert.Equal("Bob", reader.GetString(0));
                Assert.True(reader.Read());
                Assert.Equal("Carol", reader.GetString(0));
                Assert.False(reader.Read());
            }

            string limitedHtb = Path.Combine(dir, "limited.htb");
            var ex = Assert.Throws<HtbException>(() =>
                CsvToHtbConverter.ConvertFile(csvGzPath, limitedHtb, schema, new CsvToHtbOptions { MaxRowCount = 2 }));
            Assert.Equal(HtbErrorCode.LimitExceeded, ex.ErrorCode);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
