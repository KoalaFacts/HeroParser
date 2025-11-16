using Xunit;

namespace HeroParser.Tests;

/// <summary>
/// Basic correctness tests to ensure parser works correctly.
/// </summary>
public class BasicCorrectnessTests
{
    [Fact]
    public void SimpleCsv_ParsesCorrectly()
    {
        var csv = "a,b,c\n1,2,3\n4,5,6";
        var rows = new List<string[]>();

        foreach (var row in Csv.Parse(csv.AsSpan()))
        {
            rows.Add(row.ToStringArray());
        }

        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { "a", "b", "c" }, rows[0]);
        Assert.Equal(new[] { "1", "2", "3" }, rows[1]);
        Assert.Equal(new[] { "4", "5", "6" }, rows[2]);
    }

    [Fact]
    public void EmptyFields_ParsesCorrectly()
    {
        var csv = "a,,c\n,b,\n,,";
        var rows = new List<string[]>();

        foreach (var row in Csv.Parse(csv.AsSpan()))
        {
            rows.Add(row.ToStringArray());
        }

        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { "a", "", "c" }, rows[0]);
        Assert.Equal(new[] { "", "b", "" }, rows[1]);
        Assert.Equal(new[] { "", "", "" }, rows[2]);
    }

    [Fact]
    public void SingleColumn_ParsesCorrectly()
    {
        var csv = "a\nb\nc";
        var rows = new List<string[]>();

        foreach (var row in Csv.Parse(csv.AsSpan()))
        {
            rows.Add(row.ToStringArray());
        }

        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { "a" }, rows[0]);
        Assert.Equal(new[] { "b" }, rows[1]);
        Assert.Equal(new[] { "c" }, rows[2]);
    }

    [Fact]
    public void ManyColumns_ParsesCorrectly()
    {
        // Test with 100 columns to stress SIMD paths
        var columns = Enumerable.Range(0, 100).Select(i => $"col{i}");
        var csv = string.Join(",", columns);

        var reader = Csv.Parse(csv.AsSpan());
        Assert.True(reader.MoveNext());
        Assert.Equal(100, reader.Current.Count);
    }

    [Fact]
    public void LongLine_ParsesCorrectly()
    {
        // Test with 1000-char line to ensure chunking works
        var values = Enumerable.Range(0, 100).Select(i => "value12345");
        var csv = string.Join(",", values);

        var reader = Csv.Parse(csv.AsSpan());
        Assert.True(reader.MoveNext());
        Assert.Equal(100, reader.Current.Count);
        Assert.Equal("value12345", reader.Current[0].ToString());
        Assert.Equal("value12345", reader.Current[99].ToString());
    }

    [Fact]
    public void DifferentDelimiters_Work()
    {
        var csv = "a\tb\tc\n1\t2\t3";
        var rows = new List<string[]>();

        foreach (var row in Csv.Parse(csv.AsSpan(), '\t'))
        {
            rows.Add(row.ToStringArray());
        }

        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "a", "b", "c" }, rows[0]);
        Assert.Equal(new[] { "1", "2", "3" }, rows[1]);
    }

    [Fact]
    public void CrLfLineEndings_ParseCorrectly()
    {
        var csv = "a,b,c\r\n1,2,3\r\n4,5,6";
        var rows = new List<string[]>();

        foreach (var row in Csv.Parse(csv.AsSpan()))
        {
            rows.Add(row.ToStringArray());
        }

        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public void TypeParsing_Works()
    {
        var csv = "123,45.67,true";
        var reader = Csv.Parse(csv.AsSpan());
        Assert.True(reader.MoveNext());

        var row = reader.Current;
        Assert.True(row[0].TryParseInt32(out int intVal));
        Assert.Equal(123, intVal);

        Assert.True(row[1].TryParseDouble(out double doubleVal));
        Assert.Equal(45.67, doubleVal, precision: 5);

        Assert.True(row[2].TryParseBoolean(out bool boolVal));
        Assert.True(boolVal);
    }
}
