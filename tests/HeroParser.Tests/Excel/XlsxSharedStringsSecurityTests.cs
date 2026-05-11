using System.Xml;
using HeroParser.Excels.Xlsx;
using Xunit;

namespace HeroParser.Tests.Excel;

/// <summary>
/// Security tests for <see cref="XlsxSharedStrings"/> covering finding H1
/// (zip-bomb / unbounded memory consumption via crafted sharedStrings.xml).
/// </summary>
[Trait("Category", "Unit")]
public class XlsxSharedStringsSecurityTests
{
    // A maliciously large uniqueCount must not cause a List<string>(int.MaxValue)-sized
    // pre-allocation. The parse should succeed and report the actual string count.
    [Fact]
    public void Parse_OversizedUniqueCountAttribute_ClampsPreallocationAndSucceeds()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="2" uniqueCount="2147483647">
              <si><t>Hello</t></si>
              <si><t>World</t></si>
            </sst>
            """;

        var strings = XlsxSharedStrings.Parse(CreateStream(xml));

        Assert.Equal(2, strings.Count);
        Assert.Equal("Hello", strings[0]);
        Assert.Equal("World", strings[1]);
    }

    // The XML reader caps total document character count, so a crafted file with a huge
    // <t> body is rejected before the giant string allocation can be made. We build a
    // payload larger than the 100M-char document cap and assert XmlException is thrown.
    [Fact]
    public void Parse_HugeStringElement_RejectedByDocumentCharCap()
    {
        // 200 MB of 'A' inside a single <t> — twice the configured cap.
        const int payloadChars = 200_000_000;

        using var pipeStream = new HugeStringSstStream(payloadChars);

        Assert.Throws<XmlException>(() => XlsxSharedStrings.Parse(pipeStream));
    }

    private static Stream CreateStream(string xml)
        => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));

    // Streams a synthetic sst document containing one <si><t>AAA...AAA</t></si> element
    // whose body is `payloadChars` 'A' bytes long. Avoids materialising the payload in
    // memory all at once, keeping the test allocation footprint small.
    private sealed class HugeStringSstStream : Stream
    {
        private static readonly byte[] prefix = System.Text.Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><si><t>");
        private static readonly byte[] suffix = System.Text.Encoding.UTF8.GetBytes("</t></si></sst>");

        private readonly long payloadChars;
        private long position;

        public HugeStringSstStream(long payloadChars)
        {
            this.payloadChars = payloadChars;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => prefix.Length + payloadChars + suffix.Length;
        public override long Position
        {
            get => position;
            set => throw new NotSupportedException();
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (count == 0 || position >= Length) return 0;

            int written = 0;
            while (written < count && position < prefix.Length)
            {
                buffer[offset + written] = prefix[(int)position];
                position++;
                written++;
            }
            long payloadEnd = prefix.Length + payloadChars;
            while (written < count && position < payloadEnd)
            {
                buffer[offset + written] = (byte)'A';
                position++;
                written++;
            }
            while (written < count && position < Length)
            {
                int idx = (int)(position - payloadEnd);
                buffer[offset + written] = suffix[idx];
                position++;
                written++;
            }
            return written;
        }
    }
}
