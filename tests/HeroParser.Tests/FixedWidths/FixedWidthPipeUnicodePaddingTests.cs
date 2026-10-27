using System.IO.Pipelines;
using System.Text;
using HeroParser.FixedWidths;
using Xunit;

namespace HeroParser.Tests.FixedWidths;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed class FixedWidthPipeUnicodePaddingTests
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
    public async Task OwnedPipeRow_DefaultUnicodePaddingMatchesBorrowedAndImmutableRows(
        string input,
        char padChar,
        FieldAlignment alignment,
        string expected)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        var options = new FixedWidthReadOptions
        {
            RecordLength = bytes.Length,
            DefaultPadChar = padChar,
            DefaultAlignment = alignment
        };

        // Materialize the owned row before accessing its fields. A PipeReader may
        // recycle segments as soon as the enumerator advances or is disposed.
        var owned = await ReadSinglePipeRowAsync(bytes, options);
        Assert.Equal(1, owned.RecordNumber);
        Assert.True(owned.RawRecord.SequenceEqual(bytes));
        Assert.Equal(input, owned.ToDecodedString());
        Assert.Equal(input, owned.GetRawField(0, bytes.Length).ToString());
        Assert.Equal(expected, owned.GetField(0, bytes.Length).ToString());

        int borrowedCount = 0;
        foreach (var borrowed in FixedWidth.ReadFromUtf8ByteSpan(bytes, options))
        {
            Assert.Equal(expected, borrowed.GetField(0, bytes.Length).ToString());
            Assert.Equal(expected, borrowed.ToImmutable().GetField(0, bytes.Length));
            borrowedCount++;
        }

        Assert.Equal(1, borrowedCount);
    }

    [Fact]
    public async Task OwnedPipeRow_IncompleteUtf8SuffixIsNotTruncatedAsPadding()
    {
        byte[] bytes = [0xC3, 0xA9, (byte)'A', 0xC3];
        var options = new FixedWidthReadOptions
        {
            RecordLength = bytes.Length,
            DefaultPadChar = 'é',
            DefaultAlignment = FieldAlignment.Left
        };

        var owned = await ReadSinglePipeRowAsync(bytes, options);
        Assert.True(owned.RawRecord.SequenceEqual(bytes));
        Assert.Equal("éA\uFFFD", owned.GetField(0, bytes.Length).ToString());
    }

    [Fact]
    public async Task OwnedPipeRow_ExplicitBytePaddingStillOverridesUnicodeDefault()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("..éA");
        var options = new FixedWidthReadOptions
        {
            RecordLength = bytes.Length,
            DefaultPadChar = 'é',
            DefaultAlignment = FieldAlignment.Center
        };

        var owned = await ReadSinglePipeRowAsync(bytes, options);
        Assert.Equal("éA", owned.GetField(0, bytes.Length, (byte)'.', FieldAlignment.Right).ToString());
        Assert.Equal("..éA", owned.GetRawField(0, bytes.Length).ToString());
    }

    private static async Task<FixedWidthPipeRow> ReadSinglePipeRowAsync(
        byte[] bytes,
        FixedWidthReadOptions options)
    {
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync(bytes, TestContext.Current.CancellationToken);
        await pipe.Writer.CompleteAsync();

        var rows = new List<FixedWidthPipeRow>();
        await foreach (var row in FixedWidth.ReadFromPipeReaderAsync(
            pipe.Reader, options, TestContext.Current.CancellationToken))
        {
            rows.Add(row);
        }

        return Assert.Single(rows);
    }
}
