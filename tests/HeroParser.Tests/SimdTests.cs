using Xunit;

namespace HeroParser.Tests;

public class SimdTests
{
    [Fact]
    public void HardwareInfo_ReturnsValidString()
    {
        var info = HeroParser.Simd.SimdParserFactory.GetHardwareInfo();
        Assert.NotNull(info);
        Assert.NotEmpty(info);
        // Should contain either "Using:" or "SIMD:"
        Assert.True(info.Contains("Using:") || info.Contains("SIMD:"));
    }

    [Fact]
    public void LargeRow_ProcessedCorrectly()
    {
        // Test with a row larger than SIMD chunk size (64 chars)
        var csv = string.Join(",", Enumerable.Range(0, 100).Select(i => i.ToString()));
        var reader = Csv.Parse(csv);

        Assert.True(reader.MoveNext());
        var row = reader.Current;
        Assert.Equal(100, row.ColumnCount);

        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(i.ToString(), row[i].ToString());
        }
    }

    [Fact]
    public void ExactlyChunkSize_ProcessedCorrectly()
    {
        // Create a CSV with exactly 64 characters (AVX-512 chunk size)
        // Format: "1,2,3,..." to make exactly 64 chars
        var parts = new System.Collections.Generic.List<string>();
        int totalLength = 0;

        for (int i = 1; totalLength < 64; i++)
        {
            var part = i.ToString();
            if (totalLength + part.Length + (parts.Count > 0 ? 1 : 0) <= 64)
            {
                parts.Add(part);
                totalLength += part.Length + (parts.Count > 1 ? 1 : 0);
            }
            else
            {
                break;
            }
        }

        var csv = string.Join(",", parts);
        var reader = Csv.Parse(csv);

        Assert.True(reader.MoveNext());
        var row = reader.Current;
        Assert.Equal(parts.Count, row.ColumnCount);

        for (int i = 0; i < parts.Count; i++)
        {
            Assert.Equal(parts[i], row[i].ToString());
        }
    }

    [Fact]
    public void MultipleChunks_ProcessedCorrectly()
    {
        // Test data spanning multiple SIMD chunks
        var csv = string.Join(",", Enumerable.Range(0, 200).Select(i => $"val{i}"));
        var reader = Csv.Parse(csv);

        Assert.True(reader.MoveNext());
        var row = reader.Current;
        Assert.Equal(200, row.ColumnCount);

        for (int i = 0; i < 200; i++)
        {
            Assert.Equal($"val{i}", row[i].ToString());
        }
    }

    [Fact]
    public void SimdWithEmptyFields_HandledCorrectly()
    {
        // Empty fields at various positions within SIMD chunks
        var csv = new string(',', 100); // 100 commas = 101 empty fields
        var reader = Csv.Parse(csv);

        Assert.True(reader.MoveNext());
        var row = reader.Current;
        Assert.Equal(101, row.ColumnCount);

        for (int i = 0; i < 101; i++)
        {
            Assert.Equal("", row[i].ToString());
        }
    }

    [Fact]
    public void AlternatingEmptyNonEmpty_ProcessedCorrectly()
    {
        // Pattern: a,,b,,c,,d,, ...
        var parts = Enumerable.Range(0, 50)
            .SelectMany(i => new[] { ((char)('a' + (i % 26))).ToString(), "" });
        var csv = string.Join(",", parts);
        var reader = Csv.Parse(csv);

        Assert.True(reader.MoveNext());
        var row = reader.Current;

        for (int i = 0; i < row.ColumnCount; i++)
        {
            if (i % 2 == 0)
            {
                // Non-empty field
                var expected = ((char)('a' + ((i / 2) % 26))).ToString();
                Assert.Equal(expected, row[i].ToString());
            }
            else
            {
                // Empty field
                Assert.Equal("", row[i].ToString());
            }
        }
    }

    [Fact]
    public void VeryLongSingleField_ProcessedCorrectly()
    {
        // Single field longer than SIMD chunk
        var longValue = new string('x', 1000);
        var csv = $"{longValue},short";
        var reader = Csv.Parse(csv);

        Assert.True(reader.MoveNext());
        var row = reader.Current;
        Assert.Equal(2, row.ColumnCount);
        Assert.Equal(longValue, row[0].ToString());
        Assert.Equal("short", row[1].ToString());
    }

    [Fact]
    public void SpecialCharacters_InsideFields()
    {
        // Special characters that might confuse SIMD processing
        // Note: \n is a line terminator, so this will be treated as first row
        var csv = "hello\tworld,foo";
        var reader = Csv.Parse(csv);

        Assert.True(reader.MoveNext());
        var row = reader.Current;
        Assert.Equal(2, row.ColumnCount);
        Assert.Equal("hello\tworld", row[0].ToString());
        Assert.Equal("foo", row[1].ToString());
    }

    [Fact]
    public void UnicodeCharacters_ProcessedCorrectly()
    {
        // Unicode characters in fields
        var csv = "Hello,世界,🌍,Привет";
        var reader = Csv.Parse(csv);

        Assert.True(reader.MoveNext());
        var row = reader.Current;
        Assert.Equal(4, row.ColumnCount);
        Assert.Equal("Hello", row[0].ToString());
        Assert.Equal("世界", row[1].ToString());
        Assert.Equal("🌍", row[2].ToString());
        Assert.Equal("Привет", row[3].ToString());
    }

    [Fact]
    public void ChunkBoundaries_WithDelimiters()
    {
        // Place delimiters at exact chunk boundaries to test edge cases
        // Create string with delimiters at positions 32, 64, 96, etc.
        var parts = new System.Collections.Generic.List<string>();

        // Fill up to chunk boundaries
        for (int chunkSize = 32; chunkSize <= 128; chunkSize += 32)
        {
            var padding = new string('x', chunkSize - (parts.Count > 0 ? 1 : 0));
            parts.Add(padding);
        }

        var csv = string.Join(",", parts);
        var reader = Csv.Parse(csv);

        Assert.True(reader.MoveNext());
        var row = reader.Current;
        Assert.Equal(parts.Count, row.ColumnCount);
    }

    [Fact]
    public void Correctness_ComparedToScalar()
    {
        // This test ensures SIMD produces same results as scalar
        var testCases = new[]
        {
            "a,b,c",
            "1,2,3,4,5",
            string.Join(",", Enumerable.Range(0, 100)),
            ",,,,",
            "a,,b,,c",
            new string('x', 200),
            "hello,world,test,data,more,columns,here",
        };

        foreach (var csv in testCases)
        {
            var reader = Csv.Parse(csv);
            Assert.True(reader.MoveNext());
            var row = reader.Current;

            // Verify by splitting manually (scalar approach)
            var expected = csv.Split(',');
            Assert.Equal(expected.Length, row.ColumnCount);

            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], row[i].ToString());
            }
        }
    }

    [Fact]
    public void HighPerformance_LargeDataset()
    {
        // Performance smoke test - should complete quickly even with large data
        var rows = Enumerable.Range(0, 10000)
            .Select(i => string.Join(",", Enumerable.Range(0, 20).Select(j => $"{i}_{j}")));
        var csv = string.Join("\n", rows);

        var reader = Csv.Parse(csv);
        int rowCount = 0;

        foreach (var row in reader)
        {
            Assert.Equal(20, row.ColumnCount);
            rowCount++;
        }

        Assert.Equal(10000, rowCount);
    }
}
