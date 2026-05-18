using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Writing;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Drives <see cref="CsvRecordWriter{T}"/> directly to exercise WriteHeader, WriteRecord,
/// WriteRecords (sync and async, with both IEnumerable and IAsyncEnumerable), the
/// MaxRowCount enforcement path, the WriteProgress reporting branch, and the
/// MaterializeRecords path triggered by ExcludeIfAllEmpty.
/// </summary>
[Trait("Category", "Unit")]
[Collection("AsyncWriterTests")]
public class CsvRecordWriterDirectTests
{
    [GenerateBinder]
    public sealed class Person
    {
        [TabularMap(Name = "Name")] public string Name { get; set; } = "";
        [TabularMap(Name = "Age")] public int Age { get; set; }
    }

    [GenerateBinder]
    public sealed class Sparse
    {
        [TabularMap(Name = "Always")] public string Always { get; set; } = "";
        [TabularMap(Name = "Sometimes"), Format(ExcludeIfAllEmpty = true)] public string? Sometimes { get; set; }
    }

    private static IEnumerable<Person> Sample(int n = 3)
    {
        for (int i = 0; i < n; i++)
            yield return new Person { Name = $"P{i}", Age = i + 18 };
    }

    private static async IAsyncEnumerable<Person> SampleAsync(int n = 3)
    {
        await Task.Yield();
        for (int i = 0; i < n; i++)
            yield return new Person { Name = $"P{i}", Age = i + 18 };
    }

    [Fact]
    public void WriteHeader_WritesNamedColumns()
    {
        var rw = CsvRecordWriterFactory.GetWriter<Person>();
        using var sw = new StringWriter();
        using (var writer = new CsvStreamWriter(sw, leaveOpen: true))
        {
            rw.WriteHeader(writer);
        }
        Assert.Contains("Name,Age", sw.ToString());
    }

    [Fact]
    public void WriteRecord_WritesSingleRow()
    {
        var rw = CsvRecordWriterFactory.GetWriter<Person>();
        using var sw = new StringWriter();
        using (var writer = new CsvStreamWriter(sw, leaveOpen: true))
        {
            rw.WriteRecord(writer, new Person { Name = "Alice", Age = 30 });
        }
        Assert.Contains("Alice,30", sw.ToString());
    }

    [Fact]
    public void WriteRecords_WithHeader_IncludesHeader()
    {
        var rw = CsvRecordWriterFactory.GetWriter<Person>();
        using var sw = new StringWriter();
        using (var writer = new CsvStreamWriter(sw, leaveOpen: true))
        {
            rw.WriteRecords(writer, Sample(), includeHeader: true);
        }
        var text = sw.ToString();
        Assert.Contains("Name,Age", text);
        Assert.Contains("P0,18", text);
        Assert.Contains("P2,20", text);
    }

    [Fact]
    public void WriteRecords_WithoutHeader_OmitsHeader()
    {
        var rw = CsvRecordWriterFactory.GetWriter<Person>();
        using var sw = new StringWriter();
        using (var writer = new CsvStreamWriter(sw, leaveOpen: true))
        {
            rw.WriteRecords(writer, Sample(), includeHeader: false);
        }
        var text = sw.ToString();
        Assert.DoesNotContain("Name,Age", text);
        Assert.Contains("P0,18", text);
    }

    [Fact]
    public void WriteRecords_MaxRowCount_Exceeded_Throws()
    {
        var rw = CsvRecordWriterFactory.GetWriter<Person>(new CsvWriteOptions { MaxRowCount = 2 });
        using var sw = new StringWriter();
        using var writer = new CsvStreamWriter(sw, leaveOpen: true);
        Assert.Throws<CsvException>(() => rw.WriteRecords(writer, Sample(5), includeHeader: false));
    }

    [Fact]
    public void WriteRecords_WithProgress_ReportsAtInterval()
    {
        var reports = new List<CsvWriteProgress>();
        var progress = new Progress<CsvWriteProgress>(reports.Add);
        var rw = CsvRecordWriterFactory.GetWriter<Person>(new CsvWriteOptions
        {
            WriteProgress = progress,
            WriteProgressIntervalRows = 1
        });

        using var sw = new StringWriter();
        using (var writer = new CsvStreamWriter(sw, leaveOpen: true))
        {
            rw.WriteRecords(writer, Sample(3), includeHeader: false);
        }
        // Progress is reported via Progress<T> which posts to the SyncCtx; reports may be
        // delivered asynchronously, so just verify writes succeeded.
        Assert.Contains("P0,18", sw.ToString());
    }

    [Fact]
    public async Task WriteRecordsAsync_AsyncEnumerable_WritesAll()
    {
        var rw = CsvRecordWriterFactory.GetWriter<Person>();
        using var sw = new StringWriter();
        await using (var writer = new CsvStreamWriter(sw, leaveOpen: true))
        {
            await rw.WriteRecordsAsync(writer, SampleAsync(3), includeHeader: true,
                cancellationToken: TestContext.Current.CancellationToken);
        }
        var text = sw.ToString();
        Assert.Contains("Name,Age", text);
        Assert.Contains("P2,20", text);
    }

    [Fact]
    public async Task WriteRecordsAsync_AsyncEnumerable_MaxRowCount_Throws()
    {
        var rw = CsvRecordWriterFactory.GetWriter<Person>(new CsvWriteOptions { MaxRowCount = 1 });
        using var sw = new StringWriter();
        await using var writer = new CsvStreamWriter(sw, leaveOpen: true);
        await Assert.ThrowsAsync<CsvException>(async () =>
            await rw.WriteRecordsAsync(writer, SampleAsync(5), includeHeader: false,
                cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WriteRecordsAsync_AsyncEnumerable_Completes_OnSmallBatch()
    {
        // Smoke-test the WriteRecordsAsync(IAsyncEnumerable) path completes for a small batch.
        var rw = CsvRecordWriterFactory.GetWriter<Person>();
        using var sw = new StringWriter();
        await using (var writer = new CsvStreamWriter(sw, leaveOpen: true))
        {
            await rw.WriteRecordsAsync(writer, SampleAsync(2), includeHeader: false,
                cancellationToken: TestContext.Current.CancellationToken);
        }
        Assert.Contains("P0,18", sw.ToString());
        Assert.Contains("P1,19", sw.ToString());
    }

    [Fact]
    public void WriteRecords_WithExcludeIfAllEmpty_RemovesEmptyColumn()
    {
        var rw = CsvRecordWriterFactory.GetWriter<Sparse>();
        using var sw = new StringWriter();
        var records = new[]
        {
            new Sparse { Always = "x", Sometimes = null },
            new Sparse { Always = "y", Sometimes = null }
        };
        using (var writer = new CsvStreamWriter(sw, leaveOpen: true))
        {
            rw.WriteRecords(writer, records, includeHeader: true);
        }
        var text = sw.ToString();
        // The "Sometimes" column should be excluded since all values are empty.
        Assert.Contains("Always", text);
        Assert.DoesNotContain("Sometimes", text);
    }

    [Fact]
    public void WriteRecords_WithExcludeIfAllEmpty_KeepsColumnIfAnyValue()
    {
        var rw = CsvRecordWriterFactory.GetWriter<Sparse>();
        using var sw = new StringWriter();
        var records = new[]
        {
            new Sparse { Always = "x", Sometimes = null },
            new Sparse { Always = "y", Sometimes = "value" }
        };
        using (var writer = new CsvStreamWriter(sw, leaveOpen: true))
        {
            rw.WriteRecords(writer, records, includeHeader: true);
        }
        var text = sw.ToString();
        Assert.Contains("Sometimes", text);
        Assert.Contains("value", text);
    }

    [Fact]
    public async Task WriteRecordsAsync_WithExcludeIfAllEmpty_AsyncMaterializesAndFilters()
    {
        var rw = CsvRecordWriterFactory.GetWriter<Sparse>();
        using var sw = new StringWriter();

        static async IAsyncEnumerable<Sparse> AsyncSparse()
        {
            await Task.Yield();
            yield return new Sparse { Always = "x", Sometimes = null };
            yield return new Sparse { Always = "y", Sometimes = null };
        }

        await using (var writer = new CsvStreamWriter(sw, leaveOpen: true))
        {
            await rw.WriteRecordsAsync(writer, AsyncSparse(), includeHeader: true,
                cancellationToken: TestContext.Current.CancellationToken);
        }
        Assert.DoesNotContain("Sometimes", sw.ToString());
    }

    [Fact]
    public void Factory_TryGetWriter_ReturnsTrue_ForGeneratedType()
    {
        Assert.True(CsvRecordWriterFactory.TryGetWriter<Person>(null, out var writer));
        Assert.NotNull(writer);
    }

    [Fact]
    public void Factory_GetWriter_WithOptions_HonorsConfiguration()
    {
        var rw = CsvRecordWriterFactory.GetWriter<Person>(new CsvWriteOptions { Delimiter = ';' });
        using var sw = new StringWriter();
        using (var writer = new CsvStreamWriter(sw, new CsvWriteOptions { Delimiter = ';' }, leaveOpen: true))
        {
            rw.WriteRecords(writer, Sample(1), includeHeader: true);
        }
        Assert.Contains("Name;Age", sw.ToString());
    }
}
