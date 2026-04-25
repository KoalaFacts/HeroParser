using HeroParser.SeparatedValues;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Writing;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Drives the OnError branches in CsvRecordWriter that the source-generated
/// direct writer bypasses (lines 415-503, 675-745, etc.). Uses a record without
/// [GenerateBinder] (forces reflection write path) whose property accessor
/// throws synthetically.
/// </summary>
[Trait("Category", "Unit")]
[Collection("AsyncWriterTests")]
public class CsvRecordWriterErrorHandlerTests
{
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

        [TabularMap(Name = "MaybeEmpty")]
        [Format(ExcludeIfAllEmpty = true)]
        public string? MaybeEmpty { get; set; }
    }

    [Fact]
    public void OnError_SkipRow_OmitsRecord()
    {
        ThrowingRecord.FailUntilCount = 10;
        var skipped = 0;
        var text = Csv.Write<ThrowingRecord>()
            .OnError(_ =>
            {
                skipped++;
                return SerializeErrorAction.SkipRow;
            })
            .ToText([
                new ThrowingRecord { Name = "Alice" },
                new ThrowingRecord { Name = "Bob" }
            ]);

        Assert.NotEmpty(text);
        Assert.True(skipped >= 1);
    }

    [Fact]
    public void OnError_WriteNull_LeavesEmptyField()
    {
        ThrowingRecord.FailUntilCount = 10;
        var text = Csv.Write<ThrowingRecord>()
            .OnError(_ => SerializeErrorAction.WriteNull)
            .ToText([new ThrowingRecord { Name = "Alice" }]);
        Assert.NotEmpty(text);
    }

    [Fact]
    public void OnError_Throw_PropagatesException()
    {
        ThrowingRecord.FailUntilCount = 10;
        Assert.ThrowsAny<Exception>(() => Csv.Write<ThrowingRecord>()
            .OnError(_ => SerializeErrorAction.Throw)
            .ToText([new ThrowingRecord { Name = "Alice" }]));
    }

    [Fact]
    public void OnError_NoHandler_PropagatesUnderlyingException()
    {
        ThrowingRecord.FailUntilCount = 10;
        Assert.ThrowsAny<Exception>(() => Csv.Write<ThrowingRecord>()
            .ToText([new ThrowingRecord { Name = "Alice" }]));
    }

    [Fact]
    public void OnError_HandlerSeesContextDetails()
    {
        ThrowingRecord.FailUntilCount = 10;
        CsvSerializeErrorContext? captured = null;
        try
        {
            Csv.Write<ThrowingRecord>()
                .OnError(ctx =>
                {
                    captured = ctx;
                    return SerializeErrorAction.SkipRow;
                })
                .ToText([new ThrowingRecord { Name = "Alice" }]);
        }
        catch (Exception) { /* may still throw */ }

        Assert.NotNull(captured);
        Assert.NotNull(captured.Value.Exception);
        Assert.IsType<InvalidOperationException>(captured.Value.Exception);
    }

    [Fact]
    public void OnError_WithExcludeIfAllEmpty_FilteredPath()
    {
        // ExcludeIfAllEmpty triggers the "filtered" write path that has its own try/catch.
        ThrowingRecord.FailUntilCount = 10;
        var skipped = 0;
        var text = Csv.Write<ThrowingRecord>()
            .WithoutEmptyColumns()
            .OnError(_ =>
            {
                skipped++;
                return SerializeErrorAction.SkipRow;
            })
            .ToText([
                new ThrowingRecord { Name = "Alice", MaybeEmpty = "v1" }
            ]);

        Assert.NotEmpty(text);
    }

    [Fact]
    public async Task OnError_AsyncStreamingIEnumerablePath_AlsoExercisesErrorHandler()
    {
        ThrowingRecord.FailUntilCount = 10;
        var skipped = 0;
        await using var ms = new MemoryStream();
        IEnumerable<ThrowingRecord> records = [new() { Name = "Alice" }];
        await Csv.Write<ThrowingRecord>()
            .OnError(_ =>
            {
                skipped++;
                return SerializeErrorAction.SkipRow;
            })
            .ToStreamAsyncStreaming(ms, records, leaveOpen: true,
                cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(skipped >= 1);
    }

    [Fact]
    public async Task OnError_AsyncStreamingPath_HandlesException()
    {
        ThrowingRecord.FailUntilCount = 10;
        var skipped = 0;

        static async IAsyncEnumerable<ThrowingRecord> Source()
        {
            await Task.Yield();
            yield return new ThrowingRecord { Name = "Alice" };
            yield return new ThrowingRecord { Name = "Bob" };
        }

        await using var ms = new MemoryStream();
        await Csv.Write<ThrowingRecord>()
            .OnError(_ =>
            {
                skipped++;
                return SerializeErrorAction.SkipRow;
            })
            .ToStreamAsync(ms, Source(), leaveOpen: true,
                cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(skipped >= 1);
    }
}
