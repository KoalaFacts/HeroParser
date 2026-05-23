using System.IO.Pipelines;
using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Shared;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests;

/// <summary>Wave 17: explicit AVX2 char path + AVX-512 char path + AVX2 byte path + non-PCLMULQDQ.</summary>
[Collection("HardwareCaps")]
public class CoveragePushTests17
{
    private static bool Avx2 => System.Runtime.Intrinsics.X86.Avx2.IsSupported;
    private static bool Avx512BW => System.Runtime.Intrinsics.X86.Avx512BW.IsSupported;

    // ---------- AVX2 char path (force AVX-512 off, keep AVX2 on) ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_CharPath_Avx2_LargeData()
    {
        if (!Avx2) return;
        using var _scope = HardwareCapabilities.Override(avx512BW: false);
        var sb = new StringBuilder("c1,c2,c3,c4,c5,c6,c7,c8\n");
        for (int i = 0; i < 1000; i++) sb.Append("alpha,beta,gamma,delta,epsilon,zeta,eta,theta\n");

        using var reader = Csv.Read().FromText(sb.ToString());
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(1001, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_CharPath_Avx2_TooManyColumns_Throws()
    {
        // This hits the AVX2 char-path "else" branch (AppendColumnUnchecked) at lines 1529-1550
        // when a chunk's delimiters push columnCount + delimCount over columnCapacity.
        if (!Avx2) return;
        using var _scope = HardwareCapabilities.Override(avx512BW: false);
        var sb = new StringBuilder();
        for (int i = 0; i < 300; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('a');
        }
        sb.Append('\n');

        Assert.Throws<CsvException>(() =>
        {
            using var reader = Csv.Read().FromText(sb.ToString());
            while (reader.MoveNext()) { }
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_CharPath_Avx2_MaxFieldSize_Throws()
    {
        if (!Avx2) return;
        using var _scope = HardwareCapabilities.Override(avx512BW: false);
        string csv = "verylongvalue,short\n";
        Assert.Throws<CsvException>(() =>
        {
            using var reader = Csv.Read().WithMaxFieldSize(3).FromText(csv);
            while (reader.MoveNext()) { }
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_CharPath_Avx2_QuotedFields()
    {
        if (!Avx2) return;
        using var _scope = HardwareCapabilities.Override(avx512BW: false);
        var sb = new StringBuilder("a,b\n");
        for (int i = 0; i < 500; i++)
        {
            sb.Append('"').Append("val ").Append(i).Append("\"\"q\"\"").Append('"').Append(',').Append(i).Append('\n');
        }
        using var reader = Csv.Read().FromText(sb.ToString());
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(501, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_CharPath_Avx2_MultilineQuoted()
    {
        if (!Avx2) return;
        using var _scope = HardwareCapabilities.Override(avx512BW: false);
        var sb = new StringBuilder("a,b\n");
        for (int i = 0; i < 100; i++)
        {
            sb.Append('"').Append("line ").Append(i).Append("\nline2 ").Append(i).Append('"').Append(',').Append(i).Append('\n');
        }
        using var reader = Csv.Read().AllowNewlinesInQuotes().FromText(sb.ToString());
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(101, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_CharPath_Avx2_NoPclmulqdq_Quoted()
    {
        if (!Avx2) return;
        using var _scope = HardwareCapabilities.Override(avx512BW: false, pclmulqdq: false);
        var sb = new StringBuilder("a,b\n");
        for (int i = 0; i < 300; i++)
        {
            sb.Append('"').Append("text ").Append(i).Append('"').Append(',').Append(i).Append('\n');
        }
        using var reader = Csv.Read().FromText(sb.ToString());
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(301, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_CharPath_Avx2_TrimFields()
    {
        if (!Avx2) return;
        using var _scope = HardwareCapabilities.Override(avx512BW: false);
        var sb = new StringBuilder("a,b\n");
        for (int i = 0; i < 200; i++) sb.Append("  value").Append(i).Append("  ,  other  \n");
        using var reader = Csv.Read().TrimFields().FromText(sb.ToString());
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(201, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_CharPath_Avx2_Comment()
    {
        if (!Avx2) return;
        using var _scope = HardwareCapabilities.Override(avx512BW: false);
        var sb = new StringBuilder();
        for (int i = 0; i < 200; i++)
        {
            if (i % 5 == 0) sb.Append("# comment ").Append(i).Append('\n');
            else sb.Append("v").Append(i).Append(",x\n");
        }
        using var reader = Csv.Read().WithCommentCharacter('#').FromText(sb.ToString());
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.True(n >= 100);
    }

    // ---------- AVX2 byte path: force AVX-512 off (when available) ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_BytePath_Avx2_LargeData()
    {
        if (!Avx2) return;
        using var _scope = HardwareCapabilities.Override(avx512BW: false);
        var sb = new StringBuilder("c1,c2,c3,c4,c5,c6,c7,c8\n");
        for (int i = 0; i < 1000; i++) sb.Append("alpha,beta,gamma,delta,epsilon,zeta,eta,theta\n");

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        using var reader = Csv.Read().FromStream(stream, out _);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(1001, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_BytePath_Avx2_TooManyColumns_Throws()
    {
        if (!Avx2) return;
        using var _scope = HardwareCapabilities.Override(avx512BW: false);
        var sb = new StringBuilder();
        for (int i = 0; i < 300; i++)
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
    public void CsvRowParser_BytePath_Avx2_MaxFieldSize_Throws()
    {
        if (!Avx2) return;
        using var _scope = HardwareCapabilities.Override(avx512BW: false);
        string csv = "verylongvalue,short\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        Assert.Throws<CsvException>(() =>
        {
            using var reader = Csv.Read().WithMaxFieldSize(3).FromStream(stream, out _);
            while (reader.MoveNext()) { }
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_BytePath_Avx2_MultilineQuoted()
    {
        if (!Avx2) return;
        using var _scope = HardwareCapabilities.Override(avx512BW: false);
        var sb = new StringBuilder("a,b\n");
        for (int i = 0; i < 100; i++)
        {
            sb.Append('"').Append("line ").Append(i).Append("\nline2 ").Append(i).Append('"').Append(',').Append(i).Append('\n');
        }
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        using var reader = Csv.Read().AllowNewlinesInQuotes().FromStream(stream, out _);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(101, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_BytePath_Avx2_TrimFields()
    {
        if (!Avx2) return;
        using var _scope = HardwareCapabilities.Override(avx512BW: false);
        var sb = new StringBuilder("a,b\n");
        for (int i = 0; i < 200; i++) sb.Append("  value").Append(i).Append("  ,  other  \n");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        using var reader = Csv.Read().TrimFields().FromStream(stream, out _);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(201, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_BytePath_Avx2_Comment()
    {
        if (!Avx2) return;
        using var _scope = HardwareCapabilities.Override(avx512BW: false);
        var sb = new StringBuilder();
        for (int i = 0; i < 200; i++)
        {
            if (i % 5 == 0) sb.Append("# comment ").Append(i).Append('\n');
            else sb.Append("v").Append(i).Append(",x\n");
        }
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        using var reader = Csv.Read().WithCommentCharacter('#').FromStream(stream, out _);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.True(n >= 100);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_BytePath_Avx2_Escape()
    {
        if (!Avx2) return;
        using var _scope = HardwareCapabilities.Override(avx512BW: false);
        var sb = new StringBuilder("a,b\n");
        for (int i = 0; i < 200; i++) sb.Append("hi\\,").Append(i).Append(",x\n");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        using var reader = Csv.Read().WithEscapeCharacter('\\').FromStream(stream, out _);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(201, n);
    }

    // ---------- AVX-512 char path (need AVX-512 hardware) ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_CharPath_Avx512BW_LargeData()
    {
        if (!Avx512BW) return;
        // Default hardware use - AVX-512 path active.
        var sb = new StringBuilder("c1,c2,c3,c4,c5,c6,c7,c8\n");
        for (int i = 0; i < 1000; i++) sb.Append("alpha,beta,gamma,delta,epsilon,zeta,eta,theta\n");

        using var reader = Csv.Read().FromText(sb.ToString());
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(1001, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_CharPath_Avx512BW_TooManyColumns_Throws()
    {
        if (!Avx512BW) return;
        var sb = new StringBuilder();
        for (int i = 0; i < 300; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('a');
        }
        sb.Append('\n');

        Assert.Throws<CsvException>(() =>
        {
            using var reader = Csv.Read().FromText(sb.ToString());
            while (reader.MoveNext()) { }
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_CharPath_Avx512BW_QuotedFields()
    {
        if (!Avx512BW) return;
        var sb = new StringBuilder("a,b\n");
        for (int i = 0; i < 500; i++)
        {
            sb.Append('"').Append("val ").Append(i).Append("\"\"q\"\"").Append('"').Append(',').Append(i).Append('\n');
        }
        using var reader = Csv.Read().FromText(sb.ToString());
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(501, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_CharPath_Avx512BW_MultilineQuoted()
    {
        if (!Avx512BW) return;
        var sb = new StringBuilder("a,b\n");
        for (int i = 0; i < 100; i++)
        {
            sb.Append('"').Append("line ").Append(i).Append("\nline2").Append('"').Append(',').Append(i).Append('\n');
        }
        using var reader = Csv.Read().AllowNewlinesInQuotes().FromText(sb.ToString());
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(101, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_CharPath_Avx512BW_MaxFieldSize_Throws()
    {
        if (!Avx512BW) return;
        string csv = "verylongvalue,short\n";
        Assert.Throws<CsvException>(() =>
        {
            using var reader = Csv.Read().WithMaxFieldSize(3).FromText(csv);
            while (reader.MoveNext()) { }
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_CharPath_Avx512BW_TrimFields()
    {
        if (!Avx512BW) return;
        var sb = new StringBuilder("a,b\n");
        for (int i = 0; i < 200; i++) sb.Append("  value").Append(i).Append("  ,  other  \n");
        using var reader = Csv.Read().TrimFields().FromText(sb.ToString());
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(201, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_CharPath_Avx512BW_Comment()
    {
        if (!Avx512BW) return;
        var sb = new StringBuilder();
        for (int i = 0; i < 200; i++)
        {
            if (i % 5 == 0) sb.Append("# comment ").Append(i).Append('\n');
            else sb.Append("v").Append(i).Append(",x\n");
        }
        using var reader = Csv.Read().WithCommentCharacter('#').FromText(sb.ToString());
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.True(n >= 100);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_CharPath_Avx512BW_Escape()
    {
        if (!Avx512BW) return;
        var sb = new StringBuilder("a,b\n");
        for (int i = 0; i < 200; i++) sb.Append("hi\\,").Append(i).Append(",x\n");
        using var reader = Csv.Read().WithEscapeCharacter('\\').FromText(sb.ToString());
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(201, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_CharPath_Avx512BW_NoPclmulqdq_Quoted()
    {
        if (!Avx512BW) return;
        using var _scope = HardwareCapabilities.Override(pclmulqdq: false);
        var sb = new StringBuilder("a,b\n");
        for (int i = 0; i < 300; i++)
        {
            sb.Append('"').Append("text ").Append(i).Append('"').Append(',').Append(i).Append('\n');
        }
        using var reader = Csv.Read().FromText(sb.ToString());
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(301, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_CharPath_Avx512BW_DisableQuotes()
    {
        if (!Avx512BW) return;
        var sb = new StringBuilder("a,b\n");
        for (int i = 0; i < 200; i++) sb.Append('"').Append("v").Append(i).Append('"').Append(",x\n");
        using var reader = Csv.Read().DisableQuotedFields().FromText(sb.ToString());
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(201, n);
    }

    // ---------- AVX-512 byte path NoPclmulqdq ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvRowParser_BytePath_Avx512BW_NoPclmulqdq_Quoted()
    {
        if (!Avx512BW) return;
        using var _scope = HardwareCapabilities.Override(pclmulqdq: false);
        var sb = new StringBuilder("a,b\n");
        for (int i = 0; i < 300; i++)
        {
            sb.Append('"').Append("text ").Append(i).Append("\"\"q\"\"").Append('"').Append(',').Append(i).Append('\n');
        }
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        using var reader = Csv.Read().FromStream(stream, out _);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(301, n);
    }
}
