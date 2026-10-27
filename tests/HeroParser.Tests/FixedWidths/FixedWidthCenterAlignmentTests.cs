using System.Text;
using HeroParser.FixedWidths;
using Xunit;

namespace HeroParser.Tests.FixedWidths;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed class FixedWidthCenterAlignmentTests
{
    [Theory]
    [InlineData("  ALPHA  ", ' ', "ALPHA")]
    [InlineData("..漢..", '.', "漢")]
    [InlineData("..A.B..", '.', "A.B")]
    public void CenterAlignment_TrimsBothEdgesForCharacterAndUtf8Rows(
        string input,
        char padChar,
        string expected)
    {
        var options = new FixedWidthReadOptions
        {
            DefaultPadChar = padChar,
            DefaultAlignment = FieldAlignment.Center
        };

        int charRowCount = 0;
        foreach (var row in FixedWidth.ReadFromText(input, options))
        {
            Assert.Equal(input, row.RawRecord.ToString());
            Assert.Equal(input, row.GetRawField(0, input.Length).ToString());
            Assert.Equal(expected, row.GetField(0, input.Length).ToString());
            Assert.Equal(expected, row.GetField(0, input.Length, padChar, FieldAlignment.Center).ToString());

            var immutable = row.ToImmutable();
            Assert.Equal(input, immutable.GetField(0, input.Length, padChar, FieldAlignment.None));
            Assert.Equal(expected, immutable.GetField(0, input.Length));
            Assert.Equal(expected, immutable.GetField(0, input.Length, padChar, FieldAlignment.Center));
            charRowCount++;
        }

        Assert.Equal(1, charRowCount);

        byte[] utf8 = Encoding.UTF8.GetBytes(input);
        int byteRowCount = 0;
        foreach (var row in FixedWidth.ReadFromUtf8ByteSpan(utf8, options))
        {
            Assert.Equal(input, row.ToDecodedString());
            Assert.Equal(input, row.GetRawField(0, utf8.Length).ToString());
            Assert.Equal(expected, row.GetField(0, utf8.Length).ToString());

            var immutable = row.ToImmutable();
            Assert.Equal(input, immutable.GetField(0, utf8.Length, (byte)padChar, FieldAlignment.None));
            Assert.Equal(expected, immutable.GetField(0, utf8.Length));
            byteRowCount++;
        }

        Assert.Equal(1, byteRowCount);
    }

    [Fact]
    public void CharacterRows_CenterOverrideDoesNotChangeDefaultNoneAlignment()
    {
        const string input = "..A..";
        var options = new FixedWidthReadOptions { DefaultAlignment = FieldAlignment.None };
        int rowCount = 0;

        foreach (var row in FixedWidth.ReadFromText(input, options))
        {
            Assert.Equal(input, row.GetField(0, input.Length).ToString());
            Assert.Equal("A", row.GetField(0, input.Length, '.', FieldAlignment.Center).ToString());

            var immutable = row.ToImmutable();
            Assert.Equal(input, immutable.GetField(0, input.Length));
            Assert.Equal("A", immutable.GetField(0, input.Length, '.', FieldAlignment.Center));
            rowCount++;
        }

        Assert.Equal(1, rowCount);
    }
}
