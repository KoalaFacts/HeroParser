using HeroParser.SeparatedValues.Detection;
using HeroParser.SeparatedValues.Validation;
using Xunit;

namespace HeroParser.Tests;

/// <summary>
/// Security tests for the public byte-span overloads of <see cref="CsvValidator"/> and
/// <see cref="CsvDelimiterDetector"/>, covering finding H2 (unbounded UTF-16 decode allocation
/// driven by attacker-controlled byte-span length).
/// </summary>
[Trait("Category", "Unit")]
public class ParserByteSpanSecurityTests
{
    // Validate(byte[]) must reject inputs above the documented MAX_UTF8_INPUT_BYTES cap rather
    // than allocating a 2x heap buffer to decode them. The cap is read from the production
    // constant so the test stays in sync if the limit is retuned.
    [Fact]
    public void Validate_BytesAboveMaxInputSize_ThrowsArgumentException()
    {
        var data = new byte[CsvValidator.MAX_UTF8_INPUT_BYTES + 1];

        Assert.Throws<ArgumentException>(() => CsvValidator.Validate(data.AsSpan()));
    }

    // The byte-overload of DetectDelimiter previously decoded the entire UTF-8 input into a
    // heap-allocated char array before scanning. With the H2 fix, oversized inputs are sliced
    // to MAX_DETECTION_BYTES (1 MiB) before decoding. We feed 4 MiB of valid CSV preceded by a
    // header row and assert detection still works correctly on the truncated prefix.
    [Fact]
    public void DetectDelimiter_BytesLargerThanDetectionCap_StillDetectsFromPrefix()
    {
        // 4 MiB of data: one short header + many rows of "a,b,c"
        var sb = new System.Text.StringBuilder(capacity: 4 * 1024 * 1024);
        sb.Append("col1,col2,col3\n");
        const int rowsToFill = 800_000; // ~12 MB which after the cap is well over 1 MiB
        for (int i = 0; i < rowsToFill; i++)
        {
            sb.Append("a,b,c\n");
        }
        var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());

        // Pre-condition: input exceeds the internal detection cap so the slice path is exercised.
        Assert.True(bytes.Length > 1 * 1024 * 1024);

        char delimiter = CsvDelimiterDetector.DetectDelimiter(bytes.AsSpan());

        Assert.Equal(',', delimiter);
    }

    // Sanity: a small byte-span input should still detect correctly (no regression on the
    // hot path that doesn't trigger the slice).
    [Fact]
    public void DetectDelimiter_SmallBytes_DetectsDelimiterUnchanged()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("a;b;c\n1;2;3\n4;5;6\n");
        Assert.Equal(';', CsvDelimiterDetector.DetectDelimiter(bytes.AsSpan()));
    }
}
