using System.Data;
using System.Globalization;
using System.Numerics;
using System.Text;
using HeroParser.Conversion;
using HeroParser.Excels.Core;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Writing;
using HeroParser.Htbs;
using HeroParser.Htbs.Records;
using HeroParser.JsonLines;
using HeroParser.JsonLines.Writing;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Writing;
using Xunit;

namespace HeroParser.Tests;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class DataReaderExportTests
{
    private static DataTable CreateSampleTable()
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Score", typeof(decimal));
        table.Columns.Add("Active", typeof(bool));
        table.Columns.Add("CreatedAt", typeof(DateTime));
        table.Columns.Add("Token", typeof(Guid));

        table.Rows.Add(
            1,
            "Alice",
            98.5m,
            true,
            new DateTime(2026, 5, 1, 10, 30, 0, DateTimeKind.Utc),
            new Guid("11111111-2222-3333-4444-555555555555"));
        table.Rows.Add(
            2,
            DBNull.Value,
            DBNull.Value,
            false,
            DBNull.Value,
            DBNull.Value);
        return table;
    }

    [Fact]
    public void Csv_WriteFromDataReaderToText_WithHeadersAndNullValue()
    {
        using var table = CreateSampleTable();
        using var reader = table.CreateDataReader();

        string csv = Csv.WriteFromDataReaderToText(
            reader,
            new CsvWriteOptions { NullValue = "NULL", NewLine = "\n" },
            writeHeader: true);

        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Equal("Id,Name,Score,Active,CreatedAt,Token", lines[0]);
        Assert.StartsWith("1,Alice,98.5,True,", lines[1]);
        Assert.EndsWith("11111111-2222-3333-4444-555555555555", lines[1]);
        Assert.Equal("2,NULL,NULL,False,NULL,NULL", lines[2]);
    }

    [Fact]
    public async Task Csv_WriteFromDataReaderAsync_StreamAndFile_WithoutHeader()
    {
        var ct = TestContext.Current.CancellationToken;
        using var table = CreateSampleTable();

        await using var ms = new MemoryStream();
        await using (var reader = table.CreateDataReader())
        {
            await Csv.WriteFromDataReaderAsync(
                reader,
                ms,
                new CsvWriteOptions { NewLine = "\n" },
                writeHeader: false,
                leaveOpen: true,
                cancellationToken: ct);
        }

        Assert.True(ms.CanRead); // leaveOpen respected
        string text = Encoding.UTF8.GetString(ms.ToArray());
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("1,Alice,98.5", lines[0]);

        string path = Path.Combine(Path.GetTempPath(), $"hero-dr-csv-{Guid.NewGuid():N}.csv");
        try
        {
            await using var reader2 = table.CreateDataReader();
            await Csv.WriteFromDataReaderToFileAsync(path, reader2, cancellationToken: ct);
            string fileContent = await File.ReadAllTextAsync(path, ct);
            Assert.Contains("Id,Name,Score", fileContent);
            Assert.Contains("Alice", fileContent);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Csv_WriteFromDataReader_EmptyReader_WritesHeaderOnlyOrNothing()
    {
        using var table = CreateSampleTable();
        table.Clear();

        using var r1 = table.CreateDataReader();
        string withHeader = Csv.WriteFromDataReaderToText(r1, new CsvWriteOptions { NewLine = "\n" }, writeHeader: true);
        Assert.Equal("Id,Name,Score,Active,CreatedAt,Token\n", withHeader);

        using var r2 = table.CreateDataReader();
        string noHeader = Csv.WriteFromDataReaderToText(r2, new CsvWriteOptions { NewLine = "\n" }, writeHeader: false);
        Assert.Equal(string.Empty, noHeader);
    }

    [Fact]
    public async Task Excel_WriteFromDataReader_SyncAndAsync_RoundTripsTypedValues()
    {
        var ct = TestContext.Current.CancellationToken;
        using var table = CreateSampleTable();

        byte[] bytes;
        using (var reader = table.CreateDataReader())
        {
            bytes = HeroParser.Excel.WriteFromDataReaderToBytes(
                reader,
                new ExcelWriteOptions
                {
                    NullValue = "(none)",
                    DateTimeFormat = "yyyy-MM-dd",
                    Culture = CultureInfo.InvariantCulture
                },
                sheetName: "Export");
        }

        await using var xlsxStream = new MemoryStream(bytes);
        using (var excelReader = HeroParser.Excel.CreateDataReader(xlsxStream, sheetName: "Export"))
        {
            Assert.True(excelReader.Read());
            Assert.Equal("1", excelReader.GetString(0));
            Assert.Equal("Alice", excelReader.GetString(1));
            Assert.Equal("98.5", excelReader.GetString(2));
            Assert.Equal("2026-05-01", excelReader.GetString(4));
            Assert.Equal("11111111-2222-3333-4444-555555555555", excelReader.GetString(5));

            Assert.True(excelReader.Read());
            Assert.Equal("2", excelReader.GetString(0));
            Assert.Equal("(none)", excelReader.GetString(1));
            Assert.False(excelReader.Read());
        }

        string path = Path.Combine(Path.GetTempPath(), $"hero-dr-xlsx-{Guid.NewGuid():N}.xlsx");
        try
        {
            await using var reader2 = table.CreateDataReader();
            await HeroParser.Excel.WriteFromDataReaderToFileAsync(
                path,
                reader2,
                new ExcelWriteOptions { MaxRowCount = 1 },
                cancellationToken: ct);

            using var verifyReader = HeroParser.Excel.CreateDataReader(path);
            Assert.True(verifyReader.Read());
            Assert.False(verifyReader.Read());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Jsonl_WriteFromDataReader_SyncAndAsync_RoundTripsTypedValues()
    {
        var ct = TestContext.Current.CancellationToken;
        using var table = CreateSampleTable();

        string jsonl;
        using (var reader = table.CreateDataReader())
        {
            jsonl = Jsonl.WriteFromDataReaderToText(reader, new JsonlWriteOptions { WriteFinalNewline = true });
        }

        var lines = jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"Id\":1", lines[0]);
        Assert.Contains("\"Name\":\"Alice\"", lines[0]);
        Assert.Contains("\"Score\":98.5", lines[0]);
        Assert.Contains("\"Active\":true", lines[0]);
        Assert.Contains("\"Name\":null", lines[1]);

        await using var outStream = new MemoryStream();
        await using (var reader2 = table.CreateDataReader())
        {
            await Jsonl.WriteFromDataReaderAsync(
                reader2,
                outStream,
                new JsonlWriteOptions { WriteFinalNewline = false },
                leaveOpen: true,
                cancellationToken: ct);
        }

        Assert.True(outStream.CanRead); // leaveOpen respected
        string asyncJsonl = Encoding.UTF8.GetString(outStream.ToArray());
        Assert.False(asyncJsonl.EndsWith('\n'));
        Assert.Equal(2, asyncJsonl.Split('\n').Length);

        // Chained round-trip: CsvDataReader -> Jsonl
        using var csvStream = new MemoryStream(Encoding.UTF8.GetBytes("Col1,Col2\nfoo,bar\nbaz,qux\n"));
        using var csvReader = Csv.CreateDataReader(csvStream);
        string fromCsv = Jsonl.WriteFromDataReaderToText(csvReader);
        Assert.Equal("{\"Col1\":\"foo\",\"Col2\":\"bar\"}\n{\"Col1\":\"baz\",\"Col2\":\"qux\"}", fromCsv);
    }

    [Fact]
    public async Task FixedWidth_WriteFromDataReader_SyncAndAsync_HeaderNullsAndCompressedFile()
    {
        var ct = TestContext.Current.CancellationToken;
        using var table = CreateSampleTable();
        IReadOnlyList<FixedWidthFieldDefinition> columns =
        [
            new("Name", 8, FieldAlignment.Left),
            new("Id", 4, FieldAlignment.Right, '0'),
            new("Missing", 4, FieldAlignment.Left)
        ];
        var writeOptions = new FixedWidthWriteOptions { NullValue = "N/A", NewLine = "\n" };

        string text;
        using (var reader = table.CreateDataReader())
        {
            text = FixedWidth.WriteFromDataReaderToText(reader, columns, writeOptions, writeHeader: true);
        }

        string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Equal("Name    00IdMiss", lines[0]);
        Assert.Equal("Alice   0001N/A ", lines[1]);
        Assert.Equal("N/A     0002N/A ", lines[2]);

        await using var outStream = new MemoryStream();
        await using (var reader2 = table.CreateDataReader())
        {
            await FixedWidth.WriteFromDataReaderAsync(
                reader2,
                outStream,
                columns,
                writeOptions,
                writeHeader: false,
                leaveOpen: true,
                cancellationToken: ct);
        }

        Assert.True(outStream.CanRead);
        string[] asyncLines = Encoding.UTF8.GetString(outStream.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, asyncLines.Length);
        Assert.Equal("Alice   0001N/A ", asyncLines[0]);
        Assert.Equal("N/A     0002N/A ", asyncLines[1]);

        string dir = Path.Combine(Path.GetTempPath(), $"hero-dr-fw-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string brPath = Path.Combine(dir, "sync.dat.br");
            using (var reader3 = table.CreateDataReader())
                FixedWidth.WriteFromDataReaderToFile(brPath, reader3, columns, writeOptions, writeHeader: false);

            string gzPath = Path.Combine(dir, "async.dat.gz");
            await using (var reader4 = table.CreateDataReader())
                await FixedWidth.WriteFromDataReaderToFileAsync(gzPath, reader4, columns, writeOptions, writeHeader: false, cancellationToken: ct);

            foreach (string path in (string[])[brPath, gzPath])
            {
                var readRows = new List<string>();
                foreach (var row in FixedWidth.ReadFromFile(path))
                    readRows.Add($"{row.GetField(0, 8).CharSpan.TrimEnd()}:{row.GetField(8, 4).CharSpan}");
                Assert.Equal(["Alice:0001", "N/A:0002"], readRows);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Htb_DataReader_And_WriteFromDataReader_RoundTripsAllTenTypesAndMetadata()
    {
        var ct = TestContext.Current.CancellationToken;
        using var table = new DataTable();
        table.Columns.Add("I32", typeof(int));
        table.Columns.Add("I64", typeof(long));
        table.Columns.Add("F32", typeof(float));
        table.Columns.Add("F64", typeof(double));
        table.Columns.Add("Dec", typeof(decimal));
        table.Columns.Add("Flag", typeof(bool));
        table.Columns.Add("Stamp", typeof(DateTime));
        table.Columns.Add("Label", typeof(string));
        table.Columns.Add("Uid", typeof(Guid));
        table.Columns.Add("Vec", typeof(float[]));

        var stamp = new DateTime(2026, 11, 23, 8, 30, 0, DateTimeKind.Utc);
        var uid = new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        float[] vec = [1.5f, -2.25f, 3.75f];
        table.Rows.Add(42, 9_000_000_000L, 1.25f, 2.5d, 123.45m, true, stamp, "row1", uid, vec);
        table.Rows.Add(7, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, false, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value);

        await using var stream = new MemoryStream();
        await using (var sourceReader = table.CreateDataReader())
        {
            await HeroParser.Htb.WriteFromDataReaderAsync(sourceReader, stream, leaveOpen: true, cancellationToken: ct);
        }

        Assert.True(stream.CanRead);
        stream.Position = 0;

        using (var dr = HeroParser.Htb.CreateDataReader(stream, leaveOpen: true))
        {
            Assert.True(dr.HasRows);
            Assert.Equal(10, dr.FieldCount);
            Assert.Equal("I32", dr.GetName(0));
            Assert.Equal(9, dr.GetOrdinal("vec"));
            Assert.Equal(typeof(int), dr.GetFieldType(0));
            Assert.Equal(typeof(float[]), dr.GetFieldType(9));
            Assert.Equal("Int32", dr.GetDataTypeName(0));
            using DataTable schemaTable = dr.GetSchemaTable();
            Assert.Equal(10, schemaTable.Rows.Count);
            Assert.Throws<InvalidOperationException>(() => dr.GetValue(0));

            Assert.True(await dr.ReadAsync(ct));
            Assert.Equal(42, dr.GetInt32(0));
            Assert.Equal(9_000_000_000L, dr.GetInt64(1));
            Assert.Equal(1.25f, dr.GetFloat(2));
            Assert.Equal(2.5d, dr.GetDouble(3));
            Assert.Equal(123.45m, dr.GetDecimal(4));
            Assert.True(dr.GetBoolean(5));
            Assert.Equal(stamp, dr.GetDateTime(6));
            Assert.Equal("row1", dr.GetString(7));
            Assert.Equal(uid, dr.GetGuid(8));
            Assert.Equal(vec, Assert.IsType<float[]>(dr.GetValue(9)));

            Assert.True(await dr.ReadAsync(ct));
            Assert.Equal(7, dr.GetInt32(0));
            Assert.True(dr.IsDBNull(1));
            Assert.True(dr.IsDBNull(9));
            Assert.False(dr.GetBoolean(5));
            Assert.Throws<IndexOutOfRangeException>(() => { _ = dr.GetValue(10); });

            Assert.False(await dr.ReadAsync(ct));
            Assert.Throws<InvalidOperationException>(() => { _ = dr.GetValue(0); });
        }

        // SkipRows
        stream.Position = 0;
        using (var skippedDr = HeroParser.Htb.CreateDataReader(stream, new HtbReadOptions { SkipRows = 1 }, leaveOpen: true))
        {
            Assert.True(skippedDr.HasRows);
            Assert.True(skippedDr.Read());
            Assert.Equal(7, skippedDr.GetInt32(0));
            Assert.False(skippedDr.Read());
        }

        // Empty HTB stream (0 rows)
        table.Clear();
        await using var emptyStream = new MemoryStream();
        using (var emptySource = table.CreateDataReader())
            HeroParser.Htb.WriteFromDataReader(emptySource, emptyStream, leaveOpen: true);
        emptyStream.Position = 0;
        using (var emptyDr = HeroParser.Htb.CreateDataReader(emptyStream))
        {
            Assert.False(emptyDr.HasRows);
            Assert.Equal(10, emptyDr.FieldCount);
            Assert.False(emptyDr.Read());
            emptyDr.Close();
            Assert.True(emptyDr.IsClosed);
            Assert.Throws<InvalidOperationException>(() => { _ = emptyDr.Read(); });
        }

        // Sync + async compressed .htb.br file round-trip
        table.Rows.Add(99, 123L, 0.5f, 1.5d, 1.25m, true, stamp, "br-row", uid, new float[] { 4.5f });
        string dir = Path.Combine(Path.GetTempPath(), $"hero-dr-htb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string htbFile = Path.Combine(dir, "data.htb.br");
            await using (var src = table.CreateDataReader())
                await HeroParser.Htb.WriteFromDataReaderToFileAsync(htbFile, src, cancellationToken: ct);

            using var fileDr = HeroParser.Htb.CreateDataReader(htbFile);
            Assert.True(fileDr.HasRows);
            Assert.True(fileDr.Read());
            Assert.Equal(99, fileDr.GetInt32(0));
            Assert.Equal("br-row", fileDr.GetString(7));
            Assert.Equal([4.5f], Assert.IsType<float[]>(fileDr.GetValue(9)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Jsonl_WriteFromDataReader_RejectsNonUtf8Encoding()
    {
        var ct = TestContext.Current.CancellationToken;
        using var table = CreateSampleTable();
        var unicodeOptions = new JsonlWriteOptions { Encoding = Encoding.Unicode };

        using (var r1 = table.CreateDataReader())
        await using (var s1 = new MemoryStream())
        {
            var ex = Assert.Throws<JsonlException>(() => Jsonl.WriteFromDataReader(r1, s1, unicodeOptions));
            Assert.Equal(JsonlErrorCode.InvalidOptions, ex.ErrorCode);
        }

        using (var r2 = table.CreateDataReader())
        {
            var ex = Assert.Throws<JsonlException>(() => Jsonl.WriteFromDataReaderToText(r2, unicodeOptions));
            Assert.Equal(JsonlErrorCode.InvalidOptions, ex.ErrorCode);
        }

        await using (var r3 = table.CreateDataReader())
        await using (var s3 = new MemoryStream())
        {
            var ex = await Assert.ThrowsAsync<JsonlException>(
                () => Jsonl.WriteFromDataReaderAsync(r3, s3, unicodeOptions, cancellationToken: ct).AsTask());
            Assert.Equal(JsonlErrorCode.InvalidOptions, ex.ErrorCode);
        }

        using (var r4 = table.CreateDataReader())
        {
            string utf8Text = Jsonl.WriteFromDataReaderToText(r4, new JsonlWriteOptions { Encoding = Encoding.UTF8 });
            Assert.Contains("\"Name\":\"Alice\"", utf8Text);
        }

        using var r5 = table.CreateDataReader();
        string utf8NoBomText = Jsonl.WriteFromDataReaderToText(
            r5,
            new JsonlWriteOptions { Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false) });
        Assert.Contains("\"Name\":\"Alice\"", utf8NoBomText);
    }

    [Fact]
    public async Task Htb_WriteFromDataReaderAsync_DoesNotPerformSynchronousStreamWrites()
    {
        var ct = TestContext.Current.CancellationToken;
        using var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("LongText", typeof(string));
        table.Columns.Add("Payload", typeof(byte[]));
        table.Columns.Add("Vec", typeof(float[]));
        table.Columns.Add("Link", typeof(Uri));
        table.Columns.Add("Big", typeof(BigInteger));

        string largeText = new('x', 20_000);
        byte[] largeBytes = [.. Enumerable.Range(0, 20_000).Select(static i => (byte)(i % 251))];
        float[] largeVec = [.. Enumerable.Range(0, 5_200).Select(static i => i * 0.25f)];
        var uri = new Uri("https://example.com/heroparser/v2.10");
        BigInteger bigInt = BigInteger.Parse("9876543210987654321098765432109876543210", CultureInfo.InvariantCulture);

        table.Rows.Add(1, largeText, largeBytes, largeVec, uri, bigInt);
        for (int i = 2; i <= 500; i++)
        {
            table.Rows.Add(i, $"row-payload-{i:D4}-padding-to-exceed-16kb-buffer", new byte[] { (byte)(i & 0xFF) }, new float[] { i }, uri, bigInt + i);
        }

        using var backingStream = new MemoryStream();
        await using (var nonSyncStream = new NonSyncWriteStream(backingStream))
        await using (var sourceReader = table.CreateDataReader())
        {
            await HeroParser.Htb.WriteFromDataReaderAsync(sourceReader, nonSyncStream, leaveOpen: false, cancellationToken: ct);
        }

        backingStream.Position = 0;
        using var dr = HeroParser.Htb.CreateDataReader(backingStream, leaveOpen: true);
        Assert.True(await dr.ReadAsync(ct));
        Assert.Equal(1, dr.GetInt32(0));
        Assert.Equal(largeText, dr.GetString(1));
        Assert.Equal(largeBytes, Assert.IsType<byte[]>(dr.GetValue(2)));
        Assert.Equal(largeVec, Assert.IsType<float[]>(dr.GetValue(3)));
        Assert.Equal(uri, Assert.IsType<Uri>(dr.GetValue(4)));
        Assert.Equal(bigInt, Assert.IsType<BigInteger>(dr.GetValue(5)));

        int rowCount = 1;
        while (await dr.ReadAsync(ct))
        {
            rowCount++;
        }
        Assert.Equal(500, rowCount);
    }

    private sealed class NonSyncWriteStream(MemoryStream inner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }

        public override void Flush()
            => throw new InvalidOperationException("Synchronous Flush is disallowed.");

        public override Task FlushAsync(CancellationToken cancellationToken)
            => inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
            => throw new InvalidOperationException("Synchronous Write is disallowed.");

        public override void Write(ReadOnlySpan<byte> buffer)
            => throw new InvalidOperationException("Synchronous Write is disallowed.");

        public override void WriteByte(byte value)
            => throw new InvalidOperationException("Synchronous WriteByte is disallowed.");

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => inner.WriteAsync(buffer, offset, count, cancellationToken);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => inner.WriteAsync(buffer, cancellationToken);
    }
}
