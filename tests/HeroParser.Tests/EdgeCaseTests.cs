using Xunit;

namespace HeroParser.Tests;

/// <summary>
/// Edge case and boundary condition tests.
/// </summary>
public class EdgeCaseTests
{
    [Fact]
    public void EmptyCsv_ReturnsNoRows()
    {
        var csv = "";
        var rows = new List<string[]>();

        foreach (var row in Csv.Parse(csv.AsSpan()))
        {
            rows.Add(row.ToStringArray());
        }

        Assert.Empty(rows);
    }

    [Fact]
    public void SingleRow_NoNewline_ParsesCorrectly()
    {
        var csv = "a,b,c";
        var rows = new List<string[]>();

        foreach (var row in Csv.Parse(csv.AsSpan()))
        {
            rows.Add(row.ToStringArray());
        }

        Assert.Single(rows);
        Assert.Equal(new[] { "a", "b", "c" }, rows[0]);
    }

    [Fact]
    public void TrailingNewline_DoesNotCreateEmptyRow()
    {
        var csv = "a,b,c\n1,2,3\n";
        var rows = new List<string[]>();

        foreach (var row in Csv.Parse(csv.AsSpan()))
        {
            rows.Add(row.ToStringArray());
        }

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void MultipleConsecutiveNewlines_CreatesEmptyRows()
    {
        var csv = "a,b\n\n\nc,d";
        var rows = new List<string[]>();

        foreach (var row in Csv.Parse(csv.AsSpan()))
        {
            rows.Add(row.ToStringArray());
        }

        // Empty lines are skipped by default in our implementation
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "a", "b" }, rows[0]);
        Assert.Equal(new[] { "c", "d" }, rows[1]);
    }

    [Fact]
    public void TrailingComma_CreatesEmptyColumn()
    {
        var csv = "a,b,c,\n1,2,3,";
        var rows = new List<string[]>();

        foreach (var row in Csv.Parse(csv.AsSpan()))
        {
            rows.Add(row.ToStringArray());
        }

        Assert.Equal(2, rows.Count);
        Assert.Equal(4, rows[0].Length);
        Assert.Equal("", rows[0][3]);
        Assert.Equal("", rows[1][3]);
    }

    [Fact]
    public void LeadingComma_CreatesEmptyColumn()
    {
        var csv = ",a,b,c\n,1,2,3";
        var rows = new List<string[]>();

        foreach (var row in Csv.Parse(csv.AsSpan()))
        {
            rows.Add(row.ToStringArray());
        }

        Assert.Equal(2, rows.Count);
        Assert.Equal(4, rows[0].Length);
        Assert.Equal("", rows[0][0]);
        Assert.Equal("a", rows[0][1]);
    }

    [Fact]
    public void OnlyDelimiters_CreatesEmptyColumns()
    {
        var csv = ",,,\n,,,";
        var rows = new List<string[]>();

        foreach (var row in Csv.Parse(csv.AsSpan()))
        {
            rows.Add(row.ToStringArray());
        }

        Assert.Equal(2, rows.Count);
        Assert.Equal(4, rows[0].Length);
        Assert.All(rows[0], col => Assert.Equal("", col));
    }

    [Fact]
    public void VeryLongLine_Over1000Chars_ParsesCorrectly()
    {
        // Create line with >1000 characters to test SIMD chunking
        var values = Enumerable.Range(0, 200).Select(i => "value");
        var csv = string.Join(",", values);

        var reader = Csv.Parse(csv.AsSpan());
        Assert.True(reader.MoveNext());
        Assert.Equal(200, reader.Current.Count);
    }

    [Fact]
    public void VeryLongField_Over1000Chars_ParsesCorrectly()
    {
        var longField = new string('x', 2000);
        var csv = $"a,{longField},c";

        var reader = Csv.Parse(csv.AsSpan());
        Assert.True(reader.MoveNext());
        Assert.Equal(3, reader.Current.Count);
        Assert.Equal(2000, reader.Current[1].Length);
    }

    [Fact]
    public void MixedLineEndings_CR_LF_CRLF_ParseCorrectly()
    {
        var csv = "a,b,c\r1,2,3\n4,5,6\r\n7,8,9";
        var rows = new List<string[]>();

        foreach (var row in Csv.Parse(csv.AsSpan()))
        {
            rows.Add(row.ToStringArray());
        }

        Assert.Equal(4, rows.Count);
        Assert.Equal(new[] { "a", "b", "c" }, rows[0]);
        Assert.Equal(new[] { "1", "2", "3" }, rows[1]);
        Assert.Equal(new[] { "4", "5", "6" }, rows[2]);
        Assert.Equal(new[] { "7", "8", "9" }, rows[3]);
    }

    [Fact]
    public void UnicodeCharacters_ParseCorrectly()
    {
        var csv = "名前,年齢\n太郎,25\n花子,30";
        var rows = new List<string[]>();

        foreach (var row in Csv.Parse(csv.AsSpan()))
        {
            rows.Add(row.ToStringArray());
        }

        Assert.Equal(3, rows.Count);
        Assert.Equal("名前", rows[0][0]);
        Assert.Equal("太郎", rows[1][0]);
    }

    [Fact]
    public void SpecialCharacters_NotDelimiters_ParseCorrectly()
    {
        var csv = "a!b@c#d,e$f%g^h";
        var rows = new List<string[]>();

        foreach (var row in Csv.Parse(csv.AsSpan()))
        {
            rows.Add(row.ToStringArray());
        }

        Assert.Single(rows);
        Assert.Equal(2, rows[0].Length);
        Assert.Equal("a!b@c#d", rows[0][0]);
        Assert.Equal("e$f%g^h", rows[0][1]);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(32)]  // AVX2 boundary
    [InlineData(64)]  // AVX-512 boundary
    [InlineData(65)]  // Just over AVX-512 boundary
    [InlineData(100)]
    [InlineData(500)]
    public void VariousColumnCounts_ParseCorrectly(int columnCount)
    {
        var columns = Enumerable.Range(0, columnCount).Select(i => $"col{i}");
        var csv = string.Join(",", columns);

        var reader = Csv.Parse(csv.AsSpan());
        Assert.True(reader.MoveNext());
        Assert.Equal(columnCount, reader.Current.Count);
    }

    [Fact]
    public void Semicolon_Delimiter_Works()
    {
        var csv = "a;b;c\n1;2;3";
        var rows = new List<string[]>();

        foreach (var row in Csv.Parse(csv.AsSpan(), ';'))
        {
            rows.Add(row.ToStringArray());
        }

        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "a", "b", "c" }, rows[0]);
    }

    [Fact]
    public void Pipe_Delimiter_Works()
    {
        var csv = "a|b|c\n1|2|3";
        var rows = new List<string[]>();

        foreach (var row in Csv.Parse(csv.AsSpan(), '|'))
        {
            rows.Add(row.ToStringArray());
        }

        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "a", "b", "c" }, rows[0]);
    }
}
