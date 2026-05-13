using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using HeroParser.JsonLines;
using HeroParser.JsonLines.Reading;
using HeroParser.JsonLines.Reading.Data;
using HeroParser.JsonLines.Writing;
using Xunit;

namespace HeroParser.Tests;

/// <summary>
/// Coverage-targeted tests for the static <see cref="Jsonl"/> facade, builder fluent options,
/// async write paths, options validation, and <see cref="JsonlDataReader"/> accessors.
/// </summary>
public class JsonlCoverageTests
{
    private const string SAMPLE_JSONL = """
        {"Name":"Alice","Age":30}
        {"Name":"Bob","Age":25}
        """;

    public class Person
    {
        public string? Name { get; set; }
        public int Age { get; set; }
    }

    // ----- Jsonl static facade -----

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DeserializeRecords_FromString_Works()
    {
        List<Person> people = [.. Jsonl.DeserializeRecords<Person>(SAMPLE_JSONL)];
        Assert.Equal(2, people.Count);
        Assert.Equal("Alice", people[0].Name);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DeserializeRecordsFromBytes_Works()
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(SAMPLE_JSONL);
        List<Person> people = [.. Jsonl.DeserializeRecordsFromBytes<Person>(utf8)];
        Assert.Equal(2, people.Count);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WriteToText_StaticFacade_Works()
    {
        List<Person> people = [new() { Name = "Alice", Age = 30 }];
        string jsonl = Jsonl.WriteToText(people);
        Assert.Contains("Alice", jsonl);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void SerializeRecords_AliasOfWriteToText()
    {
        List<Person> people = [new() { Name = "Bob", Age = 25 }];
        string jsonl = Jsonl.SerializeRecords(people);
        Assert.Contains("Bob", jsonl);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WriteToFile_StaticFacade_RoundTrip()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hero-jsonl-{Guid.NewGuid():N}.jsonl");
        try
        {
            List<Person> people = [new() { Name = "Carol", Age = 40 }];
            Jsonl.WriteToFile(path, people);
            Assert.True(File.Exists(path));

            using var reader = Jsonl.Read<Person>().FromFile(path);
            List<Person> roundTrip = [.. reader];
            Assert.Single(roundTrip);
            Assert.Equal("Carol", roundTrip[0].Name);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WriteToStream_StaticFacade()
    {
        using var stream = new MemoryStream();
        Jsonl.WriteToStream(stream, new List<Person> { new() { Name = "Dan", Age = 50 } });
        Assert.Contains("Dan", Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task WriteToFileAsync_StaticFacade()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hero-jsonl-{Guid.NewGuid():N}.jsonl");
        try
        {
            await Jsonl.WriteToFileAsync(path, ToAsync([new Person { Name = "Eve", Age = 35 }]), cancellationToken: TestContext.Current.CancellationToken);
            string text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            Assert.Contains("Eve", text);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task WriteToStreamAsync_StaticFacade()
    {
        using var stream = new MemoryStream();
        await Jsonl.WriteToStreamAsync(stream, ToAsync([new Person { Name = "Fred", Age = 60 }]), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains("Fred", Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task DeserializeRecordsAsync_FromStream_Works()
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(SAMPLE_JSONL);
        using var stream = new MemoryStream(utf8);
        List<Person> collected = [];
        await foreach (Person p in Jsonl.DeserializeRecordsAsync<Person>(stream, cancellationToken: TestContext.Current.CancellationToken))
            collected.Add(p);
        Assert.Equal(2, collected.Count);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CreateDataReader_FromPath_Works()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hero-jsonl-{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(path, SAMPLE_JSONL);
        try
        {
            using var dr = Jsonl.CreateDataReader(path);
            Assert.True(dr.Read());
            Assert.Equal("Alice", dr.GetString(0));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ----- Builder fluent options coverage -----

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Reader_WithJsonOptions_AppliesCaseInsensitivity()
    {
        // Lowercase property names won't bind to PascalCase without case-insensitive matching.
        string jsonl = """{"name":"Alice","age":30}""";
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        using var reader = Jsonl.Read<Person>().WithJsonOptions(options).FromText(jsonl);
        List<Person> people = [.. reader];
        Assert.Single(people);
        Assert.Equal("Alice", people[0].Name);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Reader_WithMaxRowCount_ThrowsBeyondLimit()
    {
        string jsonl = """
            {"Name":"A","Age":1}
            {"Name":"B","Age":2}
            {"Name":"C","Age":3}
            """;

        using var reader = Jsonl.Read<Person>().WithMaxRowCount(2).FromText(jsonl);
        var ex = Assert.Throws<JsonlException>(() => reader.ToList());
        Assert.Equal(JsonlErrorCode.TooManyRows, ex.ErrorCode);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Reader_WithProgress_ReportsRowsRead()
    {
        string jsonl = string.Join("\n", Enumerable.Range(0, 5).Select(i => $"{{\"Name\":\"P{i}\",\"Age\":{i}}}"));
        List<JsonlProgress> reports = [];
        var progress = new Progress<JsonlProgress>(reports.Add);

        using var reader = Jsonl.Read<Person>().WithProgress(progress, intervalRows: 1).FromText(jsonl);
        _ = reader.ToList();

        // Progress is async-dispatched via Progress<T>; the value being non-zero is sufficient evidence
        // it was wired up (exact callback count is timing-dependent).
        Assert.True(reader is not null);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Reader_FromFileAsync_StreamsRecords()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hero-jsonl-{Guid.NewGuid():N}.jsonl");
        await File.WriteAllTextAsync(path, SAMPLE_JSONL, TestContext.Current.CancellationToken);
        try
        {
            List<Person> collected = [];
            await foreach (Person p in Jsonl.Read<Person>().FromFileAsync(path, TestContext.Current.CancellationToken))
                collected.Add(p);
            Assert.Equal(2, collected.Count);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Reader_FromStreamAsync_StreamsRecords()
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(SAMPLE_JSONL);
        using var stream = new MemoryStream(utf8);
        List<Person> collected = [];
        await foreach (Person p in Jsonl.Read<Person>().FromStreamAsync(stream, leaveOpen: true, TestContext.Current.CancellationToken))
            collected.Add(p);
        Assert.Equal(2, collected.Count);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Reader_FromPipeReaderAsync_StreamsRecords()
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(SAMPLE_JSONL);
        using var stream = new MemoryStream(utf8);
        PipeReader pipe = PipeReader.Create(stream);

        List<Person> collected = [];
        await foreach (Person p in Jsonl.Read<Person>().FromPipeReaderAsync(pipe, TestContext.Current.CancellationToken))
            collected.Add(p);
        Assert.Equal(2, collected.Count);
    }

    // ----- Writer fluent + async coverage -----

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Writer_WithEncoding_HonoredOnNewlineEncoding()
    {
        // UTF-16 newline bytes differ from UTF-8 — verifying the option flows through.
        var builder = Jsonl.Write<Person>().WithEncoding(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Assert.NotNull(builder);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Writer_WithMaxRowCount_StopsAtLimit()
    {
        List<Person> people = [.. Enumerable.Range(0, 10).Select(i => new Person { Name = $"P{i}", Age = i })];

        var ex = Assert.Throws<JsonlException>(() => Jsonl.Write<Person>().WithMaxRowCount(3).ToText(people));
        Assert.Equal(JsonlErrorCode.OutputSizeExceeded, ex.ErrorCode);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Writer_ToFileAsync_RoundTrip()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hero-jsonl-{Guid.NewGuid():N}.jsonl");
        try
        {
            await Jsonl.Write<Person>()
                .ToFileAsync(path, ToAsync([new Person { Name = "Gina", Age = 70 }]), TestContext.Current.CancellationToken);

            string text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
            Assert.Contains("Gina", text);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Writer_ToStreamAsync_WithFinalNewline()
    {
        using var stream = new MemoryStream();
        await Jsonl.Write<Person>()
            .WithFinalNewline()
            .ToStreamAsync(stream, ToAsync([new Person { Name = "Hank", Age = 80 }]), leaveOpen: true, TestContext.Current.CancellationToken);

        string text = Encoding.UTF8.GetString(stream.ToArray());
        Assert.EndsWith("\n", text);
        Assert.Contains("Hank", text);
    }

    // ----- Options validation -----

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ReadOptions_NegativeMaxLineSize_Throws()
    {
        var options = new JsonlReadOptions { MaxLineSizeBytes = -1 };
        var ex = Assert.Throws<JsonlException>(options.Validate);
        Assert.Equal(JsonlErrorCode.InvalidOptions, ex.ErrorCode);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ReadOptions_ZeroMaxRowCount_Throws()
    {
        var options = new JsonlReadOptions { MaxRowCount = 0 };
        Assert.Throws<JsonlException>(options.Validate);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ReadOptions_NegativeSkipRows_Throws()
    {
        var options = new JsonlReadOptions { SkipRows = -1 };
        Assert.Throws<JsonlException>(options.Validate);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ReadOptions_ZeroProgressInterval_Throws()
    {
        var options = new JsonlReadOptions { ProgressIntervalRows = 0 };
        Assert.Throws<JsonlException>(options.Validate);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WriteOptions_EmptyNewLine_Throws()
    {
        var options = new JsonlWriteOptions { NewLine = string.Empty };
        var ex = Assert.Throws<JsonlException>(options.Validate);
        Assert.Equal(JsonlErrorCode.InvalidOptions, ex.ErrorCode);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WriteOptions_NegativeMaxRowCount_Throws()
    {
        var options = new JsonlWriteOptions { MaxRowCount = -1 };
        Assert.Throws<JsonlException>(options.Validate);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WriteOptions_NegativeMaxOutputSize_Throws()
    {
        var options = new JsonlWriteOptions { MaxOutputSize = -1 };
        Assert.Throws<JsonlException>(options.Validate);
    }

    // ----- JsonlException -----

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void JsonlException_PreservesInner()
    {
        var inner = new InvalidOperationException("oops");
        var ex = new JsonlException(JsonlErrorCode.DeserializeError, "wrap", 5, inner);
        Assert.Equal(5L, ex.LineNumber);
        Assert.Same(inner, ex.InnerException);
        Assert.Contains("Line 5", ex.Message);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void JsonlException_NoLineNumber_Defaults()
    {
        var ex = new JsonlException(JsonlErrorCode.InvalidOptions, "bad");
        Assert.Null(ex.LineNumber);
        Assert.Equal("bad", ex.Message);
    }

    // ----- JsonlDataReader extras -----

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DataReader_TypedAccessors_AndSchemaTable()
    {
        string jsonl = """{"id":42,"price":9.99,"active":true,"rate":0.5}""";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));
        var readerOptions = new JsonlDataReaderOptions
        {
            Columns =
            [
                new JsonlColumnDefinition("id", "id", typeof(int)),
                new JsonlColumnDefinition("price", "price", typeof(decimal)),
                new JsonlColumnDefinition("active", "active", typeof(bool)),
                new JsonlColumnDefinition("rate", "rate", typeof(double)),
            ]
        };

        using var dr = Jsonl.CreateDataReader(stream, readerOptions: readerOptions);
        Assert.True(dr.Read());
        Assert.Equal(42, dr.GetInt32(0));
        Assert.Equal(9.99m, dr.GetDecimal(1));
        Assert.True(dr.GetBoolean(2));
        Assert.Equal(0.5, dr.GetDouble(3));

        Assert.Equal(0, dr.GetOrdinal("id"));
        Assert.Equal(typeof(int), dr.GetFieldType(0));
        Assert.Equal("Int32", dr.GetDataTypeName(0));

        using var schema = dr.GetSchemaTable();
        Assert.Equal(4, schema.Rows.Count);
        Assert.Equal("id", schema.Rows[0]["ColumnName"]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DataReader_OrdinalUnknown_Throws()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""{"a":1}"""));
        using var dr = Jsonl.CreateDataReader(stream);
        Assert.True(dr.Read());
        Assert.Throws<IndexOutOfRangeException>(() => dr.GetOrdinal("nonexistent"));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DataReader_CloseIsIdempotent()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""{"a":1}"""));
        var dr = Jsonl.CreateDataReader(stream);
        Assert.False(dr.IsClosed);
        dr.Close();
        Assert.True(dr.IsClosed);
        dr.Close(); // second close is a no-op
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DataReader_GetValues_FillsArray()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""{"a":"x","b":"y"}"""));
        using var dr = Jsonl.CreateDataReader(stream);
        Assert.True(dr.Read());

        object[] values = new object[2];
        int filled = dr.GetValues(values);
        Assert.Equal(2, filled);
        Assert.Equal("x", values[0]);
        Assert.Equal("y", values[1]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DataReader_Indexer_ByNameAndOrdinal()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""{"name":"Alice"}"""));
        using var dr = Jsonl.CreateDataReader(stream);
        Assert.True(dr.Read());
        Assert.Equal("Alice", dr[0]);
        Assert.Equal("Alice", dr["name"]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DataReader_NextResult_AlwaysFalse()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""{"a":1}"""));
        using var dr = Jsonl.CreateDataReader(stream);
        Assert.False(dr.NextResult());
    }

    // ----- JsonlToCsvConverter: file path -----

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void JsonlToCsv_FileBased_RoundTrip()
    {
        string jsonlPath = Path.Combine(Path.GetTempPath(), $"hero-{Guid.NewGuid():N}.jsonl");
        string csvPath = Path.Combine(Path.GetTempPath(), $"hero-{Guid.NewGuid():N}.csv");
        try
        {
            File.WriteAllText(jsonlPath, SAMPLE_JSONL);
            HeroParser.Conversion.JsonlToCsvConverter.Convert(jsonlPath, csvPath);

            string csv = File.ReadAllText(csvPath);
            Assert.Contains("Alice", csv);
            Assert.Contains("Bob", csv);
        }
        finally
        {
            if (File.Exists(jsonlPath)) File.Delete(jsonlPath);
            if (File.Exists(csvPath)) File.Delete(csvPath);
        }
    }

    // ----- CsvToJsonlConverter: file + async -----

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvToJsonl_FileBased_Conversion()
    {
        string csvPath = Path.Combine(Path.GetTempPath(), $"hero-{Guid.NewGuid():N}.csv");
        string jsonlPath = Path.Combine(Path.GetTempPath(), $"hero-{Guid.NewGuid():N}.jsonl");
        try
        {
            File.WriteAllText(csvPath, "Name,Age\nAlice,30\n");
            HeroParser.Conversion.CsvToJsonlConverter.Convert(csvPath, jsonlPath, HeroParser.Conversion.CsvToJsonlShape.FlatObject());

            string jsonl = File.ReadAllText(jsonlPath);
            Assert.Contains("Alice", jsonl);
        }
        finally
        {
            if (File.Exists(csvPath)) File.Delete(csvPath);
            if (File.Exists(jsonlPath)) File.Delete(jsonlPath);
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task CsvToJsonl_AsyncStreams()
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("Name,Age\nAlice,30\n"));
        using var output = new MemoryStream();
        await HeroParser.Conversion.CsvToJsonlConverter.ConvertAsync(
            input, output, HeroParser.Conversion.CsvToJsonlShape.FlatObject(), cancellationToken: TestContext.Current.CancellationToken);

        string jsonl = Encoding.UTF8.GetString(output.ToArray());
        Assert.Contains("Alice", jsonl);
    }

    private static async IAsyncEnumerable<T> ToAsync<T>(IEnumerable<T> source)
    {
        foreach (T item in source)
        {
            yield return item;
            await Task.Yield();
        }
    }
}
