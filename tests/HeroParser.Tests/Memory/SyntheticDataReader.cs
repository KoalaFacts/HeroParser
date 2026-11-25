using System.Collections;
using System.Data;
using System.Data.Common;
using System.Globalization;

namespace HeroParser.Tests.Memory;

/// <summary>
/// Zero-allocation synthetic <see cref="DbDataReader"/> covering every typed column kind supported by
/// <c>DataReaderColumnPlan</c> without allocating per-row strings, arrays, or boxed objects.
/// Tracks calls to <see cref="GetValue"/> / <see cref="GetValues"/> so tests prove boxing is never used.
/// </summary>
internal sealed class SyntheticDataReader(int initialRows, bool includeNulls = false) : DbDataReader
{
    private static readonly Task<bool> trueTask = Task.FromResult(true);
    private static readonly Task<bool> falseTask = Task.FromResult(false);

    private static readonly string[] columnNames =
    [
        "Id",
        "BigId",
        "SmallId",
        "Tag",
        "Ratio",
        "Metric",
        "Amount",
        "Active",
        "CreatedAt",
        "Token",
        "Code",
        "Label"
    ];

    private static readonly Type[] columnTypes =
    [
        typeof(int),
        typeof(long),
        typeof(short),
        typeof(byte),
        typeof(float),
        typeof(double),
        typeof(decimal),
        typeof(bool),
        typeof(DateTime),
        typeof(Guid),
        typeof(char),
        typeof(string)
    ];

    private static readonly string[] typeNames =
    [
        nameof(Int32),
        nameof(Int64),
        nameof(Int16),
        nameof(Byte),
        nameof(Single),
        nameof(Double),
        nameof(Decimal),
        nameof(Boolean),
        nameof(DateTime),
        nameof(Guid),
        nameof(Char),
        nameof(String)
    ];

    private static readonly string[] preAllocatedLabels =
        [.. Enumerable.Range(0, 16).Select(i => $"item-{i:D2}")];

    private static readonly Guid baseGuid = new("11111111-2222-3333-4444-555555555555");
    private static readonly DateTime baseTimestamp = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);

    private int rowCount = initialRows;
    private int rowIndex = -1;
    private bool closed;

    public int GetValueCallCount { get; private set; }
    public int BoxedValueTypeGetValueCallCount { get; private set; }
    public int GetValuesCallCount { get; private set; }

    public void Reset(int newRowCount)
    {
        rowCount = newRowCount;
        rowIndex = -1;
        closed = false;
        GetValueCallCount = 0;
        BoxedValueTypeGetValueCallCount = 0;
        GetValuesCallCount = 0;
    }

    public override int Depth => 0;
    public override int FieldCount => columnNames.Length;
    public override bool HasRows => !closed && rowCount > 0;
    public override bool IsClosed => closed;
    public override int RecordsAffected => -1;
    public override object this[int ordinal] => GetValue(ordinal);
    public override object this[string name] => GetValue(GetOrdinal(name));

    public override void Close() => closed = true;
    public override bool NextResult() => false;

    public override bool Read()
    {
        if (closed)
            return false;
        int next = rowIndex + 1;
        if (next >= rowCount)
            return false;
        rowIndex = next;
        return true;
    }

    public override Task<bool> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Read() ? trueTask : falseTask;
    }

    public override bool IsDBNull(int ordinal)
        => includeNulls && (rowIndex % 5 == 4) && ordinal is 1 or 5 or 6 or 11;

    public override Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return IsDBNull(ordinal) ? trueTask : falseTask;
    }

    public override string GetName(int ordinal) => columnNames[ordinal];
    public override Type GetFieldType(int ordinal) => columnTypes[ordinal];
    public override string GetDataTypeName(int ordinal) => typeNames[ordinal];

    public override int GetOrdinal(string name)
    {
        for (int i = 0; i < columnNames.Length; i++)
        {
            if (string.Equals(columnNames[i], name, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        throw new IndexOutOfRangeException($"Unknown column '{name}'.");
    }

    public override int GetInt32(int ordinal) => ordinal == 0 ? rowIndex : 0;
    public override long GetInt64(int ordinal) => 10_000_000_000L + rowIndex;
    public override short GetInt16(int ordinal) => unchecked((short)(rowIndex & 0x7FFF));
    public override byte GetByte(int ordinal) => unchecked((byte)rowIndex);
    public override float GetFloat(int ordinal) => 1.5f + (rowIndex & 31);
    public override double GetDouble(int ordinal) => 2.25 + (rowIndex & 63);
    public override decimal GetDecimal(int ordinal) => 100.25m + (rowIndex & 255);
    public override bool GetBoolean(int ordinal) => (rowIndex & 1) == 0;
    public override DateTime GetDateTime(int ordinal) => baseTimestamp.AddSeconds(rowIndex);
    public override Guid GetGuid(int ordinal) => baseGuid;
    public override char GetChar(int ordinal) => (char)('A' + (rowIndex % 26));
    public override string GetString(int ordinal) => preAllocatedLabels[rowIndex & 15];

    public override object GetValue(int ordinal)
    {
        GetValueCallCount++;
        if ((uint)ordinal < (uint)columnTypes.Length && columnTypes[ordinal].IsValueType)
            BoxedValueTypeGetValueCallCount++;
        if (IsDBNull(ordinal))
            return DBNull.Value;

        return ordinal switch
        {
            0 => GetInt32(ordinal),
            1 => GetInt64(ordinal),
            2 => GetInt16(ordinal),
            3 => GetByte(ordinal),
            4 => GetFloat(ordinal),
            5 => GetDouble(ordinal),
            6 => GetDecimal(ordinal),
            7 => GetBoolean(ordinal),
            8 => GetDateTime(ordinal),
            9 => GetGuid(ordinal),
            10 => GetChar(ordinal),
            11 => GetString(ordinal),
            _ => throw new IndexOutOfRangeException(ordinal.ToString(CultureInfo.InvariantCulture))
        };
    }

    public override int GetValues(object[] values)
    {
        GetValuesCallCount++;
        int count = Math.Min(values.Length, FieldCount);
        for (int i = 0; i < count; i++)
            values[i] = GetValue(i);
        return count;
    }

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => 0;
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => 0;
    public override IEnumerator GetEnumerator() => new DbEnumerator(this, closeReader: false);
}
