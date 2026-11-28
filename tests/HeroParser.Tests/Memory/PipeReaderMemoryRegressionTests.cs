using System.Globalization;
using System.IO.Pipelines;
using System.Text;
using HeroParser.FixedWidths;
using HeroParser.JsonLines.Reading;
using HeroParser.SeparatedValues.Core;
using Xunit;

namespace HeroParser.Tests.Memory;

[GenerateBinder]
public struct PrimitiveFixedStreamRow
{
    [PositionalMap(Start = 0, Length = 6, Alignment = FieldAlignment.Right, PadChar = '0')]
    public int Id { get; set; }

    [PositionalMap(Start = 6, Length = 10, Alignment = FieldAlignment.Right, PadChar = '0')]
    public long BigId { get; set; }
}

[Collection(MemoryRegressionCollection.NAME)]
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed class PipeReaderMemoryRegressionTests
{
    private const int SMALL_ROWS = 1_000;
    private const int LARGE_ROWS = 10_000;
    private const int DELTA_ROWS = LARGE_ROWS - SMALL_ROWS;

    [Fact]
    public async Task Csv_ReadFromPipeReader_ZeroPerRowGrowth()
    {
        var ct = TestContext.Current.CancellationToken;
        byte[] smallPayload = BuildCsvPayload(SMALL_ROWS);
        byte[] largePayload = BuildCsvPayload(LARGE_ROWS);
        var options = new CsvReadOptions { MaxRowCount = 20_000 };

        async Task RunAsync(byte[] payload, int expectedColumns)
        {
            using var stream = new MemoryStream(payload, writable: false);
            var reader = PipeReader.Create(stream);
            int total = 0;
            await foreach (var row in Csv.ReadFromPipeReaderAsync(reader, options, ct))
            {
                total += row.ColumnCount;
            }

            Assert.Equal(expectedColumns, total);
        }

        await AllocationMeter.AssertZeroPerRowGrowthAsync(
            () => RunAsync(smallPayload, SMALL_ROWS * 4),
            () => RunAsync(largePayload, LARGE_ROWS * 4),
            DELTA_ROWS,
            scenario: "Csv.ReadFromPipeReaderAsync");
    }

    [Fact]
    public async Task FixedWidth_ReadFromPipeReader_ZeroPerRowGrowth()
    {
        var ct = TestContext.Current.CancellationToken;
        byte[] smallPayload = BuildFixedWidthPayload(SMALL_ROWS);
        byte[] largePayload = BuildFixedWidthPayload(LARGE_ROWS);
        var options = new FixedWidthReadOptions { MaxRecordCount = 20_000 };

        async Task RunAsync(byte[] payload, int expectedBytes)
        {
            using var stream = new MemoryStream(payload, writable: false);
            var reader = PipeReader.Create(stream);
            int total = 0;
            await foreach (var row in FixedWidth.ReadFromPipeReaderAsync(reader, options, ct))
            {
                total += row.Length;
            }

            Assert.Equal(expectedBytes, total);
        }

        await AllocationMeter.AssertZeroPerRowGrowthAsync(
            () => RunAsync(smallPayload, SMALL_ROWS * 10),
            () => RunAsync(largePayload, LARGE_ROWS * 10),
            DELTA_ROWS,
            scenario: "FixedWidth.ReadFromPipeReaderAsync");
    }

    [Fact]
    public async Task Jsonl_ReadLinesAsync_ZeroPerRowGrowth()
    {
        var ct = TestContext.Current.CancellationToken;
        byte[] smallPayload = BuildJsonlPayload(SMALL_ROWS);
        byte[] largePayload = BuildJsonlPayload(LARGE_ROWS);

        async Task RunAsync(byte[] payload, int expectedLines)
        {
            using var stream = new MemoryStream(payload, writable: false);
            var reader = PipeReader.Create(stream);
            int lines = 0;
            long totalBytes = 0;
            await foreach (JsonlLine line in Jsonl.ReadLinesAsync(reader, cancellationToken: ct))
            {
                lines++;
                totalBytes += line.Utf8.Length;
            }

            await reader.CompleteAsync();
            Assert.Equal(expectedLines, lines);
            Assert.True(totalBytes > 0);
        }

        await AllocationMeter.AssertZeroPerRowGrowthAsync(
            () => RunAsync(smallPayload, SMALL_ROWS),
            () => RunAsync(largePayload, LARGE_ROWS),
            DELTA_ROWS,
            scenario: "Jsonl.ReadLinesAsync");
    }

    [Fact]
    public async Task FixedWidth_TypedStreamAndBuilder_ZeroPerRowGrowth()
    {
        var ct = TestContext.Current.CancellationToken;
        byte[] smallPayload = BuildPrimitiveFixedStreamPayload(SMALL_ROWS);
        byte[] largePayload = BuildPrimitiveFixedStreamPayload(LARGE_ROWS);
        var options = new FixedWidthReadOptions { MaxRecordCount = LARGE_ROWS + 10 };
        using var smallMs = new MemoryStream(smallPayload, writable: false);
        using var largeMs = new MemoryStream(largePayload, writable: false);
        long checksum = 0;

        async Task RunBuilderStreamAsync(MemoryStream stream)
        {
            stream.Position = 0;
            long sum = 0;
            await foreach (var row in FixedWidth.Read<PrimitiveFixedStreamRow>()
                .WithMaxRecords(LARGE_ROWS + 10)
                .FromStreamAsync(stream, ct))
            {
                sum += row.Id + (row.BigId & 0xFF);
            }

            checksum = sum;
        }

        await AllocationMeter.AssertZeroPerRowGrowthAsync(
            () => RunBuilderStreamAsync(smallMs),
            () => RunBuilderStreamAsync(largeMs),
            DELTA_ROWS,
            scenario: "FixedWidth.Read<PrimitiveFixedStreamRow>().FromStreamAsync");
        Assert.NotEqual(0L, checksum);

        async Task RunStaticStreamAsync(MemoryStream stream)
        {
            stream.Position = 0;
            long sum = 0;
            await foreach (var row in FixedWidth.DeserializeRecordsAsync<PrimitiveFixedStreamRow>(
                stream,
                options,
                leaveOpen: true,
                cancellationToken: ct))
            {
                sum += row.Id + (row.BigId & 0xFF);
            }

            checksum = sum;
        }

        await AllocationMeter.AssertZeroPerRowGrowthAsync(
            () => RunStaticStreamAsync(smallMs),
            () => RunStaticStreamAsync(largeMs),
            DELTA_ROWS,
            scenario: "FixedWidth.DeserializeRecordsAsync<PrimitiveFixedStreamRow>(Stream)");
        Assert.NotEqual(0L, checksum);
    }

    private static byte[] BuildCsvPayload(int rows)
    {
        var sb = new StringBuilder(rows * 48);
        for (int i = 0; i < rows; i++)
        {
            sb.Append("val").Append(i).Append("_0,val").Append(i).Append("_1,val").Append(i).Append("_2,val").Append(i).Append("_3\n");
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static byte[] BuildFixedWidthPayload(int rows)
    {
        var sb = new StringBuilder(rows * 12);
        for (int i = 0; i < rows; i++)
        {
            sb.Append((i % 10_000).ToString("D4", CultureInfo.InvariantCulture))
              .Append("Alice \n");
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static byte[] BuildJsonlPayload(int rows)
    {
        var sb = new StringBuilder(rows * 36);
        for (int i = 0; i < rows; i++)
        {
            sb.Append("{\"id\":").Append(i).Append(",\"name\":\"Item_").Append(i).Append("\"}\n");
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static byte[] BuildPrimitiveFixedStreamPayload(int rows)
    {
        var sb = new StringBuilder(rows * 17);
        for (int i = 1; i <= rows; i++)
        {
            sb.Append(i.ToString("D6", CultureInfo.InvariantCulture))
              .Append((10_000_000L + i).ToString("D10", CultureInfo.InvariantCulture))
              .Append('\n');
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}
