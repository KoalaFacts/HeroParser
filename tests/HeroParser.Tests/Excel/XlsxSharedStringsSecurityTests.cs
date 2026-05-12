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

    // Verifies that the shared XmlReaderSettings used by every .xlsx XML part has the security
    // limits configured. Behavioural testing of the underlying XmlReader is .NET's responsibility;
    // this test guarantees we wired the caps in. XmlResolver is write-only on XmlReaderSettings,
    // so it's not asserted here (the contract test below exercises the resolver = null path
    // indirectly by relying on DtdProcessing.Prohibit).
    [Fact]
    public void CreateReaderSettings_HasSecurityCaps()
    {
        var settings = XlsxXml.CreateReaderSettings();

        Assert.Equal(DtdProcessing.Prohibit, settings.DtdProcessing);
        Assert.True(settings.MaxCharactersInDocument > 0,
            "MaxCharactersInDocument must be > 0 to bound zip-bomb decompression-attack memory");
        Assert.True(settings.MaxCharactersFromEntities > 0,
            "MaxCharactersFromEntities must be > 0 even though DTD is prohibited (defence-in-depth)");
    }

    // Sanity check that the XmlReader contract used by .NET does throw XmlException when the
    // document character cap is exceeded. We use a tiny payload + a tiny custom cap so this
    // test runs in milliseconds and behaves identically across platforms.
    [Fact]
    public void XmlReader_WithMaxCharactersInDocument_ThrowsOnOversizedContent()
    {
        var xml = "<root>" + new string('A', 1000) + "</root>";
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 500
        };

        Assert.Throws<XmlException>(() =>
        {
            using var reader = XmlReader.Create(
                new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml)),
                settings);
            while (reader.Read()) { /* drain */ }
        });
    }

    private static Stream CreateStream(string xml)
        => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
}
