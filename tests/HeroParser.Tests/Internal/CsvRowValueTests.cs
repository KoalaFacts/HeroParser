using System.Text;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Rows;
using Xunit;

namespace HeroParser.Tests.Internal;

[Trait("Category", "Unit")]
public class CsvRowValueTests
{
    [Theory]
    [InlineData("\"a\"\"b\"", "a\"b", '"', null, true)]
    [InlineData("'it''s'", "it's", '\'', null, true)]
    [InlineData("\"4\\2\"", "42", '"', '\\', true)]
    [InlineData("\"a\\\\b\"", "\"a\\b\"", '"', '\\', false)]
    [InlineData("\"abc\"", "\"abc\"", '"', null, false)]
    [InlineData("plain", "plain", '"', null, true)]
    public void LogicalValues_PreserveRawFieldsAndClones(string raw, string expected, char quote, char? escape, bool quotes)
    {
        foreach (bool simd in new[] { false, true })
        {
            var options = new CsvReadOptions { Quote = quote, EscapeCharacter = escape, EnableQuotedFields = quotes, UseSimdIfAvailable = simd };
            using var bytes = Csv.ReadFromText(raw + "\n", out _, options);
            Assert.True(bytes.MoveNext());
            var byteClone = bytes.Current.Clone();
            Assert.Equal(raw, bytes.Current[0].ToString());
            Assert.Equal(expected, bytes.Current.GetValueString(0));
            Assert.Equal(expected, bytes.Current.GetValue(0).ToString());
            Assert.False(bytes.MoveNext());
            Assert.Equal(expected, byteClone.GetValueString(0));

            using var chars = Csv.ReadFromText(raw + "\n", options);
            Assert.True(chars.MoveNext());
            var charClone = chars.Current.Clone();
            Assert.Equal(raw, chars.Current[0].ToString());
            Assert.Equal(expected, chars.Current.GetValueString(0));
            Assert.Equal(expected, chars.Current.GetValue(0).ToString());
            Assert.False(chars.MoveNext());
            Assert.Equal(expected, charClone.GetValueString(0));
        }
    }

    [Fact]
    public void CustomQuotesAndTrimming_PreserveQuotedWhitespace()
    {
        var options = new CsvReadOptions { Quote = '\'', TrimFields = true };
        using var chars = Csv.ReadFromText("'  abc  ',  42  \n", options);
        Assert.True(chars.MoveNext());
        Assert.Equal("  abc  ", chars.Current.GetValueString(0));
        Assert.Equal("42", chars.Current.GetValueString(1));
        using var bytes = Csv.ReadFromText("'  abc  ',  42  \n", out _, options);
        Assert.True(bytes.MoveNext());
        Assert.Equal("  abc  ", bytes.Current.GetValueString(0));
        Assert.Equal("42", bytes.Current.GetValueString(1));
    }

    [Fact]
    public void NonCsvRows_DoNotDecodeLiteralQuotes()
    {
        var chars = new CsvRow<char>("\"literal\"".AsSpan(), [-1, 9], 1, 1, 1);
        Assert.Equal("\"literal\"", chars.GetValueString(0));
        Assert.Equal("\"literal\"", chars.GetValue(0).ToString());
        var bytes = new CsvRow<byte>(Encoding.UTF8.GetBytes("\"literal\""), [-1, 9], 1, 1, 1);
        Assert.Equal("\"literal\"", bytes.GetValueString(0));
        Assert.Equal("\"literal\"", bytes.GetValue(0).ToString());
    }
}
