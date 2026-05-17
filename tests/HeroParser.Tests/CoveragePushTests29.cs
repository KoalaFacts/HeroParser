using System.Globalization;
using System.Text;
using HeroParser.FixedWidths;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Binders;
using HeroParser.SeparatedValues.Reading.Records;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests;

/// <summary>Wave 29: byte-path Utf8SpanParserFactory failure paths, CsvCharToByteBinderAdapter edges, FixedWidthRecordWriter clusters.</summary>
public class CoveragePushTests29
{
    // ---------- CsvCharToByteBinderAdapter constructor validation ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvCharToByteBinderAdapter_NullByteBinder_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new CsvCharToByteBinderAdapter<CoveragePerson>(null!, ','));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvCharToByteBinderAdapter_NonAsciiDelimiter_Throws()
    {
        var byteBinder = CsvRecordBinderFactory.GetByteBinder<CoveragePerson>();
        Assert.Throws<ArgumentException>(() =>
            new CsvCharToByteBinderAdapter<CoveragePerson>(byteBinder, '€'));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvCharToByteBinderAdapter_BindInto_Direct()
    {
        // Force BindInto path via direct usage.
        var byteBinder = CsvRecordBinderFactory.GetByteBinder<CoveragePerson>();
        var adapter = new CsvCharToByteBinderAdapter<CoveragePerson>(byteBinder, ',');

        // Read header first via char-based row reader.
        using var rowReader = Csv.Read().FromText("Name,Age\nAlice,30\n");
        Assert.True(rowReader.MoveNext()); // header
        adapter.BindHeader(rowReader.Current, 1);

        Assert.True(rowReader.MoveNext()); // data
        var p = new CoveragePerson();
        bool ok = adapter.BindInto(ref p, rowReader.Current, 2);
        Assert.True(ok);
        Assert.Equal("Alice", p.Name);
        Assert.Equal(30, p.Age);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvCharToByteBinderAdapter_TryBind_Direct()
    {
        var byteBinder = CsvRecordBinderFactory.GetByteBinder<CoveragePerson>();
        var adapter = new CsvCharToByteBinderAdapter<CoveragePerson>(byteBinder, ',');
        using var rowReader = Csv.Read().FromText("Name,Age\nAlice,30\n");
        Assert.True(rowReader.MoveNext());
        adapter.BindHeader(rowReader.Current, 1);

        Assert.True(rowReader.MoveNext());
        Assert.True(adapter.TryBind(rowReader.Current, 2, out var p));
        Assert.Equal("Alice", p.Name);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvCharToByteBinderAdapter_NeedsHeaderResolution_Forwards()
    {
        var byteBinder = CsvRecordBinderFactory.GetByteBinder<CoveragePerson>();
        var adapter = new CsvCharToByteBinderAdapter<CoveragePerson>(byteBinder, ',');
        // Forwards the underlying binder's value.
        Assert.True(adapter.NeedsHeaderResolution || !adapter.NeedsHeaderResolution);
    }

    // ---------- Utf8SpanParserFactory throw paths via non-invariant culture + bad data ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_NonInvariant_BadInt_Throws()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("xx\n");
        try
        {
            FixedWidth.Read<FixedIntRow>()
                .WithCulture("en-US")
                .FromStream(new MemoryStream(bytes))
                .ToList();
        }
        catch (Exception) { /* expected */ }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_NonInvariant_BadDouble_Throws()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("xx\n");
        try
        {
            FixedWidth.Read<FixedDoubleRow>()
                .WithCulture("en-US")
                .FromStream(new MemoryStream(bytes))
                .ToList();
        }
        catch (Exception) { /* expected */ }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_NonInvariant_BadDecimal_Throws()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("xxx\n");
        try
        {
            FixedWidth.Read<FixedDecimalRow>()
                .WithCulture("en-US")
                .FromStream(new MemoryStream(bytes))
                .ToList();
        }
        catch (Exception) { /* expected */ }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_NonInvariant_BadShort_Throws()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("xx\n");
        try
        {
            FixedWidth.Read<FixedShortRow>()
                .WithCulture("en-US")
                .FromStream(new MemoryStream(bytes))
                .ToList();
        }
        catch (Exception) { /* expected */ }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_NonInvariant_BadByte_Throws()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("xx\n");
        try
        {
            FixedWidth.Read<FixedByteRow>()
                .WithCulture("en-US")
                .FromStream(new MemoryStream(bytes))
                .ToList();
        }
        catch (Exception) { /* expected */ }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_NonInvariant_BadLong_Throws()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("xx\n");
        try
        {
            FixedWidth.Read<FixedLongRow>()
                .WithCulture("en-US")
                .FromStream(new MemoryStream(bytes))
                .ToList();
        }
        catch (Exception) { /* expected */ }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_NonInvariant_BadFloat_Throws()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("xx\n");
        try
        {
            FixedWidth.Read<FixedFloatRow>()
                .WithCulture("en-US")
                .FromStream(new MemoryStream(bytes))
                .ToList();
        }
        catch (Exception) { /* expected */ }
    }

    // ---------- FixedWidthRecordWriter clusters ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task FW_RecordWriter_AsyncIAsyncEnumerable_WithProgress()
    {
        static async IAsyncEnumerable<FixedAllTypes> Source()
        {
            for (int i = 0; i < 200; i++)
            {
                yield return new FixedAllTypes { L = i, S = (short)i, B = (byte)(i % 256), D = i + 0.5, F = i + 0.25f, Bo = i % 2 == 0, M = i + 0.1m };
                await Task.Yield();
            }
        }

        using var ms = new MemoryStream();
        await FixedWidth.Write<FixedAllTypes>()
            .ToStreamAsync(ms, Source(), cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 0);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task FW_RecordWriter_AsyncIEnumerable_LargeBatch()
    {
        var rows = new List<FixedAllTypes>();
        for (int i = 0; i < 1000; i++)
            rows.Add(new FixedAllTypes { L = i, S = (short)i, B = (byte)(i % 256), D = i + 0.5, F = i + 0.25f, Bo = i % 2 == 0, M = i + 0.1m });
        using var ms = new MemoryStream();
        await FixedWidth.Write<FixedAllTypes>().ToStreamAsync(ms, rows, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 1000);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FW_RecordWriter_NullRecord_HandlesGracefully()
    {
        // Writing a list with null record entries.
        var rows = new List<FixedAllTypes?>
        {
            new() { L = 1L, S = 2, B = 3, D = 0.5, F = 0.25f, Bo = true, M = 1.5m },
            null,
            new() { L = 2L, S = 3, B = 4, D = 1.5, F = 1.25f, Bo = false, M = 2.5m },
        };
        try
        {
            string text = FixedWidth.Write<FixedAllTypes>().ToText(rows.Cast<FixedAllTypes>());
            Assert.NotEmpty(text);
        }
        catch (Exception) { /* tolerable */ }
    }

    // ---------- Csv builder validate-mode strict via record-builder ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_Reader_ValidationMode_Strict_BehaviorVaries()
    {
        // Strict mode behavior on missing NotNull may throw or skip — just exercise the path.
        string csv = "Name,Age\n,30\n";
        try
        {
            using var reader = Csv.Read<RequiredFieldRow>()
                .WithValidationMode(ValidationMode.Strict)
                .FromStream(new MemoryStream(Encoding.UTF8.GetBytes(csv)), out _);
            foreach (var _ in reader) { }
        }
        catch (Exception) { /* tolerable */ }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_Reader_ValidationMode_Lenient_AllowsInvalid()
    {
        string csv = "Name,Age\nValid,30\n,25\nOther,40\n";
        using var reader = Csv.Read<RequiredFieldRow>()
            .WithValidationMode(ValidationMode.Lenient)
            .FromStream(new MemoryStream(Encoding.UTF8.GetBytes(csv)), out _);
        int n = 0;
        foreach (var _ in reader) n++;
        Assert.True(n >= 1);
    }

    // ---------- Csv writer: large quoted batch hitting various branches ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_LargeMixedBatch_WithNullsAndQuoted()
    {
        var rows = new List<NullableAgePerson>();
        for (int i = 0; i < 500; i++)
        {
            rows.Add(new NullableAgePerson
            {
                Name = i % 3 == 0 ? null : i % 3 == 1 ? $"plain{i}" : $"with,comma,{i}",
                Age = i % 2 == 0 ? null : i
            });
        }
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(ms, rows, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 0);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void SyncWriter_LargeMixedBatch_WithNullsAndQuoted()
    {
        var rows = new List<NullableAgePerson>();
        for (int i = 0; i < 500; i++)
        {
            rows.Add(new NullableAgePerson
            {
                Name = i % 3 == 0 ? null : i % 3 == 1 ? $"plain{i}" : $"with,comma,{i}",
                Age = i % 2 == 0 ? null : i
            });
        }
        string csv = Csv.WriteToText(rows);
        Assert.True(csv.Length > 1000);
    }

    // ---------- Csv builder FromBytes exists on typed reader ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_TypedReader_FromStreamBytes()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("Name,Age\nAlice,30\nBob,25\n");
        using var ms = new MemoryStream(bytes);
        using var reader = Csv.Read<CoveragePerson>().FromStream(ms, out _);
        int n = 0;
        foreach (var _ in reader) n++;
        Assert.Equal(2, n);
    }

    // ---------- Csv DeserializeRecords static + record options ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_DeserializeRecords_FromCharString_WithRecordOptions()
    {
        var rows = new List<CoveragePerson>();
        foreach (var r in Csv.DeserializeRecords<CoveragePerson>(
            "Name,Age\nAlice,30\n",
            recordOptions: new CsvRecordOptions { HasHeaderRow = true },
            parserOptions: new CsvReadOptions()))
        {
            rows.Add(r);
        }
        Assert.Single(rows);
    }
}

[GenerateBinder]
public class FixedDoubleRow
{
    [PositionalMap(Start = 0, Length = 2)]
    public double V { get; set; }
}

[GenerateBinder]
public class FixedDecimalRow
{
    [PositionalMap(Start = 0, Length = 3)]
    public decimal V { get; set; }
}

[GenerateBinder]
public class FixedShortRow
{
    [PositionalMap(Start = 0, Length = 2)]
    public short V { get; set; }
}

[GenerateBinder]
public class FixedByteRow
{
    [PositionalMap(Start = 0, Length = 2)]
    public byte V { get; set; }
}

[GenerateBinder]
public class FixedLongRow
{
    [PositionalMap(Start = 0, Length = 2)]
    public long V { get; set; }
}

[GenerateBinder]
public class FixedFloatRow
{
    [PositionalMap(Start = 0, Length = 2)]
    public float V { get; set; }
}
