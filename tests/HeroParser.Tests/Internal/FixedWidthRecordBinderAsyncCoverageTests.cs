using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Pipelines;
using System.Text;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Mapping;
using HeroParser.FixedWidths.Records;
using Xunit;

namespace HeroParser.Tests.Internal;

/// <summary>
/// Drives the uncovered scenarios in FixedWidthRecordBinder.cs's BindAsync overloads:
/// multi-segment pipe rows that exercise the non-contiguous buffer rental path, and
/// non-UTF8 encoding paths that fall through the IsUtf8Encoding fast path.
/// </summary>
[SuppressMessage("Trimming", "IL2026", Justification = "Reflection-based binders.")]
[SuppressMessage("AOT", "IL3050", Justification = "Reflection-based binders.")]
[Trait("Category", "Unit")]
public class FixedWidthRecordBinderAsyncCoverageTests
{
    [GenerateBinder]
    public sealed class GenRow
    {
        [PositionalMap(Start = 0, Length = 5)] public string Name { get; set; } = "";
        [PositionalMap(Start = 5, Length = 5, Alignment = FieldAlignment.Right, PadChar = '0')]
        public int Age { get; set; }
    }

    public sealed class ReflectionRow
    {
        [PositionalMap(Start = 0, Length = 5)] public string Name { get; set; } = "";
        [PositionalMap(Start = 5, Length = 5, Alignment = FieldAlignment.Right, PadChar = '0')]
        public int Age { get; set; }
    }

    private static string Sample(int n)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < n; i++) sb.AppendLine($"name{i}{(i + 1):D5}");
        return sb.ToString();
    }

    /// <summary>
    /// Writes data into a small-segment Pipe to force multi-segment rows.
    /// </summary>
    private static async Task<PipeReader> WriteSmallSegmentsAsync(string data, int segmentSize, CancellationToken ct)
    {
        var pipe = new Pipe(new PipeOptions(minimumSegmentSize: segmentSize));
        var bytes = Encoding.UTF8.GetBytes(data);
        int written = 0;
        while (written < bytes.Length)
        {
            int chunk = Math.Min(segmentSize / 2, bytes.Length - written);
            bytes.AsSpan(written, chunk).CopyTo(pipe.Writer.GetSpan(chunk));
            pipe.Writer.Advance(chunk);
            await pipe.Writer.FlushAsync(ct);
            written += chunk;
        }
        await pipe.Writer.CompleteAsync();
        return pipe.Reader;
    }

    [Fact]
    public async Task PipeReader_MultiSegmentRows_GeneratedBinder()
    {
        var ct = TestContext.Current.CancellationToken;
        var reader = await WriteSmallSegmentsAsync(Sample(5), segmentSize: 16, ct);

        var list = new List<GenRow>();
        await foreach (var r in FixedWidth.DeserializeRecordsAsync<GenRow>(reader,
            cancellationToken: ct))
        {
            list.Add(r);
        }
        Assert.Equal(5, list.Count);
        Assert.Equal("name0", list[0].Name);
        Assert.Equal(5, list[^1].Age);
    }

    [Fact]
    public async Task PipeReader_MultiSegmentRows_ReflectionBinder()
    {
        // Record without [GenerateBinder] uses reflection binder via descriptor binder.
        var ct = TestContext.Current.CancellationToken;
        var reader = await WriteSmallSegmentsAsync(Sample(5), segmentSize: 16, ct);

        var list = new List<ReflectionRow>();
        await foreach (var r in FixedWidth.DeserializeRecordsAsync<ReflectionRow>(reader,
            cancellationToken: ct))
        {
            list.Add(r);
        }
        Assert.Equal(5, list.Count);
    }

    [Fact]
    public async Task PipeReader_Latin1Encoding_GeneratedBinder()
    {
        // Non-UTF8 encoding takes the encoding.GetChars decode path in BindAsync.
        var ct = TestContext.Current.CancellationToken;
        var bytes = Encoding.Latin1.GetBytes(Sample(3));
        using var ms = new MemoryStream(bytes);
        var pipe = PipeReader.Create(ms);

        var list = new List<GenRow>();
        await foreach (var r in FixedWidth.DeserializeRecordsAsync<GenRow>(pipe,
            encoding: Encoding.Latin1,
            cancellationToken: ct))
        {
            list.Add(r);
        }
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public async Task PipeReader_Latin1Encoding_MultiSegmentRows_GeneratedBinder()
    {
        // Combine non-UTF8 + multi-segment to hit the most complex branch.
        var ct = TestContext.Current.CancellationToken;
        var pipe = new Pipe(new PipeOptions(minimumSegmentSize: 16));
        var bytes = Encoding.Latin1.GetBytes(Sample(5));
        int written = 0;
        while (written < bytes.Length)
        {
            int chunk = Math.Min(8, bytes.Length - written);
            bytes.AsSpan(written, chunk).CopyTo(pipe.Writer.GetSpan(chunk));
            pipe.Writer.Advance(chunk);
            await pipe.Writer.FlushAsync(ct);
            written += chunk;
        }
        await pipe.Writer.CompleteAsync();

        var list = new List<GenRow>();
        await foreach (var r in FixedWidth.DeserializeRecordsAsync<GenRow>(pipe.Reader,
            encoding: Encoding.Latin1,
            cancellationToken: ct))
        {
            list.Add(r);
        }
        Assert.Equal(5, list.Count);
    }

    [Fact]
    public async Task PipeReader_Latin1_ReflectionBinder()
    {
        var ct = TestContext.Current.CancellationToken;
        var bytes = Encoding.Latin1.GetBytes(Sample(3));
        using var ms = new MemoryStream(bytes);
        var pipe = PipeReader.Create(ms);

        var list = new List<ReflectionRow>();
        await foreach (var r in FixedWidth.DeserializeRecordsAsync<ReflectionRow>(pipe,
            encoding: Encoding.Latin1,
            cancellationToken: ct))
        {
            list.Add(r);
        }
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public async Task PipeReader_WithProgressReporter()
    {
        var ct = TestContext.Current.CancellationToken;
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(Sample(50)));
        var pipe = PipeReader.Create(ms);
        var reports = new List<FixedWidthProgress>();

        var list = new List<ReflectionRow>();
        // Use the builder which supports progress reporter.
        await foreach (var r in FixedWidth.Read<ReflectionRow>()
            .WithProgress(new Progress<FixedWidthProgress>(reports.Add), intervalRows: 10)
            .FromPipeReaderAsync(pipe, ct))
        {
            list.Add(r);
        }
        Assert.Equal(50, list.Count);
    }

    [Fact]
    public async Task FluentMap_MultiSegmentPipe_RoundTrip()
    {
        var ct = TestContext.Current.CancellationToken;
        var reader = await WriteSmallSegmentsAsync(Sample(8), segmentSize: 16, ct);

        var map = new FixedWidthMap<ReflectionRow>();
        map.Map(r => r.Name, c => c.Start(0).Length(5))
           .Map(r => r.Age, c => c.Start(5).Length(5).PadChar('0').Alignment(FieldAlignment.Right));

        var list = new List<ReflectionRow>();
        await foreach (var r in FixedWidth.Read<ReflectionRow>()
            .WithMap(map)
            .FromPipeReaderAsync(reader, ct))
        {
            list.Add(r);
        }
        Assert.Equal(8, list.Count);
    }

    [Fact]
    public async Task PipeReader_Latin1_LargeRows_TriggerBufferGrowth()
    {
        var ct = TestContext.Current.CancellationToken;
        // Many rows force the contiguousRowBuffer to be returned and re-rented.
        var bytes = Encoding.Latin1.GetBytes(Sample(200));
        var pipe = new Pipe(new PipeOptions(minimumSegmentSize: 24));
        int written = 0;
        while (written < bytes.Length)
        {
            int chunk = Math.Min(12, bytes.Length - written);
            bytes.AsSpan(written, chunk).CopyTo(pipe.Writer.GetSpan(chunk));
            pipe.Writer.Advance(chunk);
            await pipe.Writer.FlushAsync(ct);
            written += chunk;
        }
        await pipe.Writer.CompleteAsync();

        var list = new List<GenRow>();
        await foreach (var r in FixedWidth.DeserializeRecordsAsync<GenRow>(pipe.Reader,
            encoding: Encoding.Latin1,
            cancellationToken: ct))
        {
            list.Add(r);
        }
        Assert.Equal(200, list.Count);
    }

    [Fact]
    public async Task PipeReader_GenerateBinder_NonUtf8_FallsBackToCharBinderPath()
    {
        // [GenerateBinder] with non-UTF8 encoding falls through the byte fast path
        // and uses the descriptor/generated char binder via decode buffer.
        var ct = TestContext.Current.CancellationToken;
        var encoding = Encoding.GetEncoding("ISO-8859-1");
        var bytes = encoding.GetBytes(Sample(5));
        using var ms = new MemoryStream(bytes);
        var pipe = PipeReader.Create(ms);

        var list = new List<GenRow>();
        await foreach (var r in FixedWidth.DeserializeRecordsAsync<GenRow>(pipe,
            encoding: encoding,
            cancellationToken: ct))
        {
            list.Add(r);
        }
        Assert.Equal(5, list.Count);
    }

    [Fact]
    public async Task PipeReader_NonUtf8_FluentMap()
    {
        var ct = TestContext.Current.CancellationToken;
        var bytes = Encoding.Latin1.GetBytes(Sample(3));
        using var ms = new MemoryStream(bytes);
        var pipe = PipeReader.Create(ms);

        var map = new FixedWidthMap<ReflectionRow>();
        map.Map(r => r.Name, c => c.Start(0).Length(5))
           .Map(r => r.Age, c => c.Start(5).Length(5).PadChar('0').Alignment(FieldAlignment.Right));

        var list = new List<ReflectionRow>();
        await foreach (var r in FixedWidth.Read<ReflectionRow>()
            .WithMap(map)
            .WithEncoding(Encoding.Latin1)
            .FromPipeReaderAsync(pipe, ct))
        {
            list.Add(r);
        }
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public async Task PipeReader_WithCulture_NonInvariant()
    {
        // Non-invariant culture exercises the culture-passing branch.
        var ct = TestContext.Current.CancellationToken;
        var bytes = Encoding.UTF8.GetBytes(Sample(3));
        using var ms = new MemoryStream(bytes);
        var pipe = PipeReader.Create(ms);

        var list = new List<ReflectionRow>();
        await foreach (var r in FixedWidth.Read<ReflectionRow>()
            .WithCulture(CultureInfo.GetCultureInfo("en-GB"))
            .FromPipeReaderAsync(pipe, ct))
        {
            list.Add(r);
        }
        Assert.Equal(3, list.Count);
    }
}
