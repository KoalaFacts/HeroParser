using Xunit;

namespace HeroParser.Tests;

/// <summary>
/// Integration tests for the full public API.
/// </summary>
public class IntegrationTests
{
    [Fact]
    public void Parse_WithSpan_Works()
    {
        var csv = "a,b,c\n1,2,3";
        var rows = new List<string[]>();

        foreach (var row in Csv.Parse(csv.AsSpan()))
        {
            rows.Add(row.ToStringArray());
        }

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void ParseComma_Specialized_Works()
    {
        var csv = "a,b,c\n1,2,3";
        var rows = new List<string[]>();

        foreach (var row in Csv.ParseComma(csv.AsSpan()))
        {
            rows.Add(row.ToStringArray());
        }

        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "a", "b", "c" }, rows[0]);
    }

    [Fact]
    public void ParseTab_Specialized_Works()
    {
        var csv = "a\tb\tc\n1\t2\t3";
        var rows = new List<string[]>();

        foreach (var row in Csv.ParseTab(csv.AsSpan()))
        {
            rows.Add(row.ToStringArray());
        }

        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { "a", "b", "c" }, rows[0]);
    }

    [Fact]
    public void CsvReader_MoveNext_Works()
    {
        var csv = "a,b\n1,2\n3,4";
        var reader = Csv.Parse(csv.AsSpan());

        Assert.True(reader.MoveNext());
        Assert.Equal(2, reader.Current.Count);
        Assert.Equal("a", reader.Current[0].ToString());

        Assert.True(reader.MoveNext());
        Assert.Equal("1", reader.Current[0].ToString());

        Assert.True(reader.MoveNext());
        Assert.Equal("3", reader.Current[0].ToString());

        Assert.False(reader.MoveNext());
    }

    [Fact]
    public void CsvRow_IndexAccess_Works()
    {
        var csv = "a,b,c";
        var reader = Csv.Parse(csv.AsSpan());
        reader.MoveNext();

        var row = reader.Current;
        Assert.Equal("a", row[0].ToString());
        Assert.Equal("b", row[1].ToString());
        Assert.Equal("c", row[2].ToString());
    }

    [Fact]
    public void CsvRow_Count_ReturnsCorrectValue()
    {
        var csv = "a,b,c,d,e";
        var reader = Csv.Parse(csv.AsSpan());
        reader.MoveNext();

        Assert.Equal(5, reader.Current.Count);
    }

    [Fact]
    public void CsvRow_ToStringArray_Materializes()
    {
        var csv = "a,b,c";
        var reader = Csv.Parse(csv.AsSpan());
        reader.MoveNext();

        var array = reader.Current.ToStringArray();

        Assert.Equal(3, array.Length);
        Assert.Equal(new[] { "a", "b", "c" }, array);
    }

    [Fact]
    public void CsvCol_Span_ReturnsRawData()
    {
        var csv = "hello,world";
        var reader = Csv.Parse(csv.AsSpan());
        reader.MoveNext();

        var span = reader.Current[0].Span;
        Assert.Equal("hello", span.ToString());
    }

    [Fact]
    public void CsvCol_Length_ReturnsCorrectValue()
    {
        var csv = "hello,world";
        var reader = Csv.Parse(csv.AsSpan());
        reader.MoveNext();

        Assert.Equal(5, reader.Current[0].Length);
        Assert.Equal(5, reader.Current[1].Length);
    }

    [Fact]
    public void CsvCol_IsEmpty_Works()
    {
        var csv = "a,,c";
        var reader = Csv.Parse(csv.AsSpan());
        reader.MoveNext();

        Assert.False(reader.Current[0].IsEmpty);
        Assert.True(reader.Current[1].IsEmpty);
        Assert.False(reader.Current[2].IsEmpty);
    }

    [Fact]
    public void CsvCol_Parse_Generic_Works()
    {
        var csv = "123,45.67";
        var reader = Csv.Parse(csv.AsSpan());
        reader.MoveNext();

        var intVal = reader.Current[0].Parse<int>();
        var doubleVal = reader.Current[1].Parse<double>();

        Assert.Equal(123, intVal);
        Assert.Equal(45.67, doubleVal, precision: 5);
    }

    [Fact]
    public void CsvCol_TryParse_SucceedsOnValid()
    {
        var csv = "123";
        var reader = Csv.Parse(csv.AsSpan());
        reader.MoveNext();

        Assert.True(reader.Current[0].TryParse<int>(out var val));
        Assert.Equal(123, val);
    }

    [Fact]
    public void CsvCol_TryParse_FailsOnInvalid()
    {
        var csv = "abc";
        var reader = Csv.Parse(csv.AsSpan());
        reader.MoveNext();

        Assert.False(reader.Current[0].TryParse<int>(out var val));
        Assert.Equal(0, val);
    }

    [Fact]
    public void CsvCol_TypeSpecificParsing_Works()
    {
        var csv = "123,45.67,true,2024-01-01,12345678-1234-1234-1234-123456789012";
        var reader = Csv.Parse(csv.AsSpan());
        reader.MoveNext();

        var row = reader.Current;

        Assert.True(row[0].TryParseInt32(out int intVal));
        Assert.Equal(123, intVal);

        Assert.True(row[1].TryParseDouble(out double doubleVal));
        Assert.Equal(45.67, doubleVal, precision: 5);

        Assert.True(row[2].TryParseBoolean(out bool boolVal));
        Assert.True(boolVal);

        Assert.True(row[3].TryParseDateTime(out DateTime dateVal));
        Assert.Equal(new DateTime(2024, 1, 1), dateVal);

        Assert.True(row[4].TryParseGuid(out Guid guidVal));
        Assert.NotEqual(Guid.Empty, guidVal);
    }

    [Fact]
    public void CsvCol_Equals_Span_Works()
    {
        var csv = "hello";
        var reader = Csv.Parse(csv.AsSpan());
        reader.MoveNext();

        Assert.True(reader.Current[0].Equals("hello".AsSpan()));
        Assert.False(reader.Current[0].Equals("world".AsSpan()));
    }

    [Fact]
    public void CsvCol_Equals_String_Works()
    {
        var csv = "hello";
        var reader = Csv.Parse(csv.AsSpan());
        reader.MoveNext();

        Assert.True(reader.Current[0].Equals("hello"));
        Assert.False(reader.Current[0].Equals("world"));
    }

    [Fact]
    public void CsvCol_ImplicitConversion_ToSpan_Works()
    {
        var csv = "hello";
        var reader = Csv.Parse(csv.AsSpan());
        reader.MoveNext();

        ReadOnlySpan<char> span = reader.Current[0];
        Assert.Equal("hello", span.ToString());
    }

    [Fact]
    public void LargeDataset_1000Rows_ParsesCorrectly()
    {
        var lines = new List<string>();
        for (int i = 0; i < 1000; i++)
        {
            lines.Add($"{i},value{i},data{i}");
        }
        var csv = string.Join("\n", lines);

        var rows = new List<string[]>();
        foreach (var row in Csv.Parse(csv.AsSpan()))
        {
            rows.Add(row.ToStringArray());
        }

        Assert.Equal(1000, rows.Count);
        Assert.Equal("0", rows[0][0]);
        Assert.Equal("999", rows[999][0]);
    }

    [Fact]
    public void LargeDataset_100Columns_ParsesCorrectly()
    {
        var columns = string.Join(",", Enumerable.Range(0, 100).Select(i => $"col{i}"));
        var csv = columns;

        var reader = Csv.Parse(csv.AsSpan());
        Assert.True(reader.MoveNext());
        Assert.Equal(100, reader.Current.Count);

        for (int i = 0; i < 100; i++)
        {
            Assert.Equal($"col{i}", reader.Current[i].ToString());
        }
    }

    [Fact]
    public void RealWorld_CSV_Example()
    {
        var csv = @"Name,Age,City,Salary
John Doe,30,New York,75000
Jane Smith,25,San Francisco,85000
Bob Johnson,35,Chicago,65000";

        var rows = new List<(string Name, int Age, string City, int Salary)>();

        var reader = Csv.Parse(csv.AsSpan());
        reader.MoveNext(); // Skip header

        while (reader.MoveNext())
        {
            var row = reader.Current;
            rows.Add((
                row[0].ToString(),
                row[1].Parse<int>(),
                row[2].ToString(),
                row[3].Parse<int>()
            ));
        }

        Assert.Equal(3, rows.Count);
        Assert.Equal("John Doe", rows[0].Name);
        Assert.Equal(30, rows[0].Age);
        Assert.Equal(85000, rows[1].Salary);
    }
}
