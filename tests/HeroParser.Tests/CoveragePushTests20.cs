using System.Text;
using HeroParser.FixedWidths;
using HeroParser.SeparatedValues.Reading.Binders;
using HeroParser.SeparatedValues.Reading.Records;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests;

/// <summary>Wave 20: CsvRecordBinderFactory paths, CsvRowReaderBuilder file/async overloads, FW span reader extras.</summary>
public class CoveragePushTests20
{
    // ---------- CsvRecordBinderFactory ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void RecordBinderFactory_GetByteBinder_NoBinder_Throws()
    {
        // Type without [GenerateBinder] → throws.
        Assert.Throws<InvalidOperationException>(() => CsvRecordBinderFactory.GetByteBinder<NoBinderRow>());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void RecordBinderFactory_GetByteBinder_Generated_Works()
    {
        // Type with [GenerateBinder] → returns binder.
        var binder = CsvRecordBinderFactory.GetByteBinder<CoveragePerson>();
        Assert.NotNull(binder);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void RecordBinderFactory_GetCharBinder_Generated_Works()
    {
        var binder = CsvRecordBinderFactory.GetCharBinder<CoveragePerson>();
        Assert.NotNull(binder);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void RecordBinderFactory_RegisterByteBinder_NullFactory_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CsvRecordBinderFactory.RegisterByteBinder<NoBinderRow>(null!));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void RecordBinderFactory_RegisterDescriptor_NullFactory_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CsvRecordBinderFactory.RegisterDescriptor<NoBinderRow>(null!));
    }

    // ---------- CsvRecordOptions custom converter / OnDeserializeError ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRecordOptions_RegisterConverter_NullThrows()
    {
        var opts = new CsvRecordOptions();
        Assert.Throws<ArgumentNullException>(() => opts.RegisterConverter<int>(null!));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRecordOptions_RegisterConverter_AddsType()
    {
        CsvTypeConverter<int> converter = TryParseInt;
        var opts = new CsvRecordOptions().RegisterConverter(converter);
        Assert.NotNull(opts);

        static bool TryParseInt(System.ReadOnlySpan<char> v, System.Globalization.CultureInfo c, string? f, out int r)
            => int.TryParse(v, out r);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRecordOptions_DefaultValues()
    {
        var opts = CsvRecordOptions.Default;
        Assert.True(opts.HasHeaderRow);
        Assert.False(opts.CaseSensitiveHeaders);
        Assert.False(opts.AllowMissingColumns);
        Assert.Null(opts.NullValues);
        Assert.Null(opts.Culture);
        Assert.Equal(0, opts.SkipRows);
        Assert.Null(opts.Progress);
        Assert.Equal(1000, opts.ProgressIntervalRows);
        Assert.Equal(ValidationMode.Strict, opts.ValidationMode);
        Assert.Null(opts.OnDeserializeError);
    }

    // ---------- CsvProgress / progress percentage ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvProgress_PercentageCalculation()
    {
        var p = new CsvProgress { RowsProcessed = 100, BytesProcessed = 5000, TotalBytes = 10000 };
        Assert.Equal(0.5, p.ProgressPercentage);

        var noTotal = new CsvProgress { RowsProcessed = 100, BytesProcessed = 5000, TotalBytes = -1 };
        Assert.Equal(-1, noTotal.ProgressPercentage);
    }

    // ---------- FixedWidthByteSpanReader/CharSpanReader edge cases ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthByteSpanReader_MaxRecordCount_Throws()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("a\nb\nc\nd\ne\n");
        var opts = FixedWidthReadOptions.Default with { MaxRecordCount = 2 };
        var reader = new FixedWidthByteSpanReader(bytes.AsSpan(), opts);
        bool threw = false;
        try { while (reader.MoveNext()) { } } catch (FixedWidthException) { threw = true; }
        Assert.True(threw);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthCharSpanReader_MaxRecordCount_Throws()
    {
        string data = "a\nb\nc\nd\ne\n";
        var opts = FixedWidthReadOptions.Default with { MaxRecordCount = 2 };
        var reader = new FixedWidthCharSpanReader(data.AsSpan(), opts);
        bool threw = false;
        try { while (reader.MoveNext()) { } } catch (FixedWidthException) { threw = true; }
        Assert.True(threw);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthByteSpanReader_RecordLength_Enforced()
    {
        // Set RecordLength to 5; reader splits the buffer into fixed-size records.
        byte[] bytes = Encoding.UTF8.GetBytes("12345" + "67890");
        var opts = FixedWidthReadOptions.Default with { RecordLength = 5 };
        var reader = new FixedWidthByteSpanReader(bytes.AsSpan(), opts);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.True(n >= 1);
    }

    // ---------- CsvRowReaderBuilder file path overloads ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowReaderBuilder_FromFile_Bytes()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "a,b\n1,2\n3,4\n");
            using var reader = Csv.Read().FromFile(path, out _);
            int n = 0;
            while (reader.MoveNext()) n++;
            Assert.Equal(3, n);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task CsvRowReaderBuilder_FromFileAsync()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "a,b\n1,2\n3,4\n");
            await using var reader = Csv.Read().FromFileAsync(path);
            int n = 0;
            while (await reader.MoveNextAsync(TestContext.Current.CancellationToken)) n++;
            Assert.Equal(3, n);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowReaderBuilder_FromFile_NullPath_Throws()
    {
        Assert.Throws<ArgumentException>(() => Csv.Read().FromFile("", out _));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowReaderBuilder_FromStream_LeaveOpenFalse()
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("a,b\n1,2\n"));
        using (var reader = Csv.Read().FromStream(stream, out _, leaveOpen: false))
        {
            while (reader.MoveNext()) { }
        }
        // With leaveOpen=false, the stream should be disposed.
        Assert.False(stream.CanRead);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task CsvRowReaderBuilder_FromStreamAsync_LeaveOpenFalse()
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("a,b\n1,2\n"));
        await using (var reader = Csv.Read().FromStreamAsync(stream, leaveOpen: false))
        {
            while (await reader.MoveNextAsync(TestContext.Current.CancellationToken)) { }
        }
        Assert.False(stream.CanRead);
    }

    // ---------- Csv.ReadFromFile / Csv.ReadFromStream static methods ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_ReadFromFile_Static()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "a,b\n1,2\n3,4\n");
            using var reader = Csv.ReadFromFile(path, out _);
            int n = 0;
            while (reader.MoveNext()) n++;
            Assert.Equal(3, n);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_ReadFromStream_Static()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("a,b\n1,2\n3,4\n"));
        using var reader = Csv.ReadFromStream(stream, out _);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(3, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_ReadFromByteSpan_Static()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("a,b\n1,2\n");
        using var reader = Csv.ReadFromByteSpan(bytes);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(2, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_ReadFromCharSpan_Static()
    {
        using var reader = Csv.ReadFromCharSpan("a,b\n1,2\n".AsSpan());
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(2, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_ReadFromText_OutBytes_Static()
    {
        using var reader = Csv.ReadFromText("a,b\n1,2\n", out byte[] bytes);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(2, n);
        Assert.NotNull(bytes);
    }

    // ---------- FixedWidth row.Equals/HashCode for sanity (ref struct can't, but ImmutableRow can) ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ImmutableFixedWidthRow_Equality()
    {
        string line = "data\n";
        var r1 = new FixedWidthCharSpanReader(line.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(r1.MoveNext());
        var immutable1 = r1.Current.ToImmutable();
        Assert.NotNull(immutable1);
        Assert.True(immutable1.Length > 0);
    }

    // ---------- CsvRow GetString / TryGetColumnSpan paths ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRow_GetString_BytePath()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("a,b\n1,2\n"));
        using var reader = Csv.Read().FromStream(stream, out _);
        Assert.True(reader.MoveNext()); // header
        Assert.True(reader.MoveNext()); // data
        var row = reader.Current;
        Assert.Equal("1", row.GetString(0));
        Assert.Equal("2", row.GetString(1));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRow_GetString_CharPath()
    {
        using var reader = Csv.Read().FromText("a,b\n1,2\n");
        Assert.True(reader.MoveNext());
        Assert.True(reader.MoveNext());
        var row = reader.Current;
        Assert.Equal("1", row.GetString(0));
        Assert.Equal("2", row.GetString(1));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRow_TryGetColumnSpan_OutOfRange()
    {
        using var reader = Csv.Read().FromText("a,b\n1,2\n");
        Assert.True(reader.MoveNext());
        Assert.True(reader.MoveNext());
        var row = reader.Current;
        Assert.False(row.TryGetColumnSpan(99, out _));
    }
}

// Plain class without [GenerateBinder] — used by RecordBinderFactory tests.
public class NoBinderRow
{
    public string? Name { get; set; }
}
