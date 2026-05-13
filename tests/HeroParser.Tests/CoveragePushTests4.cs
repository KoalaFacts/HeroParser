using System.Diagnostics.CodeAnalysis;
using System.Text;
using HeroParser.FixedWidths;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Writing;
using Xunit;

namespace HeroParser.Tests;

/// <summary>Fourth wave: reflection-based FixedWidth converters, runtime multi-schema, async streaming.</summary>
public class CoveragePushTests4
{
    // ---------- Reflection-based FixedWidth converters via fluent Map ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [RequiresUnreferencedCode("test uses fluent mapping")]
    [RequiresDynamicCode("test uses fluent mapping")]
    public void FixedWidth_FluentMap_LongConverter()
    {
        // No [GenerateBinder] → uses reflection-based ConverterFactory.
        var rows = FixedWidth.Read<PlainLongRow>()
            .Map(r => r.Value, c => c.Start(0).Length(10))
            .FromText("1234567890\n")
            .ToList();
        Assert.Single(rows);
        Assert.Equal(1234567890L, rows[0].Value);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [RequiresUnreferencedCode("test uses fluent mapping")]
    [RequiresDynamicCode("test uses fluent mapping")]
    public void FixedWidth_FluentMap_ShortConverter()
    {
        var rows = FixedWidth.Read<PlainShortRow>()
            .Map(r => r.Value, c => c.Start(0).Length(5))
            .FromText("12345\n")
            .ToList();
        Assert.Single(rows);
        Assert.Equal((short)12345, rows[0].Value);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [RequiresUnreferencedCode("test uses fluent mapping")]
    [RequiresDynamicCode("test uses fluent mapping")]
    public void FixedWidth_FluentMap_ByteConverter()
    {
        var rows = FixedWidth.Read<PlainByteRow>()
            .Map(r => r.Value, c => c.Start(0).Length(3))
            .FromText("200\n")
            .ToList();
        Assert.Single(rows);
        Assert.Equal((byte)200, rows[0].Value);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [RequiresUnreferencedCode("test uses fluent mapping")]
    [RequiresDynamicCode("test uses fluent mapping")]
    public void FixedWidth_FluentMap_DoubleConverter()
    {
        var rows = FixedWidth.Read<PlainDoubleRow>()
            .Map(r => r.Value, c => c.Start(0).Length(8))
            .FromText("3.140000\n")
            .ToList();
        Assert.Single(rows);
        Assert.Equal(3.14, rows[0].Value, 2);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [RequiresUnreferencedCode("test uses fluent mapping")]
    [RequiresDynamicCode("test uses fluent mapping")]
    public void FixedWidth_FluentMap_SingleConverter()
    {
        var rows = FixedWidth.Read<PlainSingleRow>()
            .Map(r => r.Value, c => c.Start(0).Length(8))
            .FromText("2.500000\n")
            .ToList();
        Assert.Single(rows);
        Assert.Equal(2.5f, rows[0].Value);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [RequiresUnreferencedCode("test uses fluent mapping")]
    [RequiresDynamicCode("test uses fluent mapping")]
    public void FixedWidth_FluentMap_DateTimeConverter()
    {
        var rows = FixedWidth.Read<PlainDtRow>()
            .Map(r => r.Value, c => c.Start(0).Length(19))
            .FromText("2024-06-01T12:30:45\n")
            .ToList();
        Assert.Single(rows);
        Assert.Equal(2024, rows[0].Value.Year);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [RequiresUnreferencedCode("test uses fluent mapping")]
    [RequiresDynamicCode("test uses fluent mapping")]
    public void FixedWidth_FluentMap_DateTimeOffsetConverter_NotSupported()
    {
        // DateTimeOffset is not supported by the fluent (reflection) ConverterFactory.
        Assert.Throws<NotSupportedException>(() => FixedWidth.Read<PlainDtoRow>()
            .Map(r => r.Value, c => c.Start(0).Length(25))
            .FromText("2024-06-01T12:30:45+00:00\n")
            .ToList());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [RequiresUnreferencedCode("test uses fluent mapping")]
    [RequiresDynamicCode("test uses fluent mapping")]
    public void FixedWidth_FluentMap_DateOnlyConverter()
    {
        var rows = FixedWidth.Read<PlainDateOnlyRow>()
            .Map(r => r.Value, c => c.Start(0).Length(10))
            .FromText("2024-06-01\n")
            .ToList();
        Assert.Single(rows);
        Assert.Equal(2024, rows[0].Value.Year);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [RequiresUnreferencedCode("test uses fluent mapping")]
    [RequiresDynamicCode("test uses fluent mapping")]
    public void FixedWidth_FluentMap_TimeOnlyConverter()
    {
        var rows = FixedWidth.Read<PlainTimeOnlyRow>()
            .Map(r => r.Value, c => c.Start(0).Length(8))
            .FromText("12:30:45\n")
            .ToList();
        Assert.Single(rows);
        Assert.Equal(12, rows[0].Value.Hour);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [RequiresUnreferencedCode("test uses fluent mapping")]
    [RequiresDynamicCode("test uses fluent mapping")]
    public void FixedWidth_FluentMap_EnumConverter()
    {
        var rows = FixedWidth.Read<PlainEnumRow>()
            .Map(r => r.Value, c => c.Start(0).Length(5))
            .FromText("Red  \n")
            .ToList();
        Assert.Single(rows);
        Assert.Equal(Color.Red, rows[0].Value);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [RequiresUnreferencedCode("test uses fluent mapping")]
    [RequiresDynamicCode("test uses fluent mapping")]
    public void FixedWidth_FluentMap_DecimalConverter()
    {
        var rows = FixedWidth.Read<PlainDecimalRow>()
            .Map(r => r.Value, c => c.Start(0).Length(10))
            .FromText("1234.56000\n")
            .ToList();
        Assert.Single(rows);
        Assert.Equal(1234.56m, rows[0].Value);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [RequiresUnreferencedCode("test uses fluent mapping")]
    [RequiresDynamicCode("test uses fluent mapping")]
    public void FixedWidth_FluentMap_NullableLongConverter_Empty()
    {
        var rows = FixedWidth.Read<PlainNullableLongRow>()
            .Map(r => r.Value, c => c.Start(0).Length(10))
            .FromText("          \n")
            .ToList();
        Assert.Single(rows);
        Assert.Null(rows[0].Value);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [RequiresUnreferencedCode("test uses fluent mapping")]
    [RequiresDynamicCode("test uses fluent mapping")]
    public void FixedWidth_FluentMap_AllTypesTogether()
    {
        string line = "9999999999" + "12345" + "200" + "3.140000" + "2.500000" + "true " + "1234.56000" + "\n";
        var rows = FixedWidth.Read<PlainAllTypes>()
            .Map(r => r.L, c => c.Start(0).Length(10))
            .Map(r => r.S, c => c.Start(10).Length(5))
            .Map(r => r.B, c => c.Start(15).Length(3))
            .Map(r => r.D, c => c.Start(18).Length(8))
            .Map(r => r.F, c => c.Start(26).Length(8))
            .Map(r => r.Bo, c => c.Start(34).Length(5))
            .Map(r => r.M, c => c.Start(39).Length(10))
            .FromText(line)
            .ToList();
        Assert.Single(rows);
        Assert.Equal(9999999999L, rows[0].L);
        Assert.Equal(1234.56m, rows[0].M);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [RequiresUnreferencedCode("test uses fluent mapping")]
    [RequiresDynamicCode("test uses fluent mapping")]
    public void FixedWidth_FluentMap_StringPropertyAlignment()
    {
        // Left- and right-aligned padding both supported.
        var rows = FixedWidth.Read<PlainStringRow>()
            .Map(r => r.Name, c => c.Start(0).Length(10).PadChar(' ').Alignment(FieldAlignment.Right))
            .FromText("     Alice\n")
            .ToList();
        Assert.Single(rows);
        Assert.Equal("Alice", rows[0].Name);
    }

    // ---------- ConverterFactory coverage via reflection-based FixedWidthRecordBinder ----------
    // Records have [PositionalMap] but NO [GenerateBinder] → reflection path used.

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [RequiresUnreferencedCode("test uses reflection-based binder")]
    [RequiresDynamicCode("test uses reflection-based binder")]
    public void FixedWidth_Reflection_AllPrimitiveConverters()
    {
        string line = "9999999999" + "12345" + "200" + "3.140000" + "2.500000" + "true " + "1234.56000" + "42 " + "\n";
        var rows = FixedWidth.Read<ReflectAllPrimitives>().FromText(line).ToList();
        Assert.Single(rows);
        Assert.Equal(9999999999L, rows[0].L);
        Assert.Equal((short)12345, rows[0].S);
        Assert.Equal((byte)200, rows[0].B);
        Assert.Equal(3.14, rows[0].D, 2);
        Assert.Equal(2.5f, rows[0].F);
        Assert.True(rows[0].Bo);
        Assert.Equal(1234.56m, rows[0].M);
        Assert.Equal(42, rows[0].I);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [RequiresUnreferencedCode("test uses reflection-based binder")]
    [RequiresDynamicCode("test uses reflection-based binder")]
    public void FixedWidth_Reflection_DateTypeConverters()
    {
        string line = "2024-06-01T12:30:45" + "2024-06-01T12:30:45+00:00" + "2024-06-01" + "12:30:45" + "\n";
        var rows = FixedWidth.Read<ReflectDateTypes>().FromText(line).ToList();
        Assert.Single(rows);
        Assert.Equal(2024, rows[0].Dt.Year);
        Assert.Equal(2024, rows[0].Dto.Year);
        Assert.Equal(2024, rows[0].D.Year);
        Assert.Equal(12, rows[0].T.Hour);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [RequiresUnreferencedCode("test uses reflection-based binder")]
    [RequiresDynamicCode("test uses reflection-based binder")]
    public void FixedWidth_Reflection_EnumConverter()
    {
        string line = "Red  \n";
        var rows = FixedWidth.Read<ReflectEnumRow>().FromText(line).ToList();
        Assert.Single(rows);
        Assert.Equal(Color.Red, rows[0].Color);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [RequiresUnreferencedCode("test uses reflection-based binder")]
    [RequiresDynamicCode("test uses reflection-based binder")]
    public void FixedWidth_Reflection_NullableConverters_AllPopulated()
    {
        // Layout: L(10) S(5) B(3) D(8) F(8) Bo(5) M(10) Dt(19) Date(10) Time(8) Color(5)
        string line = "9999999999" + "12345" + "200" + "3.140000" + "2.500000" + "true " + "1234.56000" +
                      "2024-06-01T12:30:45" + "2024-06-01" + "12:30:45" + "Blue " + "\n";
        var rows = FixedWidth.Read<ReflectNullableTypes>().FromText(line).ToList();
        Assert.Single(rows);
        Assert.Equal(9999999999L, rows[0].L);
        Assert.Equal(Color.Blue, rows[0].Color);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [RequiresUnreferencedCode("test uses reflection-based binder")]
    [RequiresDynamicCode("test uses reflection-based binder")]
    public void FixedWidth_Reflection_NullableConverters_AllEmpty()
    {
        string line = new string(' ', 91) + "\n";
        var rows = FixedWidth.Read<ReflectNullableTypes>().FromText(line).ToList();
        Assert.Single(rows);
        Assert.Null(rows[0].L);
        Assert.Null(rows[0].S);
        Assert.Null(rows[0].B);
        Assert.Null(rows[0].D);
        Assert.Null(rows[0].F);
        Assert.Null(rows[0].Bo);
        Assert.Null(rows[0].M);
        Assert.Null(rows[0].Dt);
        Assert.Null(rows[0].Date);
        Assert.Null(rows[0].Time);
        Assert.Null(rows[0].Color);
    }

    // ---------- Runtime Multi-Schema CSV parsing ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_MultiSchema_Runtime_DispatchesByDiscriminator()
    {
        string csv = "Type,A,B\nFoo,1,2\nBar,3,4\nFoo,5,6\n";

        int fooCount = 0, barCount = 0;
        var reader = Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .MapRecord<FooRecord>("Foo")
            .MapRecord<BarRecord>("Bar")
            .FromText(csv);
        foreach (var row in reader)
        {
            if (row is FooRecord) fooCount++;
            else if (row is BarRecord) barCount++;
        }
        Assert.Equal(2, fooCount);
        Assert.Equal(1, barCount);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_MultiSchema_UnknownDiscriminator_Skipped()
    {
        string csv = "Type,A,B\nFoo,1,2\nUnknown,3,4\nFoo,5,6\n";
        int fooCount = 0;
        var reader = Csv.Read()
            .WithMultiSchema()
            .WithDiscriminator("Type")
            .MapRecord<FooRecord>("Foo")
            .OnUnmatchedRow(SeparatedValues.Reading.Records.MultiSchema.UnmatchedRowBehavior.Skip)
            .FromText(csv);
        foreach (var row in reader)
        {
            if (row is FooRecord) fooCount++;
        }
        Assert.Equal(2, fooCount);
    }

    // ---------- CsvAsyncStreamReader extras ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncStreamReader_LargeStream_MultiplePumps()
    {
        var sb = new StringBuilder("Name,Age\n");
        for (int i = 0; i < 5000; i++) sb.Append($"Person{i},{i}\n");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        await using var reader = Csv.CreateAsyncStreamReader(stream);
        int rows = 0;
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken)) rows++;
        Assert.Equal(5001, rows); // header + 5000
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncStreamReader_WithBom_Stripped()
    {
        byte[] bom = [0xEF, 0xBB, 0xBF];
        byte[] data = Encoding.UTF8.GetBytes("Name,Age\nAlice,30\n");
        byte[] combined = [.. bom, .. data];
        using var stream = new MemoryStream(combined);
        await using var reader = Csv.CreateAsyncStreamReader(stream);
        int rows = 0;
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken)) rows++;
        Assert.Equal(2, rows);
    }

    // ---------- CsvAsyncStreamReader and FixedWidthAsyncStreamWriter dispose ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task CsvAsyncStreamReader_DisposeAsync()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("A,B\n1,2\n"));
        var reader = Csv.CreateAsyncStreamReader(stream);
        await reader.DisposeAsync();
    }

    // ---------- CSV Writer with leaveOpen and encoding variations ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_LeaveOpenFalse_DisposesStream()
    {
        var rows = new[] { new CoveragePerson { Name = "A", Age = 1 } };
        var stream = new MemoryStream();
        await Csv.WriteToStreamAsync(
            stream,
            rows,
            leaveOpen: false,
            cancellationToken: TestContext.Current.CancellationToken);
        // Disposed → can't write to it
        Assert.False(stream.CanWrite);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task AsyncWriter_NullValueWithCustomToken()
    {
        var rows = new[] { new NullableAgePerson { Name = "Alice", Age = null } };
        using var ms = new MemoryStream();
        await Csv.WriteToStreamAsync(
            ms,
            rows,
            options: new CsvWriteOptions { NullValue = "<NULL>" },
            cancellationToken: TestContext.Current.CancellationToken);
        string text = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("<NULL>", text);
    }

    // ---------- CSV reader: WithoutHeaderRow ----------

    // ---------- CSV reader: large data exercising SIMD char path ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvReader_CharPath_LargeData_MultipleSimdChunks()
    {
        // Each row is ~120 chars; 1000 rows = 120KB → multiple SIMD chunks (64-byte vectors).
        var sb = new StringBuilder("col1,col2,col3,col4,col5,col6,col7,col8,col9,col10\n");
        for (int i = 0; i < 1000; i++)
        {
            sb.Append("value_a_").Append(i).Append(",value_b_").Append(i).Append(",value_c_").Append(i)
              .Append(",value_d_").Append(i).Append(",value_e_").Append(i).Append(",value_f_").Append(i)
              .Append(",value_g_").Append(i).Append(",value_h_").Append(i).Append(",value_i_").Append(i)
              .Append(",value_j_").Append(i).Append('\n');
        }
        using var reader = Csv.Read().FromText(sb.ToString());
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(1001, n); // header + 1000
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvReader_CharPath_QuotedFieldsInBulk()
    {
        var sb = new StringBuilder("a,b,c\n");
        for (int i = 0; i < 500; i++)
        {
            sb.Append('"').Append("with, comma ").Append(i).Append('"').Append(',');
            sb.Append('"').Append("with \"\"quote\"\" ").Append(i).Append('"').Append(',');
            sb.Append("plain_").Append(i).Append('\n');
        }
        using var reader = Csv.Read().FromText(sb.ToString());
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(501, n); // header + 500
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvReader_CharPath_CrlfLineEndings()
    {
        var sb = new StringBuilder("a,b\r\n");
        for (int i = 0; i < 200; i++) sb.Append(i).Append(",val").Append(i).Append("\r\n");
        using var reader = Csv.Read().FromText(sb.ToString());
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(201, n); // header + 200
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvReader_BytePath_LargeUtf8WithMixed()
    {
        var sb = new StringBuilder("a,b\n");
        for (int i = 0; i < 500; i++) sb.Append("Café_").Append(i).Append(",日本_").Append(i).Append('\n');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        using var reader = Csv.Read().FromStream(stream, out _);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(501, n); // header + 500
    }
}

// ---------- Plain (non-generated) records to force reflection path ----------

public class PlainLongRow { public long Value { get; set; } }
public class PlainShortRow { public short Value { get; set; } }
public class PlainByteRow { public byte Value { get; set; } }
public class PlainDoubleRow { public double Value { get; set; } }
public class PlainSingleRow { public float Value { get; set; } }
public class PlainDtRow { public DateTime Value { get; set; } }
public class PlainDtoRow { public DateTimeOffset Value { get; set; } }
public class PlainDateOnlyRow { public DateOnly Value { get; set; } }
public class PlainTimeOnlyRow { public TimeOnly Value { get; set; } }
public class PlainEnumRow { public Color Value { get; set; } }
public class PlainDecimalRow { public decimal Value { get; set; } }
public class PlainNullableLongRow { public long? Value { get; set; } }
public class PlainStringRow { public string? Name { get; set; } }

public class PlainAllTypes
{
    public long L { get; set; }
    public short S { get; set; }
    public byte B { get; set; }
    public double D { get; set; }
    public float F { get; set; }
    public bool Bo { get; set; }
    public decimal M { get; set; }
}

// Records WITHOUT [GenerateBinder] but WITH [PositionalMap] attributes
// to exercise the reflection-based FixedWidthRecordBinder + ConverterFactory.
public class ReflectAllPrimitives
{
    [PositionalMap(Start = 0, Length = 10)] public long L { get; set; }
    [PositionalMap(Start = 10, Length = 5)] public short S { get; set; }
    [PositionalMap(Start = 15, Length = 3)] public byte B { get; set; }
    [PositionalMap(Start = 18, Length = 8)] public double D { get; set; }
    [PositionalMap(Start = 26, Length = 8)] public float F { get; set; }
    [PositionalMap(Start = 34, Length = 5)] public bool Bo { get; set; }
    [PositionalMap(Start = 39, Length = 10)] public decimal M { get; set; }
    [PositionalMap(Start = 49, Length = 3)] public int I { get; set; }
}

public class ReflectDateTypes
{
    [PositionalMap(Start = 0, Length = 19)] public DateTime Dt { get; set; }
    [PositionalMap(Start = 19, Length = 25)] public DateTimeOffset Dto { get; set; }
    [PositionalMap(Start = 44, Length = 10)] public DateOnly D { get; set; }
    [PositionalMap(Start = 54, Length = 8)] public TimeOnly T { get; set; }
}

public class ReflectEnumRow
{
    [PositionalMap(Start = 0, Length = 5)] public Color Color { get; set; }
}

public class ReflectNullableTypes
{
    [PositionalMap(Start = 0, Length = 10)] public long? L { get; set; }
    [PositionalMap(Start = 10, Length = 5)] public short? S { get; set; }
    [PositionalMap(Start = 15, Length = 3)] public byte? B { get; set; }
    [PositionalMap(Start = 18, Length = 8)] public double? D { get; set; }
    [PositionalMap(Start = 26, Length = 8)] public float? F { get; set; }
    [PositionalMap(Start = 34, Length = 5)] public bool? Bo { get; set; }
    [PositionalMap(Start = 39, Length = 10)] public decimal? M { get; set; }
    [PositionalMap(Start = 49, Length = 19)] public DateTime? Dt { get; set; }
    [PositionalMap(Start = 68, Length = 10)] public DateOnly? Date { get; set; }
    [PositionalMap(Start = 78, Length = 8)] public TimeOnly? Time { get; set; }
    [PositionalMap(Start = 86, Length = 5)] public Color? Color { get; set; }
}

[global::HeroParser.GenerateBinder]
public class FooRecord
{
    public string? Type { get; set; }
    public int A { get; set; }
    public int B { get; set; }
}

[global::HeroParser.GenerateBinder]
public class BarRecord
{
    public string? Type { get; set; }
    public int A { get; set; }
    public int B { get; set; }
}
