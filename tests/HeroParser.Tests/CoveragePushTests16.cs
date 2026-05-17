using System.IO.Pipelines;
using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Shared;
using HeroParser.SeparatedValues.Writing;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests;

/// <summary>Wave 16: force SIMD fallback paths via HardwareCapabilities.Override.</summary>
[Collection("HardwareCaps")] // serialize: overrides are AsyncLocal but parallel tests can still race
public class CoveragePushTests16
{
    // ---------- Scalar fallback path (avx2=false) ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_BytePath_ScalarFallback_BasicRead()
    {
        using var _scope = HardwareCapabilities.Override(avx2: false, avx512BW: false);
        var sb = new StringBuilder("a,b,c\n");
        for (int i = 0; i < 200; i++) sb.Append(i).Append(",x,y\n");

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        using var reader = Csv.Read().FromStream(stream, out _);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(201, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_CharPath_ScalarFallback_BasicRead()
    {
        using var _scope = HardwareCapabilities.Override(avx2: false, avx512BW: false);
        var sb = new StringBuilder("a,b,c\n");
        for (int i = 0; i < 200; i++) sb.Append(i).Append(",x,y\n");

        using var reader = Csv.Read().FromText(sb.ToString());
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(201, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_BytePath_Scalar_QuotedFields()
    {
        using var _scope = HardwareCapabilities.Override(avx2: false, avx512BW: false);
        string csv = "a,b\n\"hello, world\",1\n\"with \"\"quotes\"\"\",2\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        using var reader = Csv.Read().FromStream(stream, out _);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(3, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_CharPath_Scalar_QuotedFields()
    {
        using var _scope = HardwareCapabilities.Override(avx2: false, avx512BW: false);
        string csv = "a,b\n\"hello, world\",1\n\"with \"\"quotes\"\"\",2\n";
        using var reader = Csv.Read().FromText(csv);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(3, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_Scalar_MultilineQuoted()
    {
        using var _scope = HardwareCapabilities.Override(avx2: false, avx512BW: false);
        string csv = "a,b\n\"line1\nline2\",1\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        using var reader = Csv.Read().AllowNewlinesInQuotes().FromStream(stream, out _);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(2, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_Scalar_CrLf()
    {
        using var _scope = HardwareCapabilities.Override(avx2: false, avx512BW: false);
        string csv = "a,b\r\n1,2\r\n3,4\r\n5,6\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        using var reader = Csv.Read().FromStream(stream, out _);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(4, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_Scalar_Comment()
    {
        using var _scope = HardwareCapabilities.Override(avx2: false, avx512BW: false);
        string csv = "# c1\na,b\n# c2\n1,2\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        using var reader = Csv.Read().WithCommentCharacter('#').FromStream(stream, out _);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(2, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_Scalar_Escape()
    {
        using var _scope = HardwareCapabilities.Override(avx2: false, avx512BW: false);
        string csv = "a,b\nhi\\,there,2\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        using var reader = Csv.Read().WithEscapeCharacter('\\').FromStream(stream, out _);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(2, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_Scalar_TrimFields()
    {
        using var _scope = HardwareCapabilities.Override(avx2: false, avx512BW: false);
        string csv = "a,b\n  Alice  ,  30  \n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        using var reader = Csv.Read().TrimFields().FromStream(stream, out _);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(2, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_Scalar_DisableQuotes()
    {
        using var _scope = HardwareCapabilities.Override(avx2: false, avx512BW: false);
        string csv = "a,b\n\"value\",2\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        using var reader = Csv.Read().DisableQuotedFields().FromStream(stream, out _);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(2, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_Scalar_TooManyColumns_Throws()
    {
        using var _scope = HardwareCapabilities.Override(avx2: false, avx512BW: false);
        var sb = new StringBuilder();
        for (int i = 0; i < 200; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('a');
        }
        sb.Append('\n');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        Assert.Throws<CsvException>(() =>
        {
            using var reader = Csv.Read().FromStream(stream, out _);
            while (reader.MoveNext()) { }
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_Scalar_MaxFieldSize_Throws()
    {
        using var _scope = HardwareCapabilities.Override(avx2: false, avx512BW: false);
        string csv = "verylongfield,2\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        Assert.Throws<CsvException>(() =>
        {
            using var reader = Csv.Read().WithMaxFieldSize(3).FromStream(stream, out _);
            while (reader.MoveNext()) { }
        });
    }

    // ---------- AVX2 path, without PCLMULQDQ (force AVX-512 off + PCLMULQDQ off) ----------
    // This exercises the slow quote-tracking branch (not the SIMD-PCLMULQDQ fast path).

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_BytePath_Avx2_NoPclmulqdq_QuotedFields()
    {
        if (!Avx2Available()) return;
        using var _scope = HardwareCapabilities.Override(avx512BW: false, pclmulqdq: false);
        var sb = new StringBuilder("a,b\n");
        for (int i = 0; i < 200; i++)
        {
            sb.Append('"').Append("val ").Append(i).Append("\"\"quoted\"\"").Append('"').Append(',').Append(i).Append('\n');
        }
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        using var reader = Csv.Read().FromStream(stream, out _);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(201, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_CharPath_Avx2_NoPclmulqdq_QuotedFields()
    {
        if (!Avx2Available()) return;
        using var _scope = HardwareCapabilities.Override(avx512BW: false, pclmulqdq: false);
        var sb = new StringBuilder("a,b\n");
        for (int i = 0; i < 200; i++)
        {
            sb.Append('"').Append("val ").Append(i).Append("\"\"quoted\"\"").Append('"').Append(',').Append(i).Append('\n');
        }
        using var reader = Csv.Read().FromText(sb.ToString());
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(201, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_BytePath_Avx2_NoPclmulqdq_MultilineQuoted()
    {
        if (!Avx2Available()) return;
        using var _scope = HardwareCapabilities.Override(avx512BW: false, pclmulqdq: false);
        var sb = new StringBuilder("a,b\n");
        for (int i = 0; i < 50; i++)
        {
            sb.Append('"').Append("line ").Append(i).Append("\nline2 ").Append(i).Append('"').Append(',').Append(i).Append('\n');
        }
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        using var reader = Csv.Read().AllowNewlinesInQuotes().FromStream(stream, out _);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(51, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_BytePath_Avx2_LargeUnquotedData()
    {
        if (!Avx2Available()) return;
        using var _scope = HardwareCapabilities.Override(avx512BW: false);
        var sb = new StringBuilder("c1,c2,c3,c4,c5,c6,c7,c8\n");
        for (int i = 0; i < 1000; i++)
        {
            sb.Append("alpha,beta,gamma,delta,epsilon,zeta,eta,theta\n");
        }
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        using var reader = Csv.Read().FromStream(stream, out _);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(1001, n);
    }

    // ---------- AVX-512 path explicitly (if hardware supports) ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_BytePath_Avx512BW_LargeData()
    {
        if (!Avx512Available()) return;
        // No override - use hardware default which has AVX-512.
        var sb = new StringBuilder("c1,c2,c3,c4,c5\n");
        for (int i = 0; i < 5000; i++) sb.Append("a,b,c,d,e\n");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        using var reader = Csv.Read().FromStream(stream, out _);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(5001, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_CharPath_Avx512BW_LargeData()
    {
        if (!Avx512Available()) return;
        var sb = new StringBuilder("c1,c2,c3,c4,c5\n");
        for (int i = 0; i < 5000; i++) sb.Append("a,b,c,d,e\n");
        using var reader = Csv.Read().FromText(sb.ToString());
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(5001, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_Avx512BW_QuotedFields()
    {
        if (!Avx512Available()) return;
        var sb = new StringBuilder("a,b\n");
        for (int i = 0; i < 500; i++)
        {
            sb.Append('"').Append("val ").Append(i).Append('"').Append(',').Append(i).Append('\n');
        }
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        using var reader = Csv.Read().FromStream(stream, out _);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(501, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_Avx512BW_MultilineQuoted()
    {
        if (!Avx512Available()) return;
        var sb = new StringBuilder("a,b\n");
        for (int i = 0; i < 100; i++)
        {
            sb.Append('"').Append("line ").Append(i).Append("\nline2").Append('"').Append(',').Append(i).Append('\n');
        }
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        using var reader = Csv.Read().AllowNewlinesInQuotes().FromStream(stream, out _);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(101, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_Avx512BW_TooManyColumns_Throws()
    {
        if (!Avx512Available()) return;
        var sb = new StringBuilder();
        for (int i = 0; i < 200; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('a');
        }
        sb.Append('\n');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        Assert.Throws<CsvException>(() =>
        {
            using var reader = Csv.Read().FromStream(stream, out _);
            while (reader.MoveNext()) { }
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_Avx512BW_MaxFieldSize_Throws()
    {
        if (!Avx512Available()) return;
        string csv = "verylongfieldvalue,2\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        Assert.Throws<CsvException>(() =>
        {
            using var reader = Csv.Read().WithMaxFieldSize(3).FromStream(stream, out _);
            while (reader.MoveNext()) { }
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_Avx512BW_TrimFields()
    {
        if (!Avx512Available()) return;
        var sb = new StringBuilder("a,b\n");
        for (int i = 0; i < 100; i++) sb.Append("  value  ,  other  \n");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        using var reader = Csv.Read().TrimFields().FromStream(stream, out _);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(101, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_Avx512BW_NoPclmulqdq_QuotedFields()
    {
        if (!Avx512Available()) return;
        using var _scope = HardwareCapabilities.Override(pclmulqdq: false);
        var sb = new StringBuilder("a,b\n");
        for (int i = 0; i < 200; i++)
        {
            sb.Append('"').Append("val ").Append(i).Append("\"\"q\"\"").Append('"').Append(',').Append(i).Append('\n');
        }
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        using var reader = Csv.Read().FromStream(stream, out _);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(201, n);
    }

    // ---------- Pipe reader with scalar fallback ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task PipeReader_Scalar_LargeData()
    {
        using var _scope = HardwareCapabilities.Override(avx2: false, avx512BW: false);
        var sb = new StringBuilder("a,b\n");
        for (int i = 0; i < 500; i++) sb.Append("val").Append(i).Append(",x\n");

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        int n = 0;
        await foreach (var _ in Csv.ReadFromPipeReaderAsync(
            PipeReader.Create(stream),
            cancellationToken: TestContext.Current.CancellationToken))
        {
            n++;
        }
        Assert.Equal(501, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task PipeSequenceReader_Scalar_LargeData()
    {
        using var _scope = HardwareCapabilities.Override(avx2: false, avx512BW: false);
        var sb = new StringBuilder("a,b\n");
        for (int i = 0; i < 500; i++) sb.Append("val").Append(i).Append(",x\n");

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        await using var reader = Csv.Read().FromPipeReaderAsync(PipeReader.Create(stream));
        int n = 0;
        while (await reader.MoveNextAsync(TestContext.Current.CancellationToken)) n++;
        Assert.Equal(501, n);
    }

    private static bool Avx2Available()
        => System.Runtime.Intrinsics.X86.Avx2.IsSupported;

    private static bool Avx512Available()
        => System.Runtime.Intrinsics.X86.Avx512BW.IsSupported;
}

[CollectionDefinition("HardwareCaps", DisableParallelization = true)]
public class HardwareCapsCollection { }
