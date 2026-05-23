using System.Buffers;
using HeroParser.FixedWidths;
using HeroParser.SeparatedValues.Reading.Records.MultiSchema;
using Xunit;

namespace HeroParser.Tests.Internal;

[Trait("Category", "Unit")]
public class DiscriminatorKeyTests
{
    [Theory]
    [InlineData("")]
    [InlineData("H")]
    [InlineData("HDT")]
    [InlineData("ABCDEFGH")] // exactly MAX_PACKED_LENGTH
    public void TryCreate_Chars_PacksAscii(string value)
    {
        Assert.True(DiscriminatorKey.TryCreate(value.AsSpan(), out var key));
        Assert.Equal(value.Length, key.Length);
        Assert.Equal(value, key.ToString());
    }

    [Fact]
    public void TryCreate_Chars_TooLong_Fails()
    {
        Assert.False(DiscriminatorKey.TryCreate("ABCDEFGHI".AsSpan(), out var key));
        Assert.Equal(0, key.Length);
    }

    [Fact]
    public void TryCreate_Chars_NonAscii_Fails()
    {
        Assert.False(DiscriminatorKey.TryCreate("Hé".AsSpan(), out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("H")]
    [InlineData("ABCDEFGH")]
    public void TryCreate_Bytes_PacksAscii(string value)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(value);
        Assert.True(DiscriminatorKey.TryCreate(bytes.AsSpan(), out var key));
        Assert.Equal(value.Length, key.Length);
    }

    [Fact]
    public void TryCreate_Bytes_TooLong_Fails()
    {
        var bytes = new byte[9];
        Array.Fill(bytes, (byte)'A');
        Assert.False(DiscriminatorKey.TryCreate(bytes.AsSpan(), out _));
    }

    [Fact]
    public void TryCreate_Bytes_NonAscii_Fails()
    {
        Assert.False(DiscriminatorKey.TryCreate("Hé"u8, out _));
    }

    [Fact]
    public void TryCreateLowercase_Chars_ConvertsUppercase()
    {
        Assert.True(DiscriminatorKey.TryCreateLowercase("ABC".AsSpan(), out var upper));
        Assert.True(DiscriminatorKey.TryCreate("abc".AsSpan(), out var lower));
        Assert.Equal(lower, upper);
    }

    [Fact]
    public void TryCreateLowercase_Chars_TooLong_Fails()
    {
        Assert.False(DiscriminatorKey.TryCreateLowercase("ABCDEFGHI".AsSpan(), out _));
    }

    [Fact]
    public void TryCreateLowercase_Chars_NonAscii_Fails()
    {
        Assert.False(DiscriminatorKey.TryCreateLowercase("Hé".AsSpan(), out _));
    }

    [Fact]
    public void TryCreateLowercase_Bytes_ConvertsUppercase()
    {
        Assert.True(DiscriminatorKey.TryCreateLowercase("ABC"u8, out var upper));
        Assert.True(DiscriminatorKey.TryCreate("abc"u8, out var lower));
        Assert.Equal(lower, upper);
    }

    [Fact]
    public void TryCreateLowercase_Bytes_TooLong_Fails()
    {
        var bytes = new byte[9];
        Array.Fill(bytes, (byte)'A');
        Assert.False(DiscriminatorKey.TryCreateLowercase(bytes.AsSpan(), out _));
    }

    [Fact]
    public void TryCreateLowercase_Bytes_NonAscii_Fails()
    {
        Assert.False(DiscriminatorKey.TryCreateLowercase("Hé"u8, out _));
    }

    [Fact]
    public void FromString_PacksAndLowercases()
    {
        var upper = DiscriminatorKey.FromString("ABC", lowercase: false);
        var lower = DiscriminatorKey.FromString("ABC", lowercase: true);
        Assert.NotEqual(upper, lower);
        Assert.Equal("ABC", upper.ToString());
        Assert.Equal("abc", lower.ToString());
    }

    [Fact]
    public void FromString_NullThrows()
    {
        Assert.Throws<ArgumentNullException>(() => DiscriminatorKey.FromString(null!));
    }

    [Fact]
    public void FromString_TooLongThrows()
    {
        Assert.Throws<ArgumentException>(() => DiscriminatorKey.FromString("ABCDEFGHI"));
    }

    [Fact]
    public void FromString_Lowercase_TooLongThrows()
    {
        Assert.Throws<ArgumentException>(() => DiscriminatorKey.FromString("ABCDEFGHI", lowercase: true));
    }

    [Fact]
    public void FromInt_PacksDigits()
    {
        var key = DiscriminatorKey.FromInt(42);
        Assert.Equal("42", key.ToString());
    }

    [Fact]
    public void FromInt_TooLargeThrows()
    {
        // int.MinValue is "-2147483648" = 11 chars, too long for 8-char pack
        Assert.Throws<ArgumentException>(() => DiscriminatorKey.FromInt(int.MinValue));
    }

    [Fact]
    public void Equality_SameValue_IsEqual()
    {
        DiscriminatorKey.TryCreate("ABC".AsSpan(), out var a);
        DiscriminatorKey.TryCreate("ABC".AsSpan(), out var b);
        Assert.True(a.Equals(b));
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.True(a.Equals((object)b));
    }

    [Fact]
    public void Equality_DifferentLength_IsDifferent()
    {
        DiscriminatorKey.TryCreate("ABC".AsSpan(), out var a);
        DiscriminatorKey.TryCreate("AB".AsSpan(), out var b);
        Assert.NotEqual(a, b);
        Assert.False(a == b);
        Assert.True(a != b);
    }

    [Fact]
    public void Equality_AgainstNonKey_IsFalse()
    {
        DiscriminatorKey.TryCreate("ABC".AsSpan(), out var a);
        Assert.False(a.Equals("ABC"));
    }

    [Fact]
    public void MatchesRaw_RoundTrip_True()
    {
        DiscriminatorKey.TryCreate("ABC".AsSpan(), out var key);
        key.GetRawValues(out var packed, out var len);
        Assert.True(key.MatchesRaw(packed, len));
        Assert.False(key.MatchesRaw(packed ^ 1, len));
    }

    [Fact]
    public void ToString_EmptyKey_IsEmpty()
    {
        DiscriminatorKey.TryCreate(ReadOnlySpan<char>.Empty, out var key);
        Assert.Equal(string.Empty, key.ToString());
    }
}

[Trait("Category", "Unit")]
public class FixedWidthLineScannerTests
{
    [Fact]
    public void ContainsLineBreak_Chars()
    {
        Assert.False(FixedWidthLineScanner.ContainsLineBreak("abc".AsSpan()));
        Assert.True(FixedWidthLineScanner.ContainsLineBreak("ab\nc".AsSpan()));
        Assert.True(FixedWidthLineScanner.ContainsLineBreak("ab\rc".AsSpan()));
    }

    [Fact]
    public void FindLineEnd_Bytes_FindsFirst()
    {
        Assert.Equal(3, FixedWidthLineScanner.FindLineEnd("abc\ndef"u8));
        Assert.Equal(3, FixedWidthLineScanner.FindLineEnd("abc\rdef"u8));
        Assert.Equal(-1, FixedWidthLineScanner.FindLineEnd("abcdef"u8));
    }

    [Fact]
    public void FindLineEnd_Chars_FindsFirst()
    {
        Assert.Equal(3, FixedWidthLineScanner.FindLineEnd("abc\ndef".AsSpan()));
        Assert.Equal(-1, FixedWidthLineScanner.FindLineEnd("abcdef".AsSpan()));
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("abc", 0)]
    [InlineData("a\n", 1)]
    [InlineData("a\r", 1)]
    [InlineData("a\r\n", 1)] // CRLF counts as one
    [InlineData("a\nb\nc", 2)]
    [InlineData("a\r\nb\r\nc", 2)]
    [InlineData("\n\n\n", 3)]
    public void CountNewlines_Bytes(string input, int expected)
    {
        Assert.Equal(expected, FixedWidthLineScanner.CountNewlines(System.Text.Encoding.ASCII.GetBytes(input).AsSpan()));
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("abc", 0)]
    [InlineData("a\n", 1)]
    [InlineData("a\r", 1)]
    [InlineData("a\r\n", 1)]
    [InlineData("a\nb\nc", 2)]
    public void CountNewlines_Chars(string input, int expected)
    {
        Assert.Equal(expected, FixedWidthLineScanner.CountNewlines(input.AsSpan()));
    }

    [Fact]
    public void CountNewlines_Sequence_Single_Segment()
    {
        var sequence = new ReadOnlySequence<byte>(System.Text.Encoding.ASCII.GetBytes("a\nb\r\nc\r"));
        Assert.Equal(3, FixedWidthLineScanner.CountNewlines(sequence));
    }

    [Fact]
    public void CountNewlines_Sequence_CrLfSplitAcrossSegments_DedupedCorrectly()
    {
        var first = new TestSegment("a\r"u8.ToArray());
        var second = first.Append("\nb\n"u8.ToArray());
        var sequence = new ReadOnlySequence<byte>(first, 0, second, second.Memory.Length);
        // CR is the last byte of segment 1, LF is the first of segment 2 -> one CRLF
        // Then LF in segment 2 -> another newline. Total = 2.
        Assert.Equal(2, FixedWidthLineScanner.CountNewlines(sequence));
    }

    [Fact]
    public void CountNewlines_Sequence_CrAtEndOfSequence_Counts()
    {
        var first = new TestSegment("a\r"u8.ToArray());
        var second = first.Append("bc"u8.ToArray());
        var sequence = new ReadOnlySequence<byte>(first, 0, second, second.Memory.Length);
        Assert.Equal(1, FixedWidthLineScanner.CountNewlines(sequence));
    }

    private sealed class TestSegment : ReadOnlySequenceSegment<byte>
    {
        public TestSegment(byte[] data)
        {
            Memory = data;
        }

        public TestSegment Append(byte[] data)
        {
            var next = new TestSegment(data) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }
}

[Trait("Category", "Unit")]
public class FixedWidthByteSpanRowTests
{
    private static readonly FixedWidthReadOptions strictOptions = new() { AllowShortRows = false };
    private static readonly FixedWidthReadOptions lenientOptions = new() { AllowShortRows = true };

    [Fact]
    public void GetField_Default_TrimsUsingLeftAlignment()
    {
        var bytes = "hello   world   "u8.ToArray();
        var row = CreateRow(bytes, strictOptions);
        var field = row.GetField(0, 8);
        Assert.Equal("hello", System.Text.Encoding.UTF8.GetString(field.ByteSpan));
    }

    [Fact]
    public void GetField_RightAlignment_TrimsStart()
    {
        var bytes = "   42"u8.ToArray();
        var row = CreateRow(bytes, strictOptions);
        var field = row.GetField(0, 5, (byte)' ', FieldAlignment.Right);
        Assert.Equal("42", System.Text.Encoding.UTF8.GetString(field.ByteSpan));
    }

    [Fact]
    public void GetField_CenterAlignment_TrimsBothSides()
    {
        var bytes = " 42 "u8.ToArray();
        var row = CreateRow(bytes, strictOptions);
        var field = row.GetField(0, 4, (byte)' ', FieldAlignment.Center);
        Assert.Equal("42", System.Text.Encoding.UTF8.GetString(field.ByteSpan));
    }

    [Fact]
    public void GetField_NoneAlignment_DoesNotTrim()
    {
        var bytes = " 42 "u8.ToArray();
        var row = CreateRow(bytes, strictOptions);
        var field = row.GetField(0, 4, (byte)' ', FieldAlignment.None);
        Assert.Equal(" 42 ", System.Text.Encoding.UTF8.GetString(field.ByteSpan));
    }

    [Fact]
    public void GetField_NegativeStart_Throws()
    {
        var bytes = "abc"u8.ToArray();
        var row = CreateRow(bytes, strictOptions);
        // ref struct cannot be captured in a lambda — inline the assertion with a try/catch
        try
        {
            row.GetField(-1, 2);
            Assert.Fail("Expected ArgumentOutOfRangeException");
        }
        catch (ArgumentOutOfRangeException) { }
    }

    [Fact]
    public void GetField_NegativeLength_Throws()
    {
        var bytes = "abc"u8.ToArray();
        var row = CreateRow(bytes, strictOptions);
        try
        {
            row.GetField(0, -1);
            Assert.Fail("Expected ArgumentOutOfRangeException");
        }
        catch (ArgumentOutOfRangeException) { }
    }

    [Fact]
    public void GetField_BeyondRowStrict_Throws()
    {
        var bytes = "abc"u8.ToArray();
        var row = CreateRow(bytes, strictOptions);
        try
        {
            row.GetField(0, 10);
            Assert.Fail("Expected FixedWidthException");
        }
        catch (FixedWidthException ex)
        {
            Assert.Equal(FixedWidthErrorCode.FieldOutOfBounds, ex.ErrorCode);
        }
    }

    [Fact]
    public void GetField_BeyondRowLenient_TruncatesSilently()
    {
        var bytes = "abc"u8.ToArray();
        var row = CreateRow(bytes, lenientOptions);
        var field = row.GetField(0, 10, 0, FieldAlignment.None);
        Assert.Equal("abc", System.Text.Encoding.UTF8.GetString(field.ByteSpan));
    }

    [Fact]
    public void GetField_StartPastEndLenient_ReturnsEmpty()
    {
        var bytes = "abc"u8.ToArray();
        var row = CreateRow(bytes, lenientOptions);
        var field = row.GetField(10, 5);
        Assert.Equal(0, field.ByteSpan.Length);
    }

    [Fact]
    public void GetRawField_DoesNotTrim()
    {
        var bytes = " abc "u8.ToArray();
        var row = CreateRow(bytes, strictOptions);
        var field = row.GetRawField(0, 5);
        Assert.Equal(" abc ", System.Text.Encoding.UTF8.GetString(field.ByteSpan));
    }

    [Fact]
    public void ToDecodedString_ReturnsUtf8()
    {
        var bytes = "café"u8.ToArray();
        var row = CreateRow(bytes, strictOptions);
        Assert.Equal("café", row.ToDecodedString());
    }

    [Fact]
    public void RawRecord_ReturnsFullSpan()
    {
        var bytes = "hello"u8.ToArray();
        var row = CreateRow(bytes, strictOptions);
        Assert.Equal(5, row.Length);
        Assert.Equal("hello", System.Text.Encoding.UTF8.GetString(row.RawRecord));
    }

    [Fact]
    public void RecordAndLineNumber_Exposed()
    {
        var row = new FixedWidthByteSpanRow("abc"u8, recordNumber: 5, sourceLineNumber: 7, strictOptions);
        Assert.Equal(5, row.RecordNumber);
        Assert.Equal(7, row.SourceLineNumber);
    }

    [Fact]
    public void Clone_ProducesIndependentCopy()
    {
        var bytes = "hello"u8.ToArray();
        var row = new FixedWidthByteSpanRow(bytes, 1, 1, strictOptions);
        var clone = row.Clone();
        Assert.Equal(row.Length, clone.Length);
        Assert.Equal(row.ToDecodedString(), clone.ToDecodedString());
    }

    [Fact]
    public void ToImmutable_ProducesEscapableRow()
    {
        var bytes = "hello"u8.ToArray();
        var row = new FixedWidthByteSpanRow(bytes, 3, 4, strictOptions);
        var immutable = row.ToImmutable();
        Assert.Equal(3, immutable.RecordNumber);
        Assert.Equal(4, immutable.SourceLineNumber);
        Assert.Equal(5, immutable.Length);
        Assert.Equal("hello", immutable.ToDecodedString());
    }

    [Fact]
    public void Immutable_GetField_LenientShortRow_ReturnsEmpty()
    {
        var immutable = new FixedWidthByteSpanRow("abc"u8, 1, 1, lenientOptions).ToImmutable();
        Assert.Equal(string.Empty, immutable.GetField(10, 5));
    }

    [Fact]
    public void Immutable_GetField_StrictShortRow_Throws()
    {
        var immutable = new FixedWidthByteSpanRow("abc"u8, 1, 1, strictOptions).ToImmutable();
        Assert.Throws<FixedWidthException>(() => immutable.GetField(0, 10));
    }

    [Fact]
    public void Immutable_GetField_InvalidArgs_Throw()
    {
        var immutable = new FixedWidthByteSpanRow("abc"u8, 1, 1, strictOptions).ToImmutable();
        Assert.Throws<ArgumentOutOfRangeException>(() => immutable.GetField(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => immutable.GetField(0, -1));
    }

    [Theory]
    [InlineData(FieldAlignment.Left, " hello")]  // trims trailing pad
    [InlineData(FieldAlignment.Right, "hello ")] // trims leading pad
    [InlineData(FieldAlignment.Center, "hello")] // trims both
    [InlineData(FieldAlignment.None, " hello ")] // no trim
    public void Immutable_GetField_TrimAlignments(FieldAlignment alignment, string expected)
    {
        var row = new FixedWidthByteSpanRow(" hello "u8, 1, 1, strictOptions).ToImmutable();
        Assert.Equal(expected, row.GetField(0, 7, (byte)' ', alignment));
    }

    [Fact]
    public void Immutable_ToDecodedString_ReturnsUtf8()
    {
        var row = new FixedWidthByteSpanRow("café"u8, 1, 1, strictOptions).ToImmutable();
        Assert.Equal("café", row.ToDecodedString());
    }

    [Fact]
    public void Immutable_RawRecord_ExposesBytes()
    {
        var row = new FixedWidthByteSpanRow("abc"u8, 1, 1, strictOptions).ToImmutable();
        Assert.Equal("abc"u8.ToArray(), row.RawRecord.ToArray());
    }

    private static FixedWidthByteSpanRow CreateRow(byte[] data, FixedWidthReadOptions options)
        => new(data, recordNumber: 1, sourceLineNumber: 1, options);
}
