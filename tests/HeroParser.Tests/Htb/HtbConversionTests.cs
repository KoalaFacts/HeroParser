using System.Globalization;
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

    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
