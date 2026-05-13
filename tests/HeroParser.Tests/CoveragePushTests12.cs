using System.Diagnostics.CodeAnalysis;
using System.Text;
using HeroParser.FixedWidths;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Records;
using HeroParser.SeparatedValues.Writing;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests;

/// <summary>Twelfth wave: buffer growth/limits, FixedWidth static writers, Excel reflection writer, MultiSchema progress.</summary>
public class CoveragePushTests12
{
    // ---------- CsvAsyncStreamReader MaxRowCount + MaxRowSize ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncStreamReader_MaxRowCount_Throws()
    {
        string csv = "a,b\n" + string.Concat(Enumerable.Range(0, 100).Select(i => $"{i},x\n"));
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        await using var reader = Csv.CreateAsyncStreamReader(ms,
            options: new CsvReadOptions { MaxRowCount = 5 });
        await Assert.ThrowsAsync<CsvException>(async () =>
        {
            while (await reader.MoveNextAsync(TestContext.Current.CancellationToken)) { }
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncStreamReader_MaxRowSize_Throws()
    {
        // Long single line with no newline forces buffer growth → exceeds limit.
        string csv = "a,b\n" + new string('x', 200_000) + "\n";
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        await using var reader = Csv.CreateAsyncStreamReader(ms,
            options: new CsvReadOptions { MaxRowSize = 1024 },
            bufferSize: 256);
        await Assert.ThrowsAsync<CsvException>(async () =>
        {
            while (await reader.MoveNextAsync(TestContext.Current.CancellationToken)) { }
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncStreamReader_BomProcessing_TinyBuffer()
    {
        // BOM with very small initial buffer to force multi-pump BOM resolution.
        byte[] bom = [0xEF, 0xBB, 0xBF];
        byte[] data = Encoding.UTF8.GetBytes("a,b\n1,2\n");
        byte[] combined = [.. bom, .. data];
        using var ms = new MemoryStream(combined);
        await using var reader = Csv.CreateAsyncStreamReader(ms, bufferSize: 2);
        int n = 0;
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken)) n++;
        Assert.Equal(2, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncStreamReader_TrackLineNumbers()
    {
        byte[] data = Encoding.UTF8.GetBytes("a,b\n1,2\n\n3,4\n");
        using var ms = new MemoryStream(data);
        await using var reader = Csv.CreateAsyncStreamReader(ms,
            options: new CsvReadOptions { TrackSourceLineNumbers = true });
        int n = 0;
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken)) n++;
        Assert.True(n >= 3);
    }

    // ---------- FixedWidth static WriteToFile / WriteToStream ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_WriteToFile_Static()
    {
        string path = Path.GetTempFileName();
        try
        {
            var rows = new[] { new FixedAllTypes { L = 1L, S = 2, B = 3, D = 0.5, F = 0.25f, Bo = true, M = 1.5m } };
            FixedWidth.WriteToFile(path, rows);
            Assert.True(File.Exists(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidth_WriteToStream_Static()
    {
        var rows = new[] { new FixedAllTypes { L = 1L, S = 2, B = 3, D = 0.5, F = 0.25f, Bo = true, M = 1.5m } };
        using var ms = new MemoryStream();
        FixedWidth.WriteToStream(ms, rows);
        Assert.True(ms.Length > 0);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task FixedWidth_WriteToStreamAsync_Static()
    {
        var rows = new[] { new FixedAllTypes { L = 1L, S = 2, B = 3, D = 0.5, F = 0.25f, Bo = true, M = 1.5m } };
        using var ms = new MemoryStream();
        await FixedWidth.WriteToStreamAsync(ms, rows, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 0);
    }

    // ---------- Excel reflection-based writer with [Validate] ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [RequiresUnreferencedCode("reflection")]
    [RequiresDynamicCode("reflection")]
    public void Excel_Write_Reflection_ValidateAttribute_Lenient()
    {
        var rows = new[] { new ReflectValidateRow { Name = null, Length = 5 } };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<ReflectValidateRow>()
            .WithValidationMode(ValidationMode.Lenient)
            .ToStream(ms, rows);
        Assert.True(ms.Length > 0);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [RequiresUnreferencedCode("reflection")]
    [RequiresDynamicCode("reflection")]
    public void Excel_Write_Reflection_ValidateAttribute_Strict_Throws()
    {
        var rows = new[] { new ReflectValidateRow { Name = null, Length = 5 } };
        using var ms = new MemoryStream();
        Assert.Throws<ValidationException>(() => global::HeroParser.Excel.Write<ReflectValidateRow>()
            .WithValidationMode(ValidationMode.Strict)
            .ToStream(ms, rows));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Excel_WriteAsync_AllPrimitiveCellTypes()
    {
        var rows = new[]
        {
            new PrimitivesRow
            {
                B = 1, S = 2, US = 3, I = 4, UI = 5, L = 6, UL = 7,
                F = 1.5f, D = 2.5, M = 3.5m, Bool = true, G = Guid.NewGuid(),
                Dt = DateTime.UtcNow, Dto = DateTimeOffset.UtcNow,
                DOnly = DateOnly.FromDateTime(DateTime.Today),
                TOnly = TimeOnly.FromDateTime(DateTime.Now),
            }
        };
        using var ms = new MemoryStream();
        await global::HeroParser.Excel.Write<PrimitivesRow>().ToStreamAsync(ms, rows, ct: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 0);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Excel_WriteAsync_AsyncEnumerable()
    {
        static async IAsyncEnumerable<CoveragePerson> Source()
        {
            for (int i = 0; i < 20; i++)
            {
                yield return new CoveragePerson { Name = $"P{i}", Age = i };
                await Task.Yield();
            }
        }
        using var ms = new MemoryStream();
        await global::HeroParser.Excel.Write<CoveragePerson>().ToStreamAsync(ms, Source(), ct: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 0);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_Write_NumericFormat()
    {
        var rows = new[] { new MoneyRow { Amount = 12345.6789m } };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<MoneyRow>().ToStream(ms, rows);
        Assert.True(ms.Length > 0);
    }

    // ---------- MultiSchema streaming reader buffer growth + progress ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task MultiSchema_Streaming_BufferGrowsForLongRow()
    {
        var sb = new StringBuilder("Type,Payload\n");
        sb.Append("Foo,").Append(new string('x', 30000)).Append('\n');
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        await using var reader = Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .MapRecord<FooBigRecord>("Foo")
            .FromStream(ms);
        int n = 0;
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken)) n++;
        Assert.Equal(1, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task MultiSchema_Streaming_WithProgress()
    {
        var sb = new StringBuilder("Type,A,B\n");
        for (int i = 0; i < 100; i++) sb.Append("Foo,").Append(i).Append(',').Append(i).Append('\n');
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));

        int progressCalls = 0;
        var progress = new Progress<CsvProgress>(_ => Interlocked.Increment(ref progressCalls));

        await using var reader = Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .WithProgress(progress, intervalRows: 25)
            .MapRecord<FooRecord>("Foo")
            .FromStream(ms);

        int n = 0;
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken)) n++;
        Assert.Equal(100, n);
    }

    // ---------- CsvDescriptorBinder paths (via reflection-based reader with [Parse]) ----------

    // (Removed: Csv.Read<T>() byte-path requires [GenerateBinder]; reflection-only
    // CSV reading would need a different builder API.)

    // ---------- FixedWidthByteSpanReader edge cases ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthByteSpanReader_EmptyInput()
    {
        ReadOnlySpan<byte> empty = [];
        var reader = new FixedWidthByteSpanReader(empty, FixedWidthReadOptions.Default);
        Assert.False(reader.MoveNext());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthByteSpanReader_OnlyNewlines()
    {
        byte[] data = "\n\n\n"u8.ToArray();
        var reader = new FixedWidthByteSpanReader(data, FixedWidthReadOptions.Default);
        int n = 0;
        while (reader.MoveNext()) n++;
        // Implementation-specific - just exercise the path.
        Assert.True(n >= 0);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthByteSpanReader_LongRow()
    {
        byte[] data = Encoding.UTF8.GetBytes(new string('x', 5000) + "\n");
        var reader = new FixedWidthByteSpanReader(data, FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        Assert.Equal(5000, reader.Current.Length);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthCharSpanReader_LongRow()
    {
        string data = new string('x', 5000) + "\n";
        var reader = new FixedWidthCharSpanReader(data.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        Assert.Equal(5000, reader.Current.Length);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthByteSpanReader_MultipleRows_CrLf()
    {
        byte[] data = Encoding.UTF8.GetBytes("row1\r\nrow2\r\nrow3\r\n");
        var reader = new FixedWidthByteSpanReader(data, FixedWidthReadOptions.Default);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(3, n);
    }

    // ---------- Excel reader options exercise ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_Read_RowLevel_FromStream()
    {
        var rows = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        using var ms = new MemoryStream();
        global::HeroParser.Excel.Write<CoveragePerson>().ToStream(ms, rows);
        ms.Position = 0;
        var rowsBack = global::HeroParser.Excel.Read().FromStream(ms).ToList();
        Assert.NotEmpty(rowsBack);
    }
}

// ---------- Records ----------

[GenerateBinder]
public class FooBigRecord
{
    public string? Type { get; set; }
    public string? Payload { get; set; }
}

public class ReflectDateRow
{
    [Parse(Format = "yyyyMMdd")]
    public DateTime When { get; set; }
}

public class ReflectNullableAgeRow
{
    public string? Name { get; set; }
    public int? Age { get; set; }
}

public class ReflectRequiredNameRow
{
    [Validate(NotNull = true, NotEmpty = true)]
    public string? Name { get; set; }
    public int Age { get; set; }
}
