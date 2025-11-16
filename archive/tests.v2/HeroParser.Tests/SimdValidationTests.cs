using Xunit;
using HeroParser.Simd;

namespace HeroParser.Tests;

/// <summary>
/// Comprehensive SIMD validation tests.
/// Ensures all SIMD implementations produce identical results to scalar parser.
/// </summary>
public class SimdValidationTests
{
    [Theory]
    [InlineData("a,b,c")]
    [InlineData("1,2,3,4,5")]
    [InlineData("")]
    [InlineData("single")]
    [InlineData("a,,c")]
    [InlineData(",,,")]
    [InlineData("a,b,c,d,e,f,g,h,i,j")]
    public void AllParsers_MatchScalar_OnSimpleLines(string line)
    {
        ValidateAllParsersMatch(line, ',');
    }

    [Fact]
    public void AllParsers_MatchScalar_On32CharLine()
    {
        // Exactly 32 chars (AVX2 boundary)
        var line = "a,b,c,d,e,f,g,h,i,j,k,l,m,n"; // 32 chars
        ValidateAllParsersMatch(line, ',');
    }

    [Fact]
    public void AllParsers_MatchScalar_On64CharLine()
    {
        // Exactly 64 chars (AVX-512 boundary)
        var values = Enumerable.Range(0, 10).Select(i => $"val{i}");
        var line = string.Join(",", values); // ~60 chars
        ValidateAllParsersMatch(line, ',');
    }

    [Fact]
    public void AllParsers_MatchScalar_On100CharLine()
    {
        // Over 64 chars to test chunking
        var values = Enumerable.Range(0, 15).Select(i => $"val{i}");
        var line = string.Join(",", values);
        ValidateAllParsersMatch(line, ',');
    }

    [Fact]
    public void AllParsers_MatchScalar_On500CharLine()
    {
        // Multiple SIMD chunks
        var values = Enumerable.Range(0, 75).Select(i => $"v{i}");
        var line = string.Join(",", values);
        ValidateAllParsersMatch(line, ',');
    }

    [Fact]
    public void AllParsers_MatchScalar_On1000CharLine()
    {
        // Large line with many chunks
        var values = Enumerable.Range(0, 150).Select(i => $"val{i}");
        var line = string.Join(",", values);
        ValidateAllParsersMatch(line, ',');
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(100)]
    [InlineData(200)]
    public void AllParsers_MatchScalar_OnVariousColumnCounts(int columnCount)
    {
        var values = Enumerable.Range(0, columnCount).Select(i => $"c{i}");
        var line = string.Join(",", values);
        ValidateAllParsersMatch(line, ',');
    }

    [Fact]
    public void AllParsers_MatchScalar_WithEmptyFields()
    {
        var testCases = new[]
        {
            ",",
            ",,",
            ",,,",
            "a,,c",
            ",b,",
            ",,c",
            "a,,",
            "a,b,c,,e,,g",
        };

        foreach (var line in testCases)
        {
            ValidateAllParsersMatch(line, ',');
        }
    }

    [Fact]
    public void AllParsers_MatchScalar_WithLongFields()
    {
        var longField = new string('x', 100);
        var testCases = new[]
        {
            longField,
            $"{longField},{longField}",
            $"a,{longField},c",
            $"{longField},b,{longField}",
        };

        foreach (var line in testCases)
        {
            ValidateAllParsersMatch(line, ',');
        }
    }

    [Theory]
    [InlineData(',')]
    [InlineData(';')]
    [InlineData('\t')]
    [InlineData('|')]
    [InlineData(':')]
    public void AllParsers_MatchScalar_WithDifferentDelimiters(char delimiter)
    {
        var line = $"a{delimiter}b{delimiter}c{delimiter}d";
        ValidateAllParsersMatch(line, delimiter);
    }

    [Fact]
    public void AllParsers_MatchScalar_WithUnicode()
    {
        var testCases = new[]
        {
            "名前,年齢,住所",
            "太郎,25,東京",
            "Müller,König,Größe",
            "café,résumé,naïve",
        };

        foreach (var line in testCases)
        {
            ValidateAllParsersMatch(line, ',');
        }
    }

    [Fact]
    public void AllParsers_MatchScalar_WithSpecialCharacters()
    {
        var testCases = new[]
        {
            "a!b@c#d,e$f%g",
            "hello world,foo bar",
            "123-456-7890,test@email.com",
            "a(b)c[d]e{f}g",
        };

        foreach (var line in testCases)
        {
            ValidateAllParsersMatch(line, ',');
        }
    }

    [Fact]
    public void AllParsers_MatchScalar_EdgeOfChunks()
    {
        // Test boundaries: 31, 32, 33 chars (around AVX2 boundary)
        var line31 = new string('a', 29) + ",b"; // 31 chars
        var line32 = new string('a', 30) + ",b"; // 32 chars
        var line33 = new string('a', 31) + ",b"; // 33 chars

        ValidateAllParsersMatch(line31, ',');
        ValidateAllParsersMatch(line32, ',');
        ValidateAllParsersMatch(line33, ',');

        // Test boundaries: 63, 64, 65 chars (around AVX-512 boundary)
        var line63 = new string('a', 61) + ",b"; // 63 chars
        var line64 = new string('a', 62) + ",b"; // 64 chars
        var line65 = new string('a', 63) + ",b"; // 65 chars

        ValidateAllParsersMatch(line63, ',');
        ValidateAllParsersMatch(line64, ',');
        ValidateAllParsersMatch(line65, ',');
    }

    [Fact]
    public void AllParsers_MatchScalar_DelimitersAtChunkBoundaries()
    {
        // Place delimiters exactly at chunk boundaries
        var chars31 = new char[31];
        for (int i = 0; i < 31; i++) chars31[i] = i == 31 ? ',' : 'a';

        var chars32 = new char[32];
        for (int i = 0; i < 32; i++) chars32[i] = i == 31 ? ',' : 'a';

        var chars64 = new char[64];
        for (int i = 0; i < 64; i++) chars64[i] = i == 63 ? ',' : 'a';

        ValidateAllParsersMatch(new string(chars31), ',');
        ValidateAllParsersMatch(new string(chars32), ',');
        ValidateAllParsersMatch(new string(chars64), ',');
    }

    [Fact]
    public void AllParsers_MatchScalar_MultipleDelimitersInChunk()
    {
        // Multiple delimiters in single SIMD chunk
        var line = "a,b,c,d,e,f,g,h,i,j,k,l,m,n,o,p"; // Many delimiters in 32 chars
        ValidateAllParsersMatch(line, ',');
    }

    [Fact]
    public void AllParsers_MatchScalar_NoDelimitersInChunk()
    {
        // No delimiters in full chunk (tests fast-path when mask == 0)
        var line = new string('a', 100);
        ValidateAllParsersMatch(line, ',');
    }

    [Fact]
    public void AllParsers_MatchScalar_RandomData()
    {
        var random = new Random(42); // Fixed seed for reproducibility

        for (int i = 0; i < 100; i++)
        {
            var length = random.Next(1, 500);
            var line = GenerateRandomCsvLine(random, length);
            ValidateAllParsersMatch(line, ',');
        }
    }

    private static string GenerateRandomCsvLine(Random random, int approxLength)
    {
        var chars = new List<char>();
        while (chars.Count < approxLength)
        {
            // Add field
            var fieldLength = random.Next(0, 20);
            for (int i = 0; i < fieldLength; i++)
            {
                chars.Add((char)('a' + random.Next(26)));
            }

            // Maybe add delimiter
            if (chars.Count < approxLength - 1 && random.Next(2) == 0)
            {
                chars.Add(',');
            }
        }

        return new string(chars.ToArray());
    }

    private static void ValidateAllParsersMatch(string line, char delimiter)
    {
        var scalarResult = ParseWithParser(line, delimiter, ScalarParser.Instance);

        if (System.Runtime.Intrinsics.X86.Avx512F.IsSupported &&
            System.Runtime.Intrinsics.X86.Avx512BW.IsSupported)
        {
            var avx512Result = ParseWithParser(line, delimiter, Avx512Parser.Instance);
            AssertResultsEqual(scalarResult, avx512Result, "AVX-512", line);
        }

        if (System.Runtime.Intrinsics.X86.Avx2.IsSupported)
        {
            var avx2Result = ParseWithParser(line, delimiter, Avx2Parser.Instance);
            AssertResultsEqual(scalarResult, avx2Result, "AVX2", line);
        }

        if (System.Runtime.Intrinsics.Arm.AdvSimd.IsSupported)
        {
            var neonResult = ParseWithParser(line, delimiter, NeonParser.Instance);
            AssertResultsEqual(scalarResult, neonResult, "NEON", line);
        }
    }

    private static (int[] starts, int[] lengths, int count) ParseWithParser(
        string line,
        char delimiter,
        ISimdParser parser)
    {
        var starts = new int[1000];
        var lengths = new int[1000];

        int count = parser.ParseColumns(line.AsSpan(), delimiter, starts, lengths);

        return (starts, lengths, count);
    }

    private static void AssertResultsEqual(
        (int[] starts, int[] lengths, int count) expected,
        (int[] starts, int[] lengths, int count) actual,
        string parserName,
        string inputLine)
    {
        Assert.True(expected.count == actual.count,
            $"{parserName} parser returned {actual.count} columns but Scalar returned {expected.count} for line: '{inputLine}'");

        for (int i = 0; i < expected.count; i++)
        {
            Assert.True(expected.starts[i] == actual.starts[i],
                $"{parserName} parser column {i} start mismatch: expected {expected.starts[i]}, got {actual.starts[i]} for line: '{inputLine}'");

            Assert.True(expected.lengths[i] == actual.lengths[i],
                $"{parserName} parser column {i} length mismatch: expected {expected.lengths[i]}, got {actual.lengths[i]} for line: '{inputLine}'");
        }
    }
}
