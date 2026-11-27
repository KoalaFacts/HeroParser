using System.Data.Common;
using HeroParser.Excels.Core;
using HeroParser.Excels.Writing;
using HeroParser.SeparatedValues.Detection;
using Xunit;

namespace HeroParser.Tests;

[GenerateBinder]
public sealed class ExcelUtcFractionalDateRecord
{
    public DateTime UtcFractional { get; set; }
    public DateTime UtcWhole { get; set; }
    public DateTime LocalFractional { get; set; }
}

public sealed class ExcelUtcFractionalDateReflectionRecord
{
    public DateTime UtcFractional { get; set; }
    public DateTime UtcWhole { get; set; }
    public DateTime LocalFractional { get; set; }
}

public class FinalReviewRegressionTests
{
    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task Excel_UtcAndLocalDateTimeWithFractionalSeconds_PreservesWallClockTimeAndTicksAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var utcWhole = new DateTime(2026, 11, 27, 15, 0, 0, DateTimeKind.Utc);
        var utcFractional = utcWhole.AddTicks(1);
        var localFractional = new DateTime(2026, 11, 27, 9, 45, 12, DateTimeKind.Local).AddTicks(9_876_543);
        var record = new ExcelUtcFractionalDateRecord
        {
            UtcFractional = utcFractional,
            UtcWhole = utcWhole,
            LocalFractional = localFractional
        };

        static void AssertWallClockPreserved(byte[] xlsxBytes, DateTime utcFrac, DateTime utcWhole, DateTime localFrac)
        {
            using var readMs = new MemoryStream(xlsxBytes);
            var actual = Assert.Single(HeroParser.Excel.Read<ExcelUtcFractionalDateRecord>().FromStream(readMs));
            Assert.Equal(utcWhole.Ticks, actual.UtcWhole.Ticks);
            Assert.Equal(utcFrac.Ticks, actual.UtcFractional.Ticks);
            Assert.Equal(localFrac.Ticks, actual.LocalFractional.Ticks);
        }

        AssertWallClockPreserved(
            HeroParser.Excel.Write<ExcelUtcFractionalDateRecord>().ToBytes([record]),
            utcFractional,
            utcWhole,
            localFractional);

        var reflectionRecord = new ExcelUtcFractionalDateReflectionRecord
        {
            UtcFractional = utcFractional,
            UtcWhole = utcWhole,
            LocalFractional = localFractional
        };
        AssertWallClockPreserved(
            HeroParser.Excel.Write<ExcelUtcFractionalDateReflectionRecord>().ToBytes([reflectionRecord]),
            utcFractional,
            utcWhole,
            localFractional);

        ExcelWriteOptions[] optionModes =
        [
            new ExcelWriteOptions(),
            new ExcelWriteOptions { OnSerializeError = _ => ExcelSerializeErrorAction.Throw }
        ];

        foreach (var opts in optionModes)
        {
            using (var drSync = new UtcFractionalDbDataReader(record))
                AssertWallClockPreserved(HeroParser.Excel.WriteFromDataReaderToBytes(drSync, opts), utcFractional, utcWhole, localFractional);

            await using var drAsync = new UtcFractionalDbDataReader(record);
            await using var asyncMs = new MemoryStream();
            await HeroParser.Excel.WriteFromDataReaderAsync(drAsync, asyncMs, opts, leaveOpen: true, cancellationToken: ct);
            AssertWallClockPreserved(asyncMs.ToArray(), utcFractional, utcWhole, localFractional);
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void CsvSchemaInference_WeekdayOrHourWithoutYear_PreservedAsString()
    {
        const string csv =
            "MonthDayHour,WeekdayMonthDay,WeekdayMonthDayHour,WeekdayFullDate,FullDateHour\n" +
            "\"November 27, 12 PM\",\"Friday, November 27\",\"Friday, November 27, 12 PM\",\"Friday, November 27, 2026\",\"November 27, 2026 12 PM\"\n";

        var defaultSchema = Csv.InferSchema(csv);
        Assert.Equal(CsvInferredType.String, defaultSchema.Columns[0].InferredType);
        Assert.Equal(CsvInferredType.String, defaultSchema.Columns[1].InferredType);
        Assert.Equal(CsvInferredType.String, defaultSchema.Columns[2].InferredType);
        Assert.Equal(CsvInferredType.DateTime, defaultSchema.Columns[3].InferredType);
        Assert.Equal(CsvInferredType.DateTime, defaultSchema.Columns[4].InferredType);

        var typedSchema = Csv.InferSchema(csv, new CsvSchemaInferenceOptions { DistinguishTemporalTypes = true });
        Assert.Equal(CsvInferredType.String, typedSchema.Columns[0].InferredType);
        Assert.Equal(CsvInferredType.String, typedSchema.Columns[1].InferredType);
        Assert.Equal(CsvInferredType.String, typedSchema.Columns[2].InferredType);
        Assert.Equal(CsvInferredType.DateOnly, typedSchema.Columns[3].InferredType);
        Assert.Equal(CsvInferredType.DateTime, typedSchema.Columns[4].InferredType);
    }

    private sealed class UtcFractionalDbDataReader(ExcelUtcFractionalDateRecord record) : DbDataReader
    {
        private int row = -1;
        public override int FieldCount => 3;
        public override bool HasRows => true;
        public override bool IsClosed => false;
        public override int Depth => 0;
        public override int RecordsAffected => -1;
        public override object this[int ordinal] => GetValue(ordinal);
        public override object this[string name] => GetValue(GetOrdinal(name));

        public override string GetName(int ordinal) => ordinal switch
        {
            0 => nameof(ExcelUtcFractionalDateRecord.UtcFractional),
            1 => nameof(ExcelUtcFractionalDateRecord.UtcWhole),
            _ => nameof(ExcelUtcFractionalDateRecord.LocalFractional)
        };

        public override int GetOrdinal(string name) => name switch
        {
            nameof(ExcelUtcFractionalDateRecord.UtcFractional) => 0,
            nameof(ExcelUtcFractionalDateRecord.UtcWhole) => 1,
            nameof(ExcelUtcFractionalDateRecord.LocalFractional) => 2,
            _ => -1
        };

        public override Type GetFieldType(int ordinal) => typeof(DateTime);
        public override string GetDataTypeName(int ordinal) => nameof(DateTime);
        public override bool Read() => ++row == 0;
        public override Task<bool> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Read());
        public override bool IsDBNull(int ordinal) => false;
        public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken) => Task.FromResult(false);

        public override DateTime GetDateTime(int ordinal) => ordinal switch
        {
            0 => record.UtcFractional,
            1 => record.UtcWhole,
            _ => record.LocalFractional
        };

        public override object GetValue(int ordinal) => GetDateTime(ordinal);
        public override int GetValues(object[] values)
        {
            for (int i = 0; i < Math.Min(values.Length, FieldCount); i++)
                values[i] = GetValue(i);
            return Math.Min(values.Length, FieldCount);
        }

        public override bool GetBoolean(int ordinal) => throw new NotSupportedException();
        public override byte GetByte(int ordinal) => throw new NotSupportedException();
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override char GetChar(int ordinal) => throw new NotSupportedException();
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override decimal GetDecimal(int ordinal) => throw new NotSupportedException();
        public override double GetDouble(int ordinal) => throw new NotSupportedException();
        public override float GetFloat(int ordinal) => throw new NotSupportedException();
        public override Guid GetGuid(int ordinal) => throw new NotSupportedException();
        public override short GetInt16(int ordinal) => throw new NotSupportedException();
        public override int GetInt32(int ordinal) => throw new NotSupportedException();
        public override long GetInt64(int ordinal) => throw new NotSupportedException();
        public override string GetString(int ordinal) => GetDateTime(ordinal).ToString("O");
        public override bool NextResult() => false;
        public override System.Collections.IEnumerator GetEnumerator() => throw new NotSupportedException();
    }
}
