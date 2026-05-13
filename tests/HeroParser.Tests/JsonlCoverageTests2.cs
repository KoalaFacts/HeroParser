using System.IO.Pipelines;
using System.Text;
using System.Text.Json.Serialization;
using HeroParser.JsonLines;
using HeroParser.JsonLines.Reading;
using HeroParser.JsonLines.Reading.Data;
using HeroParser.JsonLines.Writing;
using Xunit;

namespace HeroParser.Tests;

public class JsonlCoveragePerson
{
    public string? Name { get; set; }
    public int Age { get; set; }
}

[JsonSerializable(typeof(JsonlCoveragePerson))]
internal partial class JsonlCoveragePersonContext : JsonSerializerContext { }

/// <summary>
/// Second batch of coverage-targeted tests: JsonTypeInfo paths, OnError invocation,
/// pipe MaxLineSize enforcement, builder fluent methods, and remaining DataReader accessors.
/// </summary>
public class JsonlCoverageTests2
{

    // ----- JsonTypeInfo (AOT) paths -----

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Read_WithTypeInfo_Builder()
    {
        string jsonl = """{"Name":"Alice","Age":30}""";
        using var reader = Jsonl.Read<JsonlCoveragePerson>().WithTypeInfo(JsonlCoveragePersonContext.Default.JsonlCoveragePerson).FromText(jsonl);
        List<JsonlCoveragePerson> people = [.. reader];
        Assert.Single(people);
        Assert.Equal("Alice", people[0].Name);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DeserializeRecords_WithTypeInfo_FromString()
    {
        string jsonl = "{\"Name\":\"Bob\",\"Age\":25}";
        List<JsonlCoveragePerson> people = [.. Jsonl.DeserializeRecords(jsonl, JsonlCoveragePersonContext.Default.JsonlCoveragePerson)];
        Assert.Single(people);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DeserializeRecordsFromBytes_WithTypeInfo()
    {
        byte[] utf8 = Encoding.UTF8.GetBytes("{\"Name\":\"Carol\",\"Age\":40}");
        List<JsonlCoveragePerson> people = [.. Jsonl.DeserializeRecordsFromBytes(utf8, JsonlCoveragePersonContext.Default.JsonlCoveragePerson)];
        Assert.Single(people);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task DeserializeRecordsAsync_WithTypeInfo_FromStream()
    {
        byte[] utf8 = Encoding.UTF8.GetBytes("{\"Name\":\"A\",\"Age\":1}\n{\"Name\":\"B\",\"Age\":2}\n");
        using var stream = new MemoryStream(utf8);
        List<JsonlCoveragePerson> collected = [];
        await foreach (JsonlCoveragePerson p in Jsonl.DeserializeRecordsAsync(stream, JsonlCoveragePersonContext.Default.JsonlCoveragePerson, cancellationToken: TestContext.Current.CancellationToken))
            collected.Add(p);
        Assert.Equal(2, collected.Count);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task DeserializeRecordsAsync_WithTypeInfo_FromPipeReader()
    {
        byte[] utf8 = Encoding.UTF8.GetBytes("{\"Name\":\"A\",\"Age\":1}\n");
        using var stream = new MemoryStream(utf8);
        PipeReader pipe = PipeReader.Create(stream);
        List<JsonlCoveragePerson> collected = [];
        await foreach (JsonlCoveragePerson p in Jsonl.DeserializeRecordsAsync(pipe, JsonlCoveragePersonContext.Default.JsonlCoveragePerson, cancellationToken: TestContext.Current.CancellationToken))
            collected.Add(p);
        Assert.Single(collected);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void WriteToText_WithTypeInfo()
    {
        string jsonl = Jsonl.WriteToText([new JsonlCoveragePerson { Name = "Dan", Age = 50 }], JsonlCoveragePersonContext.Default.JsonlCoveragePerson);
        Assert.Contains("Dan", jsonl);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Writer_WithTypeInfo_Builder()
    {
        string jsonl = Jsonl.Write<JsonlCoveragePerson>()
            .WithTypeInfo(JsonlCoveragePersonContext.Default.JsonlCoveragePerson)
            .ToText([new JsonlCoveragePerson { Name = "Eve", Age = 35 }]);
        Assert.Contains("Eve", jsonl);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Writer_TypeInfo_ToStreamAsync()
    {
        using var stream = new MemoryStream();
        await Jsonl.Write<JsonlCoveragePerson>()
            .WithTypeInfo(JsonlCoveragePersonContext.Default.JsonlCoveragePerson)
            .ToStreamAsync(stream, ToAsync([new JsonlCoveragePerson { Name = "Fred", Age = 60 }]), leaveOpen: true, TestContext.Current.CancellationToken);
        Assert.Contains("Fred", Encoding.UTF8.GetString(stream.ToArray()));
    }

    // ----- Writer OnError handler -----

    public class Unwritable
    {
        public string? Name { get; set; }
        // A custom getter that throws — forces JsonSerializer.Serialize to fail.
        public string Boom => throw new InvalidOperationException("kaboom");
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Writer_OnError_SkipRecord_SwallowsFailure()
    {
        int errors = 0;
        string jsonl = Jsonl.Write<Unwritable>()
            .OnError((_, _) => { errors++; return JsonlSerializeErrorAction.SkipRecord; })
            .ToText([new Unwritable { Name = "x" }]);

        Assert.Equal(1, errors);
        Assert.Equal(string.Empty, jsonl);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Writer_OnError_Throw_RaisesJsonlException()
    {
        Assert.Throws<JsonlException>(() => Jsonl.Write<Unwritable>()
            .OnError((_, _) => JsonlSerializeErrorAction.Throw)
            .ToText([new Unwritable { Name = "x" }]));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Writer_NoOnError_PropagatesFailure()
    {
        Assert.Throws<JsonlException>(() => Jsonl.Write<Unwritable>().ToText([new Unwritable { Name = "x" }]));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void SerializeErrorContext_Defaults()
    {
        // Exercise the small record-struct so its synthesized members are covered.
        var ctx = new JsonlSerializeErrorContext { RecordIndex = 3, SourceType = typeof(JsonlCoveragePerson) };
        Assert.Equal(3L, ctx.RecordIndex);
        Assert.Equal(typeof(JsonlCoveragePerson), ctx.SourceType);
    }

    // ----- Pipe MaxLineSize enforcement -----

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Pipe_MaxLineSize_Throws()
    {
        byte[] data = Encoding.UTF8.GetBytes("{\"Name\":\"Alice\",\"Age\":30}\n");
        using var stream = new MemoryStream(data);

        await Assert.ThrowsAsync<JsonlException>(async () =>
        {
            await foreach (JsonlLine line in Jsonl.ReadLinesAsync(
                PipeReader.Create(stream),
                new JsonlReadOptions { MaxLineSizeBytes = 5 },
                cancellationToken: TestContext.Current.CancellationToken))
            {
                _ = line;
            }
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Pipe_EmptyStream_YieldsNoLines()
    {
        using var stream = new MemoryStream();
        int count = 0;
        await foreach (var _ in Jsonl.ReadLinesAsync(PipeReader.Create(stream), cancellationToken: TestContext.Current.CancellationToken))
            count++;
        Assert.Equal(0, count);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Pipe_NoFinalNewline_StillYieldsLastLine()
    {
        byte[] data = Encoding.UTF8.GetBytes("{\"Name\":\"Alice\",\"Age\":30}");
        using var stream = new MemoryStream(data);
        List<JsonlLine> lines = [];
        await foreach (JsonlLine line in Jsonl.ReadLinesAsync(PipeReader.Create(stream), cancellationToken: TestContext.Current.CancellationToken))
            lines.Add(line);
        Assert.Single(lines);
    }

    // ----- JsonlLineReader: line-size enforcement at EOF -----

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Sync_MaxLineSize_AtEof_Throws()
    {
        string jsonl = "{\"Name\":\"Alice\",\"Age\":30}";
        var ex = Assert.Throws<JsonlException>(() =>
        {
            using var reader = Jsonl.Read<JsonlCoveragePerson>().WithMaxLineSize(5).FromText(jsonl);
            _ = reader.ToList();
        });
        Assert.Equal(JsonlErrorCode.LineTooLong, ex.ErrorCode);
    }

    // ----- JsonlRecordReader: re-enumeration is rejected -----

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Reader_DoubleEnumeration_Throws()
    {
        using var reader = Jsonl.Read<JsonlCoveragePerson>().FromText("{\"Name\":\"A\",\"Age\":1}");
        _ = reader.ToList();
        Assert.Throws<InvalidOperationException>(() => reader.ToList());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Reader_OnError_TypedPath_SkipRecord()
    {
        string jsonl = """
            {"Name":"Alice","Age":30}
            BROKEN
            {"Name":"Bob","Age":25}
            """;

        using var reader = Jsonl.Read<JsonlCoveragePerson>()
            .WithTypeInfo(JsonlCoveragePersonContext.Default.JsonlCoveragePerson)
            .OnError((_, _) => JsonlDeserializeErrorAction.SkipRecord)
            .FromText(jsonl);

        List<JsonlCoveragePerson> people = [.. reader];
        Assert.Equal(2, people.Count);
    }

    // ----- JsonlDataReader: remaining typed accessors -----

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DataReader_RemainingNumericAccessors()
    {
        string guid = Guid.NewGuid().ToString();
        string jsonl = $"{{\"i16\":1,\"i64\":99999999999,\"f\":1.5,\"g\":\"{guid}\",\"b\":7,\"c\":\"A\"}}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));

        var readerOptions = new JsonlDataReaderOptions
        {
            Columns =
            [
                new JsonlColumnDefinition("i16", "i16", typeof(short)),
                new JsonlColumnDefinition("i64", "i64", typeof(long)),
                new JsonlColumnDefinition("f", "f", typeof(float)),
                new JsonlColumnDefinition("g", "g", typeof(Guid)),
                new JsonlColumnDefinition("b", "b", typeof(byte)),
                new JsonlColumnDefinition("c", "c", typeof(char)),
            ]
        };

        using var dr = Jsonl.CreateDataReader(stream, readerOptions: readerOptions);
        Assert.True(dr.Read());
        Assert.Equal((short)1, dr.GetInt16(0));
        Assert.Equal(99999999999L, dr.GetInt64(1));
        Assert.Equal(1.5f, dr.GetFloat(2));
        Assert.Equal(Guid.Parse(guid), dr.GetGuid(3));
        Assert.Equal((byte)7, dr.GetByte(4));
        Assert.Equal('A', dr.GetChar(5));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DataReader_GetDateTime_Works()
    {
        string jsonl = """{"created":"2024-06-01T12:00:00"}""";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));
        var readerOptions = new JsonlDataReaderOptions
        {
            Columns = [new JsonlColumnDefinition("created", "created", typeof(DateTime))]
        };
        using var dr = Jsonl.CreateDataReader(stream, readerOptions: readerOptions);
        Assert.True(dr.Read());
        Assert.Equal(new DateTime(2024, 6, 1, 12, 0, 0), dr.GetDateTime(0));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DataReader_GetBytesAndChars_NotSupported()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""{"a":1}"""));
        using var dr = Jsonl.CreateDataReader(stream);
        Assert.True(dr.Read());
        Assert.Throws<NotSupportedException>(() => dr.GetBytes(0, 0, null, 0, 0));
        Assert.Throws<NotSupportedException>(() => dr.GetChars(0, 0, null, 0, 0));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DataReader_EmptyKeyResolvesToString()
    {
        string jsonl = """{"v":null}""";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));
        var readerOptions = new JsonlDataReaderOptions
        {
            Columns = [new JsonlColumnDefinition("v", "v", typeof(int))]
        };
        using var dr = Jsonl.CreateDataReader(stream, readerOptions: readerOptions);
        Assert.True(dr.Read());
        Assert.True(dr.IsDBNull(0));
        Assert.Equal(string.Empty, dr.GetString(0));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DataReader_InferenceFromNonObject_Throws()
    {
        // First non-empty line must be a JSON object for schema inference.
        string jsonl = "[1, 2, 3]";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));
        using var dr = Jsonl.CreateDataReader(stream);
        Assert.Throws<JsonlException>(() => dr.Read());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DataReader_JsonPath_InvalidIndex_ReturnsDBNull()
    {
        string jsonl = """{"messages":[{"role":"user"}]}""";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));
        var readerOptions = new JsonlDataReaderOptions
        {
            Columns =
            [
                new JsonlColumnDefinition("oob", "messages[5].role", typeof(string)),
                new JsonlColumnDefinition("notarray", "messages[0].role[2]", typeof(string)),
                new JsonlColumnDefinition("badkey", "messages[0].missing", typeof(string)),
            ]
        };
        using var dr = Jsonl.CreateDataReader(stream, readerOptions: readerOptions);
        Assert.True(dr.Read());
        Assert.True(dr.IsDBNull(0));
        Assert.True(dr.IsDBNull(1));
        Assert.True(dr.IsDBNull(2));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void DataReader_ParseFailure_Throws()
    {
        string jsonl = "this is not json";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));
        using var dr = Jsonl.CreateDataReader(stream);
        Assert.Throws<JsonlException>(() => dr.Read());
    }

    // ----- JsonlLine.ToString -----

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void JsonlLine_ToString_DecodesUtf8()
    {
        var line = new JsonlLine(Encoding.UTF8.GetBytes("hello"), 1);
        Assert.Equal("hello", line.ToString());
        Assert.Equal(1L, line.LineNumber);
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
