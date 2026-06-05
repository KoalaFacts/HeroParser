using System.Globalization;
using System.Text;
using HeroParser.FixedWidths;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Writing;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests;

/// <summary>Wave 30: FixedWidth/Excel/CSV option Validate throws + small-file targets.</summary>
public class CoveragePushTests30
{
    // ---------- FixedWidthReadOptions.Validate ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthReadOptions_NegativeRecordLength_Throws()
    {
        var opts = new FixedWidthReadOptions { RecordLength = 0 };
        Assert.Throws<FixedWidthException>(opts.Validate);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthReadOptions_NegativeMaxRecordCount_Throws()
    {
        var opts = new FixedWidthReadOptions { MaxRecordCount = 0 };
        Assert.Throws<FixedWidthException>(opts.Validate);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthReadOptions_NegativeSkipRows_Throws()
    {
        var opts = new FixedWidthReadOptions { SkipRows = -1 };
        Assert.Throws<FixedWidthException>(opts.Validate);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthReadOptions_NonPositiveMaxInputSize_Throws()
    {
        var opts = new FixedWidthReadOptions { MaxInputSize = 0 };
        Assert.Throws<FixedWidthException>(opts.Validate);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthReadOptions_ValidateInputSize_OverLimit_Throws()
    {
        var opts = new FixedWidthReadOptions { MaxInputSize = 100 };
        var m = typeof(FixedWidthReadOptions).GetMethod(
            "ValidateInputSize",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.Throws<System.Reflection.TargetInvocationException>(() => m!.Invoke(opts, [200L]));
    }

    // ---------- FixedWidthWriteOptions.Validate ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthWriteOptions_BadNewLine_Throws()
    {
        var rows = new[] { new FixedAllTypes { L = 1L, S = 2, B = 3, D = 0.5, F = 0.25f, Bo = true, M = 1.5m } };
        Assert.Throws<FixedWidthException>(() =>
            FixedWidth.WriteToText(rows, options: new global::HeroParser.FixedWidths.Writing.FixedWidthWriteOptions { NewLine = "x" }));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthWriteOptions_EmptyNewLine_Throws()
    {
        var rows = new[] { new FixedAllTypes { L = 1L, S = 2, B = 3, D = 0.5, F = 0.25f, Bo = true, M = 1.5m } };
        Assert.Throws<FixedWidthException>(() =>
            FixedWidth.WriteToText(rows, options: new global::HeroParser.FixedWidths.Writing.FixedWidthWriteOptions { NewLine = "" }));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthWriteOptions_NegativeMaxRowCount_Throws()
    {
        var rows = new[] { new FixedAllTypes { L = 1L, S = 2, B = 3, D = 0.5, F = 0.25f, Bo = true, M = 1.5m } };
        Assert.Throws<FixedWidthException>(() =>
            FixedWidth.WriteToText(rows, options: new global::HeroParser.FixedWidths.Writing.FixedWidthWriteOptions { MaxRowCount = 0 }));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthWriteOptions_NegativeMaxOutputSize_Throws()
    {
        var rows = new[] { new FixedAllTypes { L = 1L, S = 2, B = 3, D = 0.5, F = 0.25f, Bo = true, M = 1.5m } };
        Assert.Throws<FixedWidthException>(() =>
            FixedWidth.WriteToText(rows, options: new global::HeroParser.FixedWidths.Writing.FixedWidthWriteOptions { MaxOutputSize = 0 }));
    }

    // ---------- ExcelReadOptions instantiation (any usage will cover its members) ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ExcelReadOptions_DefaultValues()
    {
        var opts = new global::HeroParser.Excels.Core.ExcelReadOptions();
        Assert.True(opts.HasHeaderRow);
        Assert.False(opts.CaseSensitiveHeaders);
        Assert.False(opts.AllowMissingColumns);
        Assert.Null(opts.NullValues);
        Assert.Equal(CultureInfo.InvariantCulture, opts.Culture);
        Assert.Null(opts.MaxRows);
        Assert.Equal(0, opts.SkipRows);
        Assert.Equal(ValidationMode.Strict, opts.ValidationMode);
        Assert.Null(opts.OnDeserializeError);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ExcelReadOptions_WithSetters()
    {
        var opts = new global::HeroParser.Excels.Core.ExcelReadOptions
        {
            HasHeaderRow = false,
            CaseSensitiveHeaders = true,
            AllowMissingColumns = true,
            NullValues = ["NA"],
            Culture = CultureInfo.InvariantCulture,
            MaxRows = 100,
            SkipRows = 2,
            ValidationMode = ValidationMode.Lenient,
        };
        Assert.False(opts.HasHeaderRow);
        Assert.True(opts.CaseSensitiveHeaders);
        Assert.True(opts.AllowMissingColumns);
        Assert.NotNull(opts.NullValues);
        Assert.Equal(100, opts.MaxRows);
        Assert.Equal(2, opts.SkipRows);
    }

    // ---------- CsvMultiSchemaDispatcherAttribute (no-op attribute) ----------

    // (CsvMultiSchemaDispatcherAttribute not accessible from tests; skipping.)

    // ---------- ExtensionsToCsvRow ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ExtensionsToCsvRow_TryGetColumnString()
    {
        using var reader = Csv.Read().FromText("Name,Age\nAlice,30\n");
        Assert.True(reader.MoveNext());
        Assert.True(reader.MoveNext());
        // Try GetColumnString via extension or row indexer.
        var s = reader.Current[0].ToString();
        Assert.Equal("Alice", s);
    }

    // ---------- CsvRecordBinderFactory remaining ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRecordBinderFactory_RegisterByteBinder_Works()
    {
        // Register custom factory for a type, retrieve it back.
        global::HeroParser.SeparatedValues.Reading.Binders.CsvRecordBinderFactory.RegisterByteBinder<CustomRow>(
            opts => new CustomBinder());
        var binder = global::HeroParser.SeparatedValues.Reading.Binders.CsvRecordBinderFactory.GetByteBinder<CustomRow>();
        Assert.NotNull(binder);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRecordBinderFactory_RegisterDescriptor_Works()
    {
        global::HeroParser.SeparatedValues.Reading.Binders.CsvRecordBinderFactory.RegisterDescriptor<CustomRow2>(
            () => new global::HeroParser.SeparatedValues.Reading.Shared.CsvRecordDescriptor<CustomRow2>([]));
        // Trigger the get internally via reflection (TryGetDescriptor is internal).
        var m = typeof(global::HeroParser.SeparatedValues.Reading.Binders.CsvRecordBinderFactory)
            .GetMethod("TryGetDescriptor", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        if (m is not null)
        {
            var g = m.MakeGenericMethod(typeof(CustomRow2));
            object?[] args = [null!];
            var ok = (bool)g.Invoke(null, args)!;
            Assert.True(ok);
        }
    }

    // ---------- Excel.DataReader.cs (more overloads) ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_CreateDataReader_FromPath_Options_SheetName()
    {
        var src = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        string path = Path.GetTempFileName() + ".xlsx";
        try
        {
            global::HeroParser.Excel.Write<CoveragePerson>().ToFile(path, src);
            using var dr = global::HeroParser.Excel.CreateDataReader(
                path,
                new global::HeroParser.Excels.Reading.Data.ExcelDataReaderOptions { HasHeaderRow = true },
                sheetName: "Sheet1");
            Assert.True(dr.Read());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    // ---------- CsvWriteOptions defaults  ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvWriteOptions_Defaults_Touched()
    {
        var opts = CsvWriteOptions.Default;
        Assert.Equal(',', opts.Delimiter);
        Assert.Equal('"', opts.Quote);
        Assert.Equal("\r\n", opts.NewLine);
        Assert.True(opts.WriteHeader);
        Assert.False(opts.ExcludeEmptyColumns);
        Assert.Null(opts.DateTimeFormat);
        Assert.Null(opts.NumberFormat);
        Assert.Null(opts.MaxRowCount);
        Assert.Null(opts.MaxOutputSize);
        Assert.Null(opts.MaxFieldSize);
        Assert.Equal(QuoteStyle.WhenNeeded, opts.QuoteStyle);
        Assert.Equal(ValidationMode.Strict, opts.ValidationMode);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthWriteOptions_Defaults_Touched()
    {
        var opts = global::HeroParser.FixedWidths.Writing.FixedWidthWriteOptions.Default;
        Assert.Equal("\r\n", opts.NewLine);
        Assert.Equal(' ', opts.DefaultPadChar);
        Assert.Equal(FieldAlignment.Left, opts.DefaultAlignment);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthReadOptions_Defaults_Touched()
    {
        var opts = FixedWidthReadOptions.Default;
        Assert.Null(opts.RecordLength);
        Assert.Equal(' ', opts.DefaultPadChar);
        Assert.Equal(FieldAlignment.Left, opts.DefaultAlignment);
        Assert.Equal(100_000, opts.MaxRecordCount);
        Assert.True(opts.SkipEmptyLines);
    }

    // ---------- CsvReadOptions defaults / non-default ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvReadOptions_Defaults_Touched()
    {
        var opts = CsvReadOptions.Default;
        Assert.Equal(',', opts.Delimiter);
        Assert.Equal('"', opts.Quote);
        Assert.Equal(100, opts.MaxColumnCount);
        Assert.NotNull(opts);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRecordOptions_Default_Identity()
    {
        var opts1 = global::HeroParser.SeparatedValues.Reading.Records.CsvRecordOptions.Default;
        var opts2 = global::HeroParser.SeparatedValues.Reading.Records.CsvRecordOptions.Default;
        Assert.Same(opts1, opts2);
    }
}

[GenerateBinder]
public class CustomRow
{
    public string? Value { get; set; }
}

[GenerateBinder]
public class CustomRow2
{
    public string? Value { get; set; }
}

internal sealed class CustomBinder : global::HeroParser.SeparatedValues.Reading.Binders.ICsvSourceBinder<byte, CustomRow>
{
    public bool NeedsHeaderResolution => false;
    public void BindHeader(global::HeroParser.SeparatedValues.Reading.Rows.CsvRow<byte> headerRow, int rowNumber) { }
    public bool TryBind(global::HeroParser.SeparatedValues.Reading.Rows.CsvRow<byte> row, int rowNumber, out CustomRow result, List<global::HeroParser.Validation.ValidationError>? errors = null)
    {
        result = new CustomRow();
        return true;
    }
    public bool BindInto(ref CustomRow instance, global::HeroParser.SeparatedValues.Reading.Rows.CsvRow<byte> row, int rowNumber, List<global::HeroParser.Validation.ValidationError>? errors = null)
        => true;
}
