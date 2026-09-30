using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using HeroParser.Generators.Tests.Generated;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Binders;
using HeroParser.SeparatedValues.Reading.Records;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Generators.Tests;

[Trait("Category", "Integration")]
public class GeneratedCsvValueTests
{
    public static TheoryData<string, string, char, char?, bool> Cases => new()
    {
        { "Alice,42", "Alice", '"', null, true },
        { ",42", "", '"', null, true },
        { "Alice,42", "Alice", '"', '\\', true },
        { "Alice,\"42\"", "Alice", '"', null, true },
        { "Alice,1421", "Alice", '1', null, true },
        { "\u4F60\u597D \uD83D\uDE00,42", "\u4F60\u597D \uD83D\uDE00", '"', null, true },
        { "\"\"\"\"\"\",42", "\"\"", '"', null, true },
        { "\"Alice\",\"42\"", "Alice", '"', null, true },
        { "\"said \"\"hello\"\", row 1\",42", "said \"hello\", row 1", '"', null, true },
        { "\"\u4F60\u597D \"\"\u4E16\u754C\"\" \uD83D\uDE00\",42", "\u4F60\u597D \"\u4E16\u754C\" \uD83D\uDE00", '"', null, true },
        { "'it''s, fine','42'", "it's, fine", '\'', null, true },
        { "\"a\\\"b\",\"4\\2\"", "a\"b", '"', '\\', true },
        { "a\\\\b,42", "a\\b", '"', '\\', true },
        { "\"Alice\",42", "\"Alice\"", '"', null, false },
        { "\"\",42", "", '"', null, true }
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void GeneratedBinders_UseLogicalValues(string csv, string expected, char quote, char? escape, bool quotes)
    {
        foreach (bool simd in new[] { false, true })
        {
            var options = new CsvReadOptions { Quote = quote, EscapeCharacter = escape, EnableQuotedFields = quotes, UseSimdIfAvailable = simd };
            var records = new CsvRecordOptions { HasHeaderRow = false };
            var personByteBinder = CsvRecordBinderFactory.GetByteBinder<GeneratedPerson>(records);
            using var bytes = Csv.ReadFromText("Name,Age\n" + csv + "\n", out _, options);
            Assert.True(bytes.MoveNext());
            personByteBinder.BindHeader(bytes.Current, 1);
            Assert.True(bytes.MoveNext());
            Assert.True(personByteBinder.TryBind(bytes.Current, 2, out var bytePerson));
            Assert.Equal(expected, bytePerson.Name);
            Assert.Equal(42, bytePerson.Age);

            var charBinder = CsvRecordBinderFactory.GetCharBinder<GeneratedPerson>(records);
            using var chars = Csv.ReadFromText("Name,Age\n" + csv + "\n", options);
            Assert.True(chars.MoveNext());
            charBinder.BindHeader(chars.Current, 1);
            Assert.True(chars.MoveNext());
            Assert.True(charBinder.TryBind(chars.Current, 2, out var charPerson));
            Assert.Equal(expected, charPerson.Name);
            Assert.Equal(42, charPerson.Age);

            var adapter = new CsvCharToByteBinderAdapter<GeneratedPerson>(CsvRecordBinderFactory.GetByteBinder<GeneratedPerson>(), ',');
            using var header = Csv.ReadFromText("Name,Age", options);
            Assert.True(header.MoveNext());
            adapter.BindHeader(header.Current, 1);
            Assert.True(adapter.TryBind(chars.Current.Clone(), 2, out var adapted));
            Assert.Equal(expected, adapted.Name);
            Assert.Equal(42, adapted.Age);
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task PipeReader_UsesLogicalValues(string csv, string expected, char quote, char? escape, bool quotes)
    {
        var options = new CsvReadOptions { Quote = quote, EscapeCharacter = escape, EnableQuotedFields = quotes };
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Name,Age\n" + csv + "\n"));
        var reader = PipeReader.Create(stream, new StreamPipeReaderOptions(bufferSize: 8, minimumReadSize: 1));
        var results = new List<GeneratedPerson>();
        await foreach (var person in Csv.DeserializeRecordsAsync<GeneratedPerson>(reader, parserOptions: options,
            cancellationToken: TestContext.Current.CancellationToken))
            results.Add(person);
        var result = Assert.Single(results);
        Assert.Equal(expected, result.Name);
        Assert.Equal(42, result.Age);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task AsyncStream_UsesLogicalValues(string csv, string expected, char quote, char? escape, bool quotes)
    {
        var options = new CsvReadOptions { Quote = quote, EscapeCharacter = escape, EnableQuotedFields = quotes };
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Name,Age\n" + csv + "\n"));
        await using var reader = Csv.CreateAsyncStreamReader(stream, options, bufferSize: 8);
        var binder = CsvRecordBinderFactory.GetByteBinder<GeneratedPerson>();
        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        binder.BindHeader(reader.Current, 1);
        Assert.True(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
        Assert.True(binder.TryBind(reader.Current, 2, out var person));
        Assert.Equal(expected, person.Name);
        Assert.Equal(42, person.Age);
        Assert.False(await reader.MoveNextAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task GeneratedBinding_PreservesEverySegmentBoundary(string csv, string expected, char quote, char? escape, bool quotes)
    {
        byte[] bytes = Encoding.UTF8.GetBytes("Name,Age\n" + csv + "\n");
        var options = new CsvReadOptions { Quote = quote, EscapeCharacter = escape, EnableQuotedFields = quotes };
        for (int split = 1; split < bytes.Length; split++)
        {
            var first = new Segment(bytes.AsMemory(0, split));
            var last = first.Append(bytes.AsMemory(split));
            var pipe = new BufferedReader(new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length));
            var results = new List<GeneratedPerson>();
            await foreach (var record in Csv.DeserializeRecordsAsync<GeneratedPerson>(pipe, parserOptions: options,
                cancellationToken: TestContext.Current.CancellationToken))
                results.Add(record);
            Assert.Equal(expected, Assert.Single(results).Name);
            Assert.Equal(42, results[0].Age);
        }
    }

    [Fact]
    public void ExcelCells_PreserveLiteralCsvSyntax()
    {
        const string name = "\"literal \"\"quotes\"\" and \\slashes\"";
        var data = Excel.Write<GeneratedPerson>().ToBytes([new GeneratedPerson { Name = name, Age = 42 }]);
        using var stream = new MemoryStream(data);
        var record = Assert.Single(Excel.Read<GeneratedPerson>().FromStream(stream));
        Assert.Equal(name, record.Name);
        Assert.Equal(42, record.Age);
    }

    [Fact]
    public void QuotedScalars_UseCultureAndFormats()
    {
        const string csv = "\"true\",\"1,25\",\"2025-06-15\",\"Ready\"";
        var records = new CsvRecordOptions { Culture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR") };
        using var bytes = Csv.ReadFromText(csv, out _);
        Assert.True(bytes.MoveNext());
        Assert.True(CsvRecordBinderFactory.GetByteBinder<GeneratedQuotedScalars>(records).TryBind(bytes.Current, 1, out var byteRecord));
        Assert.True(byteRecord.Enabled);
        Assert.Equal(1.25m, byteRecord.Amount);
        Assert.Equal(new DateOnly(2025, 6, 15), byteRecord.Date);
        Assert.Equal(QuotedState.Ready, byteRecord.State);
        using var chars = Csv.ReadFromText(csv);
        Assert.True(chars.MoveNext());
        Assert.True(CsvRecordBinderFactory.GetCharBinder<GeneratedQuotedScalars>(records).TryBind(chars.Current, 1, out var charRecord));
        Assert.Equal(byteRecord.Enabled, charRecord.Enabled);
        Assert.Equal(byteRecord.Amount, charRecord.Amount);
        Assert.Equal(byteRecord.Date, charRecord.Date);
        Assert.Equal(byteRecord.State, charRecord.State);
    }

    [Fact]
    public async Task LongEscapedField_CrossesManyPipeReads()
    {
        string expected = new string('x', 4096) + "said \"hello\", row 1";
        string csv = "Name,Age\n\"" + expected.Replace("\"", "\"\"") + "\",\"42\"\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var reader = PipeReader.Create(stream, new StreamPipeReaderOptions(bufferSize: 128, minimumReadSize: 1));
        var results = new List<GeneratedPerson>();
        await foreach (var record in Csv.DeserializeRecordsAsync<GeneratedPerson>(reader,
            cancellationToken: TestContext.Current.CancellationToken))
            results.Add(record);
        Assert.Equal(expected, Assert.Single(results).Name);
        Assert.Equal(42, results[0].Age);
    }

    [Fact]
    public void InvalidUtf8_IsDecodedBeforeEscapes()
    {
        byte[] data = [(byte)'"', 0xC2, (byte)'\\', 0x80, (byte)'"', (byte)',', (byte)'4', (byte)'2'];
        using var reader = Csv.ReadFromByteSpan(data, new CsvReadOptions { EscapeCharacter = '\\' });
        Assert.True(reader.MoveNext());
        var binder = CsvRecordBinderFactory.GetByteBinder<GeneratedPerson>();
        using var header = Csv.ReadFromText("Name,Age", out _);
        Assert.True(header.MoveNext());
        binder.BindHeader(header.Current, 1);
        Assert.True(binder.TryBind(reader.Current, 2, out var record));
        Assert.Equal("\uFFFD\uFFFD", record.Name);
    }

    [Theory]
    [InlineData('"', null)]
    [InlineData('\'', null)]
    [InlineData('"', '\\')]
    public void QuotedHeaders_Resolve(char quote, char? escape)
    {
        string name = escape is null ? "Name" : "N\\ame";
        string csv = $"{quote}{name}{quote},{quote}Age{quote}\nAlice,42\n";
        var options = new CsvReadOptions { Quote = quote, EscapeCharacter = escape };
        {
            using var reader = Csv.DeserializeRecords<GeneratedPerson>(csv, out _, parserOptions: options);
            Assert.True(reader.MoveNext());
            Assert.Equal("Alice", reader.Current.Name);
            Assert.Equal(42, reader.Current.Age);
        }
        {
            var binder = CsvRecordBinderFactory.GetCharBinder<GeneratedPerson>();
            using var reader = Csv.ReadFromText(csv, options);
            Assert.True(reader.MoveNext());
            binder.BindHeader(reader.Current, 1);
            Assert.True(reader.MoveNext());
            Assert.True(binder.TryBind(reader.Current, 2, out var person));
            Assert.Equal("Alice", person.Name);
            Assert.Equal(42, person.Age);
        }
    }

    [Theory]
    [InlineData("\"NULL\",\"NULL\"", true)]
    [InlineData("\"\",\"42\"", false)]
    [InlineData("\" \",42", false)]
    public void NullAndValidation_UseLogicalValues(string csv, bool isNull)
    {
        var binder = CsvRecordBinderFactory.GetByteBinder<GeneratedValidatedValue>(new CsvRecordOptions { NullValues = ["NULL"] });
        using var reader = Csv.ReadFromText(csv, out _);
        Assert.True(reader.MoveNext());
        var errors = new List<ValidationError>();
        Assert.False(binder.TryBind(reader.Current, 1, out var result, errors));
        Assert.Contains(errors, error => error.PropertyName == "Name" && error.Rule == "NotEmpty");
        Assert.Equal(isNull ? null : 42, result.Age);
        if (isNull)
            Assert.Null(result.Name);
        Assert.All(errors, error => Assert.Equal(csv.Split(',')[0], error.RawValue));

        var charBinder = CsvRecordBinderFactory.GetCharBinder<GeneratedValidatedValue>(new CsvRecordOptions { NullValues = ["NULL"] });
        using var chars = Csv.ReadFromText(csv);
        Assert.True(chars.MoveNext());
        errors.Clear();
        Assert.False(charBinder.TryBind(chars.Current, 1, out var charResult, errors));
        Assert.Contains(errors, error => error.Rule == "NotEmpty");
        Assert.Equal(result.Name, charResult.Name);
        Assert.Equal(result.Age, charResult.Age);
        Assert.All(errors, error => Assert.Equal(csv.Split(',')[0], error.RawValue));
    }

    [Theory]
    [InlineData("\"abc\",42", "abc")]
    [InlineData("\"a\"\"b\",42", "a\"b")]
    public void StringLengthValidation_UsesDecodedLength(string csv, string expected)
    {
        using var reader = Csv.ReadFromText(csv, out _);
        Assert.True(reader.MoveNext());
        var errors = new List<ValidationError>();
        var binder = CsvRecordBinderFactory.GetByteBinder<GeneratedValidatedValue>();
        Assert.True(binder.TryBind(reader.Current, 1, out var record, errors));
        Assert.Empty(errors);
        Assert.Equal(expected, record.Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParseErrors_PreserveRawQuotedValues(bool bytes)
    {
        const string csv = "Name,Age\nabc,\"not an integer\"";
        var error = Assert.Throws<CsvException>(() =>
        {
            if (bytes)
            {
                using var reader = Csv.ReadFromText(csv, out _);
                Assert.True(reader.MoveNext());
                var binder = CsvRecordBinderFactory.GetByteBinder<GeneratedPerson>();
                binder.BindHeader(reader.Current, 1);
                Assert.True(reader.MoveNext());
                binder.TryBind(reader.Current, 7, out _);
            }
            else
            {
                using var reader = Csv.ReadFromText(csv);
                Assert.True(reader.MoveNext());
                var binder = CsvRecordBinderFactory.GetCharBinder<GeneratedPerson>();
                binder.BindHeader(reader.Current, 1);
                Assert.True(reader.MoveNext());
                binder.TryBind(reader.Current, 7, out _);
            }
        });
        Assert.Equal("\"not an integer\"", error.FieldValue);
        Assert.Equal(7, error.Row);
        Assert.Equal(2, error.Column);
    }

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory)
        {
            Memory = memory;
        }

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }

    private sealed class BufferedReader(ReadOnlySequence<byte> sequence) : PipeReader
    {
        private ReadOnlySequence<byte> remaining = sequence;
        public override void AdvanceTo(SequencePosition consumed) => remaining = remaining.Slice(consumed);
        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) => AdvanceTo(consumed);
        public override void CancelPendingRead() { }
        public override void Complete(Exception? exception = null) { }
        public override bool TryRead(out ReadResult result)
        {
            result = new ReadResult(remaining, isCanceled: false, isCompleted: true);
            return true;
        }
        public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
            => cancellationToken.IsCancellationRequested
                ? ValueTask.FromCanceled<ReadResult>(cancellationToken)
                : ValueTask.FromResult(new ReadResult(remaining, isCanceled: false, isCompleted: true));
    }
}

[GenerateBinder]
public sealed class GeneratedValidatedValue
{
    [TabularMap(Index = 0)]
    [Validate(NotEmpty = true, MaxLength = 3)]
    public string? Name { get; set; }

    [TabularMap(Index = 1)]
    public int? Age { get; set; }
}

public enum QuotedState { Ready }

[GenerateBinder]
public sealed class GeneratedQuotedScalars
{
    [TabularMap(Index = 0)]
    public bool Enabled { get; set; }

    [TabularMap(Index = 1)]
    public decimal Amount { get; set; }

    [TabularMap(Index = 2)]
    [Parse(Format = "yyyy-MM-dd")]
    public DateOnly Date { get; set; }

    [TabularMap(Index = 3)]
    public QuotedState State { get; set; }
}
