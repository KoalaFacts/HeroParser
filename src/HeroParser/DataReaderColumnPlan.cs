using System.Data;

namespace HeroParser.Internal;

internal enum DataReaderColumnKind : byte
{
    Object = 0,
    String = 1,
    Int32 = 2,
    Int64 = 3,
    Int16 = 4,
    Byte = 5,
    Single = 6,
    Double = 7,
    Decimal = 8,
    Boolean = 9,
    DateTime = 10,
    Guid = 11,
    Char = 12
}

/// <summary>
/// Caches resolved <see cref="IDataRecord"/> column types once per reader export so rows can dispatch
/// typed getters without boxing value types or allocating per-row <c>object[]</c> arrays.
/// </summary>
internal readonly struct DataReaderColumnPlan
{
    private readonly DataReaderColumnKind[] kinds;

    public DataReaderColumnPlan(IDataReader reader)
    {
        int count = reader.FieldCount;
        if (count <= 0)
        {
            kinds = [];
            return;
        }

        kinds = new DataReaderColumnKind[count];
        for (int i = 0; i < count; i++)
        {
            kinds[i] = ResolveKind(reader.GetFieldType(i));
        }
    }

    public bool IsInitialized => kinds is not null;

    public int Length => kinds?.Length ?? 0;

    public DataReaderColumnKind this[int ordinal] => kinds[ordinal];

    private static DataReaderColumnKind ResolveKind(Type? fieldType)
    {
        if (fieldType == typeof(string))
            return DataReaderColumnKind.String;
        if (fieldType == typeof(int))
            return DataReaderColumnKind.Int32;
        if (fieldType == typeof(long))
            return DataReaderColumnKind.Int64;
        if (fieldType == typeof(short))
            return DataReaderColumnKind.Int16;
        if (fieldType == typeof(byte))
            return DataReaderColumnKind.Byte;
        if (fieldType == typeof(float))
            return DataReaderColumnKind.Single;
        if (fieldType == typeof(double))
            return DataReaderColumnKind.Double;
        if (fieldType == typeof(decimal))
            return DataReaderColumnKind.Decimal;
        if (fieldType == typeof(bool))
            return DataReaderColumnKind.Boolean;
        if (fieldType == typeof(DateTime))
            return DataReaderColumnKind.DateTime;
        if (fieldType == typeof(Guid))
            return DataReaderColumnKind.Guid;
        if (fieldType == typeof(char))
            return DataReaderColumnKind.Char;
        return DataReaderColumnKind.Object;
    }
}
