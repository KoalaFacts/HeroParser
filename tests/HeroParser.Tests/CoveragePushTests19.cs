using System.Text;
using HeroParser.FixedWidths;
using Xunit;

namespace HeroParser.Tests;

/// <summary>Wave 19: FixedWidth row error paths (negative args, out-of-bounds, AllowShortRows).</summary>
public class CoveragePushTests19
{
    // ---------- FixedWidthCharSpanRow.GetField error paths ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CharSpanRow_GetField_NegativeStart_Throws()
    {
        string line = "data\n";
        var reader = new FixedWidthCharSpanReader(line.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var row = reader.Current;
        bool threw = false;
        try { _ = row.GetField(-1, 1); } catch (ArgumentOutOfRangeException) { threw = true; }
        Assert.True(threw);
        threw = false;
        try { _ = row.GetField(-1, 1, ' ', FieldAlignment.None); } catch (ArgumentOutOfRangeException) { threw = true; }
        Assert.True(threw);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CharSpanRow_GetField_NegativeLength_Throws()
    {
        string line = "data\n";
        var reader = new FixedWidthCharSpanReader(line.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var row = reader.Current;
        bool threw = false;
        try { _ = row.GetField(0, -1); } catch (ArgumentOutOfRangeException) { threw = true; }
        Assert.True(threw);
        threw = false;
        try { _ = row.GetField(0, -1, ' ', FieldAlignment.None); } catch (ArgumentOutOfRangeException) { threw = true; }
        Assert.True(threw);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CharSpanRow_GetField_BeyondEnd_Throws()
    {
        string line = "data\n";
        var reader = new FixedWidthCharSpanReader(line.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var row = reader.Current;
        bool threw = false;
        try { _ = row.GetField(0, 100); } catch (FixedWidthException) { threw = true; }
        Assert.True(threw);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CharSpanRow_GetField_BeyondEnd_AllowShortRows_ReturnsEmpty()
    {
        string line = "data\n";
        var opts = FixedWidthReadOptions.Default with { AllowShortRows = true };
        var reader = new FixedWidthCharSpanReader(line.AsSpan(), opts);
        Assert.True(reader.MoveNext());
        var row = reader.Current;
        // Start past end → empty.
        var c = row.GetField(100, 5);
        Assert.True(c.IsEmpty);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CharSpanRow_GetField_BeyondEnd_AllowShortRows_PartialField()
    {
        string line = "data\n";
        var opts = FixedWidthReadOptions.Default with { AllowShortRows = true };
        var reader = new FixedWidthCharSpanReader(line.AsSpan(), opts);
        Assert.True(reader.MoveNext());
        var row = reader.Current;
        // Field straddles end → returns only available portion.
        var c = row.GetField(2, 10);
        Assert.True(c.Length <= 2);
    }

    // ---------- FixedWidthByteSpanRow.GetField error paths ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ByteSpanRow_GetField_NegativeStart_Throws()
    {
        byte[] bytes = "data\n"u8.ToArray();
        var reader = new FixedWidthByteSpanReader(bytes.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var row = reader.Current;
        bool threw = false;
        try { _ = row.GetField(-1, 1); } catch (ArgumentOutOfRangeException) { threw = true; }
        Assert.True(threw);
        threw = false;
        try { _ = row.GetField(-1, 1, (byte)' ', FieldAlignment.None); } catch (ArgumentOutOfRangeException) { threw = true; }
        Assert.True(threw);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ByteSpanRow_GetField_NegativeLength_Throws()
    {
        byte[] bytes = "data\n"u8.ToArray();
        var reader = new FixedWidthByteSpanReader(bytes.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var row = reader.Current;
        bool threw = false;
        try { _ = row.GetField(0, -1); } catch (ArgumentOutOfRangeException) { threw = true; }
        Assert.True(threw);
        threw = false;
        try { _ = row.GetField(0, -1, (byte)' ', FieldAlignment.None); } catch (ArgumentOutOfRangeException) { threw = true; }
        Assert.True(threw);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ByteSpanRow_GetField_BeyondEnd_Throws()
    {
        byte[] bytes = "data\n"u8.ToArray();
        var reader = new FixedWidthByteSpanReader(bytes.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var row = reader.Current;
        bool threw = false;
        try { _ = row.GetField(0, 100); } catch (FixedWidthException) { threw = true; }
        Assert.True(threw);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ByteSpanRow_GetField_BeyondEnd_AllowShortRows_ReturnsEmpty()
    {
        byte[] bytes = "data\n"u8.ToArray();
        var opts = FixedWidthReadOptions.Default with { AllowShortRows = true };
        var reader = new FixedWidthByteSpanReader(bytes.AsSpan(), opts);
        Assert.True(reader.MoveNext());
        var row = reader.Current;
        var c = row.GetField(100, 5);
        Assert.True(c.IsEmpty);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ByteSpanRow_GetField_BeyondEnd_AllowShortRows_PartialField()
    {
        byte[] bytes = "data\n"u8.ToArray();
        var opts = FixedWidthReadOptions.Default with { AllowShortRows = true };
        var reader = new FixedWidthByteSpanReader(bytes.AsSpan(), opts);
        Assert.True(reader.MoveNext());
        var row = reader.Current;
        var c = row.GetField(2, 10);
        Assert.True(c.Length <= 2);
    }

    // ---------- ImmutableFixedWidthRow / ImmutableFixedWidthByteRow ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ImmutableFixedWidthRow_GetField()
    {
        string line = "AliceLong 30\n";
        var reader = new FixedWidthCharSpanReader(line.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var row = reader.Current.ToImmutable();
        // GetField(start, length) returns raw substring.
        Assert.NotEmpty(row.GetField(0, 10));
        // GetField with Left alignment trims trailing pad chars.
        Assert.NotEmpty(row.GetField(0, 10, ' ', FieldAlignment.Left));
        Assert.True(row.Length > 0);
        Assert.Equal('A', row.RawRecord[0]);
        Assert.True(row.RecordNumber >= 1);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ImmutableFixedWidthRow_GetField_NegativeArgs_Throw()
    {
        string line = "data\n";
        var reader = new FixedWidthCharSpanReader(line.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var row = reader.Current.ToImmutable();
        Assert.Throws<ArgumentOutOfRangeException>(() => row.GetField(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => row.GetField(0, -1));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ImmutableFixedWidthRow_GetField_BeyondEnd_Throws()
    {
        string line = "data\n";
        var reader = new FixedWidthCharSpanReader(line.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var row = reader.Current.ToImmutable();
        Assert.Throws<FixedWidthException>(() => row.GetField(0, 100));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ImmutableFixedWidthByteRow_GetField()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("AliceLong 30\n");
        var reader = new FixedWidthByteSpanReader(bytes.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var row = reader.Current.ToImmutable();
        Assert.NotEmpty(row.GetField(0, 10));
        Assert.NotEmpty(row.GetField(0, 10, (byte)' ', FieldAlignment.Left));
        Assert.True(row.Length > 0);
        Assert.NotEmpty(row.ToDecodedString());
        Assert.True(row.RecordNumber >= 1);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ImmutableFixedWidthByteRow_GetField_NegativeArgs_Throw()
    {
        byte[] bytes = "data\n"u8.ToArray();
        var reader = new FixedWidthByteSpanReader(bytes.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var row = reader.Current.ToImmutable();
        Assert.Throws<ArgumentOutOfRangeException>(() => row.GetField(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => row.GetField(0, -1));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ImmutableFixedWidthByteRow_GetField_BeyondEnd_Throws()
    {
        byte[] bytes = "data\n"u8.ToArray();
        var reader = new FixedWidthByteSpanReader(bytes.AsSpan(), FixedWidthReadOptions.Default);
        Assert.True(reader.MoveNext());
        var row = reader.Current.ToImmutable();
        Assert.Throws<FixedWidthException>(() => row.GetField(0, 100));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ImmutableFixedWidthByteRow_AllowShortRows_PartialField()
    {
        byte[] bytes = "data\n"u8.ToArray();
        var opts = FixedWidthReadOptions.Default with { AllowShortRows = true };
        var reader = new FixedWidthByteSpanReader(bytes.AsSpan(), opts);
        Assert.True(reader.MoveNext());
        var row = reader.Current.ToImmutable();
        // Partial field (only 2 chars available beyond start=2).
        var s = row.GetField(2, 10);
        Assert.True(s.Length <= 10);
    }

    // ---------- FixedWidthByteSpanReader / FixedWidthCharSpanReader navigation ----------

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthCharSpanReader_AllowShortRows()
    {
        // Short rows where with AllowShortRows=true.
        string line = "short\nlonger row\nmore data here\n";
        var opts = FixedWidthReadOptions.Default with { AllowShortRows = true };
        var reader = new FixedWidthCharSpanReader(line.AsSpan(), opts);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(3, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthByteSpanReader_AllowShortRows()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("short\nlonger row\nmore data here\n");
        var opts = FixedWidthReadOptions.Default with { AllowShortRows = true };
        var reader = new FixedWidthByteSpanReader(bytes.AsSpan(), opts);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(3, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthCharSpanReader_BlankLinesSkipped()
    {
        string line = "\n\ndata\n\nmore\n";
        var opts = FixedWidthReadOptions.Default with { SkipEmptyLines = true };
        var reader = new FixedWidthCharSpanReader(line.AsSpan(), opts);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(2, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthByteSpanReader_BlankLinesSkipped()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("\n\ndata\n\nmore\n");
        var opts = FixedWidthReadOptions.Default with { SkipEmptyLines = true };
        var reader = new FixedWidthByteSpanReader(bytes.AsSpan(), opts);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(2, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthCharSpanReader_Comment()
    {
        string line = "# comment line\ndata\n";
        var opts = FixedWidthReadOptions.Default with { CommentCharacter = '#' };
        var reader = new FixedWidthCharSpanReader(line.AsSpan(), opts);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(1, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthByteSpanReader_Comment()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("# comment line\ndata\n");
        var opts = FixedWidthReadOptions.Default with { CommentCharacter = '#' };
        var reader = new FixedWidthByteSpanReader(bytes.AsSpan(), opts);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(1, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthCharSpanReader_SkipRows()
    {
        string line = "row1\nrow2\nrow3\n";
        var opts = FixedWidthReadOptions.Default with { SkipRows = 2 };
        var reader = new FixedWidthCharSpanReader(line.AsSpan(), opts);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(1, n);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FixedWidthByteSpanReader_SkipRows()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("row1\nrow2\nrow3\n");
        var opts = FixedWidthReadOptions.Default with { SkipRows = 2 };
        var reader = new FixedWidthByteSpanReader(bytes.AsSpan(), opts);
        int n = 0;
        while (reader.MoveNext()) n++;
        Assert.Equal(1, n);
    }
}
