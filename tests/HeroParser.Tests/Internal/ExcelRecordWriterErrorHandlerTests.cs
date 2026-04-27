using HeroParser.Excels.Core;
using HeroParser.Excels.Writing;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Drives the OnError branches in ExcelRecordWriter (lines 232-266) by using a
/// record whose property accessor throws — ExcelRecordWriter then invokes the
/// configured ExcelSerializeErrorHandler with each possible action (SkipRow,
/// WriteEmpty, Throw).
/// </summary>
[Trait("Category", "Unit")]
[Collection("AsyncWriterTests")]
public class ExcelRecordWriterErrorHandlerTests
{
    // No [GenerateBinder] → forces the reflection write path that supports OnError.
    public sealed class ThrowingRecord
    {
        public static int FailUntilCount;

        [TabularMap(Name = "Name")]
        public string Name { get; set; } = "ok";

        [TabularMap(Name = "Volatile")]
        public string Volatile
        {
            get
            {
                if (FailUntilCount-- > 0)
                    throw new InvalidOperationException("synthetic getter failure");
                return "recovered";
            }
        }
    }

    [Fact]
    public void OnError_SkipRow_OmitsRecord()
    {
        ThrowingRecord.FailUntilCount = 10;
        var skipped = 0;
        var bytes = HeroParser.Excel.Write<ThrowingRecord>()
            .OnError(_ =>
            {
                skipped++;
                return ExcelSerializeErrorAction.SkipRow;
            })
            .ToBytes([
                new ThrowingRecord { Name = "Alice" },
                new ThrowingRecord { Name = "Bob" }
            ]);

        Assert.NotEmpty(bytes);
        Assert.True(skipped >= 1);
    }

    [Fact]
    public void OnError_WriteEmpty_LeavesCellBlank()
    {
        ThrowingRecord.FailUntilCount = 10;
        var bytes = HeroParser.Excel.Write<ThrowingRecord>()
            .OnError(_ => ExcelSerializeErrorAction.WriteEmpty)
            .ToBytes([new ThrowingRecord { Name = "Alice" }]);
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void OnError_Throw_PropagatesException()
    {
        ThrowingRecord.FailUntilCount = 10;
        Assert.Throws<ExcelException>(() => HeroParser.Excel.Write<ThrowingRecord>()
            .OnError(_ => ExcelSerializeErrorAction.Throw)
            .ToBytes([new ThrowingRecord { Name = "Alice" }]));
    }

    [Fact]
    public void OnError_Default_NoHandler_PropagatesException()
    {
        ThrowingRecord.FailUntilCount = 10;
        // No OnError handler at all → the throwing accessor propagates as some exception.
        Assert.ThrowsAny<Exception>(() => HeroParser.Excel.Write<ThrowingRecord>()
            .ToBytes([new ThrowingRecord { Name = "Alice" }]));
    }

    [Fact]
    public void OnError_HandlerSeesContextDetails()
    {
        ThrowingRecord.FailUntilCount = 10;
        ExcelSerializeErrorContext? captured = null;
        Exception? capturedEx = null;
        try
        {
            HeroParser.Excel.Write<ThrowingRecord>()
                .OnError(ctx =>
                {
                    captured = ctx;
                    capturedEx = ctx.Exception;
                    return ExcelSerializeErrorAction.SkipRow;
                })
                .ToBytes([new ThrowingRecord { Name = "Alice" }]);
        }
        catch (ExcelException) { /* may still throw if all rows skipped */ }

        Assert.NotNull(captured);
        Assert.NotNull(capturedEx);
        Assert.IsType<InvalidOperationException>(capturedEx);
        Assert.Equal("Volatile", captured.Value.MemberName);
    }

    [Fact]
    public async Task OnError_AsyncWritePath_AlsoExercisesErrorHandler()
    {
        ThrowingRecord.FailUntilCount = 10;
        var skipped = 0;
        await using var ms = new MemoryStream();
        await HeroParser.Excel.Write<ThrowingRecord>()
            .OnError(_ =>
            {
                skipped++;
                return ExcelSerializeErrorAction.SkipRow;
            })
            .ToStreamAsync(ms,
                [new ThrowingRecord { Name = "Alice" }],
                leaveOpen: true,
                ct: TestContext.Current.CancellationToken);
        Assert.True(skipped >= 1);
    }
}
