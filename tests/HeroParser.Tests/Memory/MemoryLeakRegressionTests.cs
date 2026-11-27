using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using HeroParser.SeparatedValues.Core;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests.Memory;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
[Collection(MemoryRegressionCollection.NAME)]
public sealed partial class MemoryLeakRegressionTests
{
    [GenerateBinder]
    public sealed class StreamRow
    {
        public int Id { get; set; }
    }

    [Fact]
    public void PropertyValidationRunner_DynamicRegex_IsGarbageCollected()
    {
        WeakReference weakRef = ValidateWithDynamicRegex();

        for (int i = 0; i < 4 && weakRef.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.False(weakRef.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ValidateWithDynamicRegex()
    {
        var regex = new Regex("^abc-[0-9]+$");
        var runner = new PropertyValidationRunner(CultureInfo.InvariantCulture);
        var errors = new List<ValidationError>();
        bool invalid = runner.Validate(
            "abc-123".AsSpan(),
            "Code",
            1,
            0,
            "Code",
            notEmpty: false,
            minLength: null,
            maxLength: null,
            rangeMin: null,
            rangeMax: null,
            regex,
            errors);

        Assert.False(invalid);
        Assert.Empty(errors);
        return new WeakReference(regex);
    }

    [Fact]
    public async Task StreamingReaders_EarlyBreak_DisposeUnderlyingStreams()
    {
        var ct = TestContext.Current.CancellationToken;

        var csvStream = new TrackingReadStream(Encoding.UTF8.GetBytes("Id\n1\n2\n3\n"));
        await foreach (var row in Csv.Read<StreamRow>().FromStreamAsync(csvStream, leaveOpen: false, cancellationToken: ct))
        {
            Assert.Equal(1, row.Id);
            break;
        }
        Assert.True(csvStream.IsDisposed);

        var csvRowStream = new TrackingReadStream(Encoding.UTF8.GetBytes("Id\n1\n2\n3\n"));
        var rowReader = Csv.CreateAsyncStreamReader(
            csvRowStream,
            CsvReadOptions.Default,
            leaveOpen: false);
        Assert.True(await rowReader.MoveNextAsync(ct));
        await rowReader.DisposeAsync();
        Assert.True(csvRowStream.IsDisposed);

        var jsonlStream = new TrackingReadStream(Encoding.UTF8.GetBytes("{\"Id\":1}\n{\"Id\":2}\n"));
        await foreach (var _ in Jsonl.Read<StreamRow>().FromStreamAsync(jsonlStream, leaveOpen: false, cancellationToken: ct))
        {
            break;
        }
        Assert.True(jsonlStream.IsDisposed);

        using var htbWriteMs = new MemoryStream();
        HeroParser.Htb.Write<StreamRow>().ToStream(htbWriteMs, [new StreamRow { Id = 1 }, new StreamRow { Id = 2 }], leaveOpen: true);
        byte[] htbBytes = htbWriteMs.ToArray();
        var htbStream = new TrackingReadStream(htbBytes);
        await foreach (var row in HeroParser.Htb.Read<StreamRow>().FromStreamAsync(htbStream, leaveOpen: false, ct))
        {
            Assert.Equal(1, row.Id);
            break;
        }
        Assert.True(htbStream.IsDisposed);
    }

    private sealed class TrackingReadStream(byte[] data) : MemoryStream(data)
    {
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return base.DisposeAsync();
        }
    }
}
