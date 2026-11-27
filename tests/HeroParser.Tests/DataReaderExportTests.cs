using System.Collections;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using HeroParser.Conversion;
using HeroParser.Excels.Core;
using HeroParser.Excels.Writing;
using HeroParser.Excels.Xlsx;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Writing;
using HeroParser.Htbs;
using HeroParser.Htbs.Records;
using HeroParser.Internal;
using HeroParser.JsonLines;
using HeroParser.JsonLines.Reading;
using HeroParser.JsonLines.Reading.Data;
using HeroParser.JsonLines.Writing;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Writing;
using HeroParser.Validation;
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
    public async Task Jsonl_WriteFromDataReader_EnforcesMaxOutputSizeBeforeWritingAndInvokesOnErrorAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        using var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("Name", typeof(string));
        table.Rows.Add(1, "Alice");
        table.Rows.Add(2, "Bob");

        // Cap below row 1 length must write 0 bytes before throwing.
        using (var rSyncSmall = table.CreateDataReader())
        await using (var sSyncSmall = new MemoryStream())
        {
            var ex = Assert.Throws<JsonlException>(() =>
                Jsonl.WriteFromDataReader(rSyncSmall, sSyncSmall, new JsonlWriteOptions { MaxOutputSize = 5 }));
            Assert.Equal(JsonlErrorCode.OutputSizeExceeded, ex.ErrorCode);
            Assert.Equal(0, sSyncSmall.Length);
        }

        await using (var rAsyncSmall = table.CreateDataReader())
        await using (var sAsyncSmall = new MemoryStream())
        {
            var ex = await Assert.ThrowsAsync<JsonlException>(() =>
                Jsonl.WriteFromDataReaderAsync(rAsyncSmall, sAsyncSmall, new JsonlWriteOptions { MaxOutputSize = 5 }, cancellationToken: ct).AsTask());
            Assert.Equal(JsonlErrorCode.OutputSizeExceeded, ex.ErrorCode);
            Assert.Equal(0, sAsyncSmall.Length);
        }

        // Exact cap for row 1: rejecting row 2 (or final newline) leaves no dangling separator.
        const string row1 = "{\"Id\":1,\"Name\":\"Alice\"}";
        int row1Len = Encoding.UTF8.GetByteCount(row1);

        using (var rSyncExact = table.CreateDataReader())
        await using (var sSyncExact = new MemoryStream())
        {
            var ex = Assert.Throws<JsonlException>(() =>
                Jsonl.WriteFromDataReader(rSyncExact, sSyncExact, new JsonlWriteOptions { MaxOutputSize = row1Len, NewLine = "\n" }));
            Assert.Equal(JsonlErrorCode.OutputSizeExceeded, ex.ErrorCode);
            Assert.Equal(row1, Encoding.UTF8.GetString(sSyncExact.ToArray()));
        }

        await using (var rAsyncExact = table.CreateDataReader())
        await using (var sAsyncExact = new MemoryStream())
        {
            var ex = await Assert.ThrowsAsync<JsonlException>(() =>
                Jsonl.WriteFromDataReaderAsync(rAsyncExact, sAsyncExact, new JsonlWriteOptions { MaxOutputSize = row1Len, NewLine = "\n" }, cancellationToken: ct).AsTask());
            Assert.Equal(JsonlErrorCode.OutputSizeExceeded, ex.ErrorCode);
            Assert.Equal(row1, Encoding.UTF8.GetString(sAsyncExact.ToArray()));
        }

        // Exact cap for single-row without final newline; final newline overflow leaves no trailing '\n'.
        using var singleRowTable = new DataTable();
        singleRowTable.Columns.Add("Id", typeof(int));
        singleRowTable.Columns.Add("Name", typeof(string));
        singleRowTable.Rows.Add(1, "Alice");

        using (var rSyncFinal = singleRowTable.CreateDataReader())
        await using (var sSyncFinal = new MemoryStream())
        {
            var ex = Assert.Throws<JsonlException>(() =>
                Jsonl.WriteFromDataReader(rSyncFinal, sSyncFinal, new JsonlWriteOptions { MaxOutputSize = row1Len, NewLine = "\n", WriteFinalNewline = true }));
            Assert.Equal(JsonlErrorCode.OutputSizeExceeded, ex.ErrorCode);
            Assert.Equal(row1, Encoding.UTF8.GetString(sSyncFinal.ToArray()));
        }

        await using (var rAsyncFinal = singleRowTable.CreateDataReader())
        await using (var sAsyncFinal = new MemoryStream())
        {
            var ex = await Assert.ThrowsAsync<JsonlException>(() =>
                Jsonl.WriteFromDataReaderAsync(rAsyncFinal, sAsyncFinal, new JsonlWriteOptions { MaxOutputSize = row1Len, NewLine = "\n", WriteFinalNewline = true }, cancellationToken: ct).AsTask());
            Assert.Equal(JsonlErrorCode.OutputSizeExceeded, ex.ErrorCode);
            Assert.Equal(row1, Encoding.UTF8.GetString(sAsyncFinal.ToArray()));
        }

        // OnError callback: SkipRecord skips failing middle row without dangling blank lines/separators.
        long? observedSyncIndex = null;
        using (var failingSyncReader = new ThrowingRowDbDataReader(failAtRowIndex: 1))
        await using (var outSync = new MemoryStream())
        {
            Jsonl.WriteFromDataReader(failingSyncReader, outSync, new JsonlWriteOptions
            {
                NewLine = "\n",
                OnError = (ctx, _) =>
                {
                    observedSyncIndex = ctx.RecordIndex;
                    return JsonlSerializeErrorAction.SkipRecord;
                }
            });
            Assert.Equal(1, observedSyncIndex);
            Assert.Equal("{\"Id\":1}\n{\"Id\":3}", Encoding.UTF8.GetString(outSync.ToArray()));
        }

        long? observedAsyncIndex = null;
        await using (var failingAsyncReader = new ThrowingRowDbDataReader(failAtRowIndex: 1))
        await using (var outAsync = new MemoryStream())
        {
            await Jsonl.WriteFromDataReaderAsync(failingAsyncReader, outAsync, new JsonlWriteOptions
            {
                NewLine = "\n",
                OnError = (ctx, _) =>
                {
                    observedAsyncIndex = ctx.RecordIndex;
                    return JsonlSerializeErrorAction.SkipRecord;
                }
            }, cancellationToken: ct);
            Assert.Equal(1, observedAsyncIndex);
            Assert.Equal("{\"Id\":1}\n{\"Id\":3}", Encoding.UTF8.GetString(outAsync.ToArray()));
        }

        // OnError = Throw (and default null) throws JsonlException(SerializeError).
        using (var throwSyncReader = new ThrowingRowDbDataReader(failAtRowIndex: 1))
        await using (var outThrowSync = new MemoryStream())
        {
            var ex = Assert.Throws<JsonlException>(() =>
                Jsonl.WriteFromDataReader(throwSyncReader, outThrowSync, new JsonlWriteOptions
                {
                    OnError = (_, _) => JsonlSerializeErrorAction.Throw
                }));
            Assert.Equal(JsonlErrorCode.SerializeError, ex.ErrorCode);
            Assert.Equal(2, ex.LineNumber);
        }

        await using (var throwAsyncReader = new ThrowingRowDbDataReader(failAtRowIndex: 1))
        await using (var outThrowAsync = new MemoryStream())
        {
            var ex = await Assert.ThrowsAsync<JsonlException>(() =>
                Jsonl.WriteFromDataReaderAsync(throwAsyncReader, outThrowAsync, cancellationToken: ct).AsTask());
            Assert.Equal(JsonlErrorCode.SerializeError, ex.ErrorCode);
            Assert.Equal(2, ex.LineNumber);
        }
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

    [Fact]
    public async Task Htb_WriteFromDataReader_And_WriteFromDataReaderAsync_EnforceSequentialAccess()
    {
        var ct = TestContext.Current.CancellationToken;
        using var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Score", typeof(decimal));
        table.Columns.Add("Payload", typeof(byte[]));

        table.Rows.Add(1, "Alice", 98.5m, new byte[] { 1, 2, 3 });
        table.Rows.Add(2, DBNull.Value, DBNull.Value, new byte[] { 4, 5 });
        table.Rows.Add(DBNull.Value, "Charlie", 77.25m, DBNull.Value);

        await using var syncOut = new MemoryStream();
        using (var seqReader = new SequentialAccessDataReader(table.CreateDataReader()))
        {
            HeroParser.Htb.WriteFromDataReader(seqReader, syncOut, leaveOpen: true);
        }

        await using var asyncOut = new MemoryStream();
        await using (var seqAsyncReader = new SequentialAccessDataReader(table.CreateDataReader()))
        {
            await HeroParser.Htb.WriteFromDataReaderAsync(seqAsyncReader, asyncOut, leaveOpen: true, cancellationToken: ct);
        }

        foreach (MemoryStream stream in (MemoryStream[])[syncOut, asyncOut])
        {
            stream.Position = 0;
            using var dr = HeroParser.Htb.CreateDataReader(stream);
            Assert.True(dr.Read());
            Assert.Equal(1, dr.GetInt32(0));
            Assert.Equal("Alice", dr.GetString(1));
            Assert.Equal(98.5m, dr.GetDecimal(2));
            Assert.Equal([1, 2, 3], Assert.IsType<byte[]>(dr.GetValue(3)));

            Assert.True(dr.Read());
            Assert.Equal(2, dr.GetInt32(0));
            Assert.True(dr.IsDBNull(1));
            Assert.True(dr.IsDBNull(2));
            Assert.Equal([4, 5], Assert.IsType<byte[]>(dr.GetValue(3)));

            Assert.True(dr.Read());
            Assert.True(dr.IsDBNull(0));
            Assert.Equal("Charlie", dr.GetString(1));
            Assert.Equal(77.25m, dr.GetDecimal(2));
            Assert.True(dr.IsDBNull(3));

            Assert.False(dr.Read());
        }
    }

    [Fact]
    public void Htb_WriteFromDataReaderToBytes_And_CreateDataReaderLeaveOpenDefault_Work()
    {
        using var table = CreateSampleTable();
        byte[] bytes;
        using (var source = table.CreateDataReader())
        {
            bytes = HeroParser.Htb.WriteFromDataReaderToBytes(source);
        }

        using var ms = new MemoryStream(bytes);
        using (var dr = HeroParser.Htb.CreateDataReader(ms))
        {
            Assert.True(dr.Read());
            Assert.Equal(1, dr.GetInt32(0));
            Assert.Equal("Alice", dr.GetString(1));
            Assert.True(dr.Read());
            Assert.True(dr.IsDBNull(1));
            Assert.False(dr.Read());
        }

        Assert.True(ms.CanRead);
    }

    [Fact(Timeout = 15_000)]
    public async Task HtbDataReader_ReadAsync_CancelsPromptlyWhenStreamStalls()
    {
        using var table = CreateSampleTable();
        byte[] htbBytes;
        using (var source = table.CreateDataReader())
        {
            htbBytes = HeroParser.Htb.WriteFromDataReaderToBytes(source);
        }

        // Allow header + row 1 + partial row 2, then block in Stream.ReadAsync on row 2 until cancelled.
        int pauseAfterBytes = htbBytes.Length - 2;
        using var stalledStream = new StallingAfterPrefixStream(htbBytes, pauseAfterBytes);
        await using var dr = HeroParser.Htb.CreateDataReader(stalledStream, leaveOpen: true);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Assert.True(await dr.ReadAsync(cts.Token));
        Task<bool> readTask = dr.ReadAsync(cts.Token);

        await stalledStream.StalledTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await readTask);
    }

    [Fact]
    public async Task Csv_WriteFromDataReader_RespectsOptionsWriteHeaderFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        using var table = CreateSampleTable();
        var options = new CsvWriteOptions { WriteHeader = false, NewLine = "\n" };

        string textCsv;
        using (var r1 = table.CreateDataReader())
            textCsv = Csv.WriteFromDataReaderToText(r1, options);
        Assert.DoesNotContain("Id,Name", textCsv);
        Assert.StartsWith("1,Alice,", textCsv, StringComparison.Ordinal);

        using var sw = new StringWriter();
        using (var r2 = table.CreateDataReader())
            Csv.WriteFromDataReader(r2, sw, options, leaveOpen: true);
        Assert.Equal(textCsv, sw.ToString());

        using var msSync = new MemoryStream();
        using (var r3 = table.CreateDataReader())
            Csv.WriteFromDataReader(r3, msSync, options, leaveOpen: true);
        Assert.Equal(textCsv, Encoding.UTF8.GetString(msSync.ToArray()).TrimStart('\uFEFF'));

        await using var msAsync = new MemoryStream();
        await using (var r4 = table.CreateDataReader())
            await Csv.WriteFromDataReaderAsync(r4, msAsync, options, leaveOpen: true, cancellationToken: ct);
        Assert.Equal(textCsv, Encoding.UTF8.GetString(msAsync.ToArray()));
    }

    private sealed class SequentialAccessDataReader(DbDataReader inner) : DbDataReader
    {
        private int lastOrdinal = -1;

        private void VerifyForwardOrdinal(int ordinal)
        {
            if (ordinal < lastOrdinal)
                throw new InvalidOperationException($"SequentialAccess violation: ordinal {ordinal} accessed after ordinal {lastOrdinal}.");
            lastOrdinal = ordinal;
        }

        public override bool Read()
        {
            lastOrdinal = -1;
            return inner.Read();
        }

        public override async Task<bool> ReadAsync(CancellationToken cancellationToken)
        {
            lastOrdinal = -1;
            return await inner.ReadAsync(cancellationToken).ConfigureAwait(false);
        }

        public override bool IsDBNull(int ordinal)
        {
            VerifyForwardOrdinal(ordinal);
            return inner.IsDBNull(ordinal);
        }

        public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken)
        {
            VerifyForwardOrdinal(ordinal);
            return inner.IsDBNullAsync(ordinal, cancellationToken);
        }

        public override object GetValue(int ordinal) { VerifyForwardOrdinal(ordinal); return inner.GetValue(ordinal); }
        public override bool GetBoolean(int ordinal) { VerifyForwardOrdinal(ordinal); return inner.GetBoolean(ordinal); }
        public override byte GetByte(int ordinal) { VerifyForwardOrdinal(ordinal); return inner.GetByte(ordinal); }
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) { VerifyForwardOrdinal(ordinal); return inner.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length); }
        public override char GetChar(int ordinal) { VerifyForwardOrdinal(ordinal); return inner.GetChar(ordinal); }
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) { VerifyForwardOrdinal(ordinal); return inner.GetChars(ordinal, dataOffset, buffer, bufferOffset, length); }
        public override DateTime GetDateTime(int ordinal) { VerifyForwardOrdinal(ordinal); return inner.GetDateTime(ordinal); }
        public override decimal GetDecimal(int ordinal) { VerifyForwardOrdinal(ordinal); return inner.GetDecimal(ordinal); }
        public override double GetDouble(int ordinal) { VerifyForwardOrdinal(ordinal); return inner.GetDouble(ordinal); }
        public override float GetFloat(int ordinal) { VerifyForwardOrdinal(ordinal); return inner.GetFloat(ordinal); }
        public override Guid GetGuid(int ordinal) { VerifyForwardOrdinal(ordinal); return inner.GetGuid(ordinal); }
        public override short GetInt16(int ordinal) { VerifyForwardOrdinal(ordinal); return inner.GetInt16(ordinal); }
        public override int GetInt32(int ordinal) { VerifyForwardOrdinal(ordinal); return inner.GetInt32(ordinal); }
        public override long GetInt64(int ordinal) { VerifyForwardOrdinal(ordinal); return inner.GetInt64(ordinal); }
        public override string GetString(int ordinal) { VerifyForwardOrdinal(ordinal); return inner.GetString(ordinal); }
        public override int GetValues(object[] values) => inner.GetValues(values);
        public override int FieldCount => inner.FieldCount;
        public override int Depth => inner.Depth;
        public override bool HasRows => inner.HasRows;
        public override bool IsClosed => inner.IsClosed;
        public override int RecordsAffected => inner.RecordsAffected;
        public override object this[int ordinal] => GetValue(ordinal);
        public override object this[string name] => GetValue(GetOrdinal(name));
        public override string GetDataTypeName(int ordinal) => inner.GetDataTypeName(ordinal);
        public override IEnumerator GetEnumerator() => inner.GetEnumerator();
        public override Type GetFieldType(int ordinal) => inner.GetFieldType(ordinal);
        public override string GetName(int ordinal) => inner.GetName(ordinal);
        public override int GetOrdinal(string name) => inner.GetOrdinal(name);
        public override bool NextResult() => inner.NextResult();
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class StallingAfterPrefixStream(byte[] data, int stallAfterBytes) : Stream
    {
        private readonly TaskCompletionSource stalled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int position;

        public Task StalledTask => stalled.Task;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (position < stallAfterBytes)
            {
                int toCopy = Math.Min(buffer.Length, stallAfterBytes - position);
                data.AsSpan(position, toCopy).CopyTo(buffer.Span);
                position += toCopy;
                return toCopy;
            }

            stalled.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
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

    [Fact]
    public async Task AsyncExporters_InitializeUninitializedDataReadersAsynchronouslyWithoutSyncReads()
    {
        var ct = TestContext.Current.CancellationToken;
        byte[] jsonlBytes = Encoding.UTF8.GetBytes("{\"Id\":1,\"Name\":\"Alice\"}\n{\"Id\":2,\"Name\":\"Bob\"}\n");
        byte[] emptyJsonlBytes = [];

        using var sampleTable = CreateSampleTable();
        byte[] htbBytes;
        using (var source = sampleTable.CreateDataReader())
        {
            htbBytes = HeroParser.Htb.WriteFromDataReaderToBytes(source);
        }

        byte[] emptyHtbBytes;
        using (var emptyTable = new DataTable())
        {
            emptyTable.Columns.Add("Id", typeof(int));
            emptyTable.Columns.Add("Name", typeof(string));
            using var emptySource = emptyTable.CreateDataReader();
            emptyHtbBytes = HeroParser.Htb.WriteFromDataReaderToBytes(emptySource);
        }

        // JSONL uninitialized reader -> CSV async exporter
        await using (var inStream = new AsyncOnlyReadStream(jsonlBytes))
        await using (var dr = Jsonl.CreateDataReader(inStream, leaveOpen: true))
        await using (var outMs = new MemoryStream())
        {
            await Csv.WriteFromDataReaderAsync(dr, outMs, cancellationToken: ct);
            string csv = Encoding.UTF8.GetString(outMs.ToArray());
            Assert.Contains("Alice", csv);
            Assert.Contains("Bob", csv);
        }

        // Empty JSONL uninitialized reader with explicit schema -> JSONL, Excel, FixedWidth, HTB
        var explicitColumns = new[]
        {
            new JsonlColumnDefinition("Id", "Id", typeof(string)),
            new JsonlColumnDefinition("Name", "Name", typeof(string))
        };
        await using (var inStream = new AsyncOnlyReadStream(emptyJsonlBytes))
        await using (var dr = Jsonl.CreateDataReader(inStream, readerOptions: new JsonlDataReaderOptions { Columns = explicitColumns }, leaveOpen: true))
        await using (var outMs = new MemoryStream())
        {
            await Csv.WriteFromDataReaderAsync(dr, outMs, cancellationToken: ct);
            string csv = Encoding.UTF8.GetString(outMs.ToArray());
            Assert.StartsWith("Id,Name", csv);
        }

        // HTB uninitialized reader -> CSV, JSONL, FixedWidth, Excel, HTB
        await using (var inStream = new AsyncOnlyReadStream(htbBytes))
        await using (var dr = HeroParser.Htb.CreateDataReader(inStream, leaveOpen: true))
        await using (var outMs = new MemoryStream())
        {
            await Csv.WriteFromDataReaderAsync(dr, outMs, cancellationToken: ct);
            Assert.Contains("Alice", Encoding.UTF8.GetString(outMs.ToArray()));
        }

        await using (var inStream = new AsyncOnlyReadStream(htbBytes))
        await using (var dr = HeroParser.Htb.CreateDataReader(inStream, leaveOpen: true))
        await using (var outMs = new MemoryStream())
        {
            await Jsonl.WriteFromDataReaderAsync(dr, outMs, cancellationToken: ct);
            Assert.Contains("\"Name\":\"Alice\"", Encoding.UTF8.GetString(outMs.ToArray()));
        }

        await using (var inStream = new AsyncOnlyReadStream(htbBytes))
        await using (var dr = HeroParser.Htb.CreateDataReader(inStream, leaveOpen: true))
        await using (var outMs = new MemoryStream())
        {
            IReadOnlyList<FixedWidthFieldDefinition> fwCols = [new("Name", 8), new("Id", 4)];
            await FixedWidth.WriteFromDataReaderAsync(dr, outMs, fwCols, writeHeader: true, leaveOpen: true, cancellationToken: ct);
            Assert.Contains("Alice", Encoding.UTF8.GetString(outMs.ToArray()));
        }

        await using (var inStream = new AsyncOnlyReadStream(htbBytes))
        await using (var dr = HeroParser.Htb.CreateDataReader(inStream, leaveOpen: true))
        await using (var outMs = new MemoryStream())
        {
            await HeroParser.Excel.WriteFromDataReaderAsync(dr, outMs, leaveOpen: true, cancellationToken: ct);
            outMs.Position = 0;
            var excelRows = HeroParser.Excel.Read().WithoutHeader().FromSheet("Sheet1").FromStream(outMs);
            Assert.Equal(3, excelRows.Count);
            Assert.Equal("Alice", excelRows[1][1]);
        }

        await using (var inStream = new AsyncOnlyReadStream(emptyHtbBytes))
        await using (var dr = HeroParser.Htb.CreateDataReader(inStream, leaveOpen: true))
        await using (var outMs = new MemoryStream())
        {
            await HeroParser.Htb.WriteFromDataReaderAsync(dr, outMs, leaveOpen: true, cancellationToken: ct);
            outMs.Position = 0;
            using var checkDr = HeroParser.Htb.CreateDataReader(outMs, leaveOpen: true);
            Assert.Equal(2, checkDr.FieldCount);
            Assert.False(await checkDr.ReadAsync(ct));
        }
    }

    [Fact]
    public async Task Htb_WriteFromDataReader_ExplicitNonNullableSchema_RejectsDbNullSyncAndAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        using var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Rows.Add(DBNull.Value);

        var strictSchema = new HtbSchema([new HtbColumn("Id", HtbDataType.Int32, isNullable: false)]);

        using (var src = table.CreateDataReader())
        using (var outMs = new MemoryStream())
        {
            var ex = Assert.Throws<HtbException>(() => HeroParser.Htb.WriteFromDataReader(src, outMs, schema: strictSchema, leaveOpen: true));
            Assert.Equal(HtbErrorCode.SerializationError, ex.ErrorCode);
        }

        await using (var asyncSrc = table.CreateDataReader())
        await using (var outMs = new MemoryStream())
        {
            var ex = await Assert.ThrowsAsync<HtbException>(() =>
                HeroParser.Htb.WriteFromDataReaderAsync(asyncSrc, outMs, schema: strictSchema, leaveOpen: true, cancellationToken: ct).AsTask());
            Assert.Equal(HtbErrorCode.SerializationError, ex.ErrorCode);
        }
    }

    [Fact]
    public async Task Excel_WriteFromDataReader_MaxRowCount_DoesNotAdvancePastLimitSyncAndAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        using var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Rows.Add(1);
        table.Rows.Add(2);
        table.Rows.Add(3);

        var excelOptions = new ExcelWriteOptions { MaxRowCount = 1 };

        using (var reader = table.CreateDataReader())
        using (var outMs = new MemoryStream())
        {
            HeroParser.Excel.WriteFromDataReader(reader, outMs, excelOptions, leaveOpen: true);
            Assert.True(reader.Read());
            Assert.Equal(2, reader.GetInt32(0));

            outMs.Position = 0;
            var rows = HeroParser.Excel.Read().FromStream(outMs);
            Assert.Single(rows);
            Assert.Equal("1", rows[0][0]);
        }

        await using (var asyncReader = table.CreateDataReader())
        await using (var outMs = new MemoryStream())
        {
            await HeroParser.Excel.WriteFromDataReaderAsync(asyncReader, outMs, excelOptions, leaveOpen: true, cancellationToken: ct);
            Assert.True(await asyncReader.ReadAsync(ct));
            Assert.Equal(2, asyncReader.GetInt32(0));

            outMs.Position = 0;
            var rows = HeroParser.Excel.Read().FromStream(outMs);
            Assert.Single(rows);
            Assert.Equal("1", rows[0][0]);
        }
    }

    private sealed class AsyncOnlyReadStream(byte[] data) : Stream
    {
        private readonly MemoryStream inner = new(data);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
            => throw new NotSupportedException("Synchronous Read is disallowed.");

        public override int Read(Span<byte> buffer)
            => throw new NotSupportedException("Synchronous Read is disallowed.");

        public override int ReadByte()
            => throw new NotSupportedException("Synchronous ReadByte is disallowed.");

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => inner.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }

    [Fact]
    public async Task Excel_WriteFromDataReader_MaxOutputSize_ThrowsSyncAndAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        using var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("Name", typeof(string));
        table.Rows.Add(1, "Alice");
        table.Rows.Add(2, "Bob");

        var options = new ExcelWriteOptions { MaxOutputSize = 50 };

        using (var reader = table.CreateDataReader())
        using (var outMs = new MemoryStream())
        {
            Assert.Throws<ExcelException>(() => HeroParser.Excel.WriteFromDataReader(reader, outMs, options, leaveOpen: true));
        }

        await using (var asyncReader = table.CreateDataReader())
        await using (var outMs = new MemoryStream())
        {
            await Assert.ThrowsAsync<ExcelException>(async () =>
                await HeroParser.Excel.WriteFromDataReaderAsync(asyncReader, outMs, options, leaveOpen: true, cancellationToken: ct));
        }

        // Empty headerless export must still enforce MaxOutputSize after Close() writes worksheet XML footer.
        using var emptyTable = new DataTable();
        emptyTable.Columns.Add("Id", typeof(int));
        var headerlessTinyOptions = new ExcelWriteOptions { WriteHeader = false, MaxOutputSize = 1 };

        using (var emptyReader = emptyTable.CreateDataReader())
        using (var outMs = new MemoryStream())
        {
            Assert.Throws<ExcelException>(() =>
                HeroParser.Excel.WriteFromDataReader(emptyReader, outMs, headerlessTinyOptions, leaveOpen: true));
        }

        await using (var asyncEmptyReader = emptyTable.CreateDataReader())
        await using (var outMs = new MemoryStream())
        {
            await Assert.ThrowsAsync<ExcelException>(async () =>
                await HeroParser.Excel.WriteFromDataReaderAsync(asyncEmptyReader, outMs, headerlessTinyOptions, leaveOpen: true, cancellationToken: ct));
        }
    }

    [Fact]
    public async Task Jsonl_WriteFromDataReader_DuplicateColumnNames_ThrowsBeforeWritingAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        using var htbStream = new MemoryStream();
        using (var writer = new HeroParser.Htbs.Writing.HtbStreamWriter(htbStream, leaveOpen: true))
        {
            writer.WriteHeader(new HeroParser.Htbs.Records.HtbSchema([
                new HeroParser.Htbs.Records.HtbColumn("Id", HeroParser.Htbs.Records.HtbDataType.Int32, isNullable: false),
                new HeroParser.Htbs.Records.HtbColumn("Id", HeroParser.Htbs.Records.HtbDataType.Int32, isNullable: false)
            ]));
            writer.WriteMask([0]);
            writer.WriteInt32(1);
            writer.WriteInt32(2);
            writer.Flush();
        }

        htbStream.Position = 0;
        using (var syncDr = HeroParser.Htb.CreateDataReader(htbStream, leaveOpen: true))
        using (var syncOut = new MemoryStream())
        {
            var ex = Assert.Throws<HeroParser.JsonLines.JsonlException>(() =>
                Jsonl.WriteFromDataReader(syncDr, syncOut, leaveOpen: true));
            Assert.Equal(HeroParser.JsonLines.JsonlErrorCode.SerializeError, ex.ErrorCode);
            Assert.Equal(0, syncOut.Length);
        }

        htbStream.Position = 0;
        using (var textDr = HeroParser.Htb.CreateDataReader(htbStream, leaveOpen: true))
        {
            var ex = Assert.Throws<HeroParser.JsonLines.JsonlException>(() =>
                Jsonl.WriteFromDataReaderToText(textDr));
            Assert.Equal(HeroParser.JsonLines.JsonlErrorCode.SerializeError, ex.ErrorCode);
        }

        htbStream.Position = 0;
        await using var asyncDr = HeroParser.Htb.CreateDataReader(htbStream, leaveOpen: true);
        await using var asyncOut = new MemoryStream();
        var asyncEx = await Assert.ThrowsAsync<HeroParser.JsonLines.JsonlException>(async () =>
            await Jsonl.WriteFromDataReaderAsync(asyncDr, asyncOut, leaveOpen: true, cancellationToken: ct));
        Assert.Equal(HeroParser.JsonLines.JsonlErrorCode.SerializeError, asyncEx.ErrorCode);
        Assert.Equal(0, asyncOut.Length);
    }

    [Fact]
    public async Task Excel_WriteFromDataReader_WriteProgress_ReportsIntervalsAndFinalSyncAndAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        using var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        for (int i = 1; i <= 5; i++)
            table.Rows.Add(i);

        var syncReports = new List<ExcelWriteProgress>();
        var syncOptions = new ExcelWriteOptions
        {
            WriteProgress = new SyncExcelProgress(syncReports.Add),
            WriteProgressIntervalRows = 2
        };

        using (var reader = table.CreateDataReader())
        using (var outMs = new MemoryStream())
        {
            HeroParser.Excel.WriteFromDataReader(reader, outMs, syncOptions, sheetName: "Metrics", leaveOpen: true);
        }
        Assert.Equal([2, 4, 5], [.. syncReports.Select(r => r.RowsWritten)]);
        Assert.All(syncReports, r => Assert.Equal("Metrics", r.SheetName));

        var asyncReports = new List<ExcelWriteProgress>();
        var asyncOptions = new ExcelWriteOptions
        {
            WriteProgress = new SyncExcelProgress(asyncReports.Add),
            WriteProgressIntervalRows = 2
        };

        await using (var asyncReader = table.CreateDataReader())
        await using (var outMs = new MemoryStream())
        {
            await HeroParser.Excel.WriteFromDataReaderAsync(asyncReader, outMs, asyncOptions, sheetName: "Metrics", leaveOpen: true, cancellationToken: ct);
        }
        Assert.Equal([2, 4, 5], [.. asyncReports.Select(r => r.RowsWritten)]);
        Assert.All(asyncReports, r => Assert.Equal("Metrics", r.SheetName));
    }

    [Fact]
    public async Task WriteFromDataReader_LeaveOpenFalse_DisposesStreamWhenFirstReadFailsOrCancels()
    {
        var ct = TestContext.Current.CancellationToken;
        using var canceledCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        canceledCts.Cancel();

        var excelStream = new TrackingMemoryStream();
        await using (var reader = new CancelingDbDataReader())
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await HeroParser.Excel.WriteFromDataReaderAsync(reader, excelStream, leaveOpen: false, cancellationToken: canceledCts.Token));
        }
        Assert.True(excelStream.IsDisposed);

        var htbStream = new TrackingMemoryStream();
        await using (var reader = new CancelingDbDataReader())
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await HeroParser.Htb.WriteFromDataReaderAsync(reader, htbStream, leaveOpen: false, cancellationToken: canceledCts.Token));
        }
        Assert.True(htbStream.IsDisposed);

        using var singleColTable = new DataTable();
        singleColTable.Columns.Add("Id", typeof(int));
        var htbSyncStream = new TrackingMemoryStream();
        var mismatchedSchema = new HtbSchema([new HtbColumn("A", HtbDataType.Int32, false), new HtbColumn("B", HtbDataType.Int32, false)]);
        using (var reader = singleColTable.CreateDataReader())
        {
            Assert.Throws<HtbException>(() => HeroParser.Htb.WriteFromDataReader(reader, htbSyncStream, schema: mismatchedSchema, leaveOpen: false));
        }
        Assert.True(htbSyncStream.IsDisposed);

        var excelSyncStream = new TrackingMemoryStream();
        using (var reader = singleColTable.CreateDataReader())
        {
            Assert.Throws<ExcelException>(() =>
                HeroParser.Excel.WriteFromDataReader(reader, excelSyncStream, new ExcelWriteOptions { MaxRowCount = -1 }, leaveOpen: false));
        }
        Assert.True(excelSyncStream.IsDisposed);
    }

    [Fact]
    public async Task Excel_WriteFromDataReaderAsync_DoesNotPerformSynchronousStreamWrites()
    {
        var ct = TestContext.Current.CancellationToken;
        using var table = CreateSampleTable();

        using var backingStream = new MemoryStream();
        await using (var nonSyncStream = new NonSyncWriteStream(backingStream))
        await using (var sourceReader = table.CreateDataReader())
        {
            await HeroParser.Excel.WriteFromDataReaderAsync(
                sourceReader,
                nonSyncStream,
                sheetName: "AsyncSheet",
                leaveOpen: false,
                cancellationToken: ct);
        }

        backingStream.Position = 0;
        var rows = HeroParser.Excel.Read().WithoutHeader().FromSheet("AsyncSheet").FromStream(backingStream);
        Assert.Equal(3, rows.Count);
        Assert.Equal("Id", rows[0][0]);
        Assert.Equal("Alice", rows[1][1]);
        Assert.Equal("2", rows[2][0]);
        Assert.Equal("", rows[2][1]);
    }

    private sealed class SyncExcelProgress(Action<ExcelWriteProgress> callback) : IProgress<ExcelWriteProgress>
    {
        public void Report(ExcelWriteProgress value) => callback(value);
    }

    private sealed class TrackingMemoryStream : MemoryStream
    {
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return base.DisposeAsync();
        }
    }

    private sealed class CancelingDbDataReader : DbDataReader
    {
        public override int FieldCount => 1;
        public override object this[int ordinal] => throw new NotSupportedException();
        public override object this[string name] => throw new NotSupportedException();
        public override int Depth => 0;
        public override bool HasRows => true;
        public override bool IsClosed => false;
        public override int RecordsAffected => -1;
        public override bool GetBoolean(int ordinal) => throw new NotSupportedException();
        public override byte GetByte(int ordinal) => throw new NotSupportedException();
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override char GetChar(int ordinal) => throw new NotSupportedException();
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override string GetDataTypeName(int ordinal) => "int";
        public override DateTime GetDateTime(int ordinal) => throw new NotSupportedException();
        public override decimal GetDecimal(int ordinal) => throw new NotSupportedException();
        public override double GetDouble(int ordinal) => throw new NotSupportedException();
        public override Type GetFieldType(int ordinal) => typeof(int);
        public override float GetFloat(int ordinal) => throw new NotSupportedException();
        public override Guid GetGuid(int ordinal) => throw new NotSupportedException();
        public override short GetInt16(int ordinal) => throw new NotSupportedException();
        public override int GetInt32(int ordinal) => throw new NotSupportedException();
        public override long GetInt64(int ordinal) => throw new NotSupportedException();
        public override string GetName(int ordinal) => "Id";
        public override int GetOrdinal(string name) => name == "Id" ? 0 : -1;
        public override string GetString(int ordinal) => throw new NotSupportedException();
        public override object GetValue(int ordinal) => throw new NotSupportedException();
        public override int GetValues(object[] values) => throw new NotSupportedException();
        public override bool IsDBNull(int ordinal) => true;
        public override bool NextResult() => false;
        public override bool Read() => false;
        public override IEnumerator GetEnumerator() => throw new NotSupportedException();
        public override Task<bool> ReadAsync(CancellationToken cancellationToken)
            => Task.FromCanceled<bool>(cancellationToken);
    }

    private sealed class ThrowingRowDbDataReader(int failAtRowIndex) : DbDataReader
    {
        private int rowIndex = -1;

        public override int FieldCount => 1;
        public override object this[int ordinal] => GetValue(ordinal);
        public override object this[string name] => GetValue(GetOrdinal(name));
        public override int Depth => 0;
        public override bool HasRows => true;
        public override bool IsClosed => false;
        public override int RecordsAffected => -1;
        public override bool GetBoolean(int ordinal) => throw new NotSupportedException();
        public override byte GetByte(int ordinal) => throw new NotSupportedException();
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override char GetChar(int ordinal) => throw new NotSupportedException();
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override string GetDataTypeName(int ordinal) => "int";
        public override DateTime GetDateTime(int ordinal) => throw new NotSupportedException();
        public override decimal GetDecimal(int ordinal) => throw new NotSupportedException();
        public override double GetDouble(int ordinal) => throw new NotSupportedException();
        public override Type GetFieldType(int ordinal) => typeof(int);
        public override float GetFloat(int ordinal) => throw new NotSupportedException();
        public override Guid GetGuid(int ordinal) => throw new NotSupportedException();
        public override short GetInt16(int ordinal) => throw new NotSupportedException();

        public override int GetInt32(int ordinal)
        {
            if (rowIndex == failAtRowIndex)
                throw new InvalidOperationException("Simulated row failure.");
            return rowIndex + 1;
        }

        public override long GetInt64(int ordinal) => GetInt32(ordinal);
        public override string GetName(int ordinal) => "Id";
        public override int GetOrdinal(string name) => name == "Id" ? 0 : -1;
        public override string GetString(int ordinal) => throw new NotSupportedException();
        public override object GetValue(int ordinal) => GetInt32(ordinal);
        public override int GetValues(object[] values) => throw new NotSupportedException();
        public override bool IsDBNull(int ordinal) => false;
        public override bool NextResult() => false;
        public override bool Read() => ++rowIndex < 3;
        public override Task<bool> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Read());
        public override IEnumerator GetEnumerator() => throw new NotSupportedException();
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Htb_WriteFromDataReader_EmptyByteArrayAndBase64SentinelToken_HandledCleanly()
    {
        // 1. String/Base64 CSV IDataReader -> HTB ByteArray column
        var byteSchema = new HtbSchema([
            new HtbColumn("Id", HtbDataType.Int32, isNullable: false),
            new HtbColumn("Payload", HtbDataType.ByteArray, isNullable: true)
        ]);

        using var csvStream = new MemoryStream(Encoding.UTF8.GetBytes("Id,Payload\n1,\n2,base64:\n3,AQID\n4,\"\"\n"));
        using var csvDr = Csv.CreateDataReader(csvStream, leaveOpen: true);
        using var bytesHtb = new MemoryStream();
        HeroParser.Htb.WriteFromDataReader(csvDr, bytesHtb, schema: byteSchema, leaveOpen: true);

        bytesHtb.Position = 0;
        using (var readBack = HeroParser.Htb.CreateDataReader(bytesHtb, leaveOpen: true))
        {
            Assert.True(readBack.Read());
            Assert.False(readBack.IsDBNull(1));
            Assert.Empty(Assert.IsType<byte[]>(readBack.GetValue(1)));

            Assert.True(readBack.Read());
            Assert.False(readBack.IsDBNull(1));
            Assert.Empty(Assert.IsType<byte[]>(readBack.GetValue(1)));

            Assert.True(readBack.Read());
            Assert.Equal([1, 2, 3], Assert.IsType<byte[]>(readBack.GetValue(1)));

            Assert.True(readBack.Read());
            Assert.False(readBack.IsDBNull(1));
            Assert.Empty(Assert.IsType<byte[]>(readBack.GetValue(1)));

            Assert.False(readBack.Read());
        }

        // 2. HTB IDataReader (DBNull, [], [1,2,3]) -> HTB String column preserves DBNull vs "base64:" vs "AQID"
        using var srcStream = new MemoryStream();
        CsvToHtbConverter.Convert("Id,Payload\n1,\n2,base64:\n3,AQID\n", srcStream, byteSchema);

        srcStream.Position = 0;
        var stringSchema = new HtbSchema([
            new HtbColumn("Id", HtbDataType.Int32, isNullable: false),
            new HtbColumn("Payload", HtbDataType.String, isNullable: true)
        ]);
        using var strHtb = new MemoryStream();
        using (var htbDr = HeroParser.Htb.CreateDataReader(srcStream, leaveOpen: true))
        {
            HeroParser.Htb.WriteFromDataReader(htbDr, strHtb, schema: stringSchema, leaveOpen: true);
        }

        strHtb.Position = 0;
        using var readStr = HeroParser.Htb.CreateDataReader(strHtb, leaveOpen: true);
        Assert.True(readStr.Read());
        Assert.True(readStr.IsDBNull(1));

        Assert.True(readStr.Read());
        Assert.False(readStr.IsDBNull(1));
        Assert.Equal("base64:", readStr.GetString(1));

        Assert.True(readStr.Read());
        Assert.Equal("AQID", readStr.GetString(1));

        Assert.False(readStr.Read());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task SpillToFileBufferStream_SmallThreshold_SpillsAndCleansUpTempFile()
    {
        var ct = TestContext.Current.CancellationToken;
        byte[] payload = [.. Enumerable.Range(0, 12_288).Select(static i => (byte)((i * 31) & 0xFF))];

        await using var stream = new SpillToFileBufferStream(memoryThreshold: 4_096);
        stream.Write(payload.AsSpan(0, 2_048));
        Assert.False(stream.HasSpilledToDisk);

        await stream.WriteAsync(payload.AsMemory(2_048), ct);
        Assert.True(stream.HasSpilledToDisk);
        Assert.Equal(payload.Length, stream.Length);

        stream.Position = 0;
        byte[] roundTrip = new byte[payload.Length];
        int totalRead = 0;
        while (totalRead < roundTrip.Length)
        {
            int read = await stream.ReadAsync(roundTrip.AsMemory(totalRead), ct);
            Assert.True(read > 0);
            totalRead += read;
        }
        Assert.Equal(payload, roundTrip);

        await stream.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => _ = stream.Length);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Excel_WriteFromDataReaderAsync_LargeExportSpillsBeyond1MiB_StreamsAsyncCleanly()
    {
        var ct = TestContext.Current.CancellationToken;
        const int rowCount = 1_800;
        using var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("Payload", typeof(string));

        for (int i = 0; i < rowCount; i++)
        {
            // High-entropy base64 slice keeps deflated .xlsx output > 1 MiB so SpillToFileBufferStream exercises disk spill.
            byte[] noise = new byte[600];
            uint state = (uint)(i + 1) * 2654435761u;
            for (int b = 0; b < noise.Length; b++)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                noise[b] = (byte)state;
            }
            table.Rows.Add(i, Convert.ToBase64String(noise));
        }

        using var backingStream = new MemoryStream();
        await using (var nonSyncStream = new NonSyncWriteStream(backingStream))
        await using (var sourceReader = table.CreateDataReader())
        {
            await HeroParser.Excel.WriteFromDataReaderAsync(sourceReader, nonSyncStream, leaveOpen: false, cancellationToken: ct);
        }

        Assert.True(backingStream.Length > SpillToFileBufferStream.DEFAULT_MEMORY_THRESHOLD);
        backingStream.Position = 0;
        var streamRows = HeroParser.Excel.Read().FromStream(backingStream);
        Assert.Equal(rowCount, streamRows.Count);
        Assert.Equal("0", streamRows[0][0]);
        Assert.Equal(table.Rows[rowCount - 1][1], streamRows[rowCount - 1][1]);

        string tempXlsx = Path.Combine(Path.GetTempPath(), $"heroparser-excel-direct-{Guid.NewGuid():N}.xlsx");
        try
        {
            await using (var fileSource = table.CreateDataReader())
            {
                await HeroParser.Excel.WriteFromDataReaderToFileAsync(tempXlsx, fileSource, cancellationToken: ct);
            }
            var fileRows = HeroParser.Excel.Read().FromFile(tempXlsx);
            Assert.Equal(rowCount, fileRows.Count);
            Assert.Equal(table.Rows[0][1], fileRows[0][1]);
        }
        finally
        {
            if (File.Exists(tempXlsx))
                File.Delete(tempXlsx);
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Htb_WriteFromDataReader_And_WriteFromDataReaderAsync_EnforceMaxOutputSizeBeforeWriting()
    {
        var ct = TestContext.Current.CancellationToken;
        using var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("Text", typeof(string));
        table.Rows.Add(1, "small-1");
        table.Rows.Add(2, new string('b', 20_000));

        // Measure exact size of header + row 1
        using var baselineTable = table.Clone();
        baselineTable.Rows.Add(1, "small-1");
        long row1TotalBytes;
        using (var baselineStream = new MemoryStream())
        using (var baselineReader = baselineTable.CreateDataReader())
        {
            HeroParser.Htb.WriteFromDataReader(baselineReader, baselineStream, leaveOpen: true);
            row1TotalBytes = baselineStream.Length;
        }

        long cap = row1TotalBytes + 64;
        var options = new HtbWriteOptions { MaxOutputSize = cap };

        using var syncOut = new MemoryStream();
        using (var dr = table.CreateDataReader())
        {
            var ex = Assert.Throws<HtbException>(() =>
                HeroParser.Htb.WriteFromDataReader(dr, syncOut, options, leaveOpen: true));
            Assert.Equal(HtbErrorCode.LimitExceeded, ex.ErrorCode);
        }
        Assert.Equal(row1TotalBytes, syncOut.Length);
        syncOut.Position = 0;
        using (var readSync = HeroParser.Htb.CreateDataReader(syncOut, leaveOpen: true))
        {
            Assert.True(readSync.Read());
            Assert.Equal(1, readSync.GetInt32(0));
            Assert.Equal("small-1", readSync.GetString(1));
            Assert.False(readSync.Read());
        }

        await using var asyncOut = new MemoryStream();
        await using (var asyncDr = table.CreateDataReader())
        {
            var ex = await Assert.ThrowsAsync<HtbException>(async () =>
                await HeroParser.Htb.WriteFromDataReaderAsync(asyncDr, asyncOut, options, leaveOpen: true, cancellationToken: ct));
            Assert.Equal(HtbErrorCode.LimitExceeded, ex.ErrorCode);
        }
        Assert.Equal(row1TotalBytes, asyncOut.Length);
        asyncOut.Position = 0;
        await using var readAsync = HeroParser.Htb.CreateDataReader(asyncOut, leaveOpen: true);
        Assert.True(await readAsync.ReadAsync(ct));
        Assert.Equal(1, readAsync.GetInt32(0));
        Assert.Equal("small-1", readAsync.GetString(1));
        Assert.False(await readAsync.ReadAsync(ct));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Htb_WriteFromDataReader_And_WriteFromDataReaderAsync_OnErrorSkipsAndThrows()
    {
        var ct = TestContext.Current.CancellationToken;
        var strictSchema = new HtbSchema([
            new HtbColumn("Id", HtbDataType.Int32, isNullable: false),
            new HtbColumn("BigText", HtbDataType.String, isNullable: true),
            new HtbColumn("Amount", HtbDataType.Decimal, isNullable: false)
        ]);

        // SkipRow mode (sync + async), with MaxRowCount=3 counting only committed rows and row 3 overflowing internal rowWriter buffer before throwing on Amount
        foreach (bool useAsync in (bool[])[false, true])
        {
            List<(HtbSerializeErrorContext Context, Exception Exception)> errors = [];
            var options = new HtbWriteOptions
            {
                MaxRowCount = 3,
                OnError = (ctx, ex) =>
                {
                    errors.Add((ctx, ex));
                    return HtbSerializeErrorAction.SkipRow;
                }
            };

            await using var output = new MemoryStream();
            if (useAsync)
            {
                await using var reader = new OnErrorTestDbDataReader();
                await HeroParser.Htb.WriteFromDataReaderAsync(reader, output, options, strictSchema, leaveOpen: true, cancellationToken: ct);
            }
            else
            {
                using var reader = new OnErrorTestDbDataReader();
                HeroParser.Htb.WriteFromDataReader(reader, output, options, strictSchema, leaveOpen: true);
            }

            Assert.Equal(2, errors.Count);
            Assert.Equal(1, errors[0].Context.RecordIndex);
            Assert.Equal("Id", errors[0].Context.MemberName);
            Assert.Equal(typeof(int), errors[0].Context.TargetType);

            Assert.Equal(3, errors[1].Context.RecordIndex);
            Assert.Equal("Amount", errors[1].Context.MemberName);
            Assert.Equal(typeof(decimal), errors[1].Context.TargetType);

            output.Position = 0;
            using var readBack = HeroParser.Htb.CreateDataReader(output, leaveOpen: true);
            Assert.True(readBack.Read());
            Assert.Equal(1, readBack.GetInt32(0));
            Assert.Equal("ok-0", readBack.GetString(1));
            Assert.Equal(10.5m, readBack.GetDecimal(2));

            Assert.True(readBack.Read());
            Assert.Equal(3, readBack.GetInt32(0));
            Assert.Equal("ok-2", readBack.GetString(1));
            Assert.Equal(30.25m, readBack.GetDecimal(2));

            Assert.True(readBack.Read());
            Assert.Equal(5, readBack.GetInt32(0));
            Assert.Equal("ok-4", readBack.GetString(1));
            Assert.Equal(50.75m, readBack.GetDecimal(2));

            Assert.False(readBack.Read());
        }

        // Throw mode rethrows
        var throwOptions = new HtbWriteOptions
        {
            OnError = (_, _) => HtbSerializeErrorAction.Throw
        };
        using (var reader = new OnErrorTestDbDataReader())
        using (var output = new MemoryStream())
        {
            Assert.Throws<HtbException>(() =>
                HeroParser.Htb.WriteFromDataReader(reader, output, throwOptions, strictSchema, leaveOpen: true));
        }
    }

    private sealed class OnErrorTestDbDataReader : DbDataReader
    {
        private int rowIndex = -1;

        public override int FieldCount => 3;
        public override object this[int ordinal] => GetValue(ordinal);
        public override object this[string name] => GetValue(GetOrdinal(name));
        public override int Depth => 0;
        public override bool HasRows => true;
        public override bool IsClosed => false;
        public override int RecordsAffected => -1;
        public override bool GetBoolean(int ordinal) => throw new NotSupportedException();
        public override byte GetByte(int ordinal) => throw new NotSupportedException();
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override char GetChar(int ordinal) => throw new NotSupportedException();
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;
        public override DateTime GetDateTime(int ordinal) => throw new NotSupportedException();
        public override double GetDouble(int ordinal) => throw new NotSupportedException();
        public override float GetFloat(int ordinal) => throw new NotSupportedException();
        public override Guid GetGuid(int ordinal) => throw new NotSupportedException();
        public override short GetInt16(int ordinal) => throw new NotSupportedException();
        public override long GetInt64(int ordinal) => GetInt32(ordinal);
        public override bool NextResult() => false;
        public override bool Read() => ++rowIndex < 5;
        public override Task<bool> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Read());
        public override IEnumerator GetEnumerator() => throw new NotSupportedException();

        public override Type GetFieldType(int ordinal) => ordinal switch
        {
            0 => typeof(int),
            1 => typeof(string),
            2 => typeof(decimal),
            _ => throw new ArgumentOutOfRangeException(nameof(ordinal))
        };

        public override string GetName(int ordinal) => ordinal switch
        {
            0 => "Id",
            1 => "BigText",
            2 => "Amount",
            _ => throw new ArgumentOutOfRangeException(nameof(ordinal))
        };

        public override int GetOrdinal(string name) => name switch
        {
            "Id" => 0,
            "BigText" => 1,
            "Amount" => 2,
            _ => -1
        };

        public override bool IsDBNull(int ordinal) => rowIndex == 1 && ordinal == 0;

        public override int GetInt32(int ordinal) => rowIndex + 1;

        public override string GetString(int ordinal) => rowIndex switch
        {
            0 => "ok-0",
            2 => "ok-2",
            3 => new string('f', 20_000),
            4 => "ok-4",
            _ => ""
        };

        public override decimal GetDecimal(int ordinal) => rowIndex switch
        {
            0 => 10.5m,
            1 => 20.5m,
            2 => 30.25m,
            3 => throw new InvalidCastException("Simulated bad decimal value."),
            4 => 50.75m,
            _ => 0m
        };

        public override object GetValue(int ordinal) => ordinal switch
        {
            0 => IsDBNull(0) ? DBNull.Value : GetInt32(0),
            1 => GetString(1),
            2 => GetDecimal(2),
            _ => throw new ArgumentOutOfRangeException(nameof(ordinal))
        };

        public override int GetValues(object[] values) => throw new NotSupportedException();
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Excel_TimeOnly_PreservesFractionalSecondsAcrossSyncAsyncAndTypedExportsAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var fractional = new TimeOnly(12, 34, 56, 123).Add(TimeSpan.FromTicks(4567));
        var wholeSecond = new TimeOnly(12, 34, 56);

        using var table = new DataTable();
        table.Columns.Add("TimeVal", typeof(TimeOnly));
        table.Rows.Add(fractional);
        table.Rows.Add(wholeSecond);

        using var syncStream = new MemoryStream();
        using (var dr = table.CreateDataReader())
            HeroParser.Excel.WriteFromDataReader(dr, syncStream, leaveOpen: true);

        await using var asyncStream = new MemoryStream();
        await using (var asyncDr = table.CreateDataReader())
            await HeroParser.Excel.WriteFromDataReaderAsync(asyncDr, asyncStream, leaveOpen: true, cancellationToken: ct);

        foreach (MemoryStream ms in (MemoryStream[])[syncStream, asyncStream])
        {
            ms.Position = 0;
            var typed = HeroParser.Excel.Read<GeneratedExcelTimeOnlyExportRow>().FromStream(ms);
            Assert.Equal(2, typed.Count);
            Assert.Equal(fractional, typed[0].TimeVal);
            Assert.Equal(wholeSecond, typed[1].TimeVal);
        }

        // Typed generated & reflection Excel writers also preserve fractional seconds by default
        byte[] genBytes = HeroParser.Excel.Write<GeneratedExcelTimeOnlyExportRow>()
            .ToBytes([new() { TimeVal = fractional }, new() { TimeVal = wholeSecond }]);
        using (var genMs = new MemoryStream(genBytes))
        {
            var genBack = HeroParser.Excel.Read<GeneratedExcelTimeOnlyExportRow>().FromStream(genMs);
            Assert.Equal(fractional, genBack[0].TimeVal);
            Assert.Equal(wholeSecond, genBack[1].TimeVal);
        }

        byte[] reflBytes = HeroParser.Excel.Write<ReflectionExcelTimeOnlyExportRow>()
            .ToBytes([new() { TimeVal = fractional }, new() { TimeVal = wholeSecond }]);
        using (var reflMs = new MemoryStream(reflBytes))
        {
            var reflRows = HeroParser.Excel.Read().FromStream(reflMs);
            Assert.Equal(fractional, TimeOnly.Parse(reflRows[0][0], CultureInfo.InvariantCulture));
            Assert.Equal("12:34:56", reflRows[1][0]);
        }

        // Explicit format still overrides default round-trip formatting
        var customOpts = new ExcelWriteOptions { TimeOnlyFormat = "HH:mm:ss" };
        using var customStream = new MemoryStream();
        using (var dr = table.CreateDataReader())
            HeroParser.Excel.WriteFromDataReader(dr, customStream, customOpts, leaveOpen: true);
        customStream.Position = 0;
        var customRows = HeroParser.Excel.Read().FromStream(customStream);
        Assert.Equal("12:34:56", customRows[0][0]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Excel_BoundedSharedStringTable_FallsBackToInlineStringsAndRoundTripsCleanlyAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var sharedStrings = new XlsxSharedStringTable(maxEntries: 4, maxTotalChars: 120, maxSingleStringChars: 32);

        List<string> expectedValues =
        [
            "cached-1",
            "cached-2",
            "cached-1",
            "  padded  ",
            "   ",
            "Xml & <Tag> > \"Quote\"\r\nNextLine",
            "Emoji 🚀🎉 test",
            new string('x', 60) + " & <end>",
            "overflow-unique-1",
            "overflow-unique-2"
        ];

        using (var ms = new MemoryStream())
        {
            using (var writer = new XlsxWriter(ms, leaveOpen: true, ExcelInjectionProtection.EscapeWithApostrophe, sharedStrings))
            using (var sheet = writer.StartSheet("InlineTest"))
            {
                sheet.WriteHeaderRow(["Value"]);
                for (int i = 0; i < expectedValues.Count; i++)
                {
                    sheet.StartRow(i + 2);
                    sheet.WriteCellString(1, expectedValues[i]);
                    sheet.EndRow();
                }
            }

            Assert.True(sharedStrings.Count <= 4);

            ms.Position = 0;
            var rows = HeroParser.Excel.Read().FromStream(ms);
            Assert.Equal(expectedValues.Count, rows.Count);
            for (int i = 0; i < expectedValues.Count; i++)
            {
                Assert.Equal(expectedValues[i], rows[i][0]);
            }
        }

        // End-to-end async DataReader export with strings > 4096 chars + XML escapes + emojis
        string longSpecial = new string('a', 4_500) + " & <ok> \r\n 🚀";
        using var table = new DataTable();
        table.Columns.Add("Text", typeof(string));
        table.Rows.Add(longSpecial);
        table.Rows.Add("  leading-and-trailing  ");

        await using var asyncOut = new MemoryStream();
        await using (var dr = table.CreateDataReader())
            await HeroParser.Excel.WriteFromDataReaderAsync(dr, asyncOut, leaveOpen: true, cancellationToken: ct);

        asyncOut.Position = 0;
        var roundTrip = HeroParser.Excel.Read().FromStream(asyncOut);
        Assert.Equal(2, roundTrip.Count);
        Assert.Equal(longSpecial, roundTrip[0][0]);
        Assert.Equal("  leading-and-trailing  ", roundTrip[1][0]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void HtbDataReader_DuplicateCaseColumns_PrefersExactCaseAndFallsBackCaseInsensitively()
    {
        using var table = new DataTable();
        table.Columns.Add("ID", typeof(int));
        table.Columns.Add("id", typeof(int));
        table.Rows.Add(10, 20);

        byte[] htbBytes;
        using (var ms = new MemoryStream())
        {
            using (var dr = table.CreateDataReader())
                HeroParser.Htb.WriteFromDataReader(dr, ms, leaveOpen: true);
            htbBytes = ms.ToArray();
        }

        using var readStream = new MemoryStream(htbBytes);
        using var reader = HeroParser.Htb.CreateDataReader(readStream);
        Assert.True(reader.Read());
        Assert.Equal(0, reader.GetOrdinal("ID"));
        Assert.Equal(1, reader.GetOrdinal("id"));
        Assert.Equal(0, reader.GetOrdinal("Id"));
        Assert.Equal(10, reader["ID"]);
        Assert.Equal(20, reader["id"]);
        Assert.Equal(10, reader["Id"]);
        Assert.Equal(10, reader[0]);
        Assert.Equal(20, reader[1]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_Over15SignificantDigitIntegers_PreserveAsTextAndKeep15DigitsNumeric()
    {
        const long max15Int = 999_999_999_999_999L;
        const long over15Int = 1_234_567_890_123_456L;
        const ulong over15UInt = 1_234_567_890_123_456UL;

        using var table = new DataTable();
        table.Columns.Add("Max15", typeof(long));
        table.Columns.Add("Over15", typeof(long));
        table.Columns.Add("Over15U", typeof(ulong));
        table.Rows.Add(max15Int, over15Int, over15UInt);

        using var drStream = new MemoryStream();
        using (var dr = table.CreateDataReader())
            HeroParser.Excel.WriteFromDataReader(dr, drStream, leaveOpen: true);
        Assert15DigitExcelSheet(drStream.ToArray(), over15UInt.ToString(CultureInfo.InvariantCulture));

        GeneratedExcel15DigitRow[] genRows = [new() { Max15 = max15Int, Over15 = over15Int, Over15U = over15UInt }];
        byte[] genBytes = HeroParser.Excel.Write<GeneratedExcel15DigitRow>().ToBytes(genRows);
        Assert15DigitExcelSheet(genBytes, over15UInt.ToString(CultureInfo.InvariantCulture));

        ReflectionExcel15DigitRow[] reflRows = [new() { Max15 = max15Int, Over15 = over15Int, Over15U = over15UInt }];
        byte[] reflBytes = HeroParser.Excel.Write<ReflectionExcel15DigitRow>().ToBytes(reflRows);
        Assert15DigitExcelSheet(reflBytes, over15UInt.ToString(CultureInfo.InvariantCulture));

        static void Assert15DigitExcelSheet(byte[] xlsxBytes, string expectedUnsigned)
        {
            using var ms = new MemoryStream(xlsxBytes);
            var rows = HeroParser.Excel.Read().WithoutHeader().FromStream(ms);
            Assert.Equal(2, rows.Count);
            Assert.Equal("999999999999999", rows[1][0]);
            Assert.Equal("1234567890123456", rows[1][1]);
            Assert.Equal(expectedUnsigned, rows[1][2]);

            using var archiveMs = new MemoryStream(xlsxBytes);
            using var archive = new System.IO.Compression.ZipArchive(archiveMs, System.IO.Compression.ZipArchiveMode.Read);
            using var sheetReader = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open(), Encoding.UTF8);
            string xml = sheetReader.ReadToEnd();
            Assert.Contains("<c r=\"A2\" ><v>999999999999999</v></c>", xml, StringComparison.Ordinal);
            Assert.DoesNotContain("<c r=\"B2\" ><v>", xml, StringComparison.Ordinal);
            Assert.DoesNotContain("<c r=\"C2\" ><v>", xml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Excel_ByteArrayBase64StartingWithPlus_PreservesExactRoundTripWithoutFormulaEscaping()
    {
        byte[] plusByte = [0xfb]; // Base64: "+w=="
        byte[] allPlusBytes = [0xfb, 0xef, 0xbe]; // Base64: "++++"
        const string FORMULA_TEXT = "=1+1";

        byte[] genBytes = HeroParser.Excel.Write<GeneratedExcelBase64PlusRow>().ToBytes(
            [new GeneratedExcelBase64PlusRow { Payload = plusByte, Text = FORMULA_TEXT }]);
        using (var ms = new MemoryStream(genBytes))
        {
            var back = HeroParser.Excel.Read<GeneratedExcelBase64PlusRow>().FromStream(ms);
            var row = Assert.Single(back);
            Assert.Equal(plusByte, row.Payload);
            Assert.Equal("'" + FORMULA_TEXT, row.Text);
        }

        byte[] reflBytes = HeroParser.Excel.Write<ReflectionExcelBase64PlusRow>().ToBytes(
            [new ReflectionExcelBase64PlusRow { Payload = allPlusBytes, Text = "+cmd" }]);
        using (var ms = new MemoryStream(reflBytes))
        {
            var rows = HeroParser.Excel.Read().FromStream(ms);
            var row = Assert.Single(rows);
            Assert.Equal("++++", row[0]);
            Assert.Equal(allPlusBytes, Convert.FromBase64String(row[0]));
            Assert.Equal("'+cmd", row[1]);
        }

        using var table = new DataTable();
        table.Columns.Add("Payload", typeof(byte[]));
        table.Columns.Add("Text", typeof(string));
        table.Rows.Add(plusByte, "=SUM(A1)");
        using var dr = table.CreateDataReader();
        byte[] drXlsx = HeroParser.Excel.WriteFromDataReaderToBytes(dr);
        using (var ms = new MemoryStream(drXlsx))
        {
            var rows = HeroParser.Excel.Read().FromStream(ms);
            var row = Assert.Single(rows);
            Assert.Equal("+w==", row[0]);
            Assert.Equal("'=SUM(A1)", row[1]);
        }
    }

    [Fact]
    public async Task HtbDataReader_NonNullableColumnWithNullBitSet_ThrowsCorruptDataSyncAndAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var nonNullableSchema = new HtbSchema([new HtbColumn("Id", HtbDataType.Int32, isNullable: false)]);

        using var ms = new MemoryStream();
        using (var writer = new HeroParser.Htbs.Writing.HtbStreamWriter(ms, leaveOpen: true))
        {
            writer.WriteHeader(nonNullableSchema);
            writer.WriteMask([0x00]);
            writer.WriteInt32(42);
            writer.WriteMask([0x01]);
            writer.Flush();
        }

        ms.Position = 0;
        using (var reader = new HeroParser.Htbs.Reading.Data.HtbDataReader(ms, nonNullableSchema, leaveOpen: true))
        {
            Assert.True(reader.Read());
            Assert.Equal(42, reader.GetInt32(0));
            DataTable? schemaTable = reader.GetSchemaTable();
            Assert.NotNull(schemaTable);
            Assert.False((bool)schemaTable.Rows[0][System.Data.Common.SchemaTableColumn.AllowDBNull]);

            var ex = Assert.Throws<HtbException>(() => reader.Read());
            Assert.Equal(HtbErrorCode.CorruptData, ex.ErrorCode);
        }

        ms.Position = 0;
        await using (var reader = new HeroParser.Htbs.Reading.Data.HtbDataReader(ms, nonNullableSchema, leaveOpen: true))
        {
            Assert.True(await reader.ReadAsync(ct));
            Assert.Equal(42, reader.GetInt32(0));

            var ex = await Assert.ThrowsAsync<HtbException>(() => reader.ReadAsync(ct));
            Assert.Equal(HtbErrorCode.CorruptData, ex.ErrorCode);
        }
    }

    [Fact]
    public async Task Csv_ByteArrayBase64StartingWithPlus_IsProtectedReversiblyAcrossExportPaths()
    {
        var ct = TestContext.Current.CancellationToken;
        byte[] payload = [248]; // Base64: "+A=="

        using var table = new DataTable();
        table.Columns.Add("Payload", typeof(byte[]));
        table.Columns.Add("Text", typeof(string));
        table.Rows.Add(payload, "=SUM(A1)");

        string syncCsv;
        using (var dr = table.CreateDataReader())
            syncCsv = Csv.WriteFromDataReaderToText(dr);
        Assert.Contains("base64:+A==", syncCsv, StringComparison.Ordinal);
        Assert.DoesNotContain("'+A==", syncCsv, StringComparison.Ordinal);
        Assert.Contains("'=SUM(A1)", syncCsv, StringComparison.Ordinal);

        await using var asyncMs = new MemoryStream();
        await using (var dr = table.CreateDataReader())
            await Csv.WriteFromDataReaderAsync(dr, asyncMs, leaveOpen: true, cancellationToken: ct);
        string asyncCsv = Encoding.UTF8.GetString(asyncMs.ToArray());
        Assert.Contains("base64:+A==", asyncCsv, StringComparison.Ordinal);
        Assert.DoesNotContain("'+A==", asyncCsv, StringComparison.Ordinal);

        byte[] htb;
        using (var dr = table.CreateDataReader())
            htb = HeroParser.Htb.WriteFromDataReaderToBytes(dr);
        using var htbIn = new MemoryStream(htb);
        using var htbOut = new StringWriter();
        HtbToCsvConverter.Convert(htbIn, htbOut);
        Assert.Contains("base64:+A==", htbOut.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("'+A==", htbOut.ToString(), StringComparison.Ordinal);

        // Round-trip back via generated CSV reader and CsvToHtbConverter
        string typedCsv = Csv.Write<GeneratedCsvBase64PlusRow>().ToText([new GeneratedCsvBase64PlusRow { Payload = payload }]);
        Assert.Contains("base64:+A==", typedCsv, StringComparison.Ordinal);
        List<GeneratedCsvBase64PlusRow> readBackList = [];
        foreach (var row in Csv.Read<GeneratedCsvBase64PlusRow>().FromText(typedCsv))
            readBackList.Add(row);
        var readBack = Assert.Single(readBackList);
        Assert.Equal(payload, readBack.Payload);

        // None mode preserves unprefixed +A==
        using var noneDr = table.CreateDataReader();
        string noneCsv = Csv.WriteFromDataReaderToText(noneDr, new CsvWriteOptions { InjectionProtection = CsvInjectionProtection.None });
        Assert.Contains("+A==", noneCsv, StringComparison.Ordinal);
        Assert.DoesNotContain("base64:+A==", noneCsv, StringComparison.Ordinal);

        // Reject mode still honors dangerous prefix protection (e.g. if 'b' is explicitly marked dangerous)
        using var rejectCustomDr = table.CreateDataReader();
        var ex = Assert.Throws<CsvException>(() => Csv.WriteFromDataReaderToText(
            rejectCustomDr,
            new CsvWriteOptions
            {
                InjectionProtection = CsvInjectionProtection.Reject,
                AdditionalDangerousChars = new HashSet<char>(['b'])
            }));
        Assert.Equal(CsvErrorCode.InjectionDetected, ex.ErrorCode);
    }

    [Fact]
    public void GeneratedHtbBinder_ColumnTypeMismatch_ThrowsSchemaMismatch()
    {
        using var ms = new MemoryStream();
        HeroParser.Htb.Write<HtbIntIdRecord>().ToStream(ms, [new HtbIntIdRecord { Id = 65536 }, new HtbIntIdRecord { Id = 7 }], leaveOpen: true);
        ms.Position = 0;

        var ex = Assert.Throws<HtbException>(() =>
            HeroParser.Htb.Read<HtbShortIdRecord>().FromStream(ms, leaveOpen: true).ToList());
        Assert.Equal(HtbErrorCode.SchemaMismatch, ex.ErrorCode);
    }

    [Fact]
    public async Task HtbFloatArray_JsonlWriteFromDataReader_WritesJsonArraySyncAndAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        using var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("Embedding", typeof(float[]));
        table.Rows.Add(1, new float[] { 0.25f, -0.5f, 1.5f });
        table.Rows.Add(2, Array.Empty<float>());
        table.Rows.Add(3, new float[] { float.NaN });

        using var htbStream = new MemoryStream();
        using (var sourceReader = table.CreateDataReader())
            HeroParser.Htb.WriteFromDataReader(sourceReader, htbStream, leaveOpen: true);

        htbStream.Position = 0;
        string syncJsonl;
        using (var dr = HeroParser.Htb.CreateDataReader(htbStream, leaveOpen: true))
        using (var outMs = new MemoryStream())
        {
            Jsonl.WriteFromDataReader(dr, outMs, new JsonlWriteOptions { NewLine = "\n", WriteFinalNewline = false }, leaveOpen: true);
            syncJsonl = Encoding.UTF8.GetString(outMs.ToArray());
        }

        string[] syncLines = syncJsonl.Split('\n');
        Assert.Equal(3, syncLines.Length);
        Assert.DoesNotContain("System.Single[]", syncJsonl, StringComparison.Ordinal);
        Assert.Contains("\"Embedding\":[0.25,-0.5,1.5]", syncLines[0], StringComparison.Ordinal);
        Assert.Contains("\"Embedding\":[]", syncLines[1], StringComparison.Ordinal);
        Assert.Contains("\"Embedding\":[null]", syncLines[2], StringComparison.Ordinal);

        htbStream.Position = 0;
        string asyncJsonl;
        await using (var dr = HeroParser.Htb.CreateDataReader(htbStream, leaveOpen: true))
        await using (var outMs = new MemoryStream())
        {
            await Jsonl.WriteFromDataReaderAsync(dr, outMs, new JsonlWriteOptions { NewLine = "\n", WriteFinalNewline = false }, leaveOpen: true, cancellationToken: ct);
            asyncJsonl = Encoding.UTF8.GetString(outMs.ToArray());
        }

        Assert.Equal(syncJsonl, asyncJsonl);
    }

    [Fact]
    public void GeneratedHtbBinder_FloatRangeMax_ComparesAgainstExactDoubleBound()
    {
        using var invalidStream = new MemoryStream();
        HeroParser.Htb.Write<HtbFloatRangeUnvalidatedWriteRecord>()
            .ToStream(invalidStream, [new HtbFloatRangeUnvalidatedWriteRecord { Score = 0.1f }], leaveOpen: true);
        invalidStream.Position = 0;

        var ex = Assert.Throws<HtbException>(() =>
            HeroParser.Htb.Read<HtbFloatRangeValidatedRecord>().FromStream(invalidStream, leaveOpen: true).ToList());
        Assert.Equal(HtbErrorCode.DeserializationError, ex.ErrorCode);
        Assert.Contains("Score", ex.Message, StringComparison.Ordinal);

        using var validStream = new MemoryStream();
        HeroParser.Htb.Write<HtbFloatRangeUnvalidatedWriteRecord>()
            .ToStream(validStream, [new HtbFloatRangeUnvalidatedWriteRecord { Score = 0.05f }], leaveOpen: true);
        validStream.Position = 0;

        var ok = Assert.Single(HeroParser.Htb.Read<HtbFloatRangeValidatedRecord>().FromStream(validStream, leaveOpen: true).ToList());
        Assert.Equal(0.05f, ok.Score);
    }

    [Fact]
    public async Task Csv_DataReaderBoolean_HonorsQuoteStyleAndCustomDelimiterSyncAndAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        using var table = new DataTable();
        table.Columns.Add("Flag", typeof(bool));
        table.Rows.Add(true);
        table.Rows.Add(false);

        using (var dr = table.CreateDataReader())
        {
            string quotedCsv = Csv.WriteFromDataReaderToText(dr, new CsvWriteOptions
            {
                QuoteStyle = QuoteStyle.Always,
                NewLine = "\n"
            });
            Assert.Equal("\"Flag\"\n\"True\"\n\"False\"\n", quotedCsv);
        }

        await using (var dr = table.CreateDataReader())
        await using (var ms = new MemoryStream())
        {
            await Csv.WriteFromDataReaderAsync(dr, ms, new CsvWriteOptions
            {
                QuoteStyle = QuoteStyle.Always,
                NewLine = "\n"
            }, leaveOpen: true, cancellationToken: ct);
            Assert.Equal("\"Flag\"\n\"True\"\n\"False\"\n", Encoding.UTF8.GetString(ms.ToArray()));
        }

        using (var dr = table.CreateDataReader())
        {
            string customDelimCsv = Csv.WriteFromDataReaderToText(dr, new CsvWriteOptions
            {
                Delimiter = 'r',
                NewLine = "\n"
            });
            Assert.Equal("Flag\n\"True\"\nFalse\n", customDelimCsv);

            List<string[]> rows = [];
            foreach (var row in Csv.Read().WithDelimiter('r').FromText(customDelimCsv))
                rows.Add([row[0].UnquoteToString()]);
            Assert.Equal(3, rows.Count);
            Assert.Equal("True", rows[1][0]);
            Assert.Equal("False", rows[2][0]);
        }

        using (var dr = table.CreateDataReader())
        {
            var ex = Assert.Throws<CsvException>(() =>
                Csv.WriteFromDataReaderToText(dr, new CsvWriteOptions { MaxFieldSize = 3 }, writeHeader: false));
            Assert.Equal(CsvErrorCode.FieldSizeExceeded, ex.ErrorCode);
        }

        await using (var dr = table.CreateDataReader())
        await using (var ms = new MemoryStream())
        {
            var ex = await Assert.ThrowsAsync<CsvException>(() =>
                Csv.WriteFromDataReaderAsync(dr, ms, new CsvWriteOptions { MaxFieldSize = 3 }, writeHeader: false, leaveOpen: true, cancellationToken: ct).AsTask());
            Assert.Equal(CsvErrorCode.FieldSizeExceeded, ex.ErrorCode);
        }
    }

    [Fact]
    public void HtbCharColumn_QuotedDoubleQuote_RoundTripsThroughCsvConverter()
    {
        var schema = new HtbSchema([new HtbColumn("QuoteChar", HtbDataType.Char, isNullable: false)]);
        const string csv = "QuoteChar\r\n\"\"\"\"\r\n";

        using var htbStream = new MemoryStream();
        CsvToHtbConverter.Convert(csv, htbStream, schema);

        htbStream.Position = 0;
        using (var dr = HeroParser.Htb.CreateDataReader(htbStream, leaveOpen: true))
        {
            Assert.True(dr.Read());
            Assert.Equal('"', dr.GetChar(0));
            Assert.False(dr.Read());
        }

        htbStream.Position = 0;
        using var sw = new StringWriter();
        HtbToCsvConverter.Convert(htbStream, sw);
        Assert.Equal(csv, sw.ToString());
    }

    [Fact]
    public async Task JsonlDataReader_CompressedFile_EnforcesMaxRowCountSyncAndAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        string dir = Path.Combine(Path.GetTempPath(), $"heroparser-jsonl-dr-limit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string gzPath = Path.Combine(dir, "two.jsonl.gz");
            using (var fs = File.Create(gzPath))
            using (var gz = new GZipStream(fs, CompressionLevel.Optimal))
            using (var sw = new StreamWriter(gz, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                sw.WriteLine(/*lang=json,strict*/ """{"Id":1}""");
                sw.WriteLine(/*lang=json,strict*/ """{"Id":2}""");
            }

            using (var syncDr = Jsonl.CreateDataReader(gzPath, new JsonlReadOptions { MaxRowCount = 1 }))
            {
                Assert.True(syncDr.HasRows);
                Assert.True(syncDr.Read());
                Assert.Equal("1", syncDr.GetString(0));
                var ex = Assert.Throws<JsonlException>(() => syncDr.Read());
                Assert.Equal(JsonlErrorCode.TooManyRows, ex.ErrorCode);
            }

            await using (var asyncDr = Jsonl.CreateDataReader(gzPath, new JsonlReadOptions { MaxRowCount = 1 }))
            {
                Assert.True(await asyncDr.ReadAsync(ct));
                Assert.Equal("1", asyncDr.GetString(0));
                var ex = await Assert.ThrowsAsync<JsonlException>(() => asyncDr.ReadAsync(ct));
                Assert.Equal(JsonlErrorCode.TooManyRows, ex.ErrorCode);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Excel_DataReaderDatesBefore1900_WrittenAsIsoTextInsteadOfSerialZero()
    {
        using var table = new DataTable();
        table.Columns.Add("DOnly", typeof(DateOnly));
        table.Columns.Add("DTime", typeof(DateTime));
        table.Columns.Add("Modern", typeof(DateOnly));
        table.Rows.Add(DateOnly.MinValue, DateTime.MinValue, new DateOnly(2026, 5, 1));

        using var ms = new MemoryStream();
        using (var dr = table.CreateDataReader())
            HeroParser.Excel.WriteFromDataReader(dr, ms, leaveOpen: true);

        ms.Position = 0;
        var rows = HeroParser.Excel.Read().WithoutHeader().FromStream(ms);
        Assert.Equal(2, rows.Count);
        Assert.Equal("0001-01-01", rows[1][0]);
        Assert.Equal("0001-01-01T00:00:00.0000000", rows[1][1]);
        Assert.StartsWith("2026-05-01", rows[1][2], StringComparison.Ordinal);
    }

    [Fact]
    public void Excel_SharedAndInlineStrings_PreserveCarriageReturnsIdentically()
    {
        const string value = "a\rb";

        using var sharedMs = new MemoryStream();
        using (var xlsx = new XlsxWriter(sharedMs, leaveOpen: true))
        {
            using var sheet = xlsx.StartSheet("Sheet1");
            sheet.WriteHeaderRow(["Col"]);
            sheet.StartRow(2);
            sheet.WriteCellString(1, value);
            sheet.EndRow();
            sheet.Close();
        }

        using var inlineMs = new MemoryStream();
        using (var xlsx = new XlsxWriter(
            inlineMs,
            leaveOpen: true,
            ExcelInjectionProtection.EscapeWithApostrophe,
            new XlsxSharedStringTable(maxEntries: 0, maxTotalChars: 0, maxSingleStringChars: 0)))
        {
            using var sheet = xlsx.StartSheet("Sheet1");
            sheet.WriteHeaderRow(["Col"]);
            sheet.StartRow(2);
            sheet.WriteCellString(1, value);
            sheet.EndRow();
            sheet.Close();
        }

        sharedMs.Position = 0;
        var sharedRows = HeroParser.Excel.Read().WithoutHeader().FromStream(sharedMs);
        Assert.Equal(value, sharedRows[1][0]);

        inlineMs.Position = 0;
        var inlineRows = HeroParser.Excel.Read().WithoutHeader().FromStream(inlineMs);
        Assert.Equal(value, inlineRows[1][0]);
    }

    [Fact]
    public void Excel_StreamingRepeatedLongCells_RoundTripsWithoutTrippingCompressionLimit()
    {
        string repeated = new('A', 5000);
        using var table = new DataTable();
        table.Columns.Add("Payload", typeof(string));
        for (int i = 0; i < 1000; i++)
            table.Rows.Add(repeated);

        byte[] xlsxBytes;
        using (var dr = table.CreateDataReader())
            xlsxBytes = HeroParser.Excel.WriteFromDataReaderToBytes(dr);

        using var ms = new MemoryStream(xlsxBytes);
        var rows = HeroParser.Excel.Read().FromStream(ms);
        Assert.Equal(1000, rows.Count);
        Assert.Equal(repeated, rows[0][0]);
        Assert.Equal(repeated, rows[^1][0]);
    }

    [Fact]
    public void Excel_TypedCharSpecialSymbols_RoundTripWithoutApostrophePrefix()
    {
        GeneratedExcelCharRow[] records =
        [
            new() { Symbol = '@' },
            new() { Symbol = '=' },
            new() { Symbol = '+' },
            new() { Symbol = '-' }
        ];

        byte[] genBytes = HeroParser.Excel.SerializeRecords(records);
        using (var ms = new MemoryStream(genBytes))
        {
            var genBack = HeroParser.Excel.Read<GeneratedExcelCharRow>().FromStream(ms);
            Assert.Equal(['@', '=', '+', '-'], [.. genBack.Select(r => r.Symbol)]);
        }

        ReflectionExcelCharRow[] reflRecords =
        [
            new() { Symbol = '@' },
            new() { Symbol = '=' }
        ];
        byte[] reflBytes = HeroParser.Excel.Write<ReflectionExcelCharRow>().ToBytes(reflRecords);
        using (var ms = new MemoryStream(reflBytes))
        {
            var reflBack = HeroParser.Excel.Read<GeneratedExcelCharRow>().FromStream(ms);
            Assert.Equal(['@', '='], [.. reflBack.Select(r => r.Symbol)]);
        }

        using var table = new DataTable();
        table.Columns.Add("Symbol", typeof(char));
        table.Rows.Add('@');
        table.Rows.Add('=');
        byte[] drBytes;
        using (var dr = table.CreateDataReader())
            drBytes = HeroParser.Excel.WriteFromDataReaderToBytes(dr);
        using (var ms = new MemoryStream(drBytes))
        {
            var drBack = HeroParser.Excel.Read<GeneratedExcelCharRow>().FromStream(ms);
            Assert.Equal(['@', '='], [.. drBack.Select(r => r.Symbol)]);
        }
    }

    [Fact]
    public async Task CsvToHtb_Utf32BomSeekableAndNonSeekable_PreservesTextWithoutNulBytesAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        Encoding[] utf32Encodings =
        [
            new UTF32Encoding(bigEndian: false, byteOrderMark: true),
            new UTF32Encoding(bigEndian: true, byteOrderMark: true)
        ];

        const string csv = "Name,City\r\nAlice,Sydney\r\nBob,Tokyo\r\n";
        var schema = new HtbSchema(
        [
            new HtbColumn("Name", HtbDataType.String, isNullable: false),
            new HtbColumn("City", HtbDataType.String, isNullable: false)
        ]);

        foreach (Encoding encoding in utf32Encodings)
        {
            byte[] payload = [.. encoding.GetPreamble(), .. encoding.GetBytes(csv)];

            using var seekableCsv = new MemoryStream(payload);
            using var seekableHtb = new MemoryStream();
            await CsvToHtbConverter.ConvertAsync(seekableCsv, seekableHtb, schema, cancellationToken: ct);
            seekableHtb.Position = 0;
            using var seekableOut = new StringWriter();
            HtbToCsvConverter.Convert(seekableHtb, seekableOut);
            Assert.Equal(csv, seekableOut.ToString());
            Assert.DoesNotContain('\0', seekableOut.ToString());

            using var nonSeekableCsv = new NonSeekableReadStream(new MemoryStream(payload));
            using var nonSeekableHtb = new MemoryStream();
            await CsvToHtbConverter.ConvertAsync(nonSeekableCsv, nonSeekableHtb, schema, cancellationToken: ct);
            nonSeekableHtb.Position = 0;
            using var nonSeekableOut = new StringWriter();
            HtbToCsvConverter.Convert(nonSeekableHtb, nonSeekableOut);
            Assert.Equal(csv, nonSeekableOut.ToString());
            Assert.DoesNotContain('\0', nonSeekableOut.ToString());
        }
    }

    [Fact]
    public void Excel_GetSheetNamesFromStream_LeavesStreamOpenForSubsequentRead()
    {
        using var ms = new MemoryStream();
        using (var xlsx = new XlsxWriter(ms, leaveOpen: true))
        {
            using (var sheet = xlsx.StartSheet("Orders"))
            {
                sheet.WriteHeaderRow(["Id", "Customer"]);
                sheet.StartRow(2);
                sheet.WriteCellNumber(1, 101);
                sheet.WriteCellString(2, "Alice");
                sheet.EndRow();
                sheet.Close();
            }

            using var summary = xlsx.StartSheet("Summary");
            sheetWriteSummary(summary);
        }

        ms.Position = 0;
        IReadOnlyList<string> sheetNames = HeroParser.Excel.GetSheetNames(ms);
        Assert.Equal(["Orders", "Summary"], sheetNames);
        Assert.True(ms.CanRead);

        ms.Position = 0;
        var rows = HeroParser.Excel.Read().FromSheet(sheetNames[0]).FromStream(ms);
        Assert.Single(rows);
        Assert.Equal(["101", "Alice"], rows[0]);

        static void sheetWriteSummary(XlsxWriter.SheetWriter s)
        {
            s.WriteHeaderRow(["Total"]);
            s.Close();
        }
    }

    [Fact]
    public async Task FixedWidth_WriteFromDataReader_PrefersExactColumnNamesAndRejectsAmbiguousCaseMatchAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        using var table = new DataTable { CaseSensitive = true };
        table.Columns.Add("ID", typeof(int));
        table.Columns.Add("Id", typeof(int));
        table.Rows.Add(123, 456);

        FixedWidthFieldDefinition[] columns =
        [
            new FixedWidthFieldDefinition("ID", 5, FieldAlignment.Right, ' '),
            new FixedWidthFieldDefinition("Id", 5, FieldAlignment.Right, ' ')
        ];

        string syncOutput;
        using (var reader = table.CreateDataReader())
            syncOutput = FixedWidth.WriteFromDataReaderToText(reader, columns, writeHeader: false);

        Assert.Equal("  123  456\r\n", syncOutput);

        await using var asyncStream = new MemoryStream();
        await using (var asyncReader = table.CreateDataReader())
        {
            await FixedWidth.WriteFromDataReaderAsync(
                asyncReader,
                asyncStream,
                columns,
                writeHeader: false,
                leaveOpen: true,
                cancellationToken: ct);
        }
        Assert.Equal("  123  456\r\n", Encoding.UTF8.GetString(asyncStream.ToArray()));

        FixedWidthFieldDefinition[] ambiguousColumns =
        [
            new FixedWidthFieldDefinition("id", 5, FieldAlignment.Right, ' ')
        ];

        using var ambiguousSyncReader = table.CreateDataReader();
        Assert.Throws<InvalidOperationException>(() =>
            FixedWidth.WriteFromDataReaderToText(ambiguousSyncReader, ambiguousColumns, writeHeader: false));

        await using var ambiguousAsyncStream = new MemoryStream();
        await using var ambiguousAsyncReader = table.CreateDataReader();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await FixedWidth.WriteFromDataReaderAsync(
                ambiguousAsyncReader,
                ambiguousAsyncStream,
                ambiguousColumns,
                writeHeader: false,
                leaveOpen: true,
                cancellationToken: ct));
    }

    [Fact]
    public async Task CsvToHtb_EscapedUriField_DecodesEscapeBeforeStoringUriAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        const string csv = "Link\n\"https://example.com/a\\,b\"\n";
        var options = new CsvToHtbOptions { EscapeCharacter = '\\' };
        var schema = new HtbSchema([new HtbColumn("Link", HtbDataType.Uri, isNullable: false)]);

        using (var htbStream = new MemoryStream())
        {
            CsvToHtbConverter.Convert(csv, htbStream, schema, options);
            htbStream.Position = 0;
            using var htbReader = HeroParser.Htb.CreateDataReader(htbStream);
            Assert.True(htbReader.Read());
            Assert.Equal("https://example.com/a,b", htbReader.GetString(0));
            Assert.False(htbReader.Read());
        }

        await using var csvStream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        await using var asyncHtbStream = new MemoryStream();
        await CsvToHtbConverter.ConvertAsync(csvStream, asyncHtbStream, schema, options, cancellationToken: ct);
        asyncHtbStream.Position = 0;
        await using var asyncHtbReader = HeroParser.Htb.CreateDataReader(asyncHtbStream);
        Assert.True(await asyncHtbReader.ReadAsync(ct));
        Assert.Equal("https://example.com/a,b", asyncHtbReader.GetString(0));
        Assert.False(await asyncHtbReader.ReadAsync(ct));
    }

    [Fact]
    public void Excel_LargeNegativeInt64_WithPersianCulture_RoundTripsLosslessly()
    {
        CultureInfo persianCulture = CultureInfo.GetCultureInfo("fa-IR");
        const long largeNegative = -1_234_567_890_123_456L;
        const ulong largeUnsigned = 12_345_678_901_234_567UL;

        var excelOptions = new ExcelWriteOptions { Culture = persianCulture };

        // 1. Generated record writer
        var genRow = new GeneratedExcel15DigitRow { Max15 = -10L, Over15 = largeNegative, Over15U = largeUnsigned };
        byte[] genBytes = HeroParser.Excel.Write<GeneratedExcel15DigitRow>().WithCulture(persianCulture).ToBytes([genRow]);
        using (var ms = new MemoryStream(genBytes))
        {
            var readBack = Assert.Single(HeroParser.Excel.Read<GeneratedExcel15DigitRow>().WithCulture(persianCulture).FromStream(ms));
            Assert.Equal(largeNegative, readBack.Over15);
            Assert.Equal(largeUnsigned, readBack.Over15U);
        }

        // 2. Reflection record writer
        var reflRow = new ReflectionExcel15DigitRow { Max15 = -10L, Over15 = largeNegative, Over15U = largeUnsigned };
        byte[] reflBytes = HeroParser.Excel.Write<ReflectionExcel15DigitRow>().WithCulture(persianCulture).ToBytes([reflRow]);
        using (var ms = new MemoryStream(reflBytes))
        {
            var readBack = Assert.Single(HeroParser.Excel.Read<GeneratedExcel15DigitRow>().WithCulture(persianCulture).FromStream(ms));
            Assert.Equal(largeNegative, readBack.Over15);
            Assert.Equal(largeUnsigned, readBack.Over15U);
        }

        // 3. DataReader writer
        using var table = new DataTable();
        table.Columns.Add("Max15", typeof(long));
        table.Columns.Add("Over15", typeof(long));
        table.Columns.Add("Over15U", typeof(ulong));
        table.Rows.Add(-10L, largeNegative, largeUnsigned);

        byte[] drBytes;
        using (var dr = table.CreateDataReader())
            drBytes = HeroParser.Excel.WriteFromDataReaderToBytes(dr, excelOptions);

        using (var ms = new MemoryStream(drBytes))
        {
            var readBack = Assert.Single(HeroParser.Excel.Read<GeneratedExcel15DigitRow>().WithCulture(persianCulture).FromStream(ms));
            Assert.Equal(largeNegative, readBack.Over15);
            Assert.Equal(largeUnsigned, readBack.Over15U);
        }
    }

    [Fact]
    public async Task FixedWidth_WriteFromDataReader_SequentialAccessAndInt16HexFormat_SucceedsAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        using var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("ShortVal", typeof(short));
        table.Rows.Add(42, "Bob", (short)-1);

        FixedWidthFieldDefinition[] reorderedAndDuplicate =
        [
            new FixedWidthFieldDefinition("Name", 5, FieldAlignment.Left, ' '),
            new FixedWidthFieldDefinition("Id", 4, FieldAlignment.Right, '0'),
            new FixedWidthFieldDefinition("Name", 5, FieldAlignment.Right, '_')
        ];

        using (var syncSeq = new SequentialAccessDataReader(table.CreateDataReader()))
        {
            string syncOut = FixedWidth.WriteFromDataReaderToText(
                syncSeq,
                reorderedAndDuplicate,
                new FixedWidthWriteOptions { NewLine = "\n" },
                writeHeader: false);
            Assert.Equal("Bob  0042__Bob\n", syncOut);
        }

        await using (var ms = new MemoryStream())
        {
            await using var asyncSeq = new SequentialAccessDataReader(table.CreateDataReader());
            await FixedWidth.WriteFromDataReaderAsync(
                asyncSeq,
                ms,
                reorderedAndDuplicate,
                new FixedWidthWriteOptions { NewLine = "\n" },
                writeHeader: false,
                leaveOpen: true,
                cancellationToken: ct);
            Assert.Equal("Bob  0042__Bob\n", Encoding.UTF8.GetString(ms.ToArray()));
        }

        FixedWidthFieldDefinition[] shortHexColumns =
        [
            new FixedWidthFieldDefinition("ShortVal", 4, FieldAlignment.Right, ' ')
        ];
        var hexOptions = new FixedWidthWriteOptions
        {
            NumberFormat = "X",
            OverflowBehavior = OverflowBehavior.Throw,
            NewLine = "\n"
        };

        using (var syncReader = table.CreateDataReader())
        {
            string syncHex = FixedWidth.WriteFromDataReaderToText(syncReader, shortHexColumns, hexOptions, writeHeader: false);
            Assert.Equal("FFFF\n", syncHex);
        }

        await using var asyncHexMs = new MemoryStream();
        await using var asyncReader = table.CreateDataReader();
        await FixedWidth.WriteFromDataReaderAsync(
            asyncReader,
            asyncHexMs,
            shortHexColumns,
            hexOptions,
            writeHeader: false,
            leaveOpen: true,
            cancellationToken: ct);
        Assert.Equal("FFFF\n", Encoding.UTF8.GetString(asyncHexMs.ToArray()));
    }

    [Fact]
    public async Task Jsonl_WriteFromDataReader_InvalidOptions_DisposesOwnedStreamSyncAndAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        using var table = CreateSampleTable();
        var invalidOptions = new JsonlWriteOptions { MaxRowCount = 0 };

        using (var syncReader = table.CreateDataReader())
        using (var syncStream = new MemoryStream())
        {
            Assert.Throws<JsonlException>(() =>
                Jsonl.WriteFromDataReader(syncReader, syncStream, invalidOptions, leaveOpen: false));
            Assert.False(syncStream.CanWrite);
        }

        await using var asyncReader = table.CreateDataReader();
        await using var asyncStream = new MemoryStream();
        await Assert.ThrowsAsync<JsonlException>(async () =>
            await Jsonl.WriteFromDataReaderAsync(asyncReader, asyncStream, invalidOptions, leaveOpen: false, cancellationToken: ct));
        Assert.False(asyncStream.CanWrite);
    }

    [Fact]
    public async Task CsvAndFixedWidth_WriteFromDataReader_EnforceMaxRowCountWithoutCountingHeaderAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        using var oneRowTable = new DataTable();
        oneRowTable.Columns.Add("Id", typeof(int));
        oneRowTable.Columns.Add("Name", typeof(string));
        oneRowTable.Rows.Add(1, "Alice");

        using var twoRowTable = oneRowTable.Copy();
        twoRowTable.Rows.Add(2, "Bob");

        // 1. CSV sync + async: 1 row + header succeeds under MaxRowCount = 1
        var csvOpts = new CsvWriteOptions { MaxRowCount = 1, NewLine = "\n" };
        using (var r1 = oneRowTable.CreateDataReader())
        {
            string csv = Csv.WriteFromDataReaderToText(r1, csvOpts, writeHeader: true);
            Assert.Equal("Id,Name\n1,Alice\n", csv);
        }

        await using (var ms = new MemoryStream())
        {
            await using var ar1 = oneRowTable.CreateDataReader();
            await Csv.WriteFromDataReaderAsync(ar1, ms, csvOpts, writeHeader: true, leaveOpen: true, cancellationToken: ct);
            Assert.Equal("Id,Name\n1,Alice\n", Encoding.UTF8.GetString(ms.ToArray()));
        }

        // 2 rows fails in CSV sync + async
        using (var r2 = twoRowTable.CreateDataReader())
        {
            var ex = Assert.Throws<CsvException>(() => Csv.WriteFromDataReaderToText(r2, csvOpts, writeHeader: true));
            Assert.Equal(CsvErrorCode.TooManyRows, ex.ErrorCode);
        }

        await using (var ms2 = new MemoryStream())
        {
            await using var ar2 = twoRowTable.CreateDataReader();
            var ex = await Assert.ThrowsAsync<CsvException>(async () =>
                await Csv.WriteFromDataReaderAsync(ar2, ms2, csvOpts, writeHeader: true, leaveOpen: true, cancellationToken: ct));
            Assert.Equal(CsvErrorCode.TooManyRows, ex.ErrorCode);
        }

        // 2. FixedWidth sync + async: 1 row + header succeeds under MaxRowCount = 1
        FixedWidthFieldDefinition[] fwCols =
        [
            new FixedWidthFieldDefinition("Id", 3, FieldAlignment.Right, ' '),
            new FixedWidthFieldDefinition("Name", 6, FieldAlignment.Left, ' ')
        ];
        var fwOpts = new FixedWidthWriteOptions { MaxRowCount = 1, NewLine = "\n" };

        using (var r1 = oneRowTable.CreateDataReader())
        {
            string fw = FixedWidth.WriteFromDataReaderToText(r1, fwCols, fwOpts, writeHeader: true);
            Assert.Equal(" IdName  \n  1Alice \n", fw);
        }

        await using (var fwMs = new MemoryStream())
        {
            await using var ar1 = oneRowTable.CreateDataReader();
            await FixedWidth.WriteFromDataReaderAsync(ar1, fwMs, fwCols, fwOpts, writeHeader: true, leaveOpen: true, cancellationToken: ct);
            Assert.Equal(" IdName  \n  1Alice \n", Encoding.UTF8.GetString(fwMs.ToArray()));
        }

        // 2 rows fails in FixedWidth sync + async
        using (var r2 = twoRowTable.CreateDataReader())
        {
            var ex = Assert.Throws<FixedWidthException>(() => FixedWidth.WriteFromDataReaderToText(r2, fwCols, fwOpts, writeHeader: true));
            Assert.Equal(FixedWidthErrorCode.TooManyRowsWritten, ex.ErrorCode);
        }

        await using (var fwMs2 = new MemoryStream())
        {
            await using var ar2 = twoRowTable.CreateDataReader();
            var ex = await Assert.ThrowsAsync<FixedWidthException>(async () =>
                await FixedWidth.WriteFromDataReaderAsync(ar2, fwMs2, fwCols, fwOpts, writeHeader: true, leaveOpen: true, cancellationToken: ct));
            Assert.Equal(FixedWidthErrorCode.TooManyRowsWritten, ex.ErrorCode);
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task HtbToCsvConverter_PreservesEmptyStringDistinctFromNull_SyncAndAsyncAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        const string SOURCE_CSV = "Id,Text\r\n1,\"\"\r\n2,\r\n";
        var schema = new HtbSchema(
        [
            new HtbColumn("Id", HtbDataType.Int32, isNullable: false),
            new HtbColumn("Text", HtbDataType.String, isNullable: true)
        ]);

        using var htbStream = new MemoryStream();
        CsvToHtbConverter.Convert(SOURCE_CSV, htbStream, schema);

        foreach (bool quoteAll in new[] { false, true })
        {
            var opts = new HtbToCsvOptions { QuoteAll = quoteAll, NewLine = "\n" };
            htbStream.Position = 0;
            using var syncWriter = new StringWriter();
            HtbToCsvConverter.Convert(htbStream, syncWriter, opts);
            string expected = quoteAll
                ? "\"Id\",\"Text\"\n\"1\",\"\"\n\"2\",\n"
                : "Id,Text\n1,\"\"\n2,\n";
            Assert.Equal(expected, syncWriter.ToString());

            htbStream.Position = 0;
            using var asyncWriter = new StringWriter();
            await HtbToCsvConverter.ConvertAsync(htbStream, asyncWriter, opts, ct);
            Assert.Equal(expected, asyncWriter.ToString());
        }

        // Round-trip back into HTB and confirm row 1 is empty string (non-null) while row 2 is DBNull.
        htbStream.Position = 0;
        using var csvOut = new StringWriter();
        HtbToCsvConverter.Convert(htbStream, csvOut, new HtbToCsvOptions { NewLine = "\n" });

        using var roundTripHtb = new MemoryStream();
        CsvToHtbConverter.Convert(csvOut.ToString(), roundTripHtb, schema);
        roundTripHtb.Position = 0;

        using var dr = global::HeroParser.Htb.CreateDataReader(roundTripHtb);
        Assert.True(dr.Read());
        Assert.False(dr.IsDBNull(1));
        Assert.Equal("", dr.GetString(1));

        Assert.True(dr.Read());
        Assert.True(dr.IsDBNull(1));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Htb_WriteFromDataReader_EnforcesMaxOutputSizeDuringStaging_SyncAndAsyncAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var schema = new HtbSchema(
        [
            new HtbColumn("First", HtbDataType.String, isNullable: false),
            new HtbColumn("Second", HtbDataType.String, isNullable: false)
        ]);

        using var headerOnly = new MemoryStream();
        using (var hw = new HeroParser.Htbs.Writing.HtbStreamWriter(headerOnly, leaveOpen: true))
        {
            hw.WriteHeader(schema);
            hw.Flush();
        }
        long headerBytes = headerOnly.Length;
        // Budget allows header + 1-byte mask + small prefix, far smaller than the 4096-byte payload.
        long budget = headerBytes + 1 + 16;

        bool invokedOnError = false;
        var opts = new HtbWriteOptions
        {
            MaxOutputSize = budget,
            OnError = (_, _) =>
            {
                invokedOnError = true;
                return HtbSerializeErrorAction.SkipRow;
            }
        };

        var syncSource = new LargeSingleRowDataReader(new string('A', 4096), throwIfSecondColumnRead: true);
        using var syncOut = new MemoryStream();
        var syncEx = Assert.Throws<HtbException>(() =>
            global::HeroParser.Htb.WriteFromDataReader(syncSource, syncOut, opts, schema, leaveOpen: true));
        Assert.Equal(HtbErrorCode.LimitExceeded, syncEx.ErrorCode);
        Assert.False(invokedOnError);
        Assert.False(syncSource.SecondColumnRead);

        var asyncSource = new LargeSingleRowDataReader(new string('A', 4096), throwIfSecondColumnRead: true);
        await using var asyncOut = new MemoryStream();
        var asyncEx = await Assert.ThrowsAsync<HtbException>(async () =>
            await global::HeroParser.Htb.WriteFromDataReaderAsync(asyncSource, asyncOut, opts, schema, leaveOpen: true, cancellationToken: ct));
        Assert.Equal(HtbErrorCode.LimitExceeded, asyncEx.ErrorCode);
        Assert.False(invokedOnError);
        Assert.False(asyncSource.SecondColumnRead);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void HtbDataReader_SchemaMetadata_ReadsOnlyHeaderWithoutReadingRowData()
    {
        var schema = new HtbSchema(
        [
            new HtbColumn("Id", HtbDataType.Int32, isNullable: false),
            new HtbColumn("Name", HtbDataType.String, isNullable: true)
        ]);

        using var headerStream = new MemoryStream();
        using (var writer = new HeroParser.Htbs.Writing.HtbStreamWriter(headerStream, leaveOpen: true))
        {
            writer.WriteHeader(schema);
            writer.Flush();
        }
        byte[] headerBytes = headerStream.ToArray();

        using var guardedStream = new ThrowAfterHeaderStream(headerBytes);
        using var dr = new HeroParser.Htbs.Reading.Data.HtbDataReader(guardedStream, leaveOpen: true);

        Assert.Equal(2, dr.FieldCount);
        Assert.Equal("Id", dr.GetName(0));
        Assert.Equal("Name", dr.GetName(1));
        Assert.Equal(0, dr.GetOrdinal("id"));
        Assert.Equal(typeof(int), dr.GetFieldType(0));
        DataTable schemaTable = dr.GetSchemaTable();
        Assert.Equal(2, schemaTable.Rows.Count);
        Assert.False(guardedStream.ReadPastHeaderCalled);

        Assert.Throws<TimeoutException>(() => _ = dr.HasRows);
        Assert.True(guardedStream.ReadPastHeaderCalled);

        using var guardedForReadStream = new ThrowAfterHeaderStream(headerBytes);
        using var dr2 = new HeroParser.Htbs.Reading.Data.HtbDataReader(guardedForReadStream, leaveOpen: true);
        Assert.Equal(2, dr2.FieldCount);
        Assert.False(guardedForReadStream.ReadPastHeaderCalled);
        Assert.Throws<TimeoutException>(() => dr2.Read());
        Assert.True(guardedForReadStream.ReadPastHeaderCalled);
    }

    private sealed class ThrowAfterHeaderStream(byte[] headerBytes) : Stream
    {
        private int pos;
        public bool ReadPastHeaderCalled { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (pos >= headerBytes.Length)
            {
                ReadPastHeaderCalled = true;
                throw new TimeoutException("Attempted to read past HTB header.");
            }

            int copy = Math.Min(count, headerBytes.Length - pos);
            headerBytes.AsSpan(pos, copy).CopyTo(buffer.AsSpan(offset));
            pos += copy;
            return copy;
        }
    }

    private sealed class LargeSingleRowDataReader(string payload, bool throwIfSecondColumnRead) : DbDataReader
    {
        private int state = -1;
        public bool SecondColumnRead { get; private set; }

        public override int FieldCount => 2;
        public override int Depth => 0;
        public override bool HasRows => true;
        public override bool IsClosed => false;
        public override int RecordsAffected => -1;
        public override object this[int ordinal] => GetValue(ordinal);
        public override object this[string name] => GetValue(GetOrdinal(name));

        public override bool Read()
        {
            state++;
            return state == 0;
        }

        public override Task<bool> ReadAsync(CancellationToken cancellationToken)
            => Task.FromResult(Read());

        public override string GetName(int ordinal) => ordinal == 0 ? "First" : "Second";
        public override int GetOrdinal(string name) => string.Equals(name, "First", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
        [return: System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields)]
        public override Type GetFieldType(int ordinal) => typeof(string);
        public override string GetDataTypeName(int ordinal) => nameof(String);
        public override bool IsDBNull(int ordinal) => false;
        public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken) => Task.FromResult(false);

        public override string GetString(int ordinal)
        {
            if (ordinal == 1)
            {
                SecondColumnRead = true;
                if (throwIfSecondColumnRead)
                    throw new InvalidOperationException("Should not read second column after limit overflow.");
            }
            return payload;
        }

        public override object GetValue(int ordinal) => GetString(ordinal);
        public override int GetValues(object[] values) => 0;
        public override bool GetBoolean(int ordinal) => throw new NotSupportedException();
        public override byte GetByte(int ordinal) => throw new NotSupportedException();
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override char GetChar(int ordinal) => throw new NotSupportedException();
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override DateTime GetDateTime(int ordinal) => throw new NotSupportedException();
        public override decimal GetDecimal(int ordinal) => throw new NotSupportedException();
        public override double GetDouble(int ordinal) => throw new NotSupportedException();
        public override float GetFloat(int ordinal) => throw new NotSupportedException();
        public override Guid GetGuid(int ordinal) => throw new NotSupportedException();
        public override short GetInt16(int ordinal) => throw new NotSupportedException();
        public override int GetInt32(int ordinal) => throw new NotSupportedException();
        public override long GetInt64(int ordinal) => throw new NotSupportedException();
        public override IEnumerator GetEnumerator() => throw new NotSupportedException();
        public override bool NextResult() => false;
    }

    private sealed class NonSeekableReadStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class ReflectionExcelBase64PlusRow
    {
        public byte[]? Payload { get; set; }
        public string? Text { get; set; }
    }

    private sealed class ReflectionExcel15DigitRow
    {
        public long Max15 { get; set; }
        public long Over15 { get; set; }
        public ulong Over15U { get; set; }
    }

    private sealed class ReflectionExcelTimeOnlyExportRow
    {
        public TimeOnly TimeVal { get; set; }
    }

    private sealed class ReflectionExcelCharRow
    {
        public char Symbol { get; set; }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task HtbToCsvConverter_SingleColumnNullRows_RoundTripWithAndWithoutQuoteAllAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var intSchema = new HtbSchema([new HtbColumn("Value", HtbDataType.Int32, isNullable: true)]);
        using var intHtb = new MemoryStream();
        using (var hw = new HeroParser.Htbs.Writing.HtbStreamWriter(intHtb, leaveOpen: true))
        {
            hw.WriteHeader(intSchema);
            hw.IncrementRecordCount();
            hw.WriteMask([0x01]);
            hw.IncrementRecordCount();
            hw.WriteMask([0x00]);
            hw.WriteInt32(42);
            hw.IncrementRecordCount();
            hw.WriteMask([0x01]);
            hw.Flush();
        }

        foreach (bool quoteAll in new[] { false, true })
        {
            var opts = new HtbToCsvOptions { QuoteAll = quoteAll, NewLine = "\n" };
            intHtb.Position = 0;
            using var syncOut = new StringWriter();
            HtbToCsvConverter.Convert(intHtb, syncOut, opts);
            string expected = quoteAll ? "\"Value\"\n\"\"\n\"42\"\n\"\"\n" : "Value\n\"\"\n42\n\"\"\n";
            Assert.Equal(expected, syncOut.ToString());

            intHtb.Position = 0;
            using var asyncOut = new StringWriter();
            await HtbToCsvConverter.ConvertAsync(intHtb, asyncOut, opts, ct);
            Assert.Equal(expected, asyncOut.ToString());

            using var rtHtb = new MemoryStream();
            CsvToHtbConverter.Convert(syncOut.ToString(), rtHtb, intSchema);
            rtHtb.Position = 0;
            using var dr = global::HeroParser.Htb.CreateDataReader(rtHtb);
            Assert.True(dr.Read());
            Assert.True(dr.IsDBNull(0));
            Assert.True(dr.Read());
            Assert.Equal(42, dr.GetInt32(0));
            Assert.True(dr.Read());
            Assert.True(dr.IsDBNull(0));
            Assert.False(dr.Read());
        }

        var floatArraySchema = new HtbSchema([new HtbColumn("Vec", HtbDataType.FloatArray, isNullable: true)]);
        using var vecHtb = new MemoryStream();
        using (var hw = new HeroParser.Htbs.Writing.HtbStreamWriter(vecHtb, leaveOpen: true))
        {
            hw.WriteHeader(floatArraySchema);
            hw.IncrementRecordCount();
            hw.WriteMask([0x01]);
            hw.IncrementRecordCount();
            hw.WriteMask([0x00]);
            hw.WriteFloatArray([]);
            hw.IncrementRecordCount();
            hw.WriteMask([0x00]);
            hw.WriteFloatArray([1.5f]);
            hw.Flush();
        }

        vecHtb.Position = 0;
        using var vecCsv = new StringWriter();
        HtbToCsvConverter.Convert(vecHtb, vecCsv, new HtbToCsvOptions { NewLine = "\n" });
        using var rtVecHtb = new MemoryStream();
        CsvToHtbConverter.Convert(vecCsv.ToString(), rtVecHtb, floatArraySchema);
        rtVecHtb.Position = 0;
        using var vecReader = global::HeroParser.Htb.CreateDataReader(rtVecHtb);
        Assert.True(vecReader.Read());
        Assert.True(vecReader.IsDBNull(0));
        Assert.True(vecReader.Read());
        Assert.Equal([], Assert.IsType<float[]>(vecReader.GetValue(0)));
        Assert.True(vecReader.Read());
        Assert.Equal([1.5f], Assert.IsType<float[]>(vecReader.GetValue(0)));
        Assert.False(vecReader.Read());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task CsvToHtbConverter_MaxInputRowSize_EnforcedEquallyForStringAndStreamAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var schema = new HtbSchema([new HtbColumn("Id", HtbDataType.Int32, isNullable: false)]);
        var opts = new CsvToHtbOptions { MaxInputRowSize = 4 };

        using (var okSyncOut = new MemoryStream())
        {
            CsvToHtbConverter.Convert("Id\n1234\n", okSyncOut, schema, opts);
            okSyncOut.Position = 0;
            using var dr = global::HeroParser.Htb.CreateDataReader(okSyncOut);
            Assert.True(dr.Read());
            Assert.Equal(1234, dr.GetInt32(0));
        }

        using var badDataSyncOut = new MemoryStream();
        var syncEx = Assert.Throws<CsvException>(() =>
            CsvToHtbConverter.Convert("Id\n123456\n", badDataSyncOut, schema, opts));
        Assert.True(syncEx.IsRowSizeLimitExceeded);

        using var badHeaderSyncOut = new MemoryStream();
        var syncHdrEx = Assert.Throws<CsvException>(() =>
            CsvToHtbConverter.Convert("HeaderLong\n1\n", badHeaderSyncOut, schema, opts));
        Assert.True(syncHdrEx.IsRowSizeLimitExceeded);

        await using var badDataAsyncIn = new MemoryStream(Encoding.UTF8.GetBytes("Id\n123456\n"));
        await using var badDataAsyncOut = new MemoryStream();
        var asyncEx = await Assert.ThrowsAsync<CsvException>(async () =>
            await CsvToHtbConverter.ConvertAsync(badDataAsyncIn, badDataAsyncOut, schema, opts, ct));
        Assert.True(asyncEx.IsRowSizeLimitExceeded);

        await using var badHdrAsyncIn = new MemoryStream(Encoding.UTF8.GetBytes("HeaderLong\n1\n"));
        await using var badHdrAsyncOut = new MemoryStream();
        var asyncHdrEx = await Assert.ThrowsAsync<CsvException>(async () =>
            await CsvToHtbConverter.ConvertAsync(badHdrAsyncIn, badHdrAsyncOut, schema, opts, ct));
        Assert.True(asyncHdrEx.IsRowSizeLimitExceeded);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Csv_CharWithInjectionProtection_RoundTripsWithoutWeakeningStringProtectionAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        GeneratedCsvCharAndNoteRow[] records =
        [
            new() { Symbol = '=', Note = "=SUM(A1)" },
            new() { Symbol = '@', Note = "@CMD" },
            new() { Symbol = '+', Note = "+1" },
            new() { Symbol = '-', Note = "-1" },
            new() { Symbol = '\t', Note = "\tX" },
            new() { Symbol = '\r', Note = "\rX" },
            new() { Symbol = 'A', Note = "plain" }
        ];

        // Default injection protection (EscapeWithQuote) must emit safe reversible "char:<c>" for char,
        // while keeping arbitrary strings prefixed with apostrophe.
        string defaultCsv = Csv.Write<GeneratedCsvCharAndNoteRow>().ToText(records);
        Assert.Contains("char:=,\"'=SUM(A1)\"", defaultCsv, StringComparison.Ordinal);
        Assert.Contains("char:@,\"'@CMD\"", defaultCsv, StringComparison.Ordinal);
        Assert.Contains("\nA,plain", defaultCsv, StringComparison.Ordinal);
        Assert.DoesNotContain("\n=,", defaultCsv, StringComparison.Ordinal);

        List<GeneratedCsvCharAndNoteRow> genCharReadBack = [];
        foreach (var item in Csv.Read<GeneratedCsvCharAndNoteRow>().AllowNewlinesInQuotes().FromText(defaultCsv))
            genCharReadBack.Add(item);
        Assert.Equal(records.Select(r => r.Symbol), genCharReadBack.Select(r => r.Symbol));

        List<GeneratedCsvCharAndNoteRow> genUtf8ReadBack = [];
        using (var utf8Stream = new MemoryStream(Encoding.UTF8.GetBytes(defaultCsv)))
        {
            foreach (var item in Csv.Read<GeneratedCsvCharAndNoteRow>().AllowNewlinesInQuotes().FromStream(utf8Stream, out _))
                genUtf8ReadBack.Add(item);
        }
        Assert.Equal(records.Select(r => r.Symbol), genUtf8ReadBack.Select(r => r.Symbol));

        // CsvMap / reflection writer + reader
        var charMap = new HeroParser.SeparatedValues.Mapping.CsvMap<ReflectionCsvCharRow>()
            .Map(x => x.Symbol)
            .Map(x => x.Note);
        string mapCsv = Csv.Write<ReflectionCsvCharRow>()
            .WithMap(charMap)
            .ToText([new ReflectionCsvCharRow { Symbol = '=', Note = "=SUM(A1)" }]);
        List<ReflectionCsvCharRow> mapReadList = [];
        foreach (var item in Csv.Read<ReflectionCsvCharRow>().WithMap(charMap).FromText(mapCsv))
            mapReadList.Add(item);
        var mapReadBack = Assert.Single(mapReadList);
        Assert.Equal('=', mapReadBack.Symbol);

        // Async DataReader + async writer path
        using var table = new DataTable();
        table.Columns.Add("Symbol", typeof(char));
        table.Columns.Add("Note", typeof(string));
        table.Rows.Add('=', "=SUM(A1)");
        table.Rows.Add('@', "safe");
        await using var asyncOut = new MemoryStream();
        await using (var dr = table.CreateDataReader())
            await Csv.WriteFromDataReaderAsync(dr, asyncOut, leaveOpen: true, cancellationToken: ct);
        string asyncCsv = Encoding.UTF8.GetString(asyncOut.ToArray());
        Assert.Contains("char:=,\"'=SUM(A1)\"", asyncCsv, StringComparison.Ordinal);
        List<GeneratedCsvCharAndNoteRow> asyncBack = [];
        foreach (var item in Csv.Read<GeneratedCsvCharAndNoteRow>().FromText(asyncCsv))
            asyncBack.Add(item);
        Assert.Equal(['=', '@'], asyncBack.Select(x => x.Symbol));

        // EscapeWithTab mode on char + Rejection of arbitrary multi-char strings for char column
        string tabCsv = Csv.Write<GeneratedCsvCharAndNoteRow>()
            .WithInjectionProtection(HeroParser.SeparatedValues.Writing.CsvInjectionProtection.EscapeWithTab)
            .ToText([new GeneratedCsvCharAndNoteRow { Symbol = '=', Note = "=SUM(A1)" }]);
        Assert.Contains("char:=,\"\t=SUM(A1)\"", tabCsv, StringComparison.Ordinal);
        List<GeneratedCsvCharAndNoteRow> tabBack = [];
        foreach (var item in Csv.Read<GeneratedCsvCharAndNoteRow>().FromText(tabCsv))
            tabBack.Add(item);
        Assert.Equal('=', Assert.Single(tabBack).Symbol);

        Assert.ThrowsAny<Exception>(() =>
        {
            foreach (var _ in Csv.Read<GeneratedCsvCharAndNoteRow>().FromText("Symbol,Note\n'=SUM(A1),ok\n")) { }
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncDataReaderExporters_UseAsyncFieldAccessOnly_AcrossAllFiveFormatsAsync()
    {
        var ct = TestContext.Current.CancellationToken;

        // 1. CSV
        await using var csvStream = new MemoryStream();
        await Csv.WriteFromDataReaderAsync(new AsyncOnlyDbDataReader(), csvStream, leaveOpen: true, cancellationToken: ct);
        string csvText = Encoding.UTF8.GetString(csvStream.ToArray());
        Assert.Contains("42,9000000000,7,255,1.5,2.25,12.34,True,", csvText, StringComparison.Ordinal);
        Assert.Contains(",11111111-2222-3333-4444-555555555555,Z,hello,obj-1", csvText, StringComparison.Ordinal);

        // 2. Excel
        await using var excelStream = new MemoryStream();
        await global::HeroParser.Excel.WriteFromDataReaderAsync(new AsyncOnlyDbDataReader(), excelStream, leaveOpen: true, cancellationToken: ct);
        Assert.True(excelStream.Length > 0);
        excelStream.Position = 0;
        var excelRows = global::HeroParser.Excel.Read<AsyncExportVerifyRecord>().FromStream(excelStream);
        Assert.Equal(2, excelRows.Count);
        Assert.Equal(42, excelRows[0].Id);
        Assert.Equal("hello", excelRows[0].Text);

        // 3. JSONL
        await using var jsonlStream = new MemoryStream();
        await Jsonl.WriteFromDataReaderAsync(new AsyncOnlyDbDataReader(), jsonlStream, leaveOpen: true, cancellationToken: ct);
        string jsonlText = Encoding.UTF8.GetString(jsonlStream.ToArray());
        Assert.Contains("\"Id\":42", jsonlText, StringComparison.Ordinal);
        Assert.Contains("\"Text\":\"hello\"", jsonlText, StringComparison.Ordinal);
        Assert.Contains("\"Big\":null", jsonlText, StringComparison.Ordinal);

        // 4. FixedWidth (both sequential ordinal order and reversed/out-of-order columns to cover ReadBufferedCellAsync)
        FixedWidthFieldDefinition[] inOrderCols =
        [
            new("Id", 4, FieldAlignment.Right, '0'),
            new("Text", 8, FieldAlignment.Left, ' ')
        ];
        await using var fwStream = new MemoryStream();
        await FixedWidth.WriteFromDataReaderAsync(new AsyncOnlyDbDataReader(), fwStream, inOrderCols, writeHeader: false, leaveOpen: true, cancellationToken: ct);
        string fwText = Encoding.UTF8.GetString(fwStream.ToArray());
        Assert.Contains("0042hello   ", fwText, StringComparison.Ordinal);

        FixedWidthFieldDefinition[] reorderedCols =
        [
            new("Text", 8, FieldAlignment.Left, ' '),
            new("Id", 4, FieldAlignment.Right, '0')
        ];
        await using var fwReorderedStream = new MemoryStream();
        await FixedWidth.WriteFromDataReaderAsync(new AsyncOnlyDbDataReader(), fwReorderedStream, reorderedCols, writeHeader: false, leaveOpen: true, cancellationToken: ct);
        string fwReorderedText = Encoding.UTF8.GetString(fwReorderedStream.ToArray());
        Assert.Contains("hello   0042", fwReorderedText, StringComparison.Ordinal);

        // 5. HTB
        await using var htbStream = new MemoryStream();
        await global::HeroParser.Htb.WriteFromDataReaderAsync(new AsyncOnlyDbDataReader(), htbStream, leaveOpen: true, cancellationToken: ct);
        htbStream.Position = 0;
        using var htbReader = global::HeroParser.Htb.CreateDataReader(htbStream, leaveOpen: true);
        Assert.True(htbReader.Read());
        Assert.Equal(42, htbReader.GetInt32(0));
        Assert.Equal("hello", htbReader.GetString(11));
        Assert.True(htbReader.Read());
        Assert.True(htbReader.IsDBNull(1));

        // Verify cancellation token propagates through async field reads
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await Csv.WriteFromDataReaderAsync(new AsyncOnlyDbDataReader(cancelOnFieldRead: true), new MemoryStream(), leaveOpen: false, cancellationToken: cts.Token));
    }

    private sealed class AsyncOnlyDbDataReader(bool cancelOnFieldRead = false) : DbDataReader
    {
        private static readonly Type[] types =
        [
            typeof(int), typeof(long), typeof(short), typeof(byte), typeof(float), typeof(double),
            typeof(decimal), typeof(bool), typeof(DateTime), typeof(Guid), typeof(char), typeof(string), typeof(object)
        ];

        private static readonly string[] names =
            ["Id", "Big", "Small", "ByteVal", "SingleVal", "DoubleVal", "DecVal", "Flag", "When", "Uid", "Ch", "Text", "Misc"];

        private int row = -1;

        public override int FieldCount => names.Length;
        public override int Depth => 0;
        public override bool HasRows => true;
        public override bool IsClosed => false;
        public override int RecordsAffected => -1;
        public override object this[int ordinal] => throw new NotSupportedException("Sync field access disabled.");
        public override object this[string name] => throw new NotSupportedException("Sync field access disabled.");
        public override string GetName(int ordinal) => names[ordinal];
        public override int GetOrdinal(string name) => Array.FindIndex(names, n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        [return: System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields)]
        public override Type GetFieldType(int ordinal) => types[ordinal];
        public override string GetDataTypeName(int ordinal) => types[ordinal].Name;
        public override bool NextResult() => false;
        public override IEnumerator GetEnumerator() => throw new NotSupportedException();

        public override bool Read() => throw new NotSupportedException("Sync Read is disabled.");
        public override bool IsDBNull(int ordinal) => throw new NotSupportedException("Sync IsDBNull is disabled.");
        public override object GetValue(int ordinal) => throw new NotSupportedException("Sync GetValue is disabled.");
        public override int GetValues(object[] values) => throw new NotSupportedException("Sync GetValues is disabled.");
        public override bool GetBoolean(int ordinal) => throw new NotSupportedException("Sync getter disabled.");
        public override byte GetByte(int ordinal) => throw new NotSupportedException("Sync getter disabled.");
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override char GetChar(int ordinal) => throw new NotSupportedException("Sync getter disabled.");
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override DateTime GetDateTime(int ordinal) => throw new NotSupportedException("Sync getter disabled.");
        public override decimal GetDecimal(int ordinal) => throw new NotSupportedException("Sync getter disabled.");
        public override double GetDouble(int ordinal) => throw new NotSupportedException("Sync getter disabled.");
        public override float GetFloat(int ordinal) => throw new NotSupportedException("Sync getter disabled.");
        public override Guid GetGuid(int ordinal) => throw new NotSupportedException("Sync getter disabled.");
        public override short GetInt16(int ordinal) => throw new NotSupportedException("Sync getter disabled.");
        public override int GetInt32(int ordinal) => throw new NotSupportedException("Sync getter disabled.");
        public override long GetInt64(int ordinal) => throw new NotSupportedException("Sync getter disabled.");
        public override string GetString(int ordinal) => throw new NotSupportedException("Sync getter disabled.");

        public override Task<bool> ReadAsync(CancellationToken cancellationToken)
        {
            if (!cancelOnFieldRead)
                cancellationToken.ThrowIfCancellationRequested();
            row++;
            return Task.FromResult(row < 2);
        }

        public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(row == 1 && ordinal is 1 or 11);
        }

        public override Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            object val = (row, ordinal) switch
            {
                (0, 0) => 42,
                (1, 0) => 99,
                (0, 1) => 9_000_000_000L,
                (_, 2) => (short)7,
                (_, 3) => (byte)255,
                (_, 4) => 1.5f,
                (_, 5) => 2.25d,
                (_, 6) => 12.34m,
                (_, 7) => true,
                (_, 8) => new DateTime(2026, 11, 27, 0, 0, 0, DateTimeKind.Utc),
                (_, 9) => new Guid("11111111-2222-3333-4444-555555555555"),
                (_, 10) => 'Z',
                (0, 11) => "hello",
                (_, 12) => "obj-1",
                _ => throw new InvalidOperationException($"Unexpected null ordinal {ordinal} at row {row}")
            };
            return Task.FromResult((T)val);
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task HtbToCsvConverter_SingleColumnNullableStringWithNull_RejectsLossyConversion_SyncAndAsyncAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var stringSchema = new HtbSchema([new HtbColumn("Text", HtbDataType.String, isNullable: true)]);

        // Non-null empty string in a single-column String HTB file still converts and round-trips losslessly.
        using var nonNullHtb = new MemoryStream();
        using (var hw = new HeroParser.Htbs.Writing.HtbStreamWriter(nonNullHtb, leaveOpen: true))
        {
            hw.WriteHeader(stringSchema);
            hw.IncrementRecordCount();
            hw.WriteMask([0x00]);
            hw.WriteString("");
            hw.IncrementRecordCount();
            hw.WriteMask([0x00]);
            hw.WriteString("hello");
            hw.Flush();
        }

        nonNullHtb.Position = 0;
        using var okCsv = new StringWriter();
        HtbToCsvConverter.Convert(nonNullHtb, okCsv, new HtbToCsvOptions { NewLine = "\n" });
        Assert.Equal("Text\n\"\"\nhello\n", okCsv.ToString());

        using var okRoundTripHtb = new MemoryStream();
        CsvToHtbConverter.Convert(okCsv.ToString(), okRoundTripHtb, stringSchema);
        okRoundTripHtb.Position = 0;
        using (var okReader = global::HeroParser.Htb.CreateDataReader(okRoundTripHtb))
        {
            Assert.True(okReader.Read());
            Assert.False(okReader.IsDBNull(0));
            Assert.Equal("", okReader.GetString(0));
            Assert.True(okReader.Read());
            Assert.Equal("hello", okReader.GetString(0));
            Assert.False(okReader.Read());
        }

        // Actual null in a single-column nullable String HTB cannot round-trip distinctly from "" and must be rejected.
        using var withNullHtb = new MemoryStream();
        using (var hw = new HeroParser.Htbs.Writing.HtbStreamWriter(withNullHtb, leaveOpen: true))
        {
            hw.WriteHeader(stringSchema);
            hw.IncrementRecordCount();
            hw.WriteMask([0x01]);
            hw.Flush();
        }

        withNullHtb.Position = 0;
        using var failSyncOut = new StringWriter();
        var syncEx = Assert.Throws<HtbException>(() =>
            HtbToCsvConverter.Convert(withNullHtb, failSyncOut, new HtbToCsvOptions { NewLine = "\n" }));
        Assert.Equal(HtbErrorCode.SerializationError, syncEx.ErrorCode);

        withNullHtb.Position = 0;
        using var failAsyncOut = new StringWriter();
        var asyncEx = await Assert.ThrowsAsync<HtbException>(() =>
            HtbToCsvConverter.ConvertAsync(withNullHtb, failAsyncOut, new HtbToCsvOptions { NewLine = "\n" }, ct));
        Assert.Equal(HtbErrorCode.SerializationError, asyncEx.ErrorCode);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task JsonlStreamWriter_WriteFinalNewline_ReservesNewlineInMaxOutputSizeBudget_SyncAndAsyncAsync()
    {
        var ct = TestContext.Current.CancellationToken;

        // "1" (1 byte) + "\n" (1 byte) = 2 bytes required when WriteFinalNewline = true.
        var cap1Opts = new JsonlWriteOptions { WriteFinalNewline = true, NewLine = "\n", MaxOutputSize = 1 };

        await using (var s1 = new MemoryStream())
        await using (var w1 = new JsonlStreamWriter(s1, cap1Opts, leaveOpen: true))
        {
            var ex = Assert.Throws<JsonlException>(() => w1.WriteRecord(1));
            Assert.Equal(JsonlErrorCode.OutputSizeExceeded, ex.ErrorCode);
            Assert.Equal(0, w1.RecordsWritten);
            Assert.Equal(0, s1.Length);
        }

        await using (var s1Async = new MemoryStream())
        await using (var w1Async = new JsonlStreamWriter(s1Async, cap1Opts, leaveOpen: true))
        {
            var ex = await Assert.ThrowsAsync<JsonlException>(() => w1Async.WriteRecordAsync(1, ct).AsTask());
            Assert.Equal(JsonlErrorCode.OutputSizeExceeded, ex.ErrorCode);
            Assert.Equal(0, w1Async.RecordsWritten);
            Assert.Equal(0, s1Async.Length);
        }

        // MaxOutputSize = 2 fits "1\n".
        var cap2Opts = new JsonlWriteOptions { WriteFinalNewline = true, NewLine = "\n", MaxOutputSize = 2 };
        {
            await using var s2 = new MemoryStream();
            using (var w2 = new JsonlStreamWriter(s2, cap2Opts, leaveOpen: true))
            {
                w2.WriteRecord(1);
                w2.Flush();
            }
            Assert.Equal("1\n", Encoding.UTF8.GetString(s2.ToArray()));
        }

        // MaxOutputSize = 3 fits "1\n" (2 bytes), rejects second record "2" + final "\n" (2 + 1 + 1 = 4 bytes), and still flushes "1\n".
        var cap3Opts = new JsonlWriteOptions { WriteFinalNewline = true, NewLine = "\n", MaxOutputSize = 3 };
        {
            await using var s3 = new MemoryStream();
            await using (var w3 = new JsonlStreamWriter(s3, cap3Opts, leaveOpen: true))
            {
                await w3.WriteRecordAsync(1, ct);
                var ex = await Assert.ThrowsAsync<JsonlException>(() => w3.WriteRecordAsync(2, ct).AsTask());
                Assert.Equal(JsonlErrorCode.OutputSizeExceeded, ex.ErrorCode);
                await w3.FlushAsync(ct);
            }
            Assert.Equal("1\n", Encoding.UTF8.GetString(s3.ToArray()));
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void XlsxWriter_OutOfRangeColumnIndex_DoesNotCorruptWorksheetOnRetry()
    {
        using var ms = new MemoryStream();
        int maxCol = XlsxWriter.SheetWriter.MAX_COLUMNS;

        using (var writer = new XlsxWriter(ms, leaveOpen: true))
        {
            using var sheet = writer.StartSheet("Sheet1");
            sheet.StartRow(1);

            foreach (int badCol in new[] { 0, maxCol + 1 })
            {
                Assert.Throws<ExcelException>(() => sheet.WriteCellString(badCol, "bad"));
                Assert.Throws<ExcelException>(() => sheet.WriteCellString(badCol, "bad".AsSpan()));
                Assert.Throws<ExcelException>(() => sheet.WriteCellLiteralString(badCol, "=1+1"));
                Assert.Throws<ExcelException>(() => sheet.WriteCellLiteralString(badCol, "=2+2".AsSpan()));
                Assert.Throws<ExcelException>(() => sheet.WriteCellNumber(badCol, 12.34));
                Assert.Throws<ExcelException>(() => sheet.WriteCellBoolean(badCol, true));
                Assert.Throws<ExcelException>(() => sheet.WriteCellDate(badCol, new DateTime(2026, 11, 27, 0, 0, 0, DateTimeKind.Utc)));
                Assert.Throws<ExcelException>(() => sheet.WriteCellEmpty(badCol));
            }

            // Subsequent valid cells on the same row must still produce valid XML.
            sheet.WriteCellString(1, "ok");
            sheet.WriteCellLiteralString(2, "=1+1");
            sheet.WriteCellLiteralString(3, "=2+2".AsSpan());
            sheet.WriteCellNumber(4, 42.5);
            sheet.WriteCellBoolean(5, true);
            sheet.WriteCellDate(6, new DateTime(2026, 11, 27, 0, 0, 0, DateTimeKind.Utc));
            sheet.WriteCellEmpty(7);
            sheet.EndRow();
            sheet.Close();
        }

        ms.Position = 0;
        using var reader = new XlsxReader(ms);
        using var sheetReader = reader.OpenSheet(reader.Workbook.Sheets[0]);
        var row = sheetReader.ReadNextRow();
        Assert.NotNull(row);
        Assert.Equal("ok", row[0]);
        Assert.Equal("=1+1", row[1]);
        Assert.Equal("=2+2", row[2]);
        Assert.Equal("42.5", row[3]);
        Assert.Equal("TRUE", row[4]);
        Assert.False(string.IsNullOrEmpty(row[5]));
        Assert.True(row.Length < 7 || string.IsNullOrEmpty(row[6]));
        Assert.Null(sheetReader.ReadNextRow());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Excel_WriteFromDataReader_HonorsOnSerializeError_SyncAndAsyncAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var contexts = new List<ExcelSerializeErrorContext>();

        ExcelWriteOptions CreateOptions(ExcelSerializeErrorAction action, ExcelInjectionProtection protection = ExcelInjectionProtection.EscapeWithApostrophe) => new()
        {
            InjectionProtection = protection,
            OnSerializeError = ctx =>
            {
                contexts.Add(ctx);
                return action;
            }
        };

        // 1. SkipRow on getter failure (Sync + Async): bad row 2 is omitted completely without partial row markup or blank row gap.
        contexts.Clear();
        using (var rSkipSync = new FailingExcelAsyncDbDataReader(mode: 0))
        {
            byte[] xlsx = HeroParser.Excel.WriteFromDataReaderToBytes(rSkipSync, CreateOptions(ExcelSerializeErrorAction.SkipRow), sheetName: "Orders");
            using var ms = new MemoryStream(xlsx);
            var rows = HeroParser.Excel.Read().WithoutHeader().FromSheet("Orders").FromStream(ms);
            Assert.Equal(3, rows.Count);
            Assert.Equal(["Id", "Name", "Amount"], rows[0]);
            Assert.Equal(["1", "Alpha", "10"], rows[1]);
            Assert.Equal(["3", "Gamma", "30"], rows[2]);
            var ctx = Assert.Single(contexts);
            Assert.Equal(2, ctx.Row);
            Assert.Equal(2, ctx.Column);
            Assert.Equal("Name", ctx.MemberName);
            Assert.Equal(typeof(string), ctx.SourceType);
            Assert.Equal("Orders", ctx.SheetName);
            Assert.IsType<InvalidOperationException>(ctx.Exception);
        }

        contexts.Clear();
        await using (var rSkipAsync = new FailingExcelAsyncDbDataReader(mode: 0))
        await using (var ms = new MemoryStream())
        {
            await HeroParser.Excel.WriteFromDataReaderAsync(rSkipAsync, ms, CreateOptions(ExcelSerializeErrorAction.SkipRow), sheetName: "Orders", leaveOpen: true, cancellationToken: ct);
            ms.Position = 0;
            var rows = HeroParser.Excel.Read().WithoutHeader().FromSheet("Orders").FromStream(ms);
            Assert.Equal(3, rows.Count);
            Assert.Equal(["3", "Gamma", "30"], rows[2]);
            Assert.Single(contexts);
            Assert.True(rSkipAsync.AsyncFieldAccessUsed);
        }

        // 2. WriteEmpty on formatting failure (IFormattable throwing FormatException) & InjectionProtection.Reject (Sync + Async)
        foreach (int mode in new[] { 1, 2 })
        {
            ExcelInjectionProtection protection = mode == 2 ? ExcelInjectionProtection.Reject : ExcelInjectionProtection.EscapeWithApostrophe;

            contexts.Clear();
            using (var rEmptySync = new FailingExcelAsyncDbDataReader(mode))
            {
                byte[] xlsx = HeroParser.Excel.WriteFromDataReaderToBytes(rEmptySync, CreateOptions(ExcelSerializeErrorAction.WriteEmpty, protection));
                using var ms = new MemoryStream(xlsx);
                var rows = HeroParser.Excel.Read().WithoutHeader().FromStream(ms);
                Assert.Equal(4, rows.Count);
                Assert.Equal(["1", "Alpha", "10"], rows[1]);
                Assert.Equal("2", rows[2][0]);
                Assert.Equal(string.Empty, mode == 2 ? rows[2][1] : rows[2][2]);
                Assert.Equal(["3", "Gamma", "30"], rows[3]);
                Assert.Single(contexts);
            }

            contexts.Clear();
            await using (var rEmptyAsync = new FailingExcelAsyncDbDataReader(mode))
            await using (var ms = new MemoryStream())
            {
                await HeroParser.Excel.WriteFromDataReaderAsync(rEmptyAsync, ms, CreateOptions(ExcelSerializeErrorAction.WriteEmpty, protection), leaveOpen: true, cancellationToken: ct);
                ms.Position = 0;
                var rows = HeroParser.Excel.Read().WithoutHeader().FromStream(ms);
                Assert.Equal(4, rows.Count);
                Assert.Equal(string.Empty, mode == 2 ? rows[2][1] : rows[2][2]);
                Assert.Single(contexts);
                Assert.True(rEmptyAsync.AsyncFieldAccessUsed);
            }
        }

        // 3. Throw wraps inner error in ExcelException (Sync + Async)
        using (var rThrowSync = new FailingExcelAsyncDbDataReader(mode: 0))
        {
            var ex = Assert.Throws<ExcelException>(() =>
                HeroParser.Excel.WriteFromDataReaderToBytes(rThrowSync, CreateOptions(ExcelSerializeErrorAction.Throw)));
            Assert.IsType<InvalidOperationException>(ex.InnerException);
        }

        await using (var rThrowAsync = new FailingExcelAsyncDbDataReader(mode: 0))
        await using (var msThrow = new MemoryStream())
        {
            var ex = await Assert.ThrowsAsync<ExcelException>(() =>
                HeroParser.Excel.WriteFromDataReaderAsync(rThrowAsync, msThrow, CreateOptions(ExcelSerializeErrorAction.Throw), cancellationToken: ct));
            Assert.IsType<InvalidOperationException>(ex.InnerException);
        }
    }

    private sealed class FailingFormattable : IFormattable
    {
        public string ToString(string? format, IFormatProvider? formatProvider)
            => throw new FormatException("Boom during formatting.");
    }

    private sealed class FailingExcelAsyncDbDataReader(int mode) : DbDataReader
    {
        private int row = -1;
        public bool AsyncFieldAccessUsed { get; private set; }

        public override int FieldCount => 3;
        public override bool HasRows => true;
        public override bool IsClosed => false;
        public override int Depth => 0;
        public override int RecordsAffected => -1;
        public override object this[int ordinal] => GetValue(ordinal);
        public override object this[string name] => GetValue(GetOrdinal(name));

        public override string GetName(int ordinal) => ordinal switch { 0 => "Id", 1 => "Name", 2 => "Amount", _ => throw new IndexOutOfRangeException() };
        public override int GetOrdinal(string name) => name switch { "Id" => 0, "Name" => 1, "Amount" => 2, _ => -1 };
        public override Type GetFieldType(int ordinal) => ordinal switch { 0 => typeof(int), 1 => typeof(string), 2 => typeof(object), _ => throw new IndexOutOfRangeException() };
        public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;
        public override bool Read() => ++row < 3;
        public override Task<bool> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(++row < 3);
        public override bool IsDBNull(int ordinal) => false;
        public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken)
        {
            AsyncFieldAccessUsed = true;
            return Task.FromResult(false);
        }

        public override object GetValue(int ordinal)
        {
            if (row == 1)
            {
                if (mode == 0 && ordinal == 1)
                    throw new InvalidOperationException("Getter failed on row 2.");
                if (mode == 1 && ordinal == 2)
                    return new FailingFormattable();
                if (mode == 2 && ordinal == 1)
                    return "=HYPERLINK(\"bad\")";
            }

            return (row, ordinal) switch
            {
                (0, 0) => 1,
                (0, 1) => "Alpha",
                (0, 2) => 10,
                (1, 0) => 2,
                (1, 1) => "Beta",
                (1, 2) => 20,
                (2, 0) => 3,
                (2, 1) => "Gamma",
                (2, 2) => 30,
                _ => throw new InvalidOperationException()
            };
        }

        public override Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken)
        {
            AsyncFieldAccessUsed = true;
            return Task.FromResult((T)GetValue(ordinal));
        }

        public override int GetInt32(int ordinal) => (int)GetValue(ordinal);
        public override string GetString(int ordinal) => (string)GetValue(ordinal);
        public override bool GetBoolean(int ordinal) => throw new NotSupportedException();
        public override byte GetByte(int ordinal) => throw new NotSupportedException();
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override char GetChar(int ordinal) => throw new NotSupportedException();
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override DateTime GetDateTime(int ordinal) => throw new NotSupportedException();
        public override decimal GetDecimal(int ordinal) => throw new NotSupportedException();
        public override double GetDouble(int ordinal) => throw new NotSupportedException();
        public override float GetFloat(int ordinal) => throw new NotSupportedException();
        public override Guid GetGuid(int ordinal) => throw new NotSupportedException();
        public override short GetInt16(int ordinal) => throw new NotSupportedException();
        public override long GetInt64(int ordinal) => throw new NotSupportedException();
        public override int GetValues(object[] values) => throw new NotSupportedException();
        public override bool NextResult() => false;
        public override IEnumerator GetEnumerator() => new DbEnumerator(this, closeReader: false);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task HtbToCsvConverter_EmptyRelativeUri_RoundTripsDistinctFromNull_SyncAndAsyncAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var emptyRelUri = new Uri(string.Empty, UriKind.Relative);
        var absUri = new Uri("https://example.com/path?x=1", UriKind.Absolute);

        // 1. Multi-column nullable Uri: empty relative URI stays non-null while null stays DBNull across QuoteAll=false/true & sync/async.
        var multiSchema = new HtbSchema(
        [
            new HtbColumn("Id", HtbDataType.Int32, isNullable: false),
            new HtbColumn("Link", HtbDataType.Uri, isNullable: true)
        ]);

        using var multiHtb = new MemoryStream();
        using (var hw = new HeroParser.Htbs.Writing.HtbStreamWriter(multiHtb, leaveOpen: true))
        {
            hw.WriteHeader(multiSchema);
            hw.IncrementRecordCount();
            hw.WriteMask([0x00]);
            hw.WriteInt32(1);
            hw.WriteUri(emptyRelUri);

            hw.IncrementRecordCount();
            hw.WriteMask([0x02]);
            hw.WriteInt32(2);

            hw.IncrementRecordCount();
            hw.WriteMask([0x00]);
            hw.WriteInt32(3);
            hw.WriteUri(absUri);
            hw.Flush();
        }

        foreach (bool quoteAll in new[] { false, true })
        {
            var opts = new HtbToCsvOptions { QuoteAll = quoteAll, NewLine = "\n" };
            string expected = quoteAll
                ? "\"Id\",\"Link\"\n\"1\",\"\"\n\"2\",\n\"3\",\"https://example.com/path?x=1\"\n"
                : "Id,Link\n1,\"\"\n2,\n3,https://example.com/path?x=1\n";

            multiHtb.Position = 0;
            using var syncCsv = new StringWriter();
            HtbToCsvConverter.Convert(multiHtb, syncCsv, opts);
            Assert.Equal(expected, syncCsv.ToString());

            multiHtb.Position = 0;
            using var asyncCsv = new StringWriter();
            await HtbToCsvConverter.ConvertAsync(multiHtb, asyncCsv, opts, ct);
            Assert.Equal(expected, asyncCsv.ToString());

            using var rtHtb = new MemoryStream();
            CsvToHtbConverter.Convert(syncCsv.ToString(), rtHtb, multiSchema);
            rtHtb.Position = 0;
            using var dr = global::HeroParser.Htb.CreateDataReader(rtHtb);

            Assert.True(dr.Read());
            Assert.False(dr.IsDBNull(1));
            Assert.Equal(emptyRelUri, dr.GetValue(1));

            Assert.True(dr.Read());
            Assert.True(dr.IsDBNull(1));

            Assert.True(dr.Read());
            Assert.Equal(absUri, dr.GetValue(1));
            Assert.False(dr.Read());
        }

        // 2. Single-column non-nullable Uri round-trips empty relative URI without error.
        var nonNullUriSchema = new HtbSchema([new HtbColumn("Link", HtbDataType.Uri, isNullable: false)]);
        using var nonNullHtb = new MemoryStream();
        using (var hw = new HeroParser.Htbs.Writing.HtbStreamWriter(nonNullHtb, leaveOpen: true))
        {
            hw.WriteHeader(nonNullUriSchema);
            hw.IncrementRecordCount();
            hw.WriteMask([0x00]);
            hw.WriteUri(emptyRelUri);
            hw.Flush();
        }

        nonNullHtb.Position = 0;
        using var singleCsv = new StringWriter();
        HtbToCsvConverter.Convert(nonNullHtb, singleCsv, new HtbToCsvOptions { NewLine = "\n" });
        Assert.Equal("Link\n\"\"\n", singleCsv.ToString());

        using var singleRtHtb = new MemoryStream();
        CsvToHtbConverter.Convert(singleCsv.ToString(), singleRtHtb, nonNullUriSchema);
        singleRtHtb.Position = 0;
        using (var singleDr = global::HeroParser.Htb.CreateDataReader(singleRtHtb))
        {
            Assert.True(singleDr.Read());
            Assert.Equal(emptyRelUri, singleDr.GetValue(0));
            Assert.False(singleDr.Read());
        }

        // 3. Single-column nullable Uri containing null rejects lossy CSV conversion in both sync and async.
        var nullableSingleSchema = new HtbSchema([new HtbColumn("Link", HtbDataType.Uri, isNullable: true)]);
        using var singleWithNullHtb = new MemoryStream();
        using (var hw = new HeroParser.Htbs.Writing.HtbStreamWriter(singleWithNullHtb, leaveOpen: true))
        {
            hw.WriteHeader(nullableSingleSchema);
            hw.IncrementRecordCount();
            hw.WriteMask([0x01]);
            hw.Flush();
        }

        singleWithNullHtb.Position = 0;
        using var failSyncOut = new StringWriter();
        var syncEx = Assert.Throws<HtbException>(() =>
            HtbToCsvConverter.Convert(singleWithNullHtb, failSyncOut, new HtbToCsvOptions { NewLine = "\n" }));
        Assert.Equal(HtbErrorCode.SerializationError, syncEx.ErrorCode);

        singleWithNullHtb.Position = 0;
        using var failAsyncOut = new StringWriter();
        var asyncEx = await Assert.ThrowsAsync<HtbException>(() =>
            HtbToCsvConverter.ConvertAsync(singleWithNullHtb, failAsyncOut, new HtbToCsvOptions { NewLine = "\n" }, ct));
        Assert.Equal(HtbErrorCode.SerializationError, asyncEx.ErrorCode);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Excel_JanFeb1900DatesAndSubnormalDoubles_WrittenAsTextAndRoundTrip_SyncAndAsyncAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var expectedRecord = new Excel1900AndSubnormalRecord
        {
            JanDate = new DateOnly(1900, 1, 1),
            FebDate = new DateOnly(1900, 2, 28),
            JanDateTime = new DateTime(1900, 1, 1, 12, 30, 0),
            MarDateTime = new DateTime(1900, 3, 1, 0, 0, 0),
            TinyPositive = 1e-310,
            TinyNegative = -double.Epsilon,
            Zero = 0.0,
            Normal = 1.5
        };

        static void AssertSheetXmlAndRoundTrip(byte[] xlsxBytes, Excel1900AndSubnormalRecord expected)
        {
            using (var zipMs = new MemoryStream(xlsxBytes))
            using (var zip = new ZipArchive(zipMs, ZipArchiveMode.Read))
            {
                using var sheetReader = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
                string sheetXml = sheetReader.ReadToEnd();
                // 1900-03-01 is the first valid serial date (61); Jan/Feb 1900 must not be written as OA serials 1/2/59/60.
                Assert.Contains("r=\"D2\" s=\"1\"><v>61</v>", sheetXml, StringComparison.Ordinal);
                Assert.DoesNotContain("r=\"A2\" s=\"1\"", sheetXml, StringComparison.Ordinal);
                Assert.DoesNotContain("r=\"B2\" s=\"1\"", sheetXml, StringComparison.Ordinal);
                Assert.DoesNotContain("r=\"C2\" s=\"1\"", sheetXml, StringComparison.Ordinal);
                // Subnormal doubles must be written via shared/inline strings (t="s" or t="inlineStr"), not raw <v> subnormal numbers.
                Assert.Contains("r=\"E2\" t=\"s\">", sheetXml, StringComparison.Ordinal);
                Assert.Contains("r=\"F2\" t=\"s\">", sheetXml, StringComparison.Ordinal);
                // Normal 0.0 and 1.5 stay numeric cells without t="s".
                Assert.Contains("r=\"G2\" ><v>0</v>", sheetXml, StringComparison.Ordinal);
                Assert.Contains("r=\"H2\" ><v>1.5</v>", sheetXml, StringComparison.Ordinal);
            }

            using var readMs = new MemoryStream(xlsxBytes);
            var actual = Assert.Single(HeroParser.Excel.Read<Excel1900AndSubnormalRecord>().FromStream(readMs));
            Assert.Equal(expected.JanDate, actual.JanDate);
            Assert.Equal(expected.FebDate, actual.FebDate);
            Assert.Equal(expected.JanDateTime, actual.JanDateTime);
            Assert.Equal(expected.MarDateTime, actual.MarDateTime);
            Assert.Equal(expected.TinyPositive, actual.TinyPositive);
            Assert.Equal(expected.TinyNegative, actual.TinyNegative);
            Assert.Equal(expected.Zero, actual.Zero);
            Assert.Equal(expected.Normal, actual.Normal);
        }

        // 1. Typed generated writer
        AssertSheetXmlAndRoundTrip(
            HeroParser.Excel.Write<Excel1900AndSubnormalRecord>().ToBytes([expectedRecord]),
            expectedRecord);

        // 2. Reflection writer via fluent WithMap
        var reflRecord = new Excel1900AndSubnormalReflectionRecord
        {
            JanDate = expectedRecord.JanDate,
            FebDate = expectedRecord.FebDate,
            JanDateTime = expectedRecord.JanDateTime,
            MarDateTime = expectedRecord.MarDateTime,
            TinyPositive = expectedRecord.TinyPositive,
            TinyNegative = expectedRecord.TinyNegative,
            Zero = expectedRecord.Zero,
            Normal = expectedRecord.Normal
        };
        AssertSheetXmlAndRoundTrip(
            HeroParser.Excel.Write<Excel1900AndSubnormalReflectionRecord>().ToBytes([reflRecord]),
            expectedRecord);

        // 3. DataReader sync + async direct AND staged (OnSerializeError != null), covering DateOnly(object), DateTime, Double
        ExcelWriteOptions[] writerOptionModes =
        [
            new ExcelWriteOptions(),
            new ExcelWriteOptions { OnSerializeError = _ => ExcelSerializeErrorAction.Throw }
        ];

        foreach (var opts in writerOptionModes)
        {
            {
                using var drSync = new Async1900AndSubnormalDbDataReader(expectedRecord);
                AssertSheetXmlAndRoundTrip(HeroParser.Excel.WriteFromDataReaderToBytes(drSync, opts), expectedRecord);
            }

            {
                await using var drAsync = new Async1900AndSubnormalDbDataReader(expectedRecord);
                await using var asyncMs = new MemoryStream();
                await HeroParser.Excel.WriteFromDataReaderAsync(drAsync, asyncMs, opts, leaveOpen: true, cancellationToken: ct);
                AssertSheetXmlAndRoundTrip(asyncMs.ToArray(), expectedRecord);
            }
        }
    }

    private sealed class Async1900AndSubnormalDbDataReader(Excel1900AndSubnormalRecord record) : DbDataReader
    {
        private int row = -1;
        public override int FieldCount => 8;
        public override bool HasRows => true;
        public override bool IsClosed => false;
        public override int Depth => 0;
        public override int RecordsAffected => -1;
        public override object this[int ordinal] => GetValue(ordinal);
        public override object this[string name] => GetValue(GetOrdinal(name));

        public override string GetName(int ordinal) => ordinal switch
        {
            0 => nameof(Excel1900AndSubnormalRecord.JanDate),
            1 => nameof(Excel1900AndSubnormalRecord.FebDate),
            2 => nameof(Excel1900AndSubnormalRecord.JanDateTime),
            3 => nameof(Excel1900AndSubnormalRecord.MarDateTime),
            4 => nameof(Excel1900AndSubnormalRecord.TinyPositive),
            5 => nameof(Excel1900AndSubnormalRecord.TinyNegative),
            6 => nameof(Excel1900AndSubnormalRecord.Zero),
            7 => nameof(Excel1900AndSubnormalRecord.Normal),
            _ => throw new IndexOutOfRangeException()
        };

        public override int GetOrdinal(string name) => name switch
        {
            nameof(Excel1900AndSubnormalRecord.JanDate) => 0,
            nameof(Excel1900AndSubnormalRecord.FebDate) => 1,
            nameof(Excel1900AndSubnormalRecord.JanDateTime) => 2,
            nameof(Excel1900AndSubnormalRecord.MarDateTime) => 3,
            nameof(Excel1900AndSubnormalRecord.TinyPositive) => 4,
            nameof(Excel1900AndSubnormalRecord.TinyNegative) => 5,
            nameof(Excel1900AndSubnormalRecord.Zero) => 6,
            nameof(Excel1900AndSubnormalRecord.Normal) => 7,
            _ => -1
        };

        public override Type GetFieldType(int ordinal) => ordinal switch
        {
            0 or 1 => typeof(DateOnly),
            2 or 3 => typeof(DateTime),
            4 or 5 or 6 or 7 => typeof(double),
            _ => throw new IndexOutOfRangeException()
        };

        public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;
        public override bool Read() => ++row == 0;
        public override Task<bool> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(++row == 0);
        public override bool IsDBNull(int ordinal) => false;
        public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken) => Task.FromResult(false);

        public override object GetValue(int ordinal) => ordinal switch
        {
            0 => record.JanDate,
            1 => record.FebDate,
            2 => record.JanDateTime,
            3 => record.MarDateTime,
            4 => record.TinyPositive,
            5 => record.TinyNegative,
            6 => record.Zero,
            7 => record.Normal,
            _ => throw new IndexOutOfRangeException()
        };

        public override Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken)
            => Task.FromResult((T)GetValue(ordinal));

        public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);
        public override double GetDouble(int ordinal) => (double)GetValue(ordinal);
        public override bool GetBoolean(int ordinal) => throw new NotSupportedException();
        public override byte GetByte(int ordinal) => throw new NotSupportedException();
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override char GetChar(int ordinal) => throw new NotSupportedException();
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override decimal GetDecimal(int ordinal) => throw new NotSupportedException();
        public override float GetFloat(int ordinal) => throw new NotSupportedException();
        public override Guid GetGuid(int ordinal) => throw new NotSupportedException();
        public override short GetInt16(int ordinal) => throw new NotSupportedException();
        public override int GetInt32(int ordinal) => throw new NotSupportedException();
        public override long GetInt64(int ordinal) => throw new NotSupportedException();
        public override string GetString(int ordinal) => throw new NotSupportedException();
        public override int GetValues(object[] values) => throw new NotSupportedException();
        public override bool NextResult() => false;
        public override IEnumerator GetEnumerator() => new DbEnumerator(this, closeReader: false);
    }

    private sealed class Excel1900AndSubnormalReflectionRecord
    {
        public DateOnly JanDate { get; set; }
        public DateOnly FebDate { get; set; }
        public DateTime JanDateTime { get; set; }
        public DateTime MarDateTime { get; set; }
        public double TinyPositive { get; set; }
        public double TinyNegative { get; set; }
        public double Zero { get; set; }
        public double Normal { get; set; }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task WriteFromDataReaderToFileAsync_PreCanceledToken_DoesNotTruncateExistingFile_AcrossAllFiveFormatsAsync()
    {
        const string SENTINEL = "KEEP-ME-INTACT";
        using var canceledCts = new CancellationTokenSource();
        await canceledCts.CancelAsync();
        CancellationToken preCanceled = canceledCts.Token;

        string dir = Path.Combine(Path.GetTempPath(), $"heroparser-cancel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            await using var reader = new Async1900AndSubnormalDbDataReader(new Excel1900AndSubnormalRecord());

            string[] paths =
            [
                Path.Combine(dir, "out.htb"),
                Path.Combine(dir, "out.csv"),
                Path.Combine(dir, "out.xlsx"),
                Path.Combine(dir, "out.jsonl"),
                Path.Combine(dir, "out.fw")
            ];
            foreach (string path in paths)
                await File.WriteAllTextAsync(path, SENTINEL, TestContext.Current.CancellationToken);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await global::HeroParser.Htb.WriteFromDataReaderToFileAsync(paths[0], reader, cancellationToken: preCanceled));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await Csv.WriteFromDataReaderToFileAsync(paths[1], reader, cancellationToken: preCanceled));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await HeroParser.Excel.WriteFromDataReaderToFileAsync(paths[2], reader, cancellationToken: preCanceled));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await Jsonl.WriteFromDataReaderToFileAsync(paths[3], reader, cancellationToken: preCanceled));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await FixedWidth.WriteFromDataReaderToFileAsync(
                    paths[4],
                    reader,
                    [new HeroParser.Conversion.FixedWidthFieldDefinition("JanDate", 12)],
                    cancellationToken: preCanceled));

            foreach (string path in paths)
                Assert.Equal(SENTINEL, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Excel_DateTimeWithFractionalSeconds_PreservesSubSecondTicks_AcrossAllExportersAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var expected = new ExcelFractionalDateTimeRecord
        {
            Precise = new DateTime(2026, 11, 27, 8, 30, 0).AddTicks(1_234_567),
            Whole = new DateTime(2026, 11, 27, 8, 30, 0)
        };

        static void AssertFractionalPreserved(byte[] xlsxBytes, ExcelFractionalDateTimeRecord expected)
        {
            using (var zipMs = new MemoryStream(xlsxBytes))
            using (var zip = new ZipArchive(zipMs, ZipArchiveMode.Read))
            {
                using var sheetReader = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
                string sheetXml = sheetReader.ReadToEnd();
                Assert.Contains("r=\"A2\" t=\"s\">", sheetXml, StringComparison.Ordinal);
                Assert.Contains("r=\"B2\" s=\"1\"><v>", sheetXml, StringComparison.Ordinal);
            }

            using var readMs = new MemoryStream(xlsxBytes);
            var actual = Assert.Single(HeroParser.Excel.Read<ExcelFractionalDateTimeRecord>().FromStream(readMs));
            Assert.Equal(expected.Precise, actual.Precise);
            Assert.Equal(expected.Whole, actual.Whole);
        }

        AssertFractionalPreserved(
            HeroParser.Excel.Write<ExcelFractionalDateTimeRecord>().ToBytes([expected]),
            expected);

        var reflRecord = new ExcelFractionalDateTimeReflectionRecord
        {
            Precise = expected.Precise,
            Whole = expected.Whole
        };
        AssertFractionalPreserved(
            HeroParser.Excel.Write<ExcelFractionalDateTimeReflectionRecord>().ToBytes([reflRecord]),
            expected);

        ExcelWriteOptions[] optionModes =
        [
            new ExcelWriteOptions(),
            new ExcelWriteOptions { OnSerializeError = _ => ExcelSerializeErrorAction.Throw }
        ];

        foreach (var opts in optionModes)
        {
            using (var drSync = new FractionalDateTimeDbDataReader(expected))
            {
                AssertFractionalPreserved(HeroParser.Excel.WriteFromDataReaderToBytes(drSync, opts), expected);
            }

            {
                await using var drAsync = new FractionalDateTimeDbDataReader(expected);
                await using var asyncMs = new MemoryStream();
                await HeroParser.Excel.WriteFromDataReaderAsync(drAsync, asyncMs, opts, leaveOpen: true, cancellationToken: ct);
                AssertFractionalPreserved(asyncMs.ToArray(), expected);
            }
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task HtbToCsvConverter_UriWithTrailingNonBreakingSpace_PreservesExactUri_SyncAndAsyncAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var relNbsp = new Uri("/a\u00A0", UriKind.Relative);
        var absNbsp = new Uri("https://example.com/a\u00A0", UriKind.Absolute);
        var schema = new HtbSchema(
        [
            new HtbColumn("Id", HtbDataType.Int32, isNullable: false),
            new HtbColumn("Rel", HtbDataType.Uri, isNullable: false),
            new HtbColumn("Abs", HtbDataType.Uri, isNullable: false)
        ]);

        using var htbStream = new MemoryStream();
        using (var hw = new HeroParser.Htbs.Writing.HtbStreamWriter(htbStream, leaveOpen: true))
        {
            hw.WriteHeader(schema);
            hw.IncrementRecordCount();
            hw.WriteMask([0x00]);
            hw.WriteInt32(1);
            hw.WriteUri(relNbsp);
            hw.WriteUri(absNbsp);
            hw.Flush();
        }

        htbStream.Position = 0;
        using var syncCsv = new StringWriter();
        HtbToCsvConverter.Convert(htbStream, syncCsv, new HtbToCsvOptions { NewLine = "\n" });

        htbStream.Position = 0;
        using var asyncCsv = new StringWriter();
        await HtbToCsvConverter.ConvertAsync(htbStream, asyncCsv, new HtbToCsvOptions { NewLine = "\n" }, ct);
        Assert.Equal(syncCsv.ToString(), asyncCsv.ToString());

        using var rtHtb = new MemoryStream();
        CsvToHtbConverter.Convert(syncCsv.ToString(), rtHtb, schema);
        rtHtb.Position = 0;
        using var dr = global::HeroParser.Htb.CreateDataReader(rtHtb);
        Assert.True(dr.Read());
        Assert.Equal(relNbsp.OriginalString, Assert.IsType<Uri>(dr.GetValue(1)).OriginalString);
        Assert.Equal(absNbsp.OriginalString, Assert.IsType<Uri>(dr.GetValue(2)).OriginalString);
        Assert.False(dr.Read());
    }

    private sealed class ExcelFractionalDateTimeReflectionRecord
    {
        public DateTime Precise { get; set; }
        public DateTime Whole { get; set; }
    }

    private sealed class FractionalDateTimeDbDataReader(ExcelFractionalDateTimeRecord record) : DbDataReader
    {
        private int row = -1;
        public override int FieldCount => 2;
        public override bool HasRows => true;
        public override bool IsClosed => false;
        public override int Depth => 0;
        public override int RecordsAffected => -1;
        public override object this[int ordinal] => GetValue(ordinal);
        public override object this[string name] => GetValue(GetOrdinal(name));

        public override string GetName(int ordinal) => ordinal == 0 ? nameof(ExcelFractionalDateTimeRecord.Precise) : nameof(ExcelFractionalDateTimeRecord.Whole);
        public override int GetOrdinal(string name) => name == nameof(ExcelFractionalDateTimeRecord.Precise) ? 0 : (name == nameof(ExcelFractionalDateTimeRecord.Whole) ? 1 : -1);
        public override Type GetFieldType(int ordinal) => typeof(DateTime);
        public override string GetDataTypeName(int ordinal) => nameof(DateTime);
        public override bool Read() => ++row == 0;
        public override Task<bool> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(++row == 0);
        public override bool IsDBNull(int ordinal) => false;
        public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken) => Task.FromResult(false);
        public override object GetValue(int ordinal) => ordinal == 0 ? record.Precise : record.Whole;
        public override Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken) => Task.FromResult((T)GetValue(ordinal));
        public override DateTime GetDateTime(int ordinal) => ordinal == 0 ? record.Precise : record.Whole;
        public override bool GetBoolean(int ordinal) => throw new NotSupportedException();
        public override byte GetByte(int ordinal) => throw new NotSupportedException();
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override char GetChar(int ordinal) => throw new NotSupportedException();
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override decimal GetDecimal(int ordinal) => throw new NotSupportedException();
        public override double GetDouble(int ordinal) => throw new NotSupportedException();
        public override float GetFloat(int ordinal) => throw new NotSupportedException();
        public override Guid GetGuid(int ordinal) => throw new NotSupportedException();
        public override short GetInt16(int ordinal) => throw new NotSupportedException();
        public override int GetInt32(int ordinal) => throw new NotSupportedException();
        public override long GetInt64(int ordinal) => throw new NotSupportedException();
        public override string GetString(int ordinal) => throw new NotSupportedException();
        public override int GetValues(object[] values) => throw new NotSupportedException();
        public override bool NextResult() => false;
        public override IEnumerator GetEnumerator() => new DbEnumerator(this, closeReader: false);
    }

    private sealed class ReflectionCsvCharRow
    {
        public char Symbol { get; set; }
        public string? Note { get; set; }
    }
}

[GenerateBinder]
public sealed class AsyncExportVerifyRecord
{
    public int Id { get; set; }
    public string? Text { get; set; }
}

[GenerateBinder]
public sealed class GeneratedCsvBase64PlusRow
{
    public byte[]? Payload { get; set; }
}

[GenerateBinder]
public sealed class GeneratedExcelCharRow
{
    public char Symbol { get; set; }
}

[GenerateBinder]
public sealed class HtbIntIdRecord
{
    public int Id { get; set; }
}

[GenerateBinder]
public sealed class HtbShortIdRecord
{
    public short Id { get; set; }
}

[GenerateBinder]
public sealed class GeneratedExcelBase64PlusRow
{
    public byte[]? Payload { get; set; }
    public string? Text { get; set; }
}

[GenerateBinder]
public sealed class GeneratedExcel15DigitRow
{
    public long Max15 { get; set; }
    public long Over15 { get; set; }
    public ulong Over15U { get; set; }
}

[GenerateBinder]
public sealed class GeneratedExcelTimeOnlyExportRow
{
    public TimeOnly TimeVal { get; set; }
}

[GenerateBinder]
public sealed class HtbFloatRangeUnvalidatedWriteRecord
{
    public float Score { get; set; }
}

[GenerateBinder]
public sealed class HtbFloatRangeValidatedRecord
{
    [Validate(RangeMax = 0.1)]
    public float Score { get; set; }
}

[GenerateBinder]
public sealed class GeneratedCsvCharAndNoteRow
{
    public char Symbol { get; set; }
    public string? Note { get; set; }
}

[GenerateBinder]
public sealed class Excel1900AndSubnormalRecord
{
    public DateOnly JanDate { get; set; }
    public DateOnly FebDate { get; set; }
    public DateTime JanDateTime { get; set; }
    public DateTime MarDateTime { get; set; }
    public double TinyPositive { get; set; }
    public double TinyNegative { get; set; }
    public double Zero { get; set; }
    public double Normal { get; set; }
}

[GenerateBinder]
public sealed class ExcelFractionalDateTimeRecord
{
    public DateTime Precise { get; set; }
    public DateTime Whole { get; set; }
}
