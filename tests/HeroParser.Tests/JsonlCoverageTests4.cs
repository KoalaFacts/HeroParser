using System.IO.Pipelines;
using System.Text;
using HeroParser.JsonLines;
using HeroParser.JsonLines.Reading;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests;

/// <summary>Targeted final batch — pushes Options, LineReader and PipeLineReader past 90%.</summary>
public class JsonlCoverageTests4
{
    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Builder_SkipEmptyLines_FalseIsHonored()
    {
        // Empty line in input would normally be skipped; with SkipEmptyLines(false)
        // System.Text.Json should throw on the empty input, surfaced as JsonlException.
        string jsonl = "{\"Name\":\"A\",\"Age\":1}\n\n{\"Name\":\"B\",\"Age\":2}\n";
        Assert.Throws<JsonlException>(() =>
        {
            using var reader = Jsonl.Read<JsonlCoveragePerson>().SkipEmptyLines(false).FromText(jsonl);
            _ = reader.ToList();
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Builder_WithValidationMode_AcceptsLenient()
    {
        using var reader = Jsonl.Read<JsonlCoveragePerson>()
            .WithValidationMode(ValidationMode.Lenient)
            .FromText("{\"Name\":\"A\",\"Age\":1}");
        Assert.Single(reader.ToList());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Sync_MaxLineSize_BeforeNewline_Throws()
    {
        // 30-byte line terminated by \n. MaxLineSize = 5 → exercises the line-emit branch
        // (newline found, lineEnd > maxLineSizeBytes) — distinct from the EOF branch.
        string jsonl = "{\"Name\":\"AliceLongName\",\"Age\":30}\n";

        Assert.Throws<JsonlException>(() =>
        {
            using var reader = Jsonl.Read<JsonlCoveragePerson>().WithMaxLineSize(5).FromText(jsonl);
            _ = reader.ToList();
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Sync_BufferFull_ExceedsLimit_Throws()
    {
        // 16 KiB line, MaxLineSize = 5000. The initial 8 KiB buffer fills before \n is found,
        // and 8192 >= maxLineSizeBytes(5000) so FillBuffer throws — distinct from line-emit branch.
        string payload = new('a', 16 * 1024);
        string jsonl = $"{{\"Name\":\"{payload}\",\"Age\":1}}";

        Assert.Throws<JsonlException>(() =>
        {
            using var reader = Jsonl.Read<JsonlCoveragePerson>().WithMaxLineSize(5000).FromText(jsonl);
            _ = reader.ToList();
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Sync_BufferGrowthClampedToLimit()
    {
        // 10 KiB line, MaxLineSize = 9000. Initial buffer = 8 KiB. First grow would be 16 KiB
        // but clamps to 9000 (the maxLineSizeBytes). After clamping, line is 10 KiB which exceeds
        // the clamped buffer — exercises the clamping branch then ultimately throws.
        string payload = new('a', 10 * 1024);
        string jsonl = $"{{\"Name\":\"{payload}\",\"Age\":1}}";

        Assert.Throws<JsonlException>(() =>
        {
            using var reader = Jsonl.Read<JsonlCoveragePerson>().WithMaxLineSize(9000).FromText(jsonl);
            _ = reader.ToList();
        });
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Pipe_CrlfLineEnding_Trimmed()
    {
        byte[] utf8 = Encoding.UTF8.GetBytes("{\"Name\":\"Alice\",\"Age\":30}\r\n");
        using var stream = new MemoryStream(utf8);

        List<JsonlLine> lines = [];
        await foreach (JsonlLine line in Jsonl.ReadLinesAsync(PipeReader.Create(stream), cancellationToken: TestContext.Current.CancellationToken))
            lines.Add(line);

        Assert.Single(lines);
        // Last byte of the captured line should be `}`, not `\r`.
        ReadOnlySpan<byte> bytes = lines[0].Utf8.Span;
        Assert.Equal((byte)'}', bytes[^1]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Pipe_BomStripped_FromFirstLine()
    {
        byte[] bom = [0xEF, 0xBB, 0xBF];
        byte[] body = Encoding.UTF8.GetBytes("{\"Name\":\"Alice\",\"Age\":30}\n");
        byte[] combined = [.. bom, .. body];
        using var stream = new MemoryStream(combined);

        List<JsonlLine> lines = [];
        await foreach (JsonlLine line in Jsonl.ReadLinesAsync(PipeReader.Create(stream), cancellationToken: TestContext.Current.CancellationToken))
            lines.Add(line);

        Assert.Single(lines);
        Assert.Equal((byte)'{', lines[0].Utf8.Span[0]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Pipe_BomOnlyFirstLine_YieldsEmptyAfterStrip()
    {
        // BOM followed immediately by \n means the first "line" is just the BOM — after stripping,
        // it becomes empty. Exercises the offset==length branch of CopyLineToArray.
        byte[] data = [0xEF, 0xBB, 0xBF, (byte)'\n'];
        using var stream = new MemoryStream(data);

        List<JsonlLine> lines = [];
        await foreach (JsonlLine line in Jsonl.ReadLinesAsync(PipeReader.Create(stream), cancellationToken: TestContext.Current.CancellationToken))
            lines.Add(line);

        Assert.Single(lines);
        Assert.Equal(0, lines[0].Utf8.Length);
    }
}
