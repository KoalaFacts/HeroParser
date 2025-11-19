using HeroParser.SeparatedValues;
using Xunit;

namespace HeroParser.Tests;

/// <summary>
/// Tests to verify that all code examples in README.md actually compile and work.
/// </summary>
public class ReadmeExamplesTests
{
    [Fact]
    public void BasicIteration_Example()
    {
        // Example from README: Basic Iteration (Zero Allocations)
        var csv = "1,Product A,19.99\n2,Product B,29.99\n3,Product C,39.99";

        foreach (var row in Csv.ReadFromText(csv))
        {
            // Access columns by index - no allocations
            var id = row[0].Parse<int>();
            var name = row[1].CharSpan; // ReadOnlySpan<char>
            var price = row[2].Parse<decimal>();

            Assert.True(id > 0);
            Assert.True(name.Length > 0);
            Assert.True(price > 0);
        }
    }

    [Fact]
    public void QuoteHandling_Example()
    {
        // Example from README: Quote Handling (RFC 4180)
        var csv = "field1,\"field2\",\"field,3\"\n" +
                  "aaa,\"b,bb\",ccc\n" +
                  "zzz,\"y\"\"yy\",xxx";  // Escaped quote

        foreach (var row in Csv.ReadFromText(csv))
        {
            // Access raw value (includes quotes)
            var raw = row[1].ToString();

            // Remove surrounding quotes and unescape
            var unquoted = row[1].UnquoteToString();

            // Zero-allocation unquote (returns span)
            var span = row[1].Unquote();

            Assert.NotNull(raw);
            Assert.NotNull(unquoted);
        }
    }

    [Fact]
    public void TypeParsing_Example()
    {
        // Example from README: Type Parsing
        var csv = "42,3.14,2024-01-15,true";

        foreach (var row in Csv.ReadFromText(csv))
        {
            // Generic parsing (ISpanParsable<T>)
            var value = row[0].Parse<int>();
            Assert.Equal(42, value);

            // Optimized type-specific methods
            if (row[1].TryParseDouble(out double d))
            {
                Assert.True(d > 3.0);
            }

            if (row[2].TryParseDateTime(out DateTime dt))
            {
                Assert.Equal(2024, dt.Year);
            }

            if (row[3].TryParseBoolean(out bool b))
            {
                Assert.True(b);
            }
        }
    }

    [Fact]
    public void LazyEvaluation_Example()
    {
        // Example from README: Lazy Evaluation
        var csv = "skip,data\n1,2\n3,4\n5,6";
        int processedCount = 0;

        bool ShouldSkip(CsvCharSpanRow row) => row[0].ToString() == "skip";

        // Columns are NOT parsed until first access
        foreach (var row in Csv.ReadFromText(csv))
        {
            // Skip rows without parsing columns
            if (ShouldSkip(row))
                continue;

            // Only parse columns when accessed
            var value = row[0].Parse<int>();  // First access triggers parsing
            processedCount++;
            Assert.True(value > 0);
        }

        Assert.Equal(3, processedCount);
    }

    [Fact]
    public void CustomOptions_Example()
    {
        // Example from README: Custom options
        var csvData = "a,b,c\n1,2,3";

        var options = new CsvParserOptions
        {
            Delimiter = ',',  // Default
            Quote = '"',      // Default - RFC 4180 compliant
            MaxColumns = 256  // Default
        };
        var reader = Csv.ReadFromText(csvData, options);

        int rowCount = 0;
        foreach (var row in reader)
        {
            rowCount++;
            Assert.Equal(3, row.ColumnCount);
        }

        Assert.Equal(2, rowCount);
    }
}
