using System.Globalization;
using HeroParser.Excels.Core;
using HeroParser.Excels.Reading;
using HeroParser.Excels.Writing;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Covers Excel writer builder terminals + option chains that lack direct tests:
/// ToFile/ToStream/ToBytes (sync), ToFileAsync/ToStreamAsync/ToBytesAsync (with
/// IEnumerable + IAsyncEnumerable), all option chainers, and null-arg guards.
/// </summary>
[Trait("Category", "Unit")]
[Collection("AsyncWriterTests")]
public class ExcelWriterBuilderTerminalTests
{
    [GenerateBinder]
    public sealed class Person
    {
        [TabularMap(Name = "Name")] public string Name { get; set; } = "";
        [TabularMap(Name = "Age")] public int Age { get; set; }
    }

    private static IEnumerable<Person> Sample() =>
    [
        new() { Name = "Alice", Age = 30 },
        new() { Name = "Bob", Age = 25 }
    ];

    private static async IAsyncEnumerable<Person> SampleAsync()
    {
        foreach (var p in Sample())
        {
            await Task.Yield();
            yield return p;
        }
    }

    private static List<Person> ReadBackBytes(byte[] data)
    {
        using var ms = new MemoryStream(data);
        return HeroParser.Excel.Read<Person>().FromStream(ms);
    }

    [Fact]
    public void ToBytes_RoundTrip()
    {
        var bytes = HeroParser.Excel.Write<Person>().ToBytes(Sample());
        var roundTrip = ReadBackBytes(bytes);
        Assert.Equal(2, roundTrip.Count);
        Assert.Equal("Alice", roundTrip[0].Name);
        Assert.Equal(30, roundTrip[0].Age);
    }

    [Fact]
    public void ToFile_WritesToDisk_AndReadsBack()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"excel_{Guid.NewGuid()}.xlsx");
        try
        {
            HeroParser.Excel.Write<Person>().ToFile(tmp, Sample());
            var roundTrip = HeroParser.Excel.Read<Person>().FromFile(tmp);
            Assert.Equal(2, roundTrip.Count);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact]
    public void ToStream_WritesAndReadsBack()
    {
        using var ms = new MemoryStream();
        HeroParser.Excel.Write<Person>().ToStream(ms, Sample(), leaveOpen: true);
        ms.Position = 0;
        var roundTrip = HeroParser.Excel.Read<Person>().FromStream(ms);
        Assert.Equal(2, roundTrip.Count);
    }

    [Fact]
    public async Task ToBytesAsync_RoundTrip()
    {
        var bytes = await HeroParser.Excel.Write<Person>().ToBytesAsync(Sample(),
            TestContext.Current.CancellationToken);
        Assert.Equal(2, ReadBackBytes(bytes).Count);
    }

    [Fact]
    public async Task ToFileAsync_IEnumerable()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"excel_{Guid.NewGuid()}.xlsx");
        try
        {
            await HeroParser.Excel.Write<Person>().ToFileAsync(tmp, Sample(),
                TestContext.Current.CancellationToken);
            Assert.True(File.Exists(tmp));
            Assert.True(new FileInfo(tmp).Length > 0);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact]
    public async Task ToFileAsync_IAsyncEnumerable()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"excel_{Guid.NewGuid()}.xlsx");
        try
        {
            await HeroParser.Excel.Write<Person>().ToFileAsync(tmp, SampleAsync(),
                TestContext.Current.CancellationToken);
            Assert.True(File.Exists(tmp));
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact]
    public async Task ToStreamAsync_IEnumerable()
    {
        using var ms = new MemoryStream();
        await HeroParser.Excel.Write<Person>().ToStreamAsync(ms, Sample(), leaveOpen: true,
            ct: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 0);
    }

    [Fact]
    public async Task ToStreamAsync_IAsyncEnumerable()
    {
        using var ms = new MemoryStream();
        await HeroParser.Excel.Write<Person>().ToStreamAsync(ms, SampleAsync(), leaveOpen: true,
            ct: TestContext.Current.CancellationToken);
        Assert.True(ms.Length > 0);
    }

    [Fact]
    public void FluentChain_AllOptions()
    {
        var bytes = HeroParser.Excel.Write<Person>()
            .WithCulture(CultureInfo.InvariantCulture)
            .WithCulture("en-US")
            .WithNullValue("N/A")
            .WithDateTimeFormat("yyyy-MM-dd")
            .WithDateOnlyFormat("yyyy-MM-dd")
            .WithTimeOnlyFormat("HH:mm:ss")
            .WithNumberFormat("N0")
            .WithMaxRowCount(100)
            .WithValidationMode(ValidationMode.Lenient)
            .WithHeader()
            .WithoutHeader()
            .WithSheetName("MySheet")
            .WithMaxOutputSize(10_000_000)
            .ToBytes(Sample());
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void WithoutHeader_Chain_DoesNotThrow()
    {
        var bytes = HeroParser.Excel.Write<Person>().WithoutHeader().ToBytes(Sample());
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void WithSheetName_UsedInOutput()
    {
        var bytes = HeroParser.Excel.Write<Person>().WithSheetName("Custom").ToBytes(Sample());
        using var ms = new MemoryStream(bytes);
        var dict = HeroParser.Excel.Read<Person>().AllSheets().FromStream(ms);
        Assert.Contains("Custom", dict.Keys);
    }

    [Fact]
    public void WithMaxRowCount_Throws_WhenExceeded()
    {
        var builder = HeroParser.Excel.Write<Person>().WithMaxRowCount(1);
        Assert.Throws<ExcelException>(() => builder.ToBytes(Sample()));
    }

    [Fact]
    public void WithMaxOutputSize_Throws_WhenExceeded()
    {
        var builder = HeroParser.Excel.Write<Person>().WithMaxOutputSize(50);
        Assert.Throws<ExcelException>(() => builder.ToBytes(Sample()));
    }

    [Fact]
    public void OnError_Chain_DoesNotThrow()
    {
        var builder = HeroParser.Excel.Write<Person>()
            .OnError(_ => ExcelSerializeErrorAction.SkipRow);
        Assert.NotEmpty(builder.ToBytes(Sample()));
    }

    [Fact]
    public void WithProgress_RegistersProgress()
    {
        var p = new Progress<ExcelWriteProgress>(_ => { });
        var bytes = HeroParser.Excel.Write<Person>().WithProgress(p, intervalRows: 1).ToBytes(Sample());
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void ToBytes_NullRecords_Throws()
        => Assert.Throws<ArgumentNullException>(() => HeroParser.Excel.Write<Person>().ToBytes(null!));

    [Fact]
    public void ToFile_NullPath_Throws()
        => Assert.Throws<ArgumentNullException>(() => HeroParser.Excel.Write<Person>().ToFile(null!, Sample()));

    [Fact]
    public void ToStream_NullStream_Throws()
        => Assert.Throws<ArgumentNullException>(() => HeroParser.Excel.Write<Person>().ToStream(null!, Sample()));

    [Fact]
    public void OnError_Null_Throws()
        => Assert.Throws<ArgumentNullException>(() => HeroParser.Excel.Write<Person>().OnError(null!));
}
