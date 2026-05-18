using System.IO.Pipelines;
using System.Text;
using HeroParser.FixedWidths;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Records;
using HeroParser.SeparatedValues.Writing;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests;

/// <summary>Wave 24: more Csv.Read static overloads, FixedWidth async stream reader, Csv.Write static helpers.</summary>
public class CoveragePushTests24
{
    // ---------- Csv.DeserializeRecords static overloads ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_DeserializeRecords_FromCharString()
    {
        // The char overload of DeserializeRecords (line 127).
        var rows = new List<CoveragePerson>();
        foreach (var r in Csv.DeserializeRecords<CoveragePerson>("Name,Age\nAlice,30\nBob,25\n"))
            rows.Add(r);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_DeserializeRecordsFromBytes()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("Name,Age\nAlice,30\nBob,25\n");
        var rows = new List<CoveragePerson>();
        foreach (var r in Csv.DeserializeRecordsFromBytes<CoveragePerson>(bytes))
            rows.Add(r);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Csv_DeserializeRecordsAsync_FromPipeReader()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("Name,Age\nAlice,30\nBob,25\n");
        using var stream = new MemoryStream(bytes);
        var pipe = PipeReader.Create(stream);
        var rows = new List<CoveragePerson>();
        await foreach (var r in Csv.DeserializeRecordsAsync<CoveragePerson>(pipe, cancellationToken: TestContext.Current.CancellationToken))
            rows.Add(r);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Csv_DeserializeRecordsAsync_FromPipeReader_WithRecordOptions()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("# header\nName,Age\nAlice,30\n");
        using var stream = new MemoryStream(bytes);
        var pipe = PipeReader.Create(stream);
        var rows = new List<CoveragePerson>();
        await foreach (var r in Csv.DeserializeRecordsAsync<CoveragePerson>(
            pipe,
            recordOptions: new CsvRecordOptions { SkipRows = 1 },
            cancellationToken: TestContext.Current.CancellationToken))
        {
            rows.Add(r);
        }
        Assert.Single(rows);
    }

    // ---------- Csv.Write static helpers ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_SerializeRecords_Static()
    {
        var rows = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        string csv = Csv.SerializeRecords(rows);
        Assert.Contains("Alice", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_CreateWriter_FromTextWriter()
    {
        using var sw = new StringWriter();
        using (var writer = Csv.CreateWriter(sw))
        {
            writer.WriteRow(["a", "b"]);
            writer.WriteRow(["1", "2"]);
        }
        Assert.Contains("a,b", sw.ToString());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_CreateFileWriter()
    {
        string path = Path.GetTempFileName();
        try
        {
            using (var writer = Csv.CreateFileWriter(path))
            {
                writer.WriteRow(["a", "b"]);
                writer.WriteRow(["1", "2"]);
            }
            string content = File.ReadAllText(path);
            Assert.Contains("a,b", content);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_CreateStreamWriter()
    {
        using var ms = new MemoryStream();
        using (var writer = Csv.CreateStreamWriter(ms))
        {
            writer.WriteRow(["a", "b"]);
            writer.WriteRow(["1", "2"]);
        }
        string content = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("a,b", content);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Csv_WriteToTextAsync_Static()
    {
        static async IAsyncEnumerable<CoveragePerson> Source()
        {
            yield return new CoveragePerson { Name = "Alice", Age = 30 };
            await Task.Yield();
        }
        string csv = await Csv.WriteToTextAsync(Source(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains("Alice", csv);
    }

    // ---------- FixedWidthAsyncStreamReader extras ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task FixedWidth_AsyncStreamReader_Direct()
    {
        string text = "row1\nrow2\nrow3\n";
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(text));
        await using var reader = FixedWidth.CreateAsyncStreamReader(ms);
        int n = 0;
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken)) n++;
        Assert.Equal(3, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task FixedWidth_AsyncStreamReader_LargeBuffer()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 500; i++) sb.AppendLine($"record{i:D4}");
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        await using var reader = FixedWidth.CreateAsyncStreamReader(ms, bufferSize: 64);
        int n = 0;
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken)) n++;
        Assert.Equal(500, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task FixedWidth_AsyncStreamReader_FromFile()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "row1\nrow2\nrow3\n");
            await using var reader = FixedWidth.CreateAsyncStreamReader(path);
            int n = 0;
            while (await reader.MoveNextAsync(TestContext.Current.CancellationToken)) n++;
            Assert.Equal(3, n);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task FixedWidth_AsyncStreamReader_BomDetection()
    {
        byte[] bom = [0xEF, 0xBB, 0xBF];
        byte[] data = Encoding.UTF8.GetBytes("row1\nrow2\n");
        byte[] combined = [.. bom, .. data];
        using var ms = new MemoryStream(combined);
        await using var reader = FixedWidth.CreateAsyncStreamReader(ms);
        int n = 0;
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken)) n++;
        Assert.Equal(2, n);
    }

    // ---------- FixedWidth.Read static methods ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_ReadFromText_Static()
    {
        var reader = FixedWidth.ReadFromText("row1\nrow2\nrow3\n");
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(3, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_ReadFromByteSpan_Static()
    {
        byte[] bytes = "row1\nrow2\nrow3\n"u8.ToArray();
        var reader = FixedWidth.ReadFromByteSpan(bytes);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(3, n);
    }

    // ---------- CsvAsyncStreamReader various read paths ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task CsvAsyncStreamReader_FromFile_Static()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "a,b\n1,2\n3,4\n");
            using var fs = File.OpenRead(path);
            await using var reader = Csv.CreateAsyncStreamReader(fs);
            int n = 0;
            while (await reader.MoveNextAsync(TestContext.Current.CancellationToken)) n++;
            Assert.Equal(3, n);
        }
        finally { File.Delete(path); }
    }

    // ---------- CsvRow extras ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRow_LineNumber_AndColumnCount()
    {
        using var reader = Csv.Read().FromText("a,b,c\n1,2,3\n");
        Assert.True(reader.MoveNext());
        Assert.True(reader.MoveNext());
        var row = reader.Current;
        Assert.True(row.LineNumber > 0);
        Assert.Equal(3, row.ColumnCount);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRow_BytePath_LineNumber()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("a,b\n1,2\n"));
        using var reader = Csv.Read().FromStream(stream, out _);
        Assert.True(reader.MoveNext());
        Assert.True(reader.MoveNext());
        Assert.True(reader.Current.LineNumber > 0);
    }

    // ---------- FixedWidth.PipeReader static methods ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task FixedWidth_ReadFromPipeReaderAsync_WithOptions()
    {
        byte[] bytes = "row1\r\nrow2\r\nrow3\r\n"u8.ToArray();
        using var stream = new MemoryStream(bytes);
        int n = 0;
        await foreach (var _ in FixedWidth.ReadFromPipeReaderAsync(
            PipeReader.Create(stream),
            new FixedWidthReadOptions(),
            cancellationToken: TestContext.Current.CancellationToken))
        {
            n++;
        }
        Assert.Equal(3, n);
    }

    // ---------- ExcelRecordReaderBuilder additional paths ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_Read_TypedWithOptions()
    {
        var src = new[]
        {
            new CoveragePerson { Name = "Alice", Age = 30 },
            new CoveragePerson { Name = "Bob", Age = 25 }
        };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<CoveragePerson>().ToStream(ms, src);
        ms.Position = 0;

        var read = global::HeroParser.Excel.Read<CoveragePerson>()
            .FromSheet(0)
            .FromStream(ms)
            .ToList();
        Assert.Equal(2, read.Count);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_Read_CaseInsensitiveHeaders()
    {
        var src = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<CoveragePerson>().ToStream(ms, src);
        ms.Position = 0;

        // Reading with default case-insensitive headers should work.
        var read = global::HeroParser.Excel.Read<CoveragePerson>().FromStream(ms).ToList();
        Assert.Single(read);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_Read_FromBytes()
    {
        var src = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<CoveragePerson>().ToStream(ms, src);
        byte[] bytes = ms.ToArray();
        using var ms2 = new MemoryStream(bytes);
        var read = global::HeroParser.Excel.Read<CoveragePerson>().FromStream(ms2).ToList();
        Assert.Single(read);
    }

    // ---------- CsvAsyncStreamReader.BytesRead, etc ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task CsvAsyncStreamReader_BytesRead_Tracked()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("a,b\n1,2\n3,4\n");
        using var ms = new MemoryStream(bytes);
        await using var reader = Csv.CreateAsyncStreamReader(ms);
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken)) { }
        Assert.True(reader.BytesRead > 0);
    }

    // ---------- Csv.Read.cs additional static helpers ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_ReadFromText_WithOptions()
    {
        using var reader = Csv.ReadFromText("a;b\n1;2\n", new CsvReadOptions { Delimiter = ';' });
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(2, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_ReadFromCharSpan_WithOptions()
    {
        using var reader = Csv.ReadFromCharSpan("a;b\n1;2\n".AsSpan(), new CsvReadOptions { Delimiter = ';' });
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(2, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_ReadFromByteSpan_WithOptions()
    {
        byte[] bytes = "a;b\n1;2\n"u8.ToArray();
        using var reader = Csv.ReadFromByteSpan(bytes, new CsvReadOptions { Delimiter = ';' });
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(2, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_DeserializeRecords_WithOptions()
    {
        string csv = "Name;Age\nAlice;30\n";
        var rows = new List<CoveragePerson>();
        foreach (var r in Csv.DeserializeRecords<CoveragePerson>(
            csv,
            recordOptions: null,
            parserOptions: new CsvReadOptions { Delimiter = ';' }))
        {
            rows.Add(r);
        }
        Assert.Single(rows);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_DeserializeRecordsFromBytes_WithOptions()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("Name;Age\nAlice;30\n");
        var rows = new List<CoveragePerson>();
        foreach (var r in Csv.DeserializeRecordsFromBytes<CoveragePerson>(
            bytes,
            recordOptions: null,
            parserOptions: new CsvReadOptions { Delimiter = ';' }))
        {
            rows.Add(r);
        }
        Assert.Single(rows);
    }
}
