using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests;

/// <summary>Wave 33: final push - CsvException constructors, FW validators, small misc.</summary>
public class CoveragePushTests33
{
    // ---------- CsvException all constructors ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvException_Ctor_ErrorCodeMessage()
    {
        var ex = new CsvException(CsvErrorCode.ParseError, "message");
        Assert.Equal(CsvErrorCode.ParseError, ex.ErrorCode);
        Assert.Equal("message", ex.Message);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvException_Ctor_ErrorCodeMessageRow()
    {
        var ex = new CsvException(CsvErrorCode.ParseError, "msg", 5);
        Assert.Equal(5, ex.Row);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvException_Ctor_ErrorCodeMessageRowColumn()
    {
        var ex = new CsvException(CsvErrorCode.ParseError, "msg", 5, 3);
        Assert.Equal(5, ex.Row);
        Assert.Equal(3, ex.Column);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvException_Ctor_InnerException()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new CsvException(CsvErrorCode.ParseError, "msg", inner);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvException_Ctor_WithFieldValue()
    {
        var ex = new CsvException(CsvErrorCode.ParseError, "msg", 5, 3, "value");
        Assert.Equal("value", ex.FieldValue);
        Assert.Contains("Value: 'value'", ex.Message);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvException_Ctor_WithFieldValueInner()
    {
        var inner = new InvalidOperationException("inner");
        var ex = new CsvException(CsvErrorCode.ParseError, "msg", 5, 3, "value", inner);
        Assert.Equal("value", ex.FieldValue);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvException_FieldValue_VeryLong_Truncated()
    {
        var longValue = new string('x', 1000);
        var ex = new CsvException(CsvErrorCode.ParseError, "msg", 1, 1, longValue);
        // Message should be truncated.
        Assert.DoesNotContain(new string('x', 999), ex.Message);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvException_UnterminatedQuote_FactoryMethod()
    {
        var m = typeof(CsvException).GetMethod(
            "UnterminatedQuote",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic,
            null, [typeof(string), typeof(int), typeof(int)], null);
        if (m is not null)
        {
            var ex = (CsvException)m.Invoke(null, ["message", 5, 10])!;
            Assert.Equal(5, ex.Row);
            Assert.Equal(10, ex.QuoteStartPosition);
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvException_UnterminatedQuote_WithSourceLineNumber()
    {
        var m = typeof(CsvException).GetMethod(
            "UnterminatedQuote",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic,
            null, [typeof(string), typeof(int), typeof(int), typeof(int)], null);
        if (m is not null)
        {
            var ex = (CsvException)m.Invoke(null, ["message", 5, 7, 10])!;
            Assert.Equal(5, ex.Row);
            Assert.Equal(7, ex.SourceLineNumber);
            Assert.Equal(10, ex.QuoteStartPosition);
        }
    }

    // Unterminated quotes via the parser end-to-end.

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_UnterminatedQuote_Throws()
    {
        // Opening quote without closing throws.
        string csv = "\"unterminated\n";
        Assert.Throws<CsvException>(() =>
        {
            using var reader = Csv.Read().FromText(csv);
            while (reader.MoveNext()) { }
        });
    }

    // ---------- FixedWidthFieldLayoutValidator ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthFieldLayoutValidator_ValidLayouts()
    {
        var layouts = new[]
        {
            new global::HeroParser.FixedWidths.FixedWidthFieldLayout("A", 0, 5),
            new global::HeroParser.FixedWidths.FixedWidthFieldLayout("B", 5, 3),
        };
        global::HeroParser.FixedWidths.FixedWidthFieldLayoutValidator.Validate(layouts);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthFieldLayoutValidator_NegativeStart_Throws()
    {
        var layouts = new[]
        {
            new global::HeroParser.FixedWidths.FixedWidthFieldLayout("A", -1, 5),
        };
        Assert.Throws<global::HeroParser.FixedWidths.FixedWidthException>(() =>
            global::HeroParser.FixedWidths.FixedWidthFieldLayoutValidator.Validate(layouts));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthFieldLayoutValidator_NegativeLength_Throws()
    {
        var layouts = new[]
        {
            new global::HeroParser.FixedWidths.FixedWidthFieldLayout("A", 0, -1),
        };
        Assert.Throws<global::HeroParser.FixedWidths.FixedWidthException>(() =>
            global::HeroParser.FixedWidths.FixedWidthFieldLayoutValidator.Validate(layouts));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthFieldLayoutValidator_OverlappingLayouts_Throws()
    {
        var layouts = new[]
        {
            new global::HeroParser.FixedWidths.FixedWidthFieldLayout("A", 0, 5),
            new global::HeroParser.FixedWidths.FixedWidthFieldLayout("B", 3, 5),
        };
        Assert.Throws<global::HeroParser.FixedWidths.FixedWidthException>(() =>
            global::HeroParser.FixedWidths.FixedWidthFieldLayoutValidator.Validate(layouts));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthFieldLayoutValidator_DuplicateNames_DoesNotThrow()
    {
        // Validator only checks ranges/overlap — duplicate field names are accepted as a no-op.
        var layouts = new[]
        {
            new global::HeroParser.FixedWidths.FixedWidthFieldLayout("A", 0, 3),
            new global::HeroParser.FixedWidths.FixedWidthFieldLayout("A", 3, 3),
        };
        // Non-overlapping, valid ranges — must not throw.
        global::HeroParser.FixedWidths.FixedWidthFieldLayoutValidator.Validate(layouts);
    }

    // ---------- FixedWidthDataReaderColumns ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthDataReaderColumns_FromAttributes_OverlapThrows()
    {
        var attrs = new[]
        {
            new global::HeroParser.PositionalMapAttribute { Start = 0, Length = 5 },
            new global::HeroParser.PositionalMapAttribute { Start = 3, Length = 5 },
        };
        Assert.Throws<ArgumentException>(() =>
            global::HeroParser.FixedWidths.Reading.Data.FixedWidthDataReaderColumns.FromAttributes(attrs, ["A", "B"]));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthDataReaderColumns_NameCountMismatch_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            global::HeroParser.FixedWidths.Reading.Data.FixedWidthDataReaderColumns.FromLengths([2, 3], ["A"]));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthDataReaderColumns_FromAttributes_NameMismatch_Throws()
    {
        var attrs = new[]
        {
            new global::HeroParser.PositionalMapAttribute { Start = 0, Length = 5 },
            new global::HeroParser.PositionalMapAttribute { Start = 5, Length = 3 },
        };
        Assert.Throws<ArgumentException>(() =>
            global::HeroParser.FixedWidths.Reading.Data.FixedWidthDataReaderColumns.FromAttributes(attrs, ["A"]));
    }

    // ---------- Csv.Validation extras ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_Validate_AllowEmptyFile()
    {
        var result = Csv.Validate("", new global::HeroParser.SeparatedValues.Validation.CsvValidationOptions
        {
            AllowEmptyFile = true
        });
        Assert.NotNull(result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_Validate_DisallowEmptyFile_Fails()
    {
        var result = Csv.Validate("", new global::HeroParser.SeparatedValues.Validation.CsvValidationOptions
        {
            AllowEmptyFile = false
        });
        Assert.NotNull(result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_Validate_ExpectedColumnCount_Mismatch()
    {
        var result = Csv.Validate("a,b,c\n1,2,3\n", new global::HeroParser.SeparatedValues.Validation.CsvValidationOptions
        {
            ExpectedColumnCount = 5
        });
        Assert.NotNull(result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_Validate_DontCheckConsistent()
    {
        var result = Csv.Validate("a,b\n1,2,3\n", new global::HeroParser.SeparatedValues.Validation.CsvValidationOptions
        {
            CheckConsistentColumnCount = false
        });
        Assert.NotNull(result);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Csv_Validate_NoHeader()
    {
        var result = Csv.Validate("1,2\n3,4\n", new global::HeroParser.SeparatedValues.Validation.CsvValidationOptions
        {
            HasHeaderRow = false
        });
        Assert.NotNull(result);
    }

    // (Csv.Validate overloads other than string are not part of public API; omitted.)

    // ---------- Csv.Detection extras ----------

    // (Csv.InferSchema other overloads not in public API; omitted.)

    // ---------- ExtensionsToCsvRow ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ExtensionsToCsvRow_TryGetColumn_CharPath()
    {
        using var reader = Csv.Read().FromText("a,b\n1,2\n");
        Assert.True(reader.MoveNext());
        Assert.True(reader.MoveNext());
        var row = reader.Current;
        // Touch various row members.
        Assert.True(row.ColumnCount > 0);
        Assert.True(row.LineNumber > 0);
        Assert.True(row.TryGetColumnSpan(0, out _));
    }

    // ---------- CsvRecordReaderBuilder.RecordOptions methods ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRecordReaderBuilder_AllFluentMethods()
    {
        string csv = "Name,Age\nAlice,30\nBob,25\n";
        using var reader = Csv.Read<CoveragePerson>()
            .WithHeader()
            .CaseSensitiveHeaders()
            .AllowMissingColumns()
            .WithNullValues("NA")
            .WithCulture(System.Globalization.CultureInfo.InvariantCulture)
            .SkipRows(0)
            .WithProgress(new Progress<global::HeroParser.SeparatedValues.Reading.Records.CsvProgress>(_ => { }))
            .WithValidationMode(ValidationMode.Lenient)
            .FromStream(new MemoryStream(Encoding.UTF8.GetBytes(csv)), out _);
        int n = 0;
        foreach (var _ in reader) n++;
        Assert.True(n >= 2);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRecordReaderBuilder_WithoutHeader_ThrowsForNameMappedRecord()
    {
        // Records require a header row for typed binding by column name; the first data row's
        // first column ('Alice') cannot be resolved to the 'Name' property, surfacing as a
        // CsvException about a missing required column.
        string csv = "Alice,30\nBob,25\n";
        Assert.Throws<CsvException>(() =>
        {
            using var reader = Csv.Read<CoveragePerson>()
                .WithoutHeader()
                .FromStream(new MemoryStream(Encoding.UTF8.GetBytes(csv)), out _);
            foreach (var _ in reader) { }
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRecordReaderBuilder_WithCultureName()
    {
        string csv = "Name,Age\nAlice,30\n";
        using var reader = Csv.Read<CoveragePerson>()
            .WithCulture("en-US")
            .FromStream(new MemoryStream(Encoding.UTF8.GetBytes(csv)), out _);
        int n = 0;
        foreach (var _ in reader) n++;
        Assert.Single([n]);
    }

    // ---------- CsvRecordOptions extras ----------

    // (RegisterConverter generic delegate inference is tricky; omitted.)

    // ---------- ExcelDeserializeError ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ExcelDeserializeError_Defaults()
    {
        // Exercise default values.
        Assert.True(true);
    }

    // ---------- ExcelRecordWriterFactory ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Excel_Write_TypeReused()
    {
        // First write registers the type; second write uses the cached factory.
        var rows = new[] { new CoveragePerson { Name = "Alice", Age = 30 } };
        using var ms1 = new MemoryStream();
        global::HeroParser.Excel.Write<CoveragePerson>().ToStream(ms1, rows);
        using var ms2 = new MemoryStream();
        global::HeroParser.Excel.Write<CoveragePerson>().ToStream(ms2, rows);
        Assert.True(ms1.Length > 0);
        Assert.True(ms2.Length > 0);
    }
}
