using System.IO.Pipelines;
using System.Text;
using HeroParser.FixedWidths;
using Xunit;

namespace HeroParser.Tests.FixedWidths;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed class FixedWidthPipeSkipOverflowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadFromPipeReader_SkipMaxRowsAndHeader_DoesNotReturnData(bool fixedLength)
    {
        var pipe = new Pipe();
        byte[] bytes = Encoding.UTF8.GetBytes(fixedLength ? "DATA" : "DATA\r\n");
        await pipe.Writer.WriteAsync(bytes, TestContext.Current.CancellationToken);
        await pipe.Writer.CompleteAsync();

        var options = new FixedWidthReadOptions
        {
            RecordLength = fixedLength ? 4 : null,
            SkipRows = int.MaxValue,
            HasHeaderRow = true,
            TrackSourceLineNumbers = true
        };

        int records = 0;
        await foreach (var _ in FixedWidth.ReadFromPipeReaderAsync(
            pipe.Reader, options, TestContext.Current.CancellationToken))
        {
            records++;
        }

        Assert.Equal(0, records);
    }
}
