using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Shared;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Forces specific SIMD levels via <see cref="HardwareCapabilities.Override"/> so each
/// branch in <c>CsvRowParser</c>'s SIMD dispatcher gets exercised on a single test run.
/// On AVX-512 hardware the default test path only hits the AVX-512 branch; without
/// these overrides the AVX2 + scalar fallbacks remain uncovered.
/// </summary>
[Trait("Category", "Unit")]
[Collection("AsyncWriterTests")] // mutates shared HardwareCapabilities state — run sequentially
public class CsvSimdLevelCoverageTests
{
    [GenerateBinder]
    public sealed class Row
    {
        [TabularMap(Name = "A")] public string A { get; set; } = "";
        [TabularMap(Name = "B")] public string B { get; set; } = "";
        [TabularMap(Name = "C")] public string C { get; set; } = "";
    }

    private static List<Row> Parse(string csv)
    {
        var reader = Csv.DeserializeRecords<Row>(csv, out _);
        var list = new List<Row>();
        foreach (var r in reader) list.Add(r);
        return list;
    }

    private static string Sample(int rows = 50)
    {
        var sb = new StringBuilder("A,B,C\n");
        for (int i = 0; i < rows; i++)
        {
            sb.AppendLine($"row{i}A,row{i}B,row{i}C");
        }
        return sb.ToString();
    }

    private static string SampleWithQuotes(int rows = 50)
    {
        var sb = new StringBuilder("A,B,C\n");
        for (int i = 0; i < rows; i++)
        {
            sb.AppendLine($"\"row{i}A\",\"row{i}B\",\"row{i}C\"");
        }
        return sb.ToString();
    }

    [Fact]
    public void Avx512Forced_Off_Avx2_On_Pclmul_On_Unquoted()
    {
        Assert.SkipUnless(Avx2.IsSupported, "Forces avx2 = true; requires real AVX2 hardware (skipping on ARM macOS).");
        using (HardwareCapabilities.Override(avx512BW: false, avx2: true, pclmulqdq: true))
        {
            var records = Parse(Sample(64));
            Assert.Equal(64, records.Count);
        }
    }

    [Fact]
    public void Avx512Forced_Off_Avx2_On_Pclmul_On_Quoted()
    {
        Assert.SkipUnless(Avx2.IsSupported, "Forces avx2 = true; requires real AVX2 hardware (skipping on ARM macOS).");
        using (HardwareCapabilities.Override(avx512BW: false, avx2: true, pclmulqdq: true))
        {
            var records = Parse(SampleWithQuotes(64));
            Assert.Equal(64, records.Count);
        }
    }

    [Fact]
    public void Avx512Forced_Off_Avx2_On_Pclmul_Off_Quoted()
    {
        Assert.SkipUnless(Avx2.IsSupported, "Forces avx2 = true; requires real AVX2 hardware (skipping on ARM macOS).");
        // Disabling PCLMULQDQ forces the non-CLMUL quote-handling branch in the AVX2 path.
        using (HardwareCapabilities.Override(avx512BW: false, avx2: true, pclmulqdq: false))
        {
            var records = Parse(SampleWithQuotes(64));
            Assert.Equal(64, records.Count);
        }
    }

    [Fact]
    public void Avx512Forced_Off_Avx2_Off_FallsBackToScalar()
    {
        // Disable all SIMD levels: parser must fall back to scalar.
        using (HardwareCapabilities.Override(avx512BW: false, avx2: false, advSimd: false, vector128: false))
        {
            var records = Parse(Sample(20));
            Assert.Equal(20, records.Count);
        }
    }

    [Fact]
    public void Vector128_SoftwarePrefixXor_MatchesScalarAndAvx_ForBytesAndChars()
    {
        var opts = new CsvReadOptions
        {
            AllowNewlinesInsideQuotes = true,
            TrackSourceLineNumbers = true
        };

        // Crafted so delimiter, quotes, escaped quotes (""), CRLF, LF, non-ASCII chars (\u8000, €),
        // and multiline quoted values cross 16-byte / 16-char boundaries cleanly.
        string csv =
            "A,B,C\r\n" +
            "0123456789abcde,\"val,1\",ok\r\n" +
            "\"line1\r\nline2\",\"she \"\"said\"\"\",€\u8000\n" +
            "plain01234567890,plain12345678901,plain23456789012\r\n" +
            "a1,b1,c1\r\na2,b2,c2\r\na3,b3,c3\r\na4,b4,c4\r\na5,b5,c5\n" +
            "last,row,\"end\"";

        static List<(string[] Fields, int SourceLine)> ReadAllBytes(string input, CsvReadOptions o)
        {
            var reader = Csv.ReadFromByteSpan(Encoding.UTF8.GetBytes(input), o);
            var list = new List<(string[], int)>();
            while (reader.MoveNext())
            {
                var row = reader.Current;
                var fields = new string[row.ColumnCount];
                for (int i = 0; i < fields.Length; i++)
                    fields[i] = row[i].UnquoteToString();
                list.Add((fields, row.SourceLineNumber));
            }
            return list;
        }

        static List<(string[] Fields, int SourceLine)> ReadAllChars(string input, CsvReadOptions o)
        {
            var reader = Csv.ReadFromCharSpan(input.AsSpan(), o);
            var list = new List<(string[], int)>();
            while (reader.MoveNext())
            {
                var row = reader.Current;
                var fields = new string[row.ColumnCount];
                for (int i = 0; i < fields.Length; i++)
                    fields[i] = row[i].UnquoteToString();
                list.Add((fields, row.SourceLineNumber));
            }
            return list;
        }

        List<(string[] Fields, int SourceLine)> scalarBytes;
        List<(string[] Fields, int SourceLine)> scalarChars;
        using (HardwareCapabilities.Override(avx512BW: false, avx2: false, pclmulqdq: false, advSimd: false, vector128: false))
        {
            scalarBytes = ReadAllBytes(csv, opts);
            scalarChars = ReadAllChars(csv, opts);
        }

        using (HardwareCapabilities.Override(avx512BW: false, avx2: false, pclmulqdq: false, advSimd: false, vector128: true))
        {
            var vecBytes = ReadAllBytes(csv, opts);
            var vecChars = ReadAllChars(csv, opts);

            Assert.Equal(scalarBytes.Count, vecBytes.Count);
            Assert.Equal(scalarChars.Count, vecChars.Count);
            for (int i = 0; i < scalarBytes.Count; i++)
            {
                Assert.Equal(scalarBytes[i].SourceLine, vecBytes[i].SourceLine);
                Assert.Equal(scalarBytes[i].Fields, vecBytes[i].Fields);
                Assert.Equal(scalarChars[i].SourceLine, vecChars[i].SourceLine);
                Assert.Equal(scalarChars[i].Fields, vecChars[i].Fields);
            }
        }
    }

    [Fact]
    public void Vector128_UnquotedFastPath_MatchesScalar()
    {
        var sb = new StringBuilder("A,B,C\n");
        for (int i = 0; i < 40; i++)
            sb.Append($"colA_{i},colB_{i},colC_{i}\n");
        string csv = sb.ToString();

        int scalarByteRows;
        int scalarCharRows;
        using (HardwareCapabilities.Override(avx512BW: false, avx2: false, pclmulqdq: false, advSimd: false, vector128: false))
        {
            scalarByteRows = CountRowsBytePath(csv);
            scalarCharRows = CountRowsCharPath(csv);
        }

        using (HardwareCapabilities.Override(avx512BW: false, avx2: false, pclmulqdq: false, advSimd: false, vector128: true))
        {
            Assert.Equal(scalarByteRows, CountRowsBytePath(csv));
            Assert.Equal(scalarCharRows, CountRowsCharPath(csv));
        }

        Assert.Equal(40, scalarByteRows);
        Assert.Equal(40, scalarCharRows);
    }

    private static int CountRowsBytePath(string csv)
    {
        var reader = Csv.ReadFromByteSpan(Encoding.UTF8.GetBytes(csv));
        int count = 0;
        while (reader.MoveNext())
        {
            count++;
        }
        return count - 1;
    }

    private static int CountRowsCharPath(string csv)
    {
        // Csv.ReadFromCharSpan drives the UTF-16 SIMD code path directly,
        // bypassing the char-to-byte adapter that DeserializeRecords<T> uses
        // when a generated byte binder is available.
        var reader = Csv.ReadFromCharSpan(csv.AsSpan());
        int count = 0;
        while (reader.MoveNext())
        {
            count++;
        }
        // Subtract 1 for the header row.
        return count - 1;
    }

    [Fact]
    public void Avx512_UTF16_Path_Unquoted()
    {
        // Default capabilities: AVX-512 path runs on this CPU.
        Assert.Equal(64, CountRowsCharPath(Sample(64)));
    }

    [Fact]
    public void Avx512_UTF16_Path_Quoted_WithPclmul()
    {
        Assert.Equal(64, CountRowsCharPath(SampleWithQuotes(64)));
    }

    [Fact]
    public void Avx512_UTF16_Path_Quoted_WithoutPclmul()
    {
        using (HardwareCapabilities.Override(pclmulqdq: false))
        {
            Assert.Equal(64, CountRowsCharPath(SampleWithQuotes(64)));
        }
    }

    [Fact]
    public void Avx2_UTF16_Path_FallbackWhenAvx512Disabled_Unquoted()
    {
        using (HardwareCapabilities.Override(avx512BW: false))
        {
            Assert.Equal(64, CountRowsCharPath(Sample(64)));
        }
    }

    [Fact]
    public void Avx2_UTF16_Path_FallbackWhenAvx512Disabled_Quoted()
    {
        using (HardwareCapabilities.Override(avx512BW: false))
        {
            Assert.Equal(64, CountRowsCharPath(SampleWithQuotes(64)));
        }
    }

    [Fact]
    public void Avx2_UTF16_Path_QuotedWithoutPclmul()
    {
        using (HardwareCapabilities.Override(avx512BW: false, pclmulqdq: false))
        {
            Assert.Equal(64, CountRowsCharPath(SampleWithQuotes(64)));
        }
    }

    [Fact]
    public void Scalar_UTF16_Path_NoSimdAvailable()
    {
        using (HardwareCapabilities.Override(avx512BW: false, avx2: false))
        {
            Assert.Equal(20, CountRowsCharPath(Sample(20)));
        }
    }

    [Fact]
    public void Avx512_UTF8_Path_Unquoted()
    {
        Assert.Equal(64, CountRowsBytePath(Sample(64)));
    }

    [Fact]
    public void Avx512_UTF8_Path_Quoted_WithPclmul()
    {
        Assert.Equal(64, CountRowsBytePath(SampleWithQuotes(64)));
    }

    [Fact]
    public void Avx512_UTF8_Path_Quoted_WithoutPclmul()
    {
        using (HardwareCapabilities.Override(pclmulqdq: false))
        {
            Assert.Equal(64, CountRowsBytePath(SampleWithQuotes(64)));
        }
    }

    [Fact]
    public void Avx2_UTF8_Path_FallbackWhenAvx512Disabled_Unquoted()
    {
        using (HardwareCapabilities.Override(avx512BW: false))
        {
            Assert.Equal(64, CountRowsBytePath(Sample(64)));
        }
    }

    [Fact]
    public void Avx2_UTF8_Path_FallbackWhenAvx512Disabled_Quoted()
    {
        using (HardwareCapabilities.Override(avx512BW: false))
        {
            Assert.Equal(64, CountRowsBytePath(SampleWithQuotes(64)));
        }
    }

    [Fact]
    public void Avx2_UTF8_Path_QuotedWithoutPclmul()
    {
        using (HardwareCapabilities.Override(avx512BW: false, pclmulqdq: false))
        {
            Assert.Equal(64, CountRowsBytePath(SampleWithQuotes(64)));
        }
    }

    [Fact]
    public void Scalar_UTF8_Path_NoSimdAvailable()
    {
        using (HardwareCapabilities.Override(avx512BW: false, avx2: false))
        {
            Assert.Equal(20, CountRowsBytePath(Sample(20)));
        }
    }

    [Fact]
    public void OverrideScope_RestoresPreviousValuesOnDispose()
    {
        var capabilities = HardwareCapabilities.Current;
        var beforeAvx2 = capabilities.Avx2IsSupported;
        var beforeAvx512 = capabilities.Avx512BWIsSupported;
        var beforeAdvSimd = capabilities.AdvSimdIsSupported;
        var beforeVector128 = capabilities.Vector128IsSupported;

        using (HardwareCapabilities.Override(avx2: false, avx512BW: false, advSimd: false, vector128: false))
        {
            Assert.False(capabilities.Avx2IsSupported);
            Assert.False(capabilities.Avx512BWIsSupported);
            Assert.False(capabilities.AdvSimdIsSupported);
            Assert.False(capabilities.Vector128IsSupported);
        }

        Assert.Equal(beforeAvx2, capabilities.Avx2IsSupported);
        Assert.Equal(beforeAvx512, capabilities.Avx512BWIsSupported);
        Assert.Equal(beforeAdvSimd, capabilities.AdvSimdIsSupported);
        Assert.Equal(beforeVector128, capabilities.Vector128IsSupported);
    }

    [Fact]
    public void NestedOverrides_RestoreCorrectly()
    {
        var capabilities = HardwareCapabilities.Current;
        using (HardwareCapabilities.Override(avx2: false))
        {
            Assert.False(capabilities.Avx2IsSupported);
            using (HardwareCapabilities.Override(avx512BW: false))
            {
                Assert.False(capabilities.Avx512BWIsSupported);
                Assert.False(capabilities.Avx2IsSupported); // outer override still in effect
            }
            Assert.False(capabilities.Avx2IsSupported); // outer override still in effect after inner disposed
        }
    }

    [Fact]
    public void Override_AllNullArgs_ChangesNothing()
    {
        var capabilities = HardwareCapabilities.Current;
        var beforeAvx2 = capabilities.Avx2IsSupported;
        using (HardwareCapabilities.Override())
        {
            Assert.Equal(beforeAvx2, capabilities.Avx2IsSupported);
        }
    }

    [Fact]
    public void Override_DoubleDispose_NoThrow()
    {
        var scope = HardwareCapabilities.Override(avx2: false);
        scope.Dispose();
        scope.Dispose(); // second dispose should be a no-op
    }

    [Fact]
    public void Hardware_GetHardwareInfo_ReturnsString()
    {
        var info = HeroParser.Hardware.GetHardwareInfo();
        Assert.NotNull(info);
        Assert.NotEmpty(info);
    }
}
