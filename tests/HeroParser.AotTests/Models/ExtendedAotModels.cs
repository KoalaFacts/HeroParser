using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;

namespace HeroParser.AotTests.Models;

public readonly record struct CustomAotToken(int Value) : ISpanParsable<CustomAotToken>, ISpanFormattable
{
    public static CustomAotToken Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => TryParse(s, provider, out var result) ? result : throw new FormatException("Invalid token.");

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out CustomAotToken result)
    {
        if (s.StartsWith("aot:", StringComparison.Ordinal) &&
            int.TryParse(s[4..], NumberStyles.Integer, provider ?? CultureInfo.InvariantCulture, out int num))
        {
            result = new CustomAotToken(num);
            return true;
        }
        result = default;
        return false;
    }

    public static CustomAotToken Parse(string s, IFormatProvider? provider) => Parse(s.AsSpan(), provider);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out CustomAotToken result)
    {
        if (s is null)
        {
            result = default;
            return false;
        }
        return TryParse(s.AsSpan(), provider, out result);
    }

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        if (!"aot:".AsSpan().TryCopyTo(destination))
        {
            charsWritten = 0;
            return false;
        }
        if (Value.TryFormat(destination[4..], out int written, format, provider ?? CultureInfo.InvariantCulture))
        {
            charsWritten = 4 + written;
            return true;
        }
        charsWritten = 0;
        return false;
    }

    public string ToString(string? format, IFormatProvider? formatProvider)
        => $"aot:{Value.ToString(format, formatProvider ?? CultureInfo.InvariantCulture)}";

    public override string ToString() => ToString(null, CultureInfo.InvariantCulture);
}

[GenerateBinder]
public sealed class ExtendedTabularAotRecord
{
    [PositionalMap(Start = 0, Length = 34)]
    [Format(WriteFormat = "O")]
    [Parse(Format = "O")]
    public DateTimeOffset OffsetVal { get; set; }

    [PositionalMap(Start = 34, Length = 10)]
    [Format(WriteFormat = "yyyy-MM-dd")]
    [Parse(Format = "yyyy-MM-dd")]
    public DateOnly DateVal { get; set; }

    [PositionalMap(Start = 44, Length = 12)]
    [Format(WriteFormat = "HH:mm:ss.fff")]
    [Parse(Format = "HH:mm:ss.fff")]
    public TimeOnly TimeVal { get; set; }

    [PositionalMap(Start = 56, Length = 20)]
    public TimeSpan Duration { get; set; }

    [PositionalMap(Start = 76, Length = 10)]
    public uint UIntVal { get; set; }

    [PositionalMap(Start = 86, Length = 20)]
    public ulong ULongVal { get; set; }

    [PositionalMap(Start = 106, Length = 6)]
    public ushort UShortVal { get; set; }

    [PositionalMap(Start = 112, Length = 5)]
    public sbyte SByteVal { get; set; }

    [PositionalMap(Start = 117, Length = 1)]
    public char CharVal { get; set; }

    [PositionalMap(Start = 118, Length = 12)]
    public Half HalfVal { get; set; }

    [PositionalMap(Start = 130, Length = 42)]
    public Int128 Int128Val { get; set; }

    [PositionalMap(Start = 172, Length = 42)]
    public UInt128 UInt128Val { get; set; }

    [PositionalMap(Start = 214, Length = 50)]
    public BigInteger BigIntVal { get; set; }

    [PositionalMap(Start = 264, Length = 30)]
    public Uri UriVal { get; set; } = new("https://example.invalid");

    [PositionalMap(Start = 294, Length = 16)]
    public byte[] BytesVal { get; set; } = [];

    [PositionalMap(Start = 310, Length = 12)]
    public CustomAotToken Token { get; set; }
}

[GenerateBinder]
public sealed class ExtendedHtbAotRecord
{
    public DateTimeOffset OffsetVal { get; set; }
    public DateOnly DateVal { get; set; }
    public TimeOnly TimeVal { get; set; }
    public TimeSpan Duration { get; set; }
    public short ShortVal { get; set; }
    public ushort UShortVal { get; set; }
    public byte ByteVal { get; set; }
    public sbyte SByteVal { get; set; }
    public uint UIntVal { get; set; }
    public ulong ULongVal { get; set; }
    public char CharVal { get; set; }
    public Half HalfVal { get; set; }
    public Int128 Int128Val { get; set; }
    public UInt128 UInt128Val { get; set; }
    public byte[] BytesVal { get; set; } = [];
    public BigInteger BigIntVal { get; set; }
    public Uri UriVal { get; set; } = new("https://example.invalid");
}
