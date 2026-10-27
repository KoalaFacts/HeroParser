using System.Text;
using HeroParser.FixedWidths;
using Xunit;

namespace HeroParser.Tests.FixedWidths;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed class FixedWidthUnicodePaddingTests
{
    [Theory]
    [InlineData("ééAéé", 'é', FieldAlignment.Left, "ééA")]
    [InlineData("ééAéé", 'é', FieldAlignment.Right, "Aéé")]
    [InlineData("ééAéé", 'é', FieldAlignment.Center, "A")]
    [InlineData("ééAéé", 'é', FieldAlignment.None, "ééAéé")]
    [InlineData("漢漢A漢漢", '漢', FieldAlignment.Center, "A")]
    [InlineData("\0AĀ", 'Ā', FieldAlignment.Right, "\0AĀ")]
    [InlineData("\0AĀ", 'Ā', FieldAlignment.Left, "\0A")]
    [InlineData("ĀĀAĀĀ", 'Ā', FieldAlignment.Center, "A")]
    public void UnicodeDefaultPad_TrimsCompleteUtf8SequencesAndMatchesCharacterReader(
        string source,
        char padChar,
        FieldAlignment alignment,
        string expected)
    {
        var options = new FixedWidthReadOptions
        {
            DefaultPadChar = padChar,
            DefaultAlignment = alignment
        };

        byte[] bytes = Encoding.UTF8.GetBytes(source);
        int byteRows = 0;
        foreach (var row in FixedWidth.ReadFromUtf8ByteSpan(bytes, options))
        {
            Assert.Equal(source, row.ToDecodedString());
            Assert.True(row.RawRecord.SequenceEqual(bytes));
            Assert.Equal(source, row.GetRawField(0, bytes.Length).ToString());
            Assert.Equal(expected, row.GetField(0, bytes.Length).ToString());

            var immutable = row.ToImmutable();
            Assert.True(immutable.RawRecord.SequenceEqual(bytes));
            Assert.Equal(expected, immutable.GetField(0, bytes.Length));
            Assert.Equal(source, immutable.GetField(0, bytes.Length, 0, FieldAlignment.None));
            byteRows++;
        }

        Assert.Equal(1, byteRows);

        int charRows = 0;
        foreach (var row in FixedWidth.ReadFromText(source, options))
        {
            Assert.Equal(expected, row.GetField(0, source.Length).ToString());
            Assert.Equal(expected, row.ToImmutable().GetField(0, source.Length));
            charRows++;
        }

        Assert.Equal(1, charRows);
    }

    [Fact]
    public void IncompleteUtf8Suffix_IsNotMistakenForACompletePadCharacter()
    {
        byte[] bytes = [0xC3, 0xA9, (byte)'A', 0xC3];
        var options = new FixedWidthReadOptions
        {
            DefaultPadChar = 'é',
            DefaultAlignment = FieldAlignment.Left
        };

        int rows = 0;
        foreach (var row in FixedWidth.ReadFromUtf8ByteSpan(bytes, options))
        {
            Assert.Equal("éA\uFFFD", row.GetField(0, bytes.Length).ToString());
            Assert.Equal("éA\uFFFD", row.ToImmutable().GetField(0, bytes.Length));
            rows++;
        }

        Assert.Equal(1, rows);
    }

    [Fact]
    public void ExplicitBytePaddingOverload_PreservesByteBasedSemantics()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("..éA");
        var options = new FixedWidthReadOptions
        {
            DefaultPadChar = 'é',
            DefaultAlignment = FieldAlignment.Center
        };

        int rows = 0;
        foreach (var row in FixedWidth.ReadFromUtf8ByteSpan(bytes, options))
        {
            Assert.Equal("éA", row.GetField(0, bytes.Length, (byte)'.', FieldAlignment.Right).ToString());
            Assert.Equal("éA", row.ToImmutable().GetField(0, bytes.Length, (byte)'.', FieldAlignment.Right));
            rows++;
        }

        Assert.Equal(1, rows);
    }
}
