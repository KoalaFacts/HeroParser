using System.Buffers;
using System.Buffers.Text;
using System.Data;
using System.IO.Pipelines;
using System.Numerics;
using System.Text;
using HeroParser.FixedWidths;
using HeroParser.Htbs;
using HeroParser.Htbs.Reading;
using HeroParser.Htbs.Records;
using HeroParser.Htbs.Writing;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Rows;
using Xunit;

namespace HeroParser.Tests.Memory;

[GenerateBinder]
public sealed class CsvMemoryRecord
{
    public int Id { get; set; }
    public long BigId { get; set; }
    public double Metric { get; set; }
    public decimal Amount { get; set; }
    public bool Active { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateOnly DateVal { get; set; }
    [Format(WriteFormat = "HH:mm:ss.fff")]
    [Parse(Format = "HH:mm:ss.fff")]
    public TimeOnly TimeVal { get; set; }
    public DateTimeOffset OffsetVal { get; set; }
    public TimeSpan Duration { get; set; }
    public Guid Token { get; set; }
    public Half Ratio { get; set; }
    public Int128 BigSigned { get; set; }
}

[GenerateBinder]
public sealed class FixedWidthMemoryRecord
{
    [PositionalMap(Start = 0, Length = 8, Alignment = FieldAlignment.Right, PadChar = '0')]
    public int Id { get; set; }

    [PositionalMap(Start = 8, Length = 14, Alignment = FieldAlignment.Right, PadChar = '0')]
    public long BigId { get; set; }

    [PositionalMap(Start = 22, Length = 12, Alignment = FieldAlignment.Right)]
    public decimal Amount { get; set; }

    [PositionalMap(Start = 34, Length = 6)]
    public bool Active { get; set; }

    [PositionalMap(Start = 40, Length = 10)]
    [Format(WriteFormat = "yyyy-MM-dd")]
    [Parse(Format = "yyyy-MM-dd")]
    public DateOnly DateVal { get; set; }

    [PositionalMap(Start = 50, Length = 12)]
    [Format(WriteFormat = "HH:mm:ss.fff")]
    [Parse(Format = "HH:mm:ss.fff")]
    public TimeOnly TimeVal { get; set; }

    [PositionalMap(Start = 62, Length = 34)]
    [Format(WriteFormat = "O")]
    [Parse(Format = "O")]
    public DateTimeOffset OffsetVal { get; set; }
}

[GenerateBinder]
public sealed class JsonlMemoryRecord
{
    public int Id { get; set; }
    public long BigId { get; set; }
    public double Metric { get; set; }
    public decimal Amount { get; set; }
    public bool Active { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateOnly DateVal { get; set; }
    public TimeOnly TimeVal { get; set; }
    public DateTimeOffset OffsetVal { get; set; }
    public TimeSpan Duration { get; set; }
    public Guid Token { get; set; }
}

[GenerateBinder]
public sealed class ExcelMemoryRecord
{
    public int Id { get; set; }
    public long BigId { get; set; }
    public double Metric { get; set; }
    public decimal Amount { get; set; }
    public bool Active { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateOnly DateVal { get; set; }
}

[Collection(MemoryRegressionCollection.NAME)]
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed class RecordBinderAndStreamingMemoryRegressionTests
{
    private const int SMALL_ROWS = 200;
    private const int LARGE_ROWS = 2_000;
    private const int DELTA_ROWS = LARGE_ROWS - SMALL_ROWS;

    private static List<T> BuildRecords<T>(int count, Func<int, T> factory)
    {
        var list = new List<T>(count);
        for (int i = 0; i < count; i++)
            list.Add(factory(i));
        return list;
    }

    private static byte[] BuildPrimitiveCsvBytes(int rows)
    {
        var sb = new StringBuilder(rows * 48);
        for (int i = 0; i < rows; i++)
            sb.Append(i).Append(',').Append(10_000_000L + i).Append(',').Append(i * 1.25m).Append('\n');
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static byte[] BuildValueOnlyHtbBytes(int rows)
    {
        var schema = new HtbSchema(
        [
            new HtbColumn("Id", HtbDataType.Int32, isNullable: false),
            new HtbColumn("BigId", HtbDataType.Int64, isNullable: false),
            new HtbColumn("SmallId", HtbDataType.Int16, isNullable: false),
            new HtbColumn("Tag", HtbDataType.Byte, isNullable: false),
            new HtbColumn("Ratio", HtbDataType.Float, isNullable: false),
            new HtbColumn("Metric", HtbDataType.Double, isNullable: false),
            new HtbColumn("Amount", HtbDataType.Decimal, isNullable: false),
            new HtbColumn("Active", HtbDataType.Boolean, isNullable: false),
            new HtbColumn("CreatedAt", HtbDataType.DateTime, isNullable: false),
            new HtbColumn("Token", HtbDataType.Guid, isNullable: false),
            new HtbColumn("Code", HtbDataType.Char, isNullable: false),
            new HtbColumn("DateVal", HtbDataType.DateOnly, isNullable: false),
            new HtbColumn("TimeVal", HtbDataType.TimeOnly, isNullable: false),
            new HtbColumn("OffsetVal", HtbDataType.DateTimeOffset, isNullable: false),
            new HtbColumn("Duration", HtbDataType.TimeSpan, isNullable: false),
            new HtbColumn("HalfVal", HtbDataType.Half, isNullable: false),
            new HtbColumn("Int128Val", HtbDataType.Int128, isNullable: false),
            new HtbColumn("UInt128Val", HtbDataType.UInt128, isNullable: false),
            new HtbColumn("BigIntVal", HtbDataType.BigInteger, isNullable: false)
        ]);

        using var ms = new MemoryStream(capacity: rows * 160);
        using (var writer = new HtbStreamWriter(ms, leaveOpen: true))
        {
            writer.WriteHeader(schema);
            WriteHtbRows(writer, rows, includeNullMask: true);
            writer.Flush();
        }
        return ms.ToArray();
    }

    private static void WriteHtbRows(HtbStreamWriter writer, int rows, bool includeNullMask)
    {
        Span<byte> mask = stackalloc byte[3];
        DateTime dt = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        Guid guid = new("11111111-2222-3333-4444-555555555555");
        DateOnly dateVal = new(2026, 11, 23);
        TimeOnly timeVal = new(12, 34, 56, 789);
        DateTimeOffset dto = new(2026, 11, 23, 12, 0, 0, TimeSpan.FromHours(2));
        TimeSpan duration = new(0, 1, 2, 3, 456);
        Half halfVal = (Half)1.5f;
        Int128 i128 = 1234567890123456789;
        UInt128 u128 = 9876543210987654321;
        BigInteger smallBig = new(42);

        for (int i = 0; i < rows; i++)
        {
            writer.IncrementRecordCount();
            if (includeNullMask)
                writer.WriteMask(mask);

            writer.WriteInt32(i);
            writer.WriteInt64(10_000_000_000L + i);
            writer.WriteInt16((short)(i & 0x7FFF));
            writer.WriteByte((byte)i);
            writer.WriteFloat(1.5f + (i & 15));
            writer.WriteDouble(2.25 + (i & 31));
            writer.WriteDecimal(100.25m + (i & 127));
            writer.WriteBoolean((i & 1) == 0);
            writer.WriteDateTime(dt);
            writer.WriteGuid(guid);
            writer.WriteChar('A');
            writer.WriteDateOnly(dateVal);
            writer.WriteTimeOnly(timeVal);
            writer.WriteDateTimeOffset(dto);
            writer.WriteTimeSpan(duration);
            writer.WriteHalf(halfVal);
            writer.WriteInt128(i128);
            writer.WriteUInt128(u128);
            writer.WriteBigInteger(smallBig);
        }
    }

    [Fact]
    public void Csv_GeneratedByteBinder_ValueTypeRecord_AllocatesOnlyRecordInstances()
    {
        DateTime baseDt = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        Guid guid = new("11111111-2222-3333-4444-555555555555");
        CsvMemoryRecord Create(int i) => new()
        {
            Id = i + 1,
            BigId = 10_000_000_000L + i,
            Metric = 1.25 + (i & 31),
            Amount = 99.5m + (i & 63),
            Active = (i & 1) == 0,
            CreatedAt = baseDt,
            DateVal = new DateOnly(2026, 11, 23),
            TimeVal = new TimeOnly(12, 34, 56, 789),
            OffsetVal = new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.FromHours(2)),
            Duration = new TimeSpan(0, 1, 2, 3, 456),
            Token = guid,
            Ratio = (Half)1.5f,
            BigSigned = 1234567890123456789
        };

        byte[] smallUtf8 = Encoding.UTF8.GetBytes(Csv.Write<CsvMemoryRecord>().ToText(BuildRecords(SMALL_ROWS, Create)));
        byte[] largeUtf8 = Encoding.UTF8.GetBytes(Csv.Write<CsvMemoryRecord>().ToText(BuildRecords(LARGE_ROWS, Create)));
        long checksum = 0;

        long Consume(byte[] data)
        {
            long sum = 0;
            foreach (var record in Csv.DeserializeRecordsFromBytes<CsvMemoryRecord>(data))
                sum += record.Id + (record.Active ? 1 : 0);
            return sum;
        }

        // Expect only one CsvMemoryRecord object per row (~128 B), no per-field string allocations.
        AllocationMeter.AssertBoundedPerRowGrowth(
            () => checksum = Consume(smallUtf8),
            () => checksum = Consume(largeUtf8),
            DELTA_ROWS,
            maxBytesPerRow: 256.0,
            scenario: "Csv.DeserializeRecordsFromBytes<CsvMemoryRecord>");

        Assert.NotEqual(0L, checksum);
    }

    [Fact]
    public void FixedWidth_GeneratedBinder_ValueTypeRecord_LowPerRowAllocation()
    {
        FixedWidthMemoryRecord Create(int i) => new()
        {
            Id = i + 1,
            BigId = 10_000_000_000L + i,
            Amount = 99.5m + (i & 63),
            Active = (i & 1) == 0,
            DateVal = new DateOnly(2026, 11, 23),
            TimeVal = new TimeOnly(12, 34, 56, 789),
            OffsetVal = new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.FromHours(2))
        };

        string smallText = FixedWidth.Write<FixedWidthMemoryRecord>().ToText(BuildRecords(SMALL_ROWS, Create));
        string largeText = FixedWidth.Write<FixedWidthMemoryRecord>().ToText(BuildRecords(LARGE_ROWS, Create));
        long checksum = 0;

        long Consume(string data)
        {
            long sum = 0;
            FixedWidth.Read<FixedWidthMemoryRecord>().ForEachFromText(data, r => sum += r.Id);
            return sum;
        }

        AllocationMeter.AssertZeroPerRowGrowth(
            () => checksum = Consume(smallText),
            () => checksum = Consume(largeText),
            DELTA_ROWS,
            scenario: "FixedWidth.Read<FixedWidthMemoryRecord>().ForEachFromText");

        Assert.NotEqual(0L, checksum);
    }

    [Fact]
    public void Jsonl_GeneratedBinder_ValueTypeRecord_NoPerFieldStringAllocations()
    {
        DateTime baseDt = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        Guid guid = new("11111111-2222-3333-4444-555555555555");
        JsonlMemoryRecord Create(int i) => new()
        {
            Id = i + 1,
            BigId = 10_000_000_000L + i,
            Metric = 1.25 + (i & 31),
            Amount = 99.5m + (i & 63),
            Active = (i & 1) == 0,
            CreatedAt = baseDt,
            DateVal = new DateOnly(2026, 11, 23),
            TimeVal = new TimeOnly(12, 34, 56, 789),
            OffsetVal = new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.FromHours(2)),
            Duration = new TimeSpan(0, 1, 2, 3, 456),
            Token = guid
        };

        using var smallMs = new MemoryStream(capacity: SMALL_ROWS * 300);
        using var largeMs = new MemoryStream(capacity: LARGE_ROWS * 300);
        Jsonl.Write<JsonlMemoryRecord>().ToStream(smallMs, BuildRecords(SMALL_ROWS, Create), leaveOpen: true);
        Jsonl.Write<JsonlMemoryRecord>().ToStream(largeMs, BuildRecords(LARGE_ROWS, Create), leaveOpen: true);
        long checksum = 0;

        long Consume(MemoryStream stream)
        {
            stream.Position = 0;
            long sum = 0;
            using var reader = Jsonl.Read<JsonlMemoryRecord>().FromStream(stream, leaveOpen: true);
            foreach (var r in reader)
                sum += r.Id + (r.Active ? 1 : 0);
            return sum;
        }

        // Expect only one JsonlMemoryRecord object per row (~104-128 B), zero per-field GetString() allocations.
        AllocationMeter.AssertBoundedPerRowGrowth(
            () => checksum = Consume(smallMs),
            () => checksum = Consume(largeMs),
            DELTA_ROWS,
            maxBytesPerRow: 256.0,
            scenario: "Jsonl.Read<JsonlMemoryRecord>().FromStream (generated binder)");

        Assert.NotEqual(0L, checksum);
    }

    [Fact]
    public void Htb_RawStreamWriter_And_StreamReader_ZeroPerRowGrowth()
    {
        var schema = new HtbSchema(
        [
            new HtbColumn("Id", HtbDataType.Int32, isNullable: false),
            new HtbColumn("BigId", HtbDataType.Int64, isNullable: false),
            new HtbColumn("SmallId", HtbDataType.Int16, isNullable: false),
            new HtbColumn("Tag", HtbDataType.Byte, isNullable: false),
            new HtbColumn("Ratio", HtbDataType.Float, isNullable: false),
            new HtbColumn("Metric", HtbDataType.Double, isNullable: false),
            new HtbColumn("Amount", HtbDataType.Decimal, isNullable: false),
            new HtbColumn("Active", HtbDataType.Boolean, isNullable: false),
            new HtbColumn("CreatedAt", HtbDataType.DateTime, isNullable: false),
            new HtbColumn("Token", HtbDataType.Guid, isNullable: false),
            new HtbColumn("Code", HtbDataType.Char, isNullable: false),
            new HtbColumn("DateVal", HtbDataType.DateOnly, isNullable: false),
            new HtbColumn("TimeVal", HtbDataType.TimeOnly, isNullable: false),
            new HtbColumn("OffsetVal", HtbDataType.DateTimeOffset, isNullable: false),
            new HtbColumn("Duration", HtbDataType.TimeSpan, isNullable: false),
            new HtbColumn("HalfVal", HtbDataType.Half, isNullable: false),
            new HtbColumn("Int128Val", HtbDataType.Int128, isNullable: false),
            new HtbColumn("UInt128Val", HtbDataType.UInt128, isNullable: false),
            new HtbColumn("BigIntVal", HtbDataType.BigInteger, isNullable: false)
        ]);

        void WriteRows(int rows)
        {
            using var writer = new HtbStreamWriter(Stream.Null, leaveOpen: true);
            writer.WriteHeader(schema);
            WriteHtbRows(writer, rows, includeNullMask: true);
            writer.Flush();
        }

        AllocationMeter.AssertZeroPerRowGrowth(
            () => WriteRows(SMALL_ROWS),
            () => WriteRows(LARGE_ROWS),
            DELTA_ROWS,
            scenario: "HtbStreamWriter value + BigInteger");

        using var smallMs = new MemoryStream(BuildValueOnlyHtbBytes(SMALL_ROWS), writable: false);
        using var largeMs = new MemoryStream(BuildValueOnlyHtbBytes(LARGE_ROWS), writable: false);
        long checksum = 0;

        long ReadRows(MemoryStream stream)
        {
            stream.Position = 0;
            using var reader = new HtbStreamReader(stream, leaveOpen: true);
            _ = reader.ParseHeader();
            Span<byte> mask = stackalloc byte[3];
            long sum = 0;
            while (!reader.IsEndOfStream())
            {
                reader.IncrementRecordCount();
                reader.ReadMask(mask);
                sum += reader.ReadInt32();
                sum += reader.ReadInt64() & 0xFF;
                sum += reader.ReadInt16();
                sum += reader.ReadByte();
                sum += (long)reader.ReadFloat();
                sum += (long)reader.ReadDouble();
                sum += (long)reader.ReadDecimal();
                sum += reader.ReadBoolean() ? 1 : 0;
                sum += reader.ReadDateTime().Day;
                sum += reader.ReadGuid() == Guid.Empty ? 0 : 1;
                sum += reader.ReadChar();
                sum += reader.ReadDateOnly().Day;
                sum += reader.ReadTimeOnly().Millisecond;
                sum += reader.ReadDateTimeOffset().Offset.Hours;
                sum += reader.ReadTimeSpan().Milliseconds;
                sum += (int)(double)reader.ReadHalf();
                sum += (long)(reader.ReadInt128() & 0xFF);
                sum += (long)(reader.ReadUInt128() & 0xFF);
                sum += (int)reader.ReadBigInteger();
            }
            return sum;
        }

        AllocationMeter.AssertZeroPerRowGrowth(
            () => checksum = ReadRows(smallMs),
            () => checksum = ReadRows(largeMs),
            DELTA_ROWS,
            scenario: "HtbStreamReader value + BigInteger");

        Assert.NotEqual(0L, checksum);
    }

    [Fact]
    public async Task Htb_CreateDataReader_TypedGetters_SyncAndAsync_ZeroPerRowGrowth()
    {
        var ct = TestContext.Current.CancellationToken;
        using var smallMs = new MemoryStream(BuildValueOnlyHtbBytes(SMALL_ROWS), writable: false);
        using var largeMs = new MemoryStream(BuildValueOnlyHtbBytes(LARGE_ROWS), writable: false);
        long checksum = 0;

        long ReadSync(MemoryStream stream)
        {
            stream.Position = 0;
            using var dr = global::HeroParser.Htb.CreateDataReader(stream, leaveOpen: true);
            long sum = 0;
            while (dr.Read())
            {
                sum += dr.GetInt32(0) + (dr.GetInt64(1) & 0xFF) + dr.GetInt16(2) + dr.GetByte(3) +
                       (long)dr.GetFloat(4) + (long)dr.GetDouble(5) + (long)dr.GetDecimal(6) +
                       (dr.GetBoolean(7) ? 1 : 0) + dr.GetDateTime(8).Day +
                       (dr.GetGuid(9) == Guid.Empty ? 0 : 1) + dr.GetChar(10);
            }
            return sum;
        }

        AllocationMeter.AssertZeroPerRowGrowth(
            () => checksum = ReadSync(smallMs),
            () => checksum = ReadSync(largeMs),
            DELTA_ROWS,
            scenario: "Htb.CreateDataReader sync Read() + typed getters");
        Assert.NotEqual(0L, checksum);

        async Task ReadAsync(MemoryStream stream)
        {
            stream.Position = 0;
            await using var dr = global::HeroParser.Htb.CreateDataReader(stream, leaveOpen: true);
            long sum = 0;
            while (await dr.ReadAsync(ct))
            {
                sum += dr.GetInt32(0) + (dr.GetInt64(1) & 0xFF) + dr.GetInt16(2) + dr.GetByte(3) +
                       (long)dr.GetFloat(4) + (long)dr.GetDouble(5) + (long)dr.GetDecimal(6) +
                       (dr.GetBoolean(7) ? 1 : 0) + dr.GetDateTime(8).Day +
                       (dr.GetGuid(9) == Guid.Empty ? 0 : 1) + dr.GetChar(10);
            }
            checksum = sum;
        }

        await AllocationMeter.AssertZeroPerRowGrowthAsync(
            () => ReadAsync(smallMs),
            () => ReadAsync(largeMs),
            DELTA_ROWS,
            scenario: "Htb.CreateDataReader async ReadAsync() + typed getters");
        Assert.NotEqual(0L, checksum);
    }

    [Fact]
    public async Task Csv_AsyncStreamReader_And_PipeSequenceReader_ZeroPerRowGrowth()
    {
        var ct = TestContext.Current.CancellationToken;
        byte[] smallBytes = BuildPrimitiveCsvBytes(SMALL_ROWS);
        byte[] largeBytes = BuildPrimitiveCsvBytes(LARGE_ROWS);
        using var smallMs = new MemoryStream(smallBytes, writable: false);
        using var largeMs = new MemoryStream(largeBytes, writable: false);
        long checksum = 0;

        async Task ReadAsyncStream(MemoryStream stream)
        {
            stream.Position = 0;
            await using var reader = Csv.CreateAsyncStreamReader(stream, leaveOpen: true);
            long sum = 0;
            while (await reader.MoveNextAsync(ct))
            {
                var row = reader.Current;
                if (row[0].TryParseInt32(out int id) &&
                    row[1].TryParseInt64(out long big) &&
                    row[2].TryParseDecimal(out decimal dec))
                {
                    sum += id + (big & 0xFF) + (long)dec;
                }
            }
            checksum = sum;
        }

        await AllocationMeter.AssertZeroPerRowGrowthAsync(
            () => ReadAsyncStream(smallMs),
            () => ReadAsyncStream(largeMs),
            DELTA_ROWS,
            scenario: "Csv.CreateAsyncStreamReader primitive parsing");
        Assert.NotEqual(0L, checksum);

        async Task ReadPipe(byte[] payload)
        {
            var pipe = PipeReader.Create(new ReadOnlySequence<byte>(payload));
            await using var reader = Csv.CreatePipeSequenceReader(pipe);
            long sum = 0;
            while (await reader.MoveNextAsync(ct))
            {
                var row = reader.Current;
                sum += ParsePipeInt32(row[0]) + (ParsePipeInt64(row[1]) & 0xFF);
            }
            checksum = sum;
        }

        await AllocationMeter.AssertZeroPerRowGrowthAsync(
            () => ReadPipe(smallBytes),
            () => ReadPipe(largeBytes),
            DELTA_ROWS,
            scenario: "Csv.CreatePipeSequenceReader primitive parsing");
        Assert.NotEqual(0L, checksum);
    }

    [Fact]
    public void Excel_GeneratedRecordWrite_And_Read_BoundedPerRowMemory()
    {
        DateTime baseDt = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        ExcelMemoryRecord Create(int i) => new()
        {
            Id = i + 1,
            BigId = 10_000_000_000L + i,
            Metric = 1.25 + (i & 31),
            Amount = 99.5m + (i & 63),
            Active = (i & 1) == 0,
            CreatedAt = baseDt,
            DateVal = new DateOnly(2026, 11, 23)
        };

        List<ExcelMemoryRecord> smallRecords = BuildRecords(SMALL_ROWS, Create);
        List<ExcelMemoryRecord> largeRecords = BuildRecords(LARGE_ROWS, Create);
        using var reusableMs = new MemoryStream(capacity: 512 * 1024);
        long bytesWritten = 0;

        void WriteBatch(List<ExcelMemoryRecord> records)
        {
            reusableMs.Position = 0;
            reusableMs.SetLength(0);
            global::HeroParser.Excel.Write<ExcelMemoryRecord>().ToStream(reusableMs, records, leaveOpen: true);
            bytesWritten = reusableMs.Length;
        }

        AllocationMeter.AssertBoundedPerRowGrowth(
            () => WriteBatch(smallRecords),
            () => WriteBatch(largeRecords),
            DELTA_ROWS,
            maxBytesPerRow: 2048.0,
            scenario: "Excel.Write<ExcelMemoryRecord>().ToStream");
        Assert.True(bytesWritten > 0);

        byte[] smallXlsx = global::HeroParser.Excel.Write<ExcelMemoryRecord>().ToBytes(smallRecords);
        byte[] largeXlsx = global::HeroParser.Excel.Write<ExcelMemoryRecord>().ToBytes(largeRecords);
        long checksum = 0;

        long ReadBatch(byte[] xlsx)
        {
            using var ms = new MemoryStream(xlsx, writable: false);
            var list = global::HeroParser.Excel.Read<ExcelMemoryRecord>().FromStream(ms);
            return list.Count;
        }

        AllocationMeter.AssertBoundedPerRowGrowth(
            () => checksum = ReadBatch(smallXlsx),
            () => checksum = ReadBatch(largeXlsx),
            DELTA_ROWS,
            maxBytesPerRow: 2048.0,
            scenario: "Excel.Read<ExcelMemoryRecord>().FromStream");
        Assert.Equal(LARGE_ROWS, checksum);
    }

    private static int ParsePipeInt32(CsvPipeSequenceColumn col)
    {
        if (col.IsSingleSegment)
        {
            return Utf8Parser.TryParse(col.Span, out int val, out _) ? val : 0;
        }
        Span<byte> scratch = stackalloc byte[32];
        if (col.Length <= scratch.Length && col.TryCopyTo(scratch))
        {
            return Utf8Parser.TryParse(scratch[..col.Length], out int val, out _) ? val : 0;
        }
        return 0;
    }

    private static long ParsePipeInt64(CsvPipeSequenceColumn col)
    {
        if (col.IsSingleSegment)
        {
            return Utf8Parser.TryParse(col.Span, out long val, out _) ? val : 0;
        }
        Span<byte> scratch = stackalloc byte[32];
        if (col.Length <= scratch.Length && col.TryCopyTo(scratch))
        {
            return Utf8Parser.TryParse(scratch[..col.Length], out long val, out _) ? val : 0;
        }
        return 0;
    }
}
