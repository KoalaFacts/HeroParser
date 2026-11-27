using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HeroParser.Conversion;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Mapping;
using HeroParser.Htbs;
using HeroParser.Htbs.Reading;
using HeroParser.Htbs.Records;
using HeroParser.JsonLines;
using HeroParser.SeparatedValues;
using HeroParser.SeparatedValues.Detection;
using HeroParser.SeparatedValues.Mapping;
using HeroParser.SeparatedValues.Reading.Records;
using HeroParser.SeparatedValues.Writing;
using HeroParser.Validation;
using Xunit;

namespace HeroParser.Tests;

public readonly record struct CustomImplicitToken(int Number) : ISpanParsable<CustomImplicitToken>, ISpanFormattable
{
    public static CustomImplicitToken Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => TryParse(s, provider, out var result) ? result : throw new FormatException("Invalid token.");

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out CustomImplicitToken result)
    {
        if (s.StartsWith("tok:", StringComparison.Ordinal) && int.TryParse(s[4..], NumberStyles.Integer, provider ?? CultureInfo.InvariantCulture, out int num))
        {
            result = new CustomImplicitToken(num);
            return true;
        }
        result = default;
        return false;
    }

    public static CustomImplicitToken Parse(string s, IFormatProvider? provider)
        => Parse(s.AsSpan(), provider);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out CustomImplicitToken result)
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
        if (!"tok:".AsSpan().TryCopyTo(destination))
        {
            charsWritten = 0;
            return false;
        }
        if (Number.TryFormat(destination[4..], out int numWritten, format, provider ?? CultureInfo.InvariantCulture))
        {
            charsWritten = 4 + numWritten;
            return true;
        }
        charsWritten = 0;
        return false;
    }

    public string ToString(string? format, IFormatProvider? formatProvider)
        => $"tok:{Number.ToString(format, formatProvider ?? CultureInfo.InvariantCulture)}";

    public override string ToString() => ToString(null, CultureInfo.InvariantCulture);
}

public readonly struct CustomExplicitTag(string value) : ISpanParsable<CustomExplicitTag>, IFormattable
{
    public string Value { get; } = value;

    static CustomExplicitTag IParsable<CustomExplicitTag>.Parse(string s, IFormatProvider? provider)
        => TryParseCore(s.AsSpan(), out var result) ? result : throw new FormatException("Invalid explicit tag.");

    static bool IParsable<CustomExplicitTag>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out CustomExplicitTag result)
    {
        if (s is null)
        {
            result = default;
            return false;
        }
        return TryParseCore(s.AsSpan(), out result);
    }

    static CustomExplicitTag ISpanParsable<CustomExplicitTag>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => TryParseCore(s, out var result) ? result : throw new FormatException("Invalid explicit tag.");

    static bool ISpanParsable<CustomExplicitTag>.TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out CustomExplicitTag result)
        => TryParseCore(s, out result);

    private static bool TryParseCore(ReadOnlySpan<char> s, out CustomExplicitTag result)
    {
        if (s.StartsWith("tag:", StringComparison.Ordinal) && s.Length > 4)
        {
            result = new CustomExplicitTag(new string(s[4..]));
            return true;
        }
        result = default;
        return false;
    }

    string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => $"tag:{Value}";

    public override string ToString() => $"tag:{Value}";
}

public readonly record struct CustomUtf8OnlyCode(int Code) : IUtf8SpanParsable<CustomUtf8OnlyCode>, ISpanFormattable
{
    public static CustomUtf8OnlyCode Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider)
        => TryParse(utf8Text, provider, out var result) ? result : throw new FormatException("Invalid UTF-8 code.");

    public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, out CustomUtf8OnlyCode result)
    {
        if (utf8Text.StartsWith("u8:"u8) &&
            int.TryParse(utf8Text[3..], NumberStyles.Integer, provider ?? CultureInfo.InvariantCulture, out int num))
        {
            result = new CustomUtf8OnlyCode(num);
            return true;
        }
        result = default;
        return false;
    }

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        if (!"u8:".AsSpan().TryCopyTo(destination))
        {
            charsWritten = 0;
            return false;
        }
        if (Code.TryFormat(destination[3..], out int numWritten, format, provider ?? CultureInfo.InvariantCulture))
        {
            charsWritten = 3 + numWritten;
            return true;
        }
        charsWritten = 0;
        return false;
    }

    public string ToString(string? format, IFormatProvider? formatProvider)
        => $"u8:{Code.ToString(format, formatProvider ?? CultureInfo.InvariantCulture)}";

    public override string ToString() => ToString(null, CultureInfo.InvariantCulture);
}

public sealed class CustomReferenceLabel : ISpanParsable<CustomReferenceLabel>, ISpanFormattable
{
    public string Text { get; init; } = "";

    public static CustomReferenceLabel Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => TryParse(s, provider, out var result) ? result : throw new FormatException("Invalid label.");

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, [MaybeNullWhen(false)] out CustomReferenceLabel result)
    {
        if (s.StartsWith("lbl:", StringComparison.Ordinal) && s.Length > 4)
        {
            result = new CustomReferenceLabel { Text = new string(s[4..]) };
            return true;
        }
        result = null;
        return false;
    }

    public static CustomReferenceLabel Parse(string s, IFormatProvider? provider) => Parse(s.AsSpan(), provider);

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out CustomReferenceLabel result)
    {
        if (s is null)
        {
            result = null;
            return false;
        }
        return TryParse(s.AsSpan(), provider, out result);
    }

    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        int total = 4 + Text.Length;
        if (destination.Length < total)
        {
            charsWritten = 0;
            return false;
        }
        "lbl:".AsSpan().CopyTo(destination);
        Text.AsSpan().CopyTo(destination[4..]);
        charsWritten = total;
        return true;
    }

    public string ToString(string? format, IFormatProvider? formatProvider) => $"lbl:{Text}";

    public override string ToString() => ToString(null, CultureInfo.InvariantCulture);
}

[GenerateBinder]
public sealed class CsvExtendedRecord
{
    public DateTimeOffset OffsetVal { get; set; }
    public DateOnly DateVal { get; set; }
    [Format(WriteFormat = "HH:mm:ss.fff")]
    [Parse(Format = "HH:mm:ss.fff")]
    public TimeOnly TimeVal { get; set; }
    public TimeSpan DurationVal { get; set; }
    public uint UIntVal { get; set; }
    public ulong ULongVal { get; set; }
    public ushort UShortVal { get; set; }
    public sbyte SByteVal { get; set; }
    public char CharVal { get; set; }
    public Half HalfVal { get; set; }
    public Int128 Int128Val { get; set; }
    public UInt128 UInt128Val { get; set; }
    public BigInteger BigIntVal { get; set; }
    public Uri UriVal { get; set; } = new("https://example.invalid");
    public byte[] BytesVal { get; set; } = [];
    public CustomImplicitToken Token { get; set; }
    public CustomExplicitTag ExplicitTag { get; set; }
    public CustomUtf8OnlyCode Utf8Code { get; set; }
    public CustomReferenceLabel Label { get; set; } = new();
}

[GenerateBinder]
public sealed class CsvExtendedNullableRecord
{
    public TimeSpan? Duration { get; set; }
    public char? Symbol { get; set; }
    public Half? Ratio { get; set; }
    public Int128? BigSigned { get; set; }
    public UInt128? BigUnsigned { get; set; }
    public BigInteger? Arbitrary { get; set; }
    public Uri? Link { get; set; }
    public byte[]? Payload { get; set; }
    public CustomImplicitToken? Token { get; set; }
    public CustomReferenceLabel? Label { get; set; }
}

[GenerateBinder]
public sealed class FixedWidthExtendedRecord
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
    public TimeSpan DurationVal { get; set; }

    [PositionalMap(Start = 76, Length = 10, Alignment = FieldAlignment.Right)]
    public uint UIntVal { get; set; }

    [PositionalMap(Start = 86, Length = 20, Alignment = FieldAlignment.Right)]
    public ulong ULongVal { get; set; }

    [PositionalMap(Start = 106, Length = 6, Alignment = FieldAlignment.Right)]
    public ushort UShortVal { get; set; }

    [PositionalMap(Start = 112, Length = 5, Alignment = FieldAlignment.Right)]
    public sbyte SByteVal { get; set; }

    [PositionalMap(Start = 117, Length = 1)]
    public char CharVal { get; set; }

    [PositionalMap(Start = 118, Length = 12)]
    public Half HalfVal { get; set; }

    [PositionalMap(Start = 130, Length = 42, Alignment = FieldAlignment.Right)]
    public Int128 Int128Val { get; set; }

    [PositionalMap(Start = 172, Length = 42, Alignment = FieldAlignment.Right)]
    public UInt128 UInt128Val { get; set; }

    [PositionalMap(Start = 214, Length = 60, Alignment = FieldAlignment.Right)]
    public BigInteger BigIntVal { get; set; }

    [PositionalMap(Start = 274, Length = 36)]
    public Uri UriVal { get; set; } = new("https://example.invalid");

    [PositionalMap(Start = 310, Length = 20)]
    public byte[] BytesVal { get; set; } = [];

    [PositionalMap(Start = 330, Length = 16)]
    public CustomImplicitToken Token { get; set; }

    [PositionalMap(Start = 346, Length = 16)]
    public CustomExplicitTag ExplicitTag { get; set; }

    [PositionalMap(Start = 362, Length = 16)]
    public CustomUtf8OnlyCode Utf8Code { get; set; }

    [PositionalMap(Start = 378, Length = 16)]
    public CustomReferenceLabel Label { get; set; } = new();
}

[GenerateBinder]
public sealed class FixedWidthExtendedNullableRecord
{
    [PositionalMap(Start = 0, Length = 20)]
    public TimeSpan? Duration { get; set; }

    [PositionalMap(Start = 20, Length = 1)]
    public char? Symbol { get; set; }

    [PositionalMap(Start = 21, Length = 12)]
    public Half? Ratio { get; set; }

    [PositionalMap(Start = 33, Length = 42)]
    public Int128? BigSigned { get; set; }

    [PositionalMap(Start = 75, Length = 30)]
    public Uri? Link { get; set; }

    [PositionalMap(Start = 105, Length = 16)]
    public byte[]? Payload { get; set; }

    [PositionalMap(Start = 121, Length = 16)]
    public CustomReferenceLabel? Label { get; set; }
}

public sealed class FixedWidthReflectionRecord
{
    [PositionalMap(Start = 0, Length = 20)]
    public TimeSpan Duration { get; set; }

    [PositionalMap(Start = 20, Length = 1)]
    public char Symbol { get; set; }

    [PositionalMap(Start = 21, Length = 12)]
    public Half Ratio { get; set; }

    [PositionalMap(Start = 33, Length = 42)]
    public Int128 BigSigned { get; set; }

    [PositionalMap(Start = 75, Length = 42)]
    public UInt128 BigUnsigned { get; set; }

    [PositionalMap(Start = 117, Length = 50)]
    public BigInteger Arbitrary { get; set; }

    [PositionalMap(Start = 167, Length = 32)]
    public Uri Link { get; set; } = new("https://example.invalid");

    [PositionalMap(Start = 199, Length = 16)]
    public byte[] Payload { get; set; } = [];

    [PositionalMap(Start = 215, Length = 16)]
    public CustomImplicitToken Token { get; set; }
}

public sealed class FluentExtendedRecord
{
    public DateTimeOffset OffsetVal { get; set; }
    public uint UIntVal { get; set; }
    public ulong ULongVal { get; set; }
    public ushort UShortVal { get; set; }
    public sbyte SByteVal { get; set; }
    public TimeSpan Duration { get; set; }
    public char Symbol { get; set; }
    public Half Ratio { get; set; }
    public Int128 BigSigned { get; set; }
    public UInt128 BigUnsigned { get; set; }
    public BigInteger Arbitrary { get; set; }
    public Uri? Link { get; set; }
    public byte[]? Payload { get; set; }
    public CustomImplicitToken Token { get; set; }
    public CustomUtf8OnlyCode Utf8Code { get; set; }
    public CustomReferenceLabel? Label { get; set; }
}

[GenerateBinder]
public sealed class JsonlExtendedRecord
{
    public DateTimeOffset OffsetVal { get; set; }
    public DateOnly DateVal { get; set; }
    public TimeOnly TimeVal { get; set; }
    public TimeSpan DurationVal { get; set; }
    public uint UIntVal { get; set; }
    public ulong ULongVal { get; set; }
    public ushort UShortVal { get; set; }
    public sbyte SByteVal { get; set; }
    public char CharVal { get; set; }
    public Half HalfVal { get; set; }
    public Int128 Int128Val { get; set; }
    public UInt128 UInt128Val { get; set; }
    public BigInteger BigIntVal { get; set; }
    public Uri UriVal { get; set; } = new("https://example.invalid");
    public byte[] BytesVal { get; set; } = [];
    public CustomImplicitToken Token { get; set; }
    public CustomExplicitTag ExplicitTag { get; set; }
    public CustomUtf8OnlyCode Utf8Code { get; set; }
    public CustomReferenceLabel? Label { get; set; }
}

[GenerateBinder]
public sealed class HtbExtendedRecord
{
    public DateTimeOffset OffsetVal { get; set; }
    public DateOnly DateVal { get; set; }
    public TimeOnly TimeVal { get; set; }
    public TimeSpan DurationVal { get; set; }
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

[GenerateBinder]
public sealed class HtbExtendedNullableRecord
{
    public DateTimeOffset? OffsetVal { get; set; }
    public DateOnly? DateVal { get; set; }
    public TimeOnly? TimeVal { get; set; }
    public TimeSpan? DurationVal { get; set; }
    public Half? HalfVal { get; set; }
    public Int128? Int128Val { get; set; }
    public UInt128? UInt128Val { get; set; }
    public byte[]? BytesVal { get; set; }
    public BigInteger? BigIntVal { get; set; }
    public Uri? UriVal { get; set; }
}

public sealed class HtbReflectionExtendedRecord
{
    public DateTimeOffset OffsetVal { get; set; }
    public DateOnly DateVal { get; set; }
    public TimeOnly TimeVal { get; set; }
    public TimeSpan DurationVal { get; set; }
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

[GenerateBinder]
public sealed class ExtendedRangeValidatedRecord
{
    [PositionalMap(Start = 0, Length = 8)]
    [Validate(RangeMin = 1, RangeMax = 100)]
    public Half Ratio { get; set; }

    [PositionalMap(Start = 8, Length = 10)]
    [Validate(RangeMin = 1, RangeMax = 1000)]
    public Int128 BigSigned { get; set; }

    [PositionalMap(Start = 18, Length = 10)]
    [Validate(RangeMin = 1, RangeMax = 1000)]
    public UInt128 BigUnsigned { get; set; }

    [PositionalMap(Start = 28, Length = 10)]
    [Validate(RangeMin = 1, RangeMax = 1000)]
    public BigInteger Arbitrary { get; set; }
}

[GenerateBinder]
public sealed class FractionalExtendedRangeRecord
{
    [PositionalMap(Start = 0, Length = 6)]
    [Validate(RangeMin = 0.5)]
    public Int128 PosInt128 { get; set; }

    [PositionalMap(Start = 6, Length = 6)]
    [Validate(RangeMin = 0.5)]
    public UInt128 PosUInt128 { get; set; }

    [PositionalMap(Start = 12, Length = 6)]
    [Validate(RangeMin = 0.5)]
    public BigInteger PosBigInt { get; set; }

    [PositionalMap(Start = 18, Length = 6)]
    [Validate(RangeMax = -0.5)]
    public Int128 NegInt128 { get; set; }
}

[GenerateBinder]
public sealed class NegativeUInt128RangeRecord
{
    [PositionalMap(Start = 0, Length = 6)]
    [Validate(RangeMax = -0.5)]
    public UInt128 Value { get; set; }
}

[GenerateBinder]
public sealed class OutOfBoundsExtendedRangeRecord
{
    [PositionalMap(Start = 0, Length = 6)]
    [Validate(RangeMin = -5.5)]
    public UInt128 LowerOutOfBoundsUInt128 { get; set; }

    [PositionalMap(Start = 6, Length = 6)]
    [Validate(RangeMin = 1e50)]
    public Int128 ImpossibleInt128 { get; set; }
}

[GenerateBinder]
public sealed class TimeSpanFormatCsvRecord
{
    public TimeSpan Duration { get; set; }
}

[GenerateBinder]
public sealed class TimeSpanFormatFixedWidthRecord
{
    [PositionalMap(Start = 0, Length = 20)]
    public TimeSpan Duration { get; set; }
}

[GenerateBinder]
public sealed class JsonlBigIntegerGeneratedRecord
{
    public BigInteger Value { get; set; }
    public BigInteger? OptionalValue { get; set; }
    public string Label { get; set; } = string.Empty;
}

public sealed class JsonlBigIntegerReflectionRecord
{
    public BigInteger Value { get; set; }
    public BigInteger? OptionalValue { get; set; }
    public string Label { get; set; } = string.Empty;
}

[GenerateBinder]
public sealed class WideIntegerRangeRecord
{
    [PositionalMap(Start = 0, Length = 22)]
    [Validate(RangeMax = 9007199254740992d)]
    public Int128 BigSigned { get; set; }

    [PositionalMap(Start = 22, Length = 22)]
    [Validate(RangeMax = 9007199254740992d)]
    public UInt128 BigUnsigned { get; set; }

    [PositionalMap(Start = 44, Length = 22)]
    [Validate(RangeMax = 9007199254740992d)]
    public BigInteger Arbitrary { get; set; }

    [PositionalMap(Start = 66, Length = 22)]
    [Validate(RangeMax = 9007199254740992d)]
    public long WideLong { get; set; }
}

public sealed class WideIntegerRangeReflectionRecord
{
    [Validate(RangeMax = 9007199254740992d)]
    public Int128 BigSigned { get; set; }

    [Validate(RangeMax = 9007199254740992d)]
    public UInt128 BigUnsigned { get; set; }

    [Validate(RangeMax = 9007199254740992d)]
    public BigInteger Arbitrary { get; set; }

    [Validate(RangeMax = 9007199254740992d)]
    public long WideLong { get; set; }
}

[GenerateBinder]
public sealed class BidiCultureSignedGeneratedRecord
{
    [PositionalMap(Start = 0, Length = 6)]
    public Half HalfVal { get; set; }

    [PositionalMap(Start = 6, Length = 6)]
    public Int128 Int128Val { get; set; }

    [PositionalMap(Start = 12, Length = 6)]
    public BigInteger BigIntVal { get; set; }

    [PositionalMap(Start = 18, Length = 6)]
    public sbyte SByteVal { get; set; }
}

public sealed class BidiCultureSignedReflectionRecord
{
    [PositionalMap(Start = 0, Length = 6)]
    public Half HalfVal { get; set; }

    [PositionalMap(Start = 6, Length = 6)]
    public Int128 Int128Val { get; set; }

    [PositionalMap(Start = 12, Length = 6)]
    public BigInteger BigIntVal { get; set; }

    [PositionalMap(Start = 18, Length = 6)]
    public sbyte SByteVal { get; set; }
}

[GenerateBinder]
public sealed class DecimalExactBoundGeneratedRecord
{
    [TabularMap(Name = "Rate")]
    [PositionalMap(Start = 0, Length = 8)]
    [Validate(RangeMin = 0.3, RangeMax = 0.3)]
    public decimal Rate { get; set; }
}

[GenerateBinder]
public sealed class ExcelByteArrayGeneratedRecord
{
    public byte[] RequiredBytes { get; set; } = [];
    public byte[]? OptionalBytes { get; set; }
}

public sealed class ExcelByteArrayReflectionRecord
{
    public byte[] RequiredBytes { get; set; } = [];
    public byte[]? OptionalBytes { get; set; }
}

[GenerateBinder]
public sealed class FixedWidthByteArrayGeneratedRecord
{
    [PositionalMap(Start = 0, Length = 12)]
    public byte[] RequiredBytes { get; set; } = [];

    [PositionalMap(Start = 12, Length = 12)]
    public byte[]? OptionalBytes { get; set; }
}

public sealed class FixedWidthByteArrayReflectionRecord
{
    [PositionalMap(Start = 0, Length = 12)]
    public byte[] RequiredBytes { get; set; } = [];

    [PositionalMap(Start = 12, Length = 12)]
    public byte[]? OptionalBytes { get; set; }
}

[GenerateBinder]
public sealed class JsonlBigIntegerAotRecord
{
    public BigInteger RequiredBigInt { get; set; }
    public BigInteger? OptionalBigInt { get; set; }
}

[JsonSourceGenerationOptions(Converters = [typeof(BigIntegerJsonConverter)])]
[JsonSerializable(typeof(JsonlBigIntegerAotRecord))]
internal sealed partial class JsonlBigIntegerAotContext : JsonSerializerContext;

public sealed class DecimalExactBoundReflectionRecord
{
    [TabularMap(Name = "Rate")]
    [PositionalMap(Start = 0, Length = 8)]
    [Validate(RangeMin = 0.3, RangeMax = 0.3)]
    public decimal Rate { get; set; }
}

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class ExtendedTypesTests
{
    private static CsvExtendedRecord CreateSampleCsvRecord() => new()
    {
        OffsetVal = new DateTimeOffset(2026, 11, 23, 14, 30, 45, TimeSpan.FromHours(11)),
        DateVal = new DateOnly(2026, 11, 23),
        TimeVal = new TimeOnly(14, 30, 45, 123),
        DurationVal = new TimeSpan(2, 3, 4, 5, 250),
        UIntVal = 4_000_000_001u,
        ULongVal = 18_000_000_000_000_000_001ul,
        UShortVal = 65_001,
        SByteVal = -120,
        CharVal = 'Z',
        HalfVal = (Half)12.5,
        Int128Val = Int128.Parse("-170141183460469231731687303715884105720", CultureInfo.InvariantCulture),
        UInt128Val = UInt128.Parse("340282366920938463463374607431768211400", CultureInfo.InvariantCulture),
        BigIntVal = BigInteger.Parse("12345678901234567890123456789012345678901234567890", CultureInfo.InvariantCulture),
        UriVal = new Uri("https://example.com/items?id=42#top"),
        BytesVal = [1, 2, 3, 4, 250, 255],
        Token = new CustomImplicitToken(77),
        ExplicitTag = new CustomExplicitTag("alpha"),
        Utf8Code = new CustomUtf8OnlyCode(99),
        Label = new CustomReferenceLabel { Text = "hero" }
    };

    private static void AssertCsvExtendedEqual(CsvExtendedRecord expected, CsvExtendedRecord actual)
    {
        Assert.Equal(expected.OffsetVal, actual.OffsetVal);
        Assert.Equal(expected.DateVal, actual.DateVal);
        Assert.Equal(expected.TimeVal, actual.TimeVal);
        Assert.Equal(expected.DurationVal, actual.DurationVal);
        Assert.Equal(expected.UIntVal, actual.UIntVal);
        Assert.Equal(expected.ULongVal, actual.ULongVal);
        Assert.Equal(expected.UShortVal, actual.UShortVal);
        Assert.Equal(expected.SByteVal, actual.SByteVal);
        Assert.Equal(expected.CharVal, actual.CharVal);
        Assert.Equal(expected.HalfVal, actual.HalfVal);
        Assert.Equal(expected.Int128Val, actual.Int128Val);
        Assert.Equal(expected.UInt128Val, actual.UInt128Val);
        Assert.Equal(expected.BigIntVal, actual.BigIntVal);
        Assert.Equal(expected.UriVal, actual.UriVal);
        Assert.Equal(expected.BytesVal, actual.BytesVal);
        Assert.Equal(expected.Token, actual.Token);
        Assert.Equal(expected.ExplicitTag.Value, actual.ExplicitTag.Value);
        Assert.Equal(expected.Utf8Code, actual.Utf8Code);
        Assert.Equal(expected.Label.Text, actual.Label.Text);
    }

    [Fact]
    public void Csv_GeneratedBinders_CharAndUtf8_RoundTripAllExtendedTypes()
    {
        CsvExtendedRecord[] original = [CreateSampleCsvRecord()];
        string csv = Csv.WriteToText(original);

        var fromChar = Csv.DeserializeRecords<CsvExtendedRecord>(csv).ToList();
        Assert.Single(fromChar);
        AssertCsvExtendedEqual(original[0], fromChar[0]);

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var fromByte = new List<CsvExtendedRecord>();
        using (var byteReader = Csv.Read<CsvExtendedRecord>().FromStream(ms, out _))
        {
            while (byteReader.MoveNext())
            {
                fromByte.Add(byteReader.Current);
            }
            byteReader.ThrowOnStrictErrors();
        }
        Assert.Single(fromByte);
        AssertCsvExtendedEqual(original[0], fromByte[0]);
    }

    [Fact]
    public void Csv_NullableExtendedTypes_RoundTripNullAndPopulated()
    {
        CsvExtendedNullableRecord[] original =
        [
            new CsvExtendedNullableRecord
            {
                Duration = TimeSpan.FromMinutes(90),
                Symbol = 'é',
                Ratio = (Half)3.25,
                BigSigned = -42,
                BigUnsigned = 84,
                Arbitrary = BigInteger.Pow(10, 45),
                Link = new Uri("/relative/path", UriKind.Relative),
                Payload = [9, 8, 7],
                Token = new CustomImplicitToken(12),
                Label = new CustomReferenceLabel { Text = "present" }
            },
            new CsvExtendedNullableRecord()
        ];

        string csv = Csv.WriteToText(original);
        var fromChar = Csv.DeserializeRecords<CsvExtendedNullableRecord>(csv).ToList();
        Assert.Equal(2, fromChar.Count);
        Assert.Equal(TimeSpan.FromMinutes(90), fromChar[0].Duration);
        Assert.Equal('é', fromChar[0].Symbol);
        Assert.Equal((Half)3.25, fromChar[0].Ratio);
        Assert.Equal((Int128)(-42), fromChar[0].BigSigned);
        Assert.Equal((UInt128)84, fromChar[0].BigUnsigned);
        Assert.Equal(BigInteger.Pow(10, 45), fromChar[0].Arbitrary);
        Assert.Equal(new Uri("/relative/path", UriKind.Relative), fromChar[0].Link);
        Assert.Equal([9, 8, 7], fromChar[0].Payload);
        Assert.Equal(new CustomImplicitToken(12), fromChar[0].Token);
        Assert.Equal("present", fromChar[0].Label?.Text);

        Assert.Null(fromChar[1].Duration);
        Assert.Null(fromChar[1].Symbol);
        Assert.Null(fromChar[1].Ratio);
        Assert.Null(fromChar[1].BigSigned);
        Assert.Null(fromChar[1].BigUnsigned);
        Assert.Null(fromChar[1].Arbitrary);
        Assert.Null(fromChar[1].Link);
        Assert.Null(fromChar[1].Payload);
        Assert.Null(fromChar[1].Token);
        Assert.Null(fromChar[1].Label);

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var fromByte = new List<CsvExtendedNullableRecord>();
        using (var byteReader = Csv.Read<CsvExtendedNullableRecord>().FromStream(ms, out _))
        {
            while (byteReader.MoveNext())
            {
                fromByte.Add(byteReader.Current);
            }
            byteReader.ThrowOnStrictErrors();
        }
        Assert.Equal(2, fromByte.Count);
        Assert.Null(fromByte[1].Label);
        Assert.Null(fromByte[1].Payload);
        Assert.Equal("present", fromByte[0].Label?.Text);
    }

    [Fact]
    public async Task CsvStreamWriters_AllIntegerAndExtendedOverloads_ResolveUnambiguouslyAsync()
    {
        using var sw = new StringWriter();
        using (var writer = new CsvStreamWriter(sw, new CsvWriteOptions { NewLine = "\n" }, leaveOpen: true))
        {
            writer.WriteField((byte)1);
            writer.WriteField((sbyte)-2);
            writer.WriteField((short)-300);
            writer.WriteField((ushort)60000);
            writer.WriteField(42);
            writer.WriteField(42u);
            writer.WriteField(42L);
            writer.WriteField(18_000_000_000_000_000_001ul);
            writer.WriteField((Half)1.5);
            writer.WriteField((Int128)(-99));
            writer.WriteField((UInt128)99);
            writer.WriteField('X');
            writer.WriteField(TimeSpan.FromHours(1.5));
            writer.WriteField(new DateOnly(2026, 5, 1));
            writer.WriteField(new TimeOnly(8, 30));
            writer.WriteField(new DateTimeOffset(2026, 5, 1, 8, 30, 0, TimeSpan.Zero));
            writer.WriteField(null);
            writer.EndRow();
            writer.WriteRow([BigInteger.Parse("99999999999999999999999999", CultureInfo.InvariantCulture)]);
            writer.Flush();
        }

        string syncRow = sw.ToString();
        Assert.Contains("18000000000000000001", syncRow);
        Assert.Contains("99999999999999999999999999", syncRow);
        Assert.EndsWith(",\n99999999999999999999999999\n", syncRow);

        var ct = TestContext.Current.CancellationToken;
        using var ms = new MemoryStream();
        await using (var asyncWriter = new CsvAsyncStreamWriter(ms, new CsvWriteOptions { NewLine = "\n" }, leaveOpen: true))
        {
            await asyncWriter.WriteFieldAsync((byte)1, ct);
            await asyncWriter.WriteFieldAsync((sbyte)-2, ct);
            await asyncWriter.WriteFieldAsync((short)-300, ct);
            await asyncWriter.WriteFieldAsync((ushort)60000, ct);
            await asyncWriter.WriteFieldAsync(42u, ct);
            await asyncWriter.WriteFieldAsync(18_000_000_000_000_000_001ul, ct);
            await asyncWriter.WriteFieldAsync((Half)1.5, ct);
            await asyncWriter.WriteFieldAsync((Int128)(-99), ct);
            await asyncWriter.WriteFieldAsync((UInt128)99, ct);
            await asyncWriter.WriteFieldAsync('X', ct);
            await asyncWriter.WriteFieldAsync(TimeSpan.FromHours(1.5), ct);
            await asyncWriter.WriteFieldAsync(new DateOnly(2026, 5, 1), ct);
            await asyncWriter.WriteFieldAsync(new TimeOnly(8, 30), ct);
            await asyncWriter.WriteFieldAsync(new DateTimeOffset(2026, 5, 1, 8, 30, 0, TimeSpan.Zero), ct);
            await asyncWriter.WriteFieldAsync((string?)null, ct);
            await asyncWriter.EndRowAsync(ct);
            await asyncWriter.WriteRowAsync([BigInteger.Parse("99999999999999999999999999", CultureInfo.InvariantCulture)], ct);
            await asyncWriter.FlushAsync(ct);
        }

        string asyncRow = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("18000000000000000001", asyncRow);
        Assert.Contains("99999999999999999999999999", asyncRow);
    }

    [Fact]
    public void CsvAndFixedWidthColumns_CharAndByteSpans_TryParseExtendedTypes()
    {
        string csvLine = "1.02:03:04,Q,3.5,-123456789012345678901234567890,987654321098765432109876543210,12345678901234567890123456789012345,https://example.org/a,AQID,tok:15,u8:25";
        var charReader = Csv.ReadFromText(csvLine);
        Assert.True(charReader.MoveNext());
        var row = charReader.Current;

        Assert.True(row[0].TryParseTimeSpan(out var ts) && ts == new TimeSpan(1, 2, 3, 4));
        Assert.True(row[1].TryParseChar(out var ch) && ch == 'Q');
        Assert.True(row[2].TryParseHalf(out var h) && h == (Half)3.5);
        Assert.True(row[3].TryParseInt128(out var i128) && i128 == Int128.Parse("-123456789012345678901234567890", CultureInfo.InvariantCulture));
        Assert.True(row[4].TryParseUInt128(out var u128) && u128 == UInt128.Parse("987654321098765432109876543210", CultureInfo.InvariantCulture));
        Assert.True(row[5].TryParseBigInteger(out var bi) && bi == BigInteger.Parse("12345678901234567890123456789012345", CultureInfo.InvariantCulture));
        Assert.True(row[6].TryParseUri(out var uri) && uri == new Uri("https://example.org/a"));
        Assert.True(row[7].TryParseBase64Bytes(out var bytes) && bytes is [1, 2, 3]);
        Assert.True(row[8].TryParse<CustomImplicitToken>(out var tok) && tok == new CustomImplicitToken(15));
        Assert.True(row[9].TryParseUtf8<CustomUtf8OnlyCode>(out var code) && code == new CustomUtf8OnlyCode(25));

        var byteReader = Csv.ReadFromByteSpan(Encoding.UTF8.GetBytes(csvLine));
        Assert.True(byteReader.MoveNext());
        var byteRow = byteReader.Current;
        Assert.True(byteRow[0].TryParseTimeSpan(out var bTs) && bTs == new TimeSpan(1, 2, 3, 4));
        Assert.True(byteRow[1].TryParseChar(out var bCh) && bCh == 'Q');
        Assert.True(byteRow[6].TryParseUri(out var bUri) && bUri == new Uri("https://example.org/a"));
        Assert.True(byteRow[7].TryParseBase64Bytes(out var bBytes) && bBytes is [1, 2, 3]);
        Assert.True(byteRow[9].TryParseUtf8<CustomUtf8OnlyCode>(out var bCode) && bCode == new CustomUtf8OnlyCode(25));

        string fwLine = "01:02:03  Q   3.5  -999      888       77777777777777777777     https://x.y AQID  tok:8   u8:9    ";
        var fwCharReader = FixedWidth.ReadFromText(fwLine);
        Assert.True(fwCharReader.MoveNext());
        var fwRow = fwCharReader.Current;
        Assert.True(fwRow.GetField(0, 10).TryParseTimeSpan(out var fwTs) && fwTs == new TimeSpan(1, 2, 3));
        Assert.True(fwRow.GetField(10, 4).TryParseChar(out var fwCh) && fwCh == 'Q');
        Assert.True(fwRow.GetField(14, 5).TryParseHalf(out var fwHalf) && fwHalf == (Half)3.5);
        Assert.True(fwRow.GetField(19, 10).TryParseInt128(out var fwI128) && fwI128 == -999);
        Assert.True(fwRow.GetField(29, 10).TryParseUInt128(out var fwU128) && fwU128 == 888);
        Assert.True(fwRow.GetField(39, 25).TryParseBigInteger(out var fwBi) && fwBi == BigInteger.Parse("77777777777777777777", CultureInfo.InvariantCulture));
        Assert.True(fwRow.GetField(64, 12).TryParseUri(out var fwUri) && fwUri == new Uri("https://x.y"));
        Assert.True(fwRow.GetField(76, 6).TryParseBase64Bytes(out var fwBytes) && fwBytes is [1, 2, 3]);
        Assert.True(fwRow.GetField(82, 8).TryParse<CustomImplicitToken>(out var fwTok) && fwTok == new CustomImplicitToken(8));
        Assert.True(fwRow.GetField(90, 8).TryParseUtf8<CustomUtf8OnlyCode>(out var fwCode) && fwCode == new CustomUtf8OnlyCode(9));

        var fwByteReader = FixedWidth.ReadFromUtf8ByteSpan(Encoding.UTF8.GetBytes(fwLine));
        Assert.True(fwByteReader.MoveNext());
        var fwByteRow = fwByteReader.Current;
        Assert.True(fwByteRow.GetField(0, 10).TryParseTimeSpan(out var fwbTs) && fwbTs == new TimeSpan(1, 2, 3));
        Assert.True(fwByteRow.GetField(10, 4).TryParseChar(out var fwbCh) && fwbCh == 'Q');
        Assert.True(fwByteRow.GetField(76, 6).TryParseBase64Bytes(out var fwbBytes) && fwbBytes is [1, 2, 3]);
        Assert.True(fwByteRow.GetField(90, 8).TryParseUtf8<CustomUtf8OnlyCode>(out var fwbCode) && fwbCode == new CustomUtf8OnlyCode(9));
    }

    [Fact]
    public void FixedWidth_GeneratedAndReflectionAndFluentMap_RoundTripExtendedTypes()
    {
        FixedWidthExtendedRecord original = new()
        {
            OffsetVal = new DateTimeOffset(2026, 6, 15, 9, 15, 30, TimeSpan.FromHours(-5)),
            DateVal = new DateOnly(2026, 6, 15),
            TimeVal = new TimeOnly(9, 15, 30, 500),
            DurationVal = new TimeSpan(1, 2, 3, 4),
            UIntVal = 123_456_789u,
            ULongVal = 9_876_543_210_987ul,
            UShortVal = 54_321,
            SByteVal = -99,
            CharVal = 'K',
            HalfVal = (Half)7.25,
            Int128Val = Int128.Parse("-98765432109876543210987654321", CultureInfo.InvariantCulture),
            UInt128Val = UInt128.Parse("98765432109876543210987654321", CultureInfo.InvariantCulture),
            BigIntVal = BigInteger.Parse("5555555555555555555555555555555555555555", CultureInfo.InvariantCulture),
            UriVal = new Uri("https://example.org/fixed"),
            BytesVal = [10, 20, 30, 40],
            Token = new CustomImplicitToken(11),
            ExplicitTag = new CustomExplicitTag("beta"),
            Utf8Code = new CustomUtf8OnlyCode(22),
            Label = new CustomReferenceLabel { Text = "gamma" }
        };

        string written = FixedWidth.WriteToText([original]);
        var fromChar = FixedWidth.DeserializeRecords<FixedWidthExtendedRecord>(written).ToList();
        Assert.Single(fromChar);
        Assert.Equal(original.OffsetVal, fromChar[0].OffsetVal);
        Assert.Equal(original.DateVal, fromChar[0].DateVal);
        Assert.Equal(original.TimeVal, fromChar[0].TimeVal);
        Assert.Equal(original.DurationVal, fromChar[0].DurationVal);
        Assert.Equal(original.UIntVal, fromChar[0].UIntVal);
        Assert.Equal(original.ULongVal, fromChar[0].ULongVal);
        Assert.Equal(original.UShortVal, fromChar[0].UShortVal);
        Assert.Equal(original.SByteVal, fromChar[0].SByteVal);
        Assert.Equal(original.CharVal, fromChar[0].CharVal);
        Assert.Equal(original.HalfVal, fromChar[0].HalfVal);
        Assert.Equal(original.Int128Val, fromChar[0].Int128Val);
        Assert.Equal(original.UInt128Val, fromChar[0].UInt128Val);
        Assert.Equal(original.BigIntVal, fromChar[0].BigIntVal);
        Assert.Equal(original.UriVal, fromChar[0].UriVal);
        Assert.Equal(original.BytesVal, fromChar[0].BytesVal);
        Assert.Equal(original.Token, fromChar[0].Token);
        Assert.Equal(original.ExplicitTag.Value, fromChar[0].ExplicitTag.Value);
        Assert.Equal(original.Utf8Code, fromChar[0].Utf8Code);
        Assert.Equal(original.Label.Text, fromChar[0].Label.Text);

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(written));
        var fromByte = FixedWidth.Read<FixedWidthExtendedRecord>().FromStream(ms).ToList();
        Assert.Single(fromByte);
        Assert.Equal(original.BigIntVal, fromByte[0].BigIntVal);
        Assert.Equal(original.ExplicitTag.Value, fromByte[0].ExplicitTag.Value);
        Assert.Equal(original.Utf8Code, fromByte[0].Utf8Code);
        Assert.Equal(original.Label.Text, fromByte[0].Label.Text);

        // Reflection binder path (type without [GenerateBinder])
        var reflRecord = new FixedWidthReflectionRecord
        {
            Duration = TimeSpan.FromSeconds(75),
            Symbol = 'R',
            Ratio = (Half)2.5,
            BigSigned = -12345,
            BigUnsigned = 67890,
            Arbitrary = BigInteger.Pow(2, 100),
            Link = new Uri("https://example.org/refl"),
            Payload = [4, 5, 6],
            Token = new CustomImplicitToken(33)
        };
        string reflText = FixedWidth.WriteToText([reflRecord]);
        var reflParsed = FixedWidth.DeserializeRecords<FixedWidthReflectionRecord>(reflText).ToList();
        Assert.Single(reflParsed);
        Assert.Equal(reflRecord.Duration, reflParsed[0].Duration);
        Assert.Equal(reflRecord.Symbol, reflParsed[0].Symbol);
        Assert.Equal(reflRecord.Ratio, reflParsed[0].Ratio);
        Assert.Equal(reflRecord.BigSigned, reflParsed[0].BigSigned);
        Assert.Equal(reflRecord.BigUnsigned, reflParsed[0].BigUnsigned);
        Assert.Equal(reflRecord.Arbitrary, reflParsed[0].Arbitrary);
        Assert.Equal(reflRecord.Link, reflParsed[0].Link);
        Assert.Equal(reflRecord.Payload, reflParsed[0].Payload);
        Assert.Equal(reflRecord.Token, reflParsed[0].Token);
    }

    [Fact]
    public void FixedWidth_NullableAndFluentMapAndCsvMap_HandleAllExtendedTypes()
    {
        FixedWidthExtendedNullableRecord[] nullRecords =
        [
            new FixedWidthExtendedNullableRecord(),
            new FixedWidthExtendedNullableRecord
            {
                Duration = TimeSpan.FromMinutes(12),
                Symbol = 'M',
                Ratio = (Half)1.5,
                BigSigned = 999,
                Link = new Uri("https://ok.example"),
                Payload = [1, 2],
                Label = new CustomReferenceLabel { Text = "ok" }
            }
        ];

        string fwText = FixedWidth.WriteToText(nullRecords);
        var readBack = FixedWidth.DeserializeRecords<FixedWidthExtendedNullableRecord>(fwText).ToList();
        Assert.Equal(2, readBack.Count);
        Assert.Null(readBack[0].Duration);
        Assert.Null(readBack[0].Symbol);
        Assert.Null(readBack[0].Ratio);
        Assert.Null(readBack[0].BigSigned);
        Assert.Null(readBack[0].Link);
        Assert.Null(readBack[0].Payload);
        Assert.Null(readBack[0].Label);
        Assert.Equal("ok", readBack[1].Label?.Text);

        var fwMap = new FixedWidthMap<FluentExtendedRecord>()
            .Map(x => x.OffsetVal, c => c.Start(0).Length(34).Format("O"))
            .Map(x => x.UIntVal, c => c.Start(34).Length(10))
            .Map(x => x.ULongVal, c => c.Start(44).Length(20))
            .Map(x => x.UShortVal, c => c.Start(64).Length(6))
            .Map(x => x.SByteVal, c => c.Start(70).Length(5))
            .Map(x => x.Duration, c => c.Start(75).Length(20))
            .Map(x => x.Symbol, c => c.Start(95).Length(1))
            .Map(x => x.Ratio, c => c.Start(96).Length(10))
            .Map(x => x.BigSigned, c => c.Start(106).Length(20))
            .Map(x => x.BigUnsigned, c => c.Start(126).Length(20))
            .Map(x => x.Arbitrary, c => c.Start(146).Length(40))
            .Map(x => x.Link, c => c.Start(186).Length(24))
            .Map(x => x.Payload, c => c.Start(210).Length(12))
            .Map(x => x.Token, c => c.Start(222).Length(12))
            .Map(x => x.Utf8Code, c => c.Start(234).Length(12))
            .Map(x => x.Label, c => c.Start(246).Length(12));

        FluentExtendedRecord fluentItem = new()
        {
            OffsetVal = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.FromHours(2)),
            UIntVal = 400000u,
            ULongVal = 9000000000000ul,
            UShortVal = 50000,
            SByteVal = -42,
            Duration = TimeSpan.FromMinutes(7),
            Symbol = 'F',
            Ratio = (Half)4.5,
            BigSigned = -123456,
            BigUnsigned = 654321,
            Arbitrary = BigInteger.Pow(3, 40),
            Link = new Uri("https://map.example"),
            Payload = [7, 8, 9],
            Token = new CustomImplicitToken(5),
            Utf8Code = new CustomUtf8OnlyCode(6),
            Label = new CustomReferenceLabel { Text = "map" }
        };

        string fluentFw = FixedWidth.Write<FluentExtendedRecord>().WithMap(fwMap).ToText([fluentItem]);
        var fwMapChar = FixedWidth.Read<FluentExtendedRecord>().WithMap(fwMap).FromText(fluentFw).ToList();
        Assert.Single(fwMapChar);
        Assert.Equal(fluentItem.Arbitrary, fwMapChar[0].Arbitrary);
        Assert.Equal(fluentItem.Token, fwMapChar[0].Token);
        Assert.Equal(fluentItem.Utf8Code, fwMapChar[0].Utf8Code);
        Assert.Equal("map", fwMapChar[0].Label?.Text);

        using var fwMs = new MemoryStream(Encoding.UTF8.GetBytes(fluentFw));
        var fwMapByte = FixedWidth.Read<FluentExtendedRecord>().WithMap(fwMap).FromStream(fwMs).ToList();
        Assert.Single(fwMapByte);
        Assert.Equal(fluentItem.Link, fwMapByte[0].Link);
        Assert.Equal(fluentItem.Payload, fwMapByte[0].Payload);

        var csvMap = new CsvMap<FluentExtendedRecord>()
            .Map(x => x.OffsetVal, c => c.Name("Offset").Format("O"))
            .Map(x => x.UIntVal, c => c.Name("UInt"))
            .Map(x => x.ULongVal, c => c.Name("ULong"))
            .Map(x => x.UShortVal, c => c.Name("UShort"))
            .Map(x => x.SByteVal, c => c.Name("SByte"))
            .Map(x => x.Duration, c => c.Name("Duration"))
            .Map(x => x.Symbol, c => c.Name("Symbol"))
            .Map(x => x.Ratio, c => c.Name("Ratio"))
            .Map(x => x.BigSigned, c => c.Name("BigSigned"))
            .Map(x => x.BigUnsigned, c => c.Name("BigUnsigned"))
            .Map(x => x.Arbitrary, c => c.Name("Arbitrary"))
            .Map(x => x.Link, c => c.Name("Link"))
            .Map(x => x.Payload, c => c.Name("Payload"))
            .Map(x => x.Token, c => c.Name("Token"))
            .Map(x => x.Utf8Code, c => c.Name("Utf8Code"))
            .Map(x => x.Label, c => c.Name("Label"));

        string fluentCsv = Csv.Write<FluentExtendedRecord>().WithMap(csvMap).ToText([fluentItem]);
        var csvMapParsed = Csv.Read<FluentExtendedRecord>().WithMap(csvMap).FromText(fluentCsv).ToList();
        Assert.Single(csvMapParsed);
        Assert.Equal(fluentItem.OffsetVal, csvMapParsed[0].OffsetVal);
        Assert.Equal(fluentItem.Arbitrary, csvMapParsed[0].Arbitrary);
        Assert.Equal(fluentItem.Utf8Code, csvMapParsed[0].Utf8Code);
        Assert.Equal("map", csvMapParsed[0].Label?.Text);
    }

    [Fact]
    public void TimeSpanFormat_BuilderOption_AppliesAcrossCsvFixedWidthAndExcel()
    {
        TimeSpan duration = new(1, 2, 3, 4);

        string csv = Csv.Write<TimeSpanFormatCsvRecord>()
            .WithTimeSpanFormat("g")
            .ToText([new TimeSpanFormatCsvRecord { Duration = duration }]);
        Assert.Contains("1:2:03:04", csv);

        string fw = FixedWidth.Write<TimeSpanFormatFixedWidthRecord>()
            .WithTimeSpanFormat("g")
            .ToText([new TimeSpanFormatFixedWidthRecord { Duration = duration }]);
        Assert.Contains("1:2:03:04", fw);

        byte[] xlsx = HeroParser.Excel.Write<TimeSpanFormatCsvRecord>()
            .WithTimeSpanFormat("g")
            .ToBytes([new TimeSpanFormatCsvRecord { Duration = duration }]);
        using var ms = new MemoryStream(xlsx);
        var excelBack = HeroParser.Excel.Read<TimeSpanFormatCsvRecord>().FromStream(ms);
        Assert.Single(excelBack);
        Assert.Equal(duration, excelBack[0].Duration);
    }

    [Fact]
    public void Excel_RoundTripsAllExtendedTypes()
    {
        CsvExtendedRecord[] original = [CreateSampleCsvRecord()];
        byte[] bytes = HeroParser.Excel.Write<CsvExtendedRecord>().ToBytes(original);
        using var ms = new MemoryStream(bytes);
        var parsed = HeroParser.Excel.Read<CsvExtendedRecord>().FromStream(ms);

        Assert.Single(parsed);
        AssertCsvExtendedEqual(original[0], parsed[0]);
    }

    [Fact]
    public async Task Jsonl_GeneratedCharAndByteBinders_ParseExtendedTypesAndCustomParsablesAsync()
    {
        string jsonl =
            "{\"OffsetVal\":\"2026-11-23T14:30:45+11:00\",\"DateVal\":\"2026-11-23\",\"TimeVal\":\"14:30:45.123\"," +
            "\"DurationVal\":\"2.03:04:05.2500000\",\"UIntVal\":4000000001,\"ULongVal\":18000000000000000001," +
            "\"UShortVal\":65001,\"SByteVal\":-120,\"CharVal\":\"Z\",\"HalfVal\":12.5," +
            "\"Int128Val\":-170141183460469231731687303715884105720," +
            "\"UInt128Val\":\"340282366920938463463374607431768211400\"," +
            "\"BigIntVal\":1234567890123456789012345678901234567890," +
            "\"UriVal\":\"https://example.com/jsonl\",\"BytesVal\":\"AQIDBA==\"," +
            "\"Token\":\"tok:55\",\"ExplicitTag\":\"tag:exp\",\"Utf8Code\":\"u8:66\",\"Label\":\"lbl:ref\"}\n";

        var fromChar = Jsonl.Read<JsonlExtendedRecord>().FromText(jsonl).ToList();
        Assert.Single(fromChar);
        Assert.Equal(new DateTimeOffset(2026, 11, 23, 14, 30, 45, TimeSpan.FromHours(11)), fromChar[0].OffsetVal);
        Assert.Equal(new DateOnly(2026, 11, 23), fromChar[0].DateVal);
        Assert.Equal(new TimeOnly(14, 30, 45, 123), fromChar[0].TimeVal);
        Assert.Equal(new TimeSpan(2, 3, 4, 5, 250), fromChar[0].DurationVal);
        Assert.Equal(4_000_000_001u, fromChar[0].UIntVal);
        Assert.Equal(18_000_000_000_000_000_001ul, fromChar[0].ULongVal);
        Assert.Equal((ushort)65_001, fromChar[0].UShortVal);
        Assert.Equal((sbyte)-120, fromChar[0].SByteVal);
        Assert.Equal('Z', fromChar[0].CharVal);
        Assert.Equal((Half)12.5, fromChar[0].HalfVal);
        Assert.Equal(Int128.Parse("-170141183460469231731687303715884105720", CultureInfo.InvariantCulture), fromChar[0].Int128Val);
        Assert.Equal(UInt128.Parse("340282366920938463463374607431768211400", CultureInfo.InvariantCulture), fromChar[0].UInt128Val);
        Assert.Equal(BigInteger.Parse("1234567890123456789012345678901234567890", CultureInfo.InvariantCulture), fromChar[0].BigIntVal);
        Assert.Equal(new Uri("https://example.com/jsonl"), fromChar[0].UriVal);
        Assert.Equal([1, 2, 3, 4], fromChar[0].BytesVal);
        Assert.Equal(new CustomImplicitToken(55), fromChar[0].Token);
        Assert.Equal("exp", fromChar[0].ExplicitTag.Value);
        Assert.Equal(new CustomUtf8OnlyCode(66), fromChar[0].Utf8Code);
        Assert.Equal("ref", fromChar[0].Label?.Text);

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(jsonl));
        var fromStream = new List<JsonlExtendedRecord>();
        await foreach (var item in Jsonl.Read<JsonlExtendedRecord>().FromStreamAsync(ms, cancellationToken: TestContext.Current.CancellationToken))
        {
            fromStream.Add(item);
        }
        Assert.Single(fromStream);
        Assert.Equal(fromChar[0].BigIntVal, fromStream[0].BigIntVal);
        Assert.Equal(fromChar[0].Utf8Code, fromStream[0].Utf8Code);
        Assert.Equal("ref", fromStream[0].Label?.Text);
    }

    [Fact]
    public async Task Htb_GeneratedReflectionDataReaderConvertersAndSkip_RoundTripAllExtendedTypesAsync()
    {
        HtbExtendedRecord sample = new()
        {
            OffsetVal = new DateTimeOffset(2026, 11, 23, 14, 30, 45, TimeSpan.FromHours(-5)),
            DateVal = new DateOnly(2026, 11, 23),
            TimeVal = new TimeOnly(14, 30, 45, 789),
            DurationVal = new TimeSpan(3, 4, 5, 6, 500),
            ShortVal = -30000,
            UShortVal = 60000,
            ByteVal = 240,
            SByteVal = -100,
            UIntVal = 3_999_999_999u,
            ULongVal = 17_999_999_999_999_999_999ul,
            CharVal = 'Ω',
            HalfVal = (Half)(-42.5),
            Int128Val = Int128.MinValue + 42,
            UInt128Val = UInt128.MaxValue - 42,
            BytesVal = [0, 1, 127, 128, 255],
            BigIntVal = -BigInteger.Pow(2, 200),
            UriVal = new Uri("https://example.org/htb?q=1")
        };

        // 1. Source-generated sync + async
        using var htbStream = new MemoryStream();
        HeroParser.Htb.Write<HtbExtendedRecord>().ToStream(htbStream, [sample], leaveOpen: true);
        htbStream.Position = 0;
        var htbRead = HeroParser.Htb.Read<HtbExtendedRecord>().FromStream(htbStream, leaveOpen: true).ToList();
        Assert.Single(htbRead);
        Assert.Equal(sample.OffsetVal, htbRead[0].OffsetVal);
        Assert.Equal(sample.DateVal, htbRead[0].DateVal);
        Assert.Equal(sample.TimeVal, htbRead[0].TimeVal);
        Assert.Equal(sample.DurationVal, htbRead[0].DurationVal);
        Assert.Equal(sample.ShortVal, htbRead[0].ShortVal);
        Assert.Equal(sample.UShortVal, htbRead[0].UShortVal);
        Assert.Equal(sample.ByteVal, htbRead[0].ByteVal);
        Assert.Equal(sample.SByteVal, htbRead[0].SByteVal);
        Assert.Equal(sample.UIntVal, htbRead[0].UIntVal);
        Assert.Equal(sample.ULongVal, htbRead[0].ULongVal);
        Assert.Equal(sample.CharVal, htbRead[0].CharVal);
        Assert.Equal(sample.HalfVal, htbRead[0].HalfVal);
        Assert.Equal(sample.Int128Val, htbRead[0].Int128Val);
        Assert.Equal(sample.UInt128Val, htbRead[0].UInt128Val);
        Assert.Equal(sample.BytesVal, htbRead[0].BytesVal);
        Assert.Equal(sample.BigIntVal, htbRead[0].BigIntVal);
        Assert.Equal(sample.UriVal, htbRead[0].UriVal);

        // 2. Nullable HTB async
        using var nullStream = new MemoryStream();
        HtbExtendedNullableRecord[] nullItems =
        [
            new HtbExtendedNullableRecord(),
            new HtbExtendedNullableRecord
            {
                OffsetVal = sample.OffsetVal,
                DateVal = sample.DateVal,
                TimeVal = sample.TimeVal,
                DurationVal = sample.DurationVal,
                HalfVal = sample.HalfVal,
                Int128Val = sample.Int128Val,
                UInt128Val = sample.UInt128Val,
                BytesVal = sample.BytesVal,
                BigIntVal = sample.BigIntVal,
                UriVal = sample.UriVal
            }
        ];
        await HeroParser.Htb.Write<HtbExtendedNullableRecord>().ToStreamAsync(nullStream, nullItems, leaveOpen: true, TestContext.Current.CancellationToken);
        nullStream.Position = 0;
        var nullBack = new List<HtbExtendedNullableRecord>();
        await foreach (var item in HeroParser.Htb.Read<HtbExtendedNullableRecord>().FromStreamAsync(nullStream, leaveOpen: true, TestContext.Current.CancellationToken))
        {
            nullBack.Add(item);
        }
        Assert.Equal(2, nullBack.Count);
        Assert.Null(nullBack[0].UriVal);
        Assert.Null(nullBack[0].BytesVal);
        Assert.Null(nullBack[0].BigIntVal);
        Assert.Equal(sample.BigIntVal, nullBack[1].BigIntVal);
        Assert.Equal(sample.UriVal, nullBack[1].UriVal);

        // 3. Reflection binder HTB + HtbDataReader.GetBytes + WriteFromDataReader
        HtbReflectionExtendedRecord reflSample = new()
        {
            OffsetVal = sample.OffsetVal,
            DateVal = sample.DateVal,
            TimeVal = sample.TimeVal,
            DurationVal = sample.DurationVal,
            ShortVal = sample.ShortVal,
            UShortVal = sample.UShortVal,
            ByteVal = sample.ByteVal,
            SByteVal = sample.SByteVal,
            UIntVal = sample.UIntVal,
            ULongVal = sample.ULongVal,
            CharVal = sample.CharVal,
            HalfVal = sample.HalfVal,
            Int128Val = sample.Int128Val,
            UInt128Val = sample.UInt128Val,
            BytesVal = sample.BytesVal,
            BigIntVal = BigInteger.Zero,
            UriVal = sample.UriVal
        };
        using var reflStream = new MemoryStream();
        HeroParser.Htb.Write<HtbReflectionExtendedRecord>().ToStream(reflStream, [reflSample], leaveOpen: true);
        reflStream.Position = 0;
        var reflBack = HeroParser.Htb.Read<HtbReflectionExtendedRecord>().FromStream(reflStream).ToList();
        Assert.Single(reflBack);
        Assert.Equal(BigInteger.Zero, reflBack[0].BigIntVal);
        Assert.Equal(reflSample.BytesVal, reflBack[0].BytesVal);

        htbStream.Position = 0;
        using var dataReader = HeroParser.Htb.CreateDataReader(htbStream, leaveOpen: true);
        Assert.True(dataReader.Read());
        int bytesOrdinal = dataReader.GetOrdinal(nameof(HtbExtendedRecord.BytesVal));
        Assert.Equal(5, dataReader.GetBytes(bytesOrdinal, 0, null, 0, 0));
        byte[] slice = new byte[4];
        long copied = dataReader.GetBytes(bytesOrdinal, 1, slice, 1, 3);
        Assert.Equal(3, copied);
        Assert.Equal(new byte[] { 0, 1, 127, 128 }, slice);
        Assert.Equal(sample.UriVal.OriginalString, dataReader.GetString(dataReader.GetOrdinal(nameof(HtbExtendedRecord.UriVal))));
        Assert.Equal(sample.CharVal, dataReader.GetChar(dataReader.GetOrdinal(nameof(HtbExtendedRecord.CharVal))));
        Assert.Equal(sample.ByteVal, dataReader.GetByte(dataReader.GetOrdinal(nameof(HtbExtendedRecord.ByteVal))));

        using var exportedFromReader = new MemoryStream();
        htbStream.Position = 0;
        using (var srcReader = HeroParser.Htb.CreateDataReader(htbStream, leaveOpen: true))
        {
            HeroParser.Htb.WriteFromDataReader(srcReader, exportedFromReader, leaveOpen: true);
        }
        exportedFromReader.Position = 0;
        var exportedRecords = HeroParser.Htb.Read<HtbExtendedRecord>().FromStream(exportedFromReader).ToList();
        Assert.Single(exportedRecords);
        Assert.Equal(sample.Int128Val, exportedRecords[0].Int128Val);
        Assert.Equal(sample.BigIntVal, exportedRecords[0].BigIntVal);

        // 4. CsvToHtbConverter + HtbToCsvConverter sync & async
        HtbSchema convSchema = HtbSchema.FromType<HtbExtendedRecord>();
        using var csvBuf = new StringWriter();
        htbStream.Position = 0;
        HtbToCsvConverter.Convert(htbStream, csvBuf);
        string csvText = csvBuf.ToString();

        using var roundHtb = new MemoryStream();
        CsvToHtbConverter.Convert(csvText, roundHtb, convSchema);
        roundHtb.Position = 0;
        var convertedRecords = HeroParser.Htb.Read<HtbExtendedRecord>().FromStream(roundHtb).ToList();
        Assert.Single(convertedRecords);
        Assert.Equal(sample.OffsetVal, convertedRecords[0].OffsetVal);
        Assert.Equal(sample.DateVal, convertedRecords[0].DateVal);
        Assert.Equal(sample.TimeVal, convertedRecords[0].TimeVal);
        Assert.Equal(sample.DurationVal, convertedRecords[0].DurationVal);
        Assert.Equal(sample.ULongVal, convertedRecords[0].ULongVal);
        Assert.Equal(sample.BigIntVal, convertedRecords[0].BigIntVal);

        // 5. Unbound column skipping (sync & async)
        htbStream.Position = 0;
        using var skipStreamReader = new HtbStreamReader(htbStream, leaveOpen: true);
        var headerSchema = skipStreamReader.ParseHeader();
        byte[] mask = new byte[(headerSchema.Columns.Count + 7) / 8];
        skipStreamReader.ReadMask(mask);
        foreach (var col in headerSchema.Columns)
        {
            skipStreamReader.SkipColumnValue(col.DataType);
        }
        Assert.True(skipStreamReader.IsEndOfStream());

        htbStream.Position = 0;
        await using var asyncSkipReader = new HtbStreamReader(htbStream, leaveOpen: true);
        var asyncHeaderSchema = await asyncSkipReader.ParseHeaderAsync(TestContext.Current.CancellationToken);
        await asyncSkipReader.ReadMaskAsync(mask, TestContext.Current.CancellationToken);
        foreach (var col in asyncHeaderSchema.Columns)
        {
            await asyncSkipReader.SkipColumnValueAsync(col.DataType, TestContext.Current.CancellationToken);
        }
        Assert.True(await asyncSkipReader.IsEndOfStreamAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void HtbStreamReader_CorruptExtendedTemporalOrUriBounds_ThrowsHtbException()
    {
        static byte[] WriteSingleColHeaderAndValue(HtbDataType type, Action<BinaryWriter> writeValue)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
            bw.Write([(byte)'H', (byte)'T', (byte)'B', 0x01]);
            bw.Write(1);
            bw.Write((byte)type);
            bw.Write(1);
            bw.Write((byte)'C');
            bw.Write((byte)0); // mask byte (non-null)
            writeValue(bw);
            bw.Flush();
            return ms.ToArray();
        }

        byte[] badOffset = WriteSingleColHeaderAndValue(HtbDataType.DateTimeOffset, bw =>
        {
            bw.Write(DateTime.UtcNow.Ticks);
            bw.Write((short)900); // > 840 minutes
        });
        using (var ms = new MemoryStream(badOffset))
        using (var r = new HtbStreamReader(ms))
        {
            _ = r.ParseHeader();
            Span<byte> m = stackalloc byte[1];
            r.ReadMask(m);
            Assert.Throws<HtbException>(() => { _ = r.ReadDateTimeOffset(); });
        }

        byte[] badDateOnly = WriteSingleColHeaderAndValue(HtbDataType.DateOnly, bw => bw.Write(DateOnly.MaxValue.DayNumber + 10));
        using (var ms = new MemoryStream(badDateOnly))
        using (var r = new HtbStreamReader(ms))
        {
            _ = r.ParseHeader();
            Span<byte> m = stackalloc byte[1];
            r.ReadMask(m);
            Assert.Throws<HtbException>(() => { _ = r.ReadDateOnly(); });
        }

        byte[] badTimeOnly = WriteSingleColHeaderAndValue(HtbDataType.TimeOnly, bw => bw.Write(TimeOnly.MaxValue.Ticks + 1));
        using (var ms = new MemoryStream(badTimeOnly))
        using (var r = new HtbStreamReader(ms))
        {
            _ = r.ParseHeader();
            Span<byte> m = stackalloc byte[1];
            r.ReadMask(m);
            Assert.Throws<HtbException>(() => { _ = r.ReadTimeOnly(); });
        }

        byte[] badUriBytes = Encoding.UTF8.GetBytes("http://[invalid");
        byte[] badUri = WriteSingleColHeaderAndValue(HtbDataType.Uri, bw =>
        {
            bw.Write(badUriBytes.Length);
            bw.Write(badUriBytes);
        });
        using (var ms = new MemoryStream(badUri))
        using (var r = new HtbStreamReader(ms))
        {
            _ = r.ParseHeader();
            Span<byte> m = stackalloc byte[1];
            r.ReadMask(m);
            Assert.Throws<HtbException>(() => { _ = r.ReadUri(); });
        }
    }

    [Fact]
    public void ExtendedNumericRangeValidation_HalfInt128UInt128BigInteger_EnforcedOnReadAndWrite()
    {
        var invalidCsv = "Ratio,BigSigned,BigUnsigned,Arbitrary\n200,500,500,500\n";
        var csvResult = Csv.Read<ExtendedRangeValidatedRecord>().WithValidationMode(ValidationMode.Lenient).FromText(invalidCsv);
        _ = csvResult.ToList();
        Assert.NotEmpty(csvResult.Errors);
        Assert.Equal("Range", csvResult.Errors[0].Rule);

        string invalidFw = "200     500       500       500       ";
        var fwResult = FixedWidth.Read<ExtendedRangeValidatedRecord>().WithValidationMode(ValidationMode.Lenient).FromText(invalidFw);
        _ = fwResult.ToList();
        Assert.NotEmpty(fwResult.Errors);
        Assert.Equal("Range", fwResult.Errors[0].Rule);

        var outOfRange = new ExtendedRangeValidatedRecord
        {
            Ratio = (Half)150,
            BigSigned = 5,
            BigUnsigned = 5,
            Arbitrary = 5
        };
        Assert.Throws<ValidationException>(() => { _ = Csv.WriteToText([outOfRange]); });

        using var htbMs = new MemoryStream();
        HeroParser.Htb.Write<ExtendedRangeValidatedRecord>().ToStream(htbMs, [outOfRange], leaveOpen: true);
        htbMs.Position = 0;
        Assert.Throws<HtbException>(() => { _ = HeroParser.Htb.Read<ExtendedRangeValidatedRecord>().FromStream(htbMs).ToList(); });
    }

    [Fact]
    public void CsvSchemaInference_DistinguishTemporalTypes_DefaultFalseAndOptInTrueWithWidening()
    {
        string csv =
            "D,T,Dur,Dto,Dt,MixDateDt,MixDateDto,MixDtoDt,MixDurTime,MixDateTimeOnly\n" +
            "2026-11-23,14:30:00,01:02:03,2026-11-23T14:30:00+02:00,2026-11-23 14:30:00,2026-11-23,2026-11-23,2026-11-23T14:30:00Z,01:02:03,2026-11-23\n" +
            "2026-11-24,15:45:00,1.02:03:04,2026-11-24T15:45:00Z,2026-11-24T15:45:00,2026-11-24 08:00:00,2026-11-24T08:00:00-05:00,2026-11-24 08:00:00,25:00:00,14:30:00\n";

        var defaultResult = Csv.InferSchema(csv);
        Assert.Equal(CsvInferredType.DateTime, defaultResult.Columns[0].InferredType);
        Assert.Equal(CsvInferredType.DateTime, defaultResult.Columns[3].InferredType);
        Assert.Equal(CsvInferredType.String, defaultResult.Columns[2].InferredType);

        var typedResult = Csv.InferSchema(csv, new CsvSchemaInferenceOptions { DistinguishTemporalTypes = true });
        Assert.Equal(CsvInferredType.DateOnly, typedResult.Columns[0].InferredType);
        Assert.Equal(CsvInferredType.TimeOnly, typedResult.Columns[1].InferredType);
        Assert.Equal(CsvInferredType.TimeSpan, typedResult.Columns[2].InferredType);
        Assert.Equal(CsvInferredType.DateTimeOffset, typedResult.Columns[3].InferredType);
        Assert.Equal(CsvInferredType.DateTime, typedResult.Columns[4].InferredType);
        Assert.Equal(CsvInferredType.DateTime, typedResult.Columns[5].InferredType);
        Assert.Equal(CsvInferredType.String, typedResult.Columns[6].InferredType);
        Assert.Equal(CsvInferredType.String, typedResult.Columns[7].InferredType);
        Assert.Equal(CsvInferredType.TimeSpan, typedResult.Columns[8].InferredType);
        Assert.Equal(CsvInferredType.String, typedResult.Columns[9].InferredType);
    }

    [Fact]
    public void FractionalAndOutOfBoundsIntegerRanges_EnforcedConsistentlyOnReadAndStrictWrite()
    {
        // Zero violates RangeMin = 0.5 for Int128/UInt128/BigInteger and RangeMax = -0.5 for Int128
        const string badCsv = "PosInt128,PosUInt128,PosBigInt,NegInt128\n0,0,0,0\n";
        var badCsvResult = Csv.Read<FractionalExtendedRangeRecord>().WithValidationMode(ValidationMode.Lenient).FromText(badCsv);
        _ = badCsvResult.ToList();
        Assert.Equal(4, badCsvResult.Errors.Count);

        const string badFw = "0     0     0     0     ";
        var badFwResult = FixedWidth.Read<FractionalExtendedRangeRecord>().WithValidationMode(ValidationMode.Lenient).FromText(badFw);
        _ = badFwResult.ToList();
        Assert.Equal(4, badFwResult.Errors.Count);

        const string badJsonl = "{\"PosInt128\":0,\"PosUInt128\":0,\"PosBigInt\":0,\"NegInt128\":0}\n";
        Assert.Throws<ValidationException>(() => { _ = Jsonl.Read<FractionalExtendedRangeRecord>().FromText(badJsonl).ToList(); });

        var invalidWrite = new FractionalExtendedRangeRecord
        {
            PosInt128 = 0,
            PosUInt128 = 1,
            PosBigInt = 1,
            NegInt128 = -1
        };
        Assert.Throws<ValidationException>(() => { _ = Csv.WriteToText([invalidWrite]); });
        Assert.Throws<ValidationException>(() => { _ = FixedWidth.WriteToText([invalidWrite]); });
        Assert.Throws<ValidationException>(() => { _ = HeroParser.Excel.SerializeRecords([invalidWrite]); });

        // Boundary passing values (1, 1, 1, -1)
        const string goodCsv = "PosInt128,PosUInt128,PosBigInt,NegInt128\n1,1,1,-1\n";
        var goodCsvRows = Csv.Read<FractionalExtendedRangeRecord>().FromText(goodCsv).ToList();
        Assert.Single(goodCsvRows);
        Assert.Equal((Int128)1, goodCsvRows[0].PosInt128);
        Assert.Equal((Int128)(-1), goodCsvRows[0].NegInt128);

        const string goodFw = "1     1     1     -1    ";
        var goodFwRows = FixedWidth.Read<FractionalExtendedRangeRecord>().FromText(goodFw).ToList();
        Assert.Single(goodFwRows);
        Assert.Equal((UInt128)1, goodFwRows[0].PosUInt128);

        const string goodJsonl = "{\"PosInt128\":1,\"PosUInt128\":1,\"PosBigInt\":1,\"NegInt128\":-1}\n";
        var goodJsonlRows = Jsonl.Read<FractionalExtendedRangeRecord>().FromText(goodJsonl).ToList();
        Assert.Single(goodJsonlRows);

        // UInt128 with RangeMax = -0.5 rejects 0 consistently across all readers and writers
        var negUIntCsv = Csv.Read<NegativeUInt128RangeRecord>().WithValidationMode(ValidationMode.Lenient).FromText("Value\n0\n");
        _ = negUIntCsv.ToList();
        Assert.Single(negUIntCsv.Errors);

        var negUIntFw = FixedWidth.Read<NegativeUInt128RangeRecord>().WithValidationMode(ValidationMode.Lenient).FromText("0     ");
        _ = negUIntFw.ToList();
        Assert.Single(negUIntFw.Errors);

        Assert.Throws<ValidationException>(() => { _ = Jsonl.Read<NegativeUInt128RangeRecord>().FromText("{\"Value\":0}\n").ToList(); });
        Assert.Throws<ValidationException>(() => { _ = Csv.WriteToText([new NegativeUInt128RangeRecord { Value = 0 }]); });

        // Out-of-bounds limits: UInt128 RangeMin = -5.5 accepts 0; Int128 RangeMin = 1e50 rejects 1
        var outBoundsCsv = Csv.Read<OutOfBoundsExtendedRangeRecord>()
            .WithValidationMode(ValidationMode.Lenient)
            .FromText("LowerOutOfBoundsUInt128,ImpossibleInt128\n0,1\n");
        _ = outBoundsCsv.ToList();
        Assert.Single(outBoundsCsv.Errors);
        Assert.Equal("ImpossibleInt128", outBoundsCsv.Errors[0].PropertyName);

        Assert.Throws<ValidationException>(() =>
        {
            _ = Jsonl.Read<OutOfBoundsExtendedRangeRecord>()
                .FromText("{\"LowerOutOfBoundsUInt128\":0,\"ImpossibleInt128\":1}\n")
                .ToList();
        });
    }

    [Fact]
    public async Task Jsonl_BigInteger_GeneratedAndReflection_RoundTripAsRawJsonNumbersAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        BigInteger hugePos = BigInteger.Parse("1" + new string('2', 149), CultureInfo.InvariantCulture);
        BigInteger hugeNeg = -BigInteger.Parse("9" + new string('8', 139), CultureInfo.InvariantCulture);

        JsonlBigIntegerGeneratedRecord[] genInput =
        [
            new() { Value = hugePos, OptionalValue = hugeNeg, Label = "first" },
            new() { Value = BigInteger.Zero, OptionalValue = null, Label = "second" }
        ];

        string genJsonl = Jsonl.WriteToText(genInput);
        Assert.DoesNotContain("IsPowerOfTwo", genJsonl, StringComparison.Ordinal);
        Assert.Contains(hugePos.ToString(CultureInfo.InvariantCulture), genJsonl, StringComparison.Ordinal);
        Assert.Contains(hugeNeg.ToString(CultureInfo.InvariantCulture), genJsonl, StringComparison.Ordinal);

        var genSyncBack = Jsonl.Read<JsonlBigIntegerGeneratedRecord>().FromText(genJsonl).ToList();
        Assert.Equal(2, genSyncBack.Count);
        Assert.Equal(hugePos, genSyncBack[0].Value);
        Assert.Equal(hugeNeg, genSyncBack[0].OptionalValue);
        Assert.Null(genSyncBack[1].OptionalValue);

        await using var genMs = new MemoryStream(Encoding.UTF8.GetBytes(genJsonl));
        var genAsyncBack = new List<JsonlBigIntegerGeneratedRecord>();
        await foreach (var item in Jsonl.Read<JsonlBigIntegerGeneratedRecord>().FromStreamAsync(genMs, cancellationToken: ct))
        {
            genAsyncBack.Add(item);
        }
        Assert.Equal(2, genAsyncBack.Count);
        Assert.Equal(hugePos, genAsyncBack[0].Value);
        Assert.Equal(hugeNeg, genAsyncBack[0].OptionalValue);

        // Reflection write & read + user-supplied JsonSerializerOptions (must not mutate caller options)
        JsonlBigIntegerReflectionRecord[] reflInput =
        [
            new() { Value = hugeNeg, OptionalValue = hugePos, Label = "refl" },
            new() { Value = 42, OptionalValue = null, Label = "small" }
        ];
        var callerOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        int initialConverterCount = callerOptions.Converters.Count;

        string reflJsonl = Jsonl.Write<JsonlBigIntegerReflectionRecord>()
            .WithJsonOptions(callerOptions)
            .ToText(reflInput);
        Assert.Equal(initialConverterCount, callerOptions.Converters.Count);
        Assert.DoesNotContain("isPowerOfTwo", reflJsonl, StringComparison.OrdinalIgnoreCase);

        var reflReadBack = Jsonl.Read<JsonlBigIntegerReflectionRecord>()
            .WithJsonOptions(callerOptions)
            .FromText(reflJsonl)
            .ToList();
        Assert.Equal(initialConverterCount, callerOptions.Converters.Count);
        Assert.Equal(2, reflReadBack.Count);
        Assert.Equal(hugeNeg, reflReadBack[0].Value);
        Assert.Equal(hugePos, reflReadBack[0].OptionalValue);
        Assert.Equal(42, reflReadBack[1].Value);
        Assert.Null(reflReadBack[1].OptionalValue);

        // Default options reflection round-trip (async stream + Jsonl.DeserializeAsync)
        string defaultReflJsonl = Jsonl.WriteToText(reflInput);
        await using var reflMs = new MemoryStream(Encoding.UTF8.GetBytes(defaultReflJsonl));
        var reflAsyncBack = new List<JsonlBigIntegerReflectionRecord>();
        await foreach (var item in Jsonl.Read<JsonlBigIntegerReflectionRecord>().FromStreamAsync(reflMs, cancellationToken: ct))
        {
            reflAsyncBack.Add(item);
        }
        Assert.Equal(2, reflAsyncBack.Count);
        Assert.Equal(hugeNeg, reflAsyncBack[0].Value);

        reflMs.Position = 0;
        var pipeBack = new List<JsonlBigIntegerReflectionRecord>();
        await foreach (var item in Jsonl.DeserializeRecordsAsync<JsonlBigIntegerReflectionRecord>(reflMs, cancellationToken: ct))
        {
            pipeBack.Add(item);
        }
        Assert.Equal(2, pipeBack.Count);
        Assert.Equal(hugePos, pipeBack[0].OptionalValue);
    }

    [Fact]
    public void WideIntegerRangeValidation_ExceedingDoubleMantissaPrecision_EnforcedConsistently()
    {
        // 2^53 = 9007199254740992; 2^53 + 1 = 9007199254740993 rounds down to 2^53 in double
        const long maxExact = 9_007_199_254_740_992L;
        const long justAbove = 9_007_199_254_740_993L;

        var passing = new WideIntegerRangeRecord
        {
            BigSigned = maxExact,
            BigUnsigned = maxExact,
            Arbitrary = maxExact,
            WideLong = maxExact
        };

        var failing = new WideIntegerRangeRecord
        {
            BigSigned = justAbove,
            BigUnsigned = justAbove,
            Arbitrary = justAbove,
            WideLong = justAbove
        };

        // Passing boundary succeeds across strict writes and reads
        string okCsv = Csv.WriteToText([passing]);
        Assert.Single(Csv.Read<WideIntegerRangeRecord>().FromText(okCsv).ToList());
        string okFw = FixedWidth.WriteToText([passing]);
        Assert.Single(FixedWidth.Read<WideIntegerRangeRecord>().FromText(okFw).ToList());
        byte[] okXlsx = HeroParser.Excel.Write<WideIntegerRangeRecord>().ToBytes([passing]);
        using (var ms = new MemoryStream(okXlsx))
            Assert.Single(HeroParser.Excel.Read<WideIntegerRangeRecord>().FromStream(ms));
        using (var htbOk = new MemoryStream())
        {
            HeroParser.Htb.Write<WideIntegerRangeRecord>().ToStream(htbOk, [passing], leaveOpen: true);
            htbOk.Position = 0;
            Assert.Single(HeroParser.Htb.Read<WideIntegerRangeRecord>().FromStream(htbOk).ToList());
        }

        // Strict write rejection (WritePropertyValidation)
        Assert.Throws<ValidationException>(() => { _ = Csv.WriteToText([failing]); });
        Assert.Throws<ValidationException>(() => { _ = FixedWidth.WriteToText([failing]); });
        Assert.Throws<ValidationException>(() => { _ = HeroParser.Excel.SerializeRecords([failing]); });

        // Source-generated CSV, FixedWidth, JSONL reads
        const string badCsv = "BigSigned,BigUnsigned,Arbitrary,WideLong\n9007199254740993,9007199254740993,9007199254740993,9007199254740993\n";
        var badCsvResult = Csv.Read<WideIntegerRangeRecord>().WithValidationMode(ValidationMode.Lenient).FromText(badCsv);
        _ = badCsvResult.ToList();
        Assert.Equal(4, badCsvResult.Errors.Count);

        string badFw = $"{justAbove,-22}{justAbove,-22}{justAbove,-22}{justAbove,-22}";
        var badFwResult = FixedWidth.Read<WideIntegerRangeRecord>().WithValidationMode(ValidationMode.Lenient).FromText(badFw);
        _ = badFwResult.ToList();
        Assert.Equal(4, badFwResult.Errors.Count);

        const string badJsonl = "{\"BigSigned\":9007199254740993,\"BigUnsigned\":9007199254740993,\"Arbitrary\":9007199254740993,\"WideLong\":9007199254740993}\n";
        Assert.Throws<ValidationException>(() => { _ = Jsonl.Read<WideIntegerRangeRecord>().FromText(badJsonl).ToList(); });

        // Source-generated HTB read
        using (var htbBadGen = new MemoryStream())
        {
            HeroParser.Htb.Write<WideIntegerRangeRecord>().ToStream(htbBadGen, [failing], leaveOpen: true);
            htbBadGen.Position = 0;
            Assert.Throws<HtbException>(() => { _ = HeroParser.Htb.Read<WideIntegerRangeRecord>().FromStream(htbBadGen).ToList(); });
        }

        // Reflection HTB read
        var reflFailing = new WideIntegerRangeReflectionRecord
        {
            BigSigned = justAbove,
            BigUnsigned = justAbove,
            Arbitrary = justAbove,
            WideLong = justAbove
        };
        using (var htbBadRefl = new MemoryStream())
        {
            HeroParser.Htb.Write<WideIntegerRangeReflectionRecord>().ToStream(htbBadRefl, [reflFailing], leaveOpen: true);
            htbBadRefl.Position = 0;
            Assert.Throws<HtbException>(() => { _ = HeroParser.Htb.Read<WideIntegerRangeReflectionRecord>().FromStream(htbBadRefl).ToList(); });
        }

        // Fluent CsvMap / PropertyValidationRunner
        var map = new CsvMap<WideIntegerRangeReflectionRecord>()
            .Map(x => x.BigSigned, c => c.Name("BigSigned").Range(0, maxExact))
            .Map(x => x.BigUnsigned, c => c.Name("BigUnsigned").Range(0, maxExact))
            .Map(x => x.Arbitrary, c => c.Name("Arbitrary").Range(0, maxExact))
            .Map(x => x.WideLong, c => c.Name("WideLong").Range(0, maxExact));
        var mapResult = Csv.Read<WideIntegerRangeReflectionRecord>()
            .WithMap(map)
            .WithValidationMode(ValidationMode.Lenient)
            .FromText(badCsv);
        _ = mapResult.ToList();
        Assert.Equal(4, mapResult.Errors.Count);
    }

    [Theory]
    [InlineData("fa-IR")]
    [InlineData("ar-SA")]
    [InlineData("he-IL")]
    public void BidiMarkedCultures_AsciiMinus_SupportedForExtendedSignedTypes(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        const string fwLine = "-2    -2    -2    -2    ";
        const string csvText = "HalfVal,Int128Val,BigIntVal,SByteVal\n-2,-2,-2,-2\n";

        var fwGen = FixedWidth.Read<BidiCultureSignedGeneratedRecord>().WithCulture(culture).FromText(fwLine).ToList();
        Assert.Single(fwGen);
        Assert.Equal((Half)(-2), fwGen[0].HalfVal);
        Assert.Equal((Int128)(-2), fwGen[0].Int128Val);
        Assert.Equal(new BigInteger(-2), fwGen[0].BigIntVal);
        Assert.Equal((sbyte)-2, fwGen[0].SByteVal);

        using (var fwStream = new MemoryStream(Encoding.UTF8.GetBytes(fwLine)))
        {
            var fwGenStream = FixedWidth.Read<BidiCultureSignedGeneratedRecord>().WithCulture(culture).FromStream(fwStream).ToList();
            Assert.Single(fwGenStream);
            Assert.Equal((Half)(-2), fwGenStream[0].HalfVal);
            Assert.Equal((Int128)(-2), fwGenStream[0].Int128Val);
            Assert.Equal(new BigInteger(-2), fwGenStream[0].BigIntVal);
            Assert.Equal((sbyte)-2, fwGenStream[0].SByteVal);
        }

        var fwRefl = FixedWidth.Read<BidiCultureSignedReflectionRecord>().WithCulture(culture).FromText(fwLine).ToList();
        Assert.Single(fwRefl);
        Assert.Equal((Half)(-2), fwRefl[0].HalfVal);
        Assert.Equal((Int128)(-2), fwRefl[0].Int128Val);
        Assert.Equal(new BigInteger(-2), fwRefl[0].BigIntVal);
        Assert.Equal((sbyte)-2, fwRefl[0].SByteVal);

        var csvChar = Csv.Read<BidiCultureSignedGeneratedRecord>().WithCulture(culture).FromText(csvText).ToList();
        Assert.Single(csvChar);
        Assert.Equal((Half)(-2), csvChar[0].HalfVal);
        Assert.Equal((Int128)(-2), csvChar[0].Int128Val);
        Assert.Equal(new BigInteger(-2), csvChar[0].BigIntVal);
        Assert.Equal((sbyte)-2, csvChar[0].SByteVal);

        using var csvStream = new MemoryStream(Encoding.UTF8.GetBytes(csvText));
        using var csvReader = Csv.Read<BidiCultureSignedGeneratedRecord>().WithCulture(culture).FromStream(csvStream, out _);
        var csvByte = new List<BidiCultureSignedGeneratedRecord>();
        while (csvReader.MoveNext())
        {
            csvByte.Add(csvReader.Current);
        }
        csvReader.ThrowOnStrictErrors();
        Assert.Single(csvByte);
        Assert.Equal((Half)(-2), csvByte[0].HalfVal);
        Assert.Equal((Int128)(-2), csvByte[0].Int128Val);
        Assert.Equal(new BigInteger(-2), csvByte[0].BigIntVal);
        Assert.Equal((sbyte)-2, csvByte[0].SByteVal);
    }

    [Fact]
    public void Decimal_RangeBound03_ConsistentBetweenGeneratedAndReflectionOnReadAndStrictWrite()
    {
        var genOk = new DecimalExactBoundGeneratedRecord { Rate = 0.3m };
        var reflOk = new DecimalExactBoundReflectionRecord { Rate = 0.3m };

        Assert.Contains("0.3", Csv.WriteToText([genOk]), StringComparison.Ordinal);
        Assert.Contains("0.3", Csv.WriteToText([reflOk]), StringComparison.Ordinal);
        Assert.StartsWith("0.3", FixedWidth.WriteToText([genOk]), StringComparison.Ordinal);
        Assert.StartsWith("0.3", FixedWidth.WriteToText([reflOk]), StringComparison.Ordinal);

        var csvMap = new CsvMap<DecimalExactBoundReflectionRecord>()
            .Map(x => x.Rate, c => c.Name("Rate").Range(0.3, 0.3));

        const string okCsv = "Rate\n0.3\n";
        Assert.Equal(0.3m, Csv.Read<DecimalExactBoundGeneratedRecord>().FromText(okCsv).ToList()[0].Rate);
        Assert.Equal(0.3m, Csv.Read<DecimalExactBoundReflectionRecord>().WithMap(csvMap).FromText(okCsv).ToList()[0].Rate);

        const string okFw = "0.3     ";
        Assert.Equal(0.3m, FixedWidth.Read<DecimalExactBoundGeneratedRecord>().FromText(okFw).ToList()[0].Rate);
        Assert.Equal(0.3m, FixedWidth.Read<DecimalExactBoundReflectionRecord>().FromText(okFw).ToList()[0].Rate);

        using (var genHtb = new MemoryStream())
        {
            HeroParser.Htb.Write<DecimalExactBoundGeneratedRecord>().ToStream(genHtb, [genOk], leaveOpen: true);
            genHtb.Position = 0;
            Assert.Equal(0.3m, HeroParser.Htb.Read<DecimalExactBoundGeneratedRecord>().FromStream(genHtb).ToList()[0].Rate);
        }

        using (var reflHtb = new MemoryStream())
        {
            HeroParser.Htb.Write<DecimalExactBoundReflectionRecord>().ToStream(reflHtb, [reflOk], leaveOpen: true);
            reflHtb.Position = 0;
            Assert.Equal(0.3m, HeroParser.Htb.Read<DecimalExactBoundReflectionRecord>().FromStream(reflHtb).ToList()[0].Rate);
        }

        foreach (decimal badRate in (decimal[])[0.29m, 0.31m])
        {
            var genBad = new DecimalExactBoundGeneratedRecord { Rate = badRate };
            var reflBad = new DecimalExactBoundReflectionRecord { Rate = badRate };

            Assert.Throws<ValidationException>(() => { _ = Csv.WriteToText([genBad]); });
            Assert.Throws<ValidationException>(() => { _ = Csv.WriteToText([reflBad]); });
            Assert.Throws<ValidationException>(() => { _ = FixedWidth.WriteToText([genBad]); });
            Assert.Throws<ValidationException>(() => { _ = FixedWidth.WriteToText([reflBad]); });

            string badCsv = $"Rate\n{badRate.ToString(CultureInfo.InvariantCulture)}\n";
            var genLenient = Csv.Read<DecimalExactBoundGeneratedRecord>().WithValidationMode(ValidationMode.Lenient).FromText(badCsv);
            _ = genLenient.ToList();
            Assert.Single(genLenient.Errors);

            var reflLenient = Csv.Read<DecimalExactBoundReflectionRecord>().WithMap(csvMap).WithValidationMode(ValidationMode.Lenient).FromText(badCsv);
            _ = reflLenient.ToList();
            Assert.Single(reflLenient.Errors);
        }
    }

    [Fact]
    public void NumericRangeCheck_ValidSpanEvaluation_DoesNotAllocate()
    {
        var check = new NumericRangeCheck(0, 100);
        var largeCheck = new NumericRangeCheck(0, 1e25);
        ReadOnlySpan<char> validInt = "42".AsSpan();
        ReadOnlySpan<char> validFloat = "42.5".AsSpan();
        ReadOnlySpan<char> longFloat = "3.14159265358979323846".AsSpan();
        ReadOnlySpan<char> largeInt128 = "9223372036854775808".AsSpan();

        // Warm up JIT and static initializers before measuring allocations.
        Assert.True(check.TryEvaluateSpan(validInt, CultureInfo.InvariantCulture, out bool belowMin, out bool aboveMax, out string formatted));
        Assert.False(belowMin);
        Assert.False(aboveMax);
        Assert.Equal(string.Empty, formatted);

        Assert.True(check.TryEvaluateSpan(validFloat, CultureInfo.InvariantCulture, out belowMin, out aboveMax, out formatted));
        Assert.False(belowMin);
        Assert.False(aboveMax);
        Assert.Equal(string.Empty, formatted);

        Assert.True(check.TryEvaluateSpan(longFloat, CultureInfo.InvariantCulture, out belowMin, out aboveMax, out formatted));
        Assert.False(belowMin);
        Assert.False(aboveMax);
        Assert.Equal(string.Empty, formatted);

        Assert.True(largeCheck.TryEvaluateSpan(largeInt128, CultureInfo.InvariantCulture, out belowMin, out aboveMax, out formatted));
        Assert.False(belowMin);
        Assert.False(aboveMax);
        Assert.Equal(string.Empty, formatted);

#if !DEBUG
        long before = GC.GetAllocatedBytesForCurrentThread();
#endif
        bool intOk = check.TryEvaluateSpan(validInt, CultureInfo.InvariantCulture, out bool intBelow, out bool intAbove, out string intFormatted);
        bool floatOk = check.TryEvaluateSpan(validFloat, CultureInfo.InvariantCulture, out bool floatBelow, out bool floatAbove, out string floatFormatted);
        bool longFloatOk = check.TryEvaluateSpan(longFloat, CultureInfo.InvariantCulture, out bool longFloatBelow, out bool longFloatAbove, out string longFloatFormatted);
        bool largeIntOk = largeCheck.TryEvaluateSpan(largeInt128, CultureInfo.InvariantCulture, out bool largeIntBelow, out bool largeIntAbove, out string largeIntFormatted);
#if !DEBUG
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
#endif

        Assert.True(intOk && !intBelow && !intAbove && intFormatted.Length == 0);
        Assert.True(floatOk && !floatBelow && !floatAbove && floatFormatted.Length == 0);
        Assert.True(longFloatOk && !longFloatBelow && !longFloatAbove && longFloatFormatted.Length == 0);
        Assert.True(largeIntOk && !largeIntBelow && !largeIntAbove && largeIntFormatted.Length == 0);
#if !DEBUG
        Assert.Equal(0L, allocated);
#endif

        var bigIntCheck = new NumericRangeCheck(0, 1e30);
        Assert.True(bigIntCheck.TryEvaluateSpan("1234567890123456789012345678901234567890".AsSpan(), CultureInfo.InvariantCulture, out belowMin, out aboveMax, out formatted));
        Assert.False(belowMin);
        Assert.True(aboveMax);
        Assert.Equal("1234567890123456789012345678901234567890", formatted);

        Assert.True(bigIntCheck.TryEvaluateSpan("-1234567890123456789012345678901234567890".AsSpan(), CultureInfo.InvariantCulture, out belowMin, out aboveMax, out formatted));
        Assert.True(belowMin);
        Assert.False(aboveMax);
        Assert.Equal("-1234567890123456789012345678901234567890", formatted);

        Assert.True(largeCheck.TryEvaluateSpan("-9223372036854775809".AsSpan(), CultureInfo.GetCultureInfo("fa-IR"), out belowMin, out aboveMax, out formatted));
        Assert.True(belowMin);
        Assert.False(aboveMax);
        Assert.Equal("-9223372036854775809", formatted);
    }

    [Fact]
    public void CsvSchemaInference_DistinguishTemporalTypes_TimeWithOffsetOnly_PreservesAsString()
    {
        const string csv = "OffsetPlus,OffsetMinus,UtcTime\n09:30+02:00,14:15-05:00,23:59Z\n";
        var result = Csv.InferSchema(csv, new CsvSchemaInferenceOptions { DistinguishTemporalTypes = true });

        Assert.Equal(CsvInferredType.String, result.Columns[0].InferredType);
        Assert.Equal(CsvInferredType.String, result.Columns[1].InferredType);
        Assert.Equal(CsvInferredType.String, result.Columns[2].InferredType);
    }

    [Fact]
    public void CsvSchemaInference_PartialDates_PreservedAsStringWithoutInventingYear()
    {
        const string csv =
            "SlashDayMonth,MonthDay,YearMonth,NamedMonthYear,FullIso,FullMonthName,MinDate\n" +
            "11/25,Nov 25,2026-11,Nov 2026,2026-11-25,\"Oct 25, 2026\",0001-01-01\n";

        var defaultSchema = Csv.InferSchema(csv);
        Assert.Equal(CsvInferredType.String, defaultSchema.Columns[0].InferredType);
        Assert.Equal(CsvInferredType.String, defaultSchema.Columns[1].InferredType);
        Assert.Equal(CsvInferredType.String, defaultSchema.Columns[2].InferredType);
        Assert.Equal(CsvInferredType.String, defaultSchema.Columns[3].InferredType);
        Assert.Equal(CsvInferredType.DateTime, defaultSchema.Columns[4].InferredType);
        Assert.Equal(CsvInferredType.DateTime, defaultSchema.Columns[5].InferredType);
        Assert.Equal(CsvInferredType.DateTime, defaultSchema.Columns[6].InferredType);

        var typedSchema = Csv.InferSchema(csv, new CsvSchemaInferenceOptions { DistinguishTemporalTypes = true });
        Assert.Equal(CsvInferredType.String, typedSchema.Columns[0].InferredType);
        Assert.Equal(CsvInferredType.String, typedSchema.Columns[1].InferredType);
        Assert.Equal(CsvInferredType.String, typedSchema.Columns[2].InferredType);
        Assert.Equal(CsvInferredType.String, typedSchema.Columns[3].InferredType);
        Assert.Equal(CsvInferredType.DateOnly, typedSchema.Columns[4].InferredType);
        Assert.Equal(CsvInferredType.DateOnly, typedSchema.Columns[5].InferredType);
        Assert.Equal(CsvInferredType.DateOnly, typedSchema.Columns[6].InferredType);

        const string partialCsv = "Label,MonthDay\r\na,11/25\r\n";
        var inferred = Csv.InferSchema(partialCsv, new CsvSchemaInferenceOptions { DistinguishTemporalTypes = true });
        Assert.Equal(CsvInferredType.String, inferred.Columns[1].InferredType);
        var htbSchema = new global::HeroParser.Htbs.Records.HtbSchema(
        [
            .. inferred.Columns.Select(c => new global::HeroParser.Htbs.Records.HtbColumn(
                c.Name,
                c.InferredType == CsvInferredType.DateOnly
                    ? global::HeroParser.Htbs.Records.HtbDataType.DateOnly
                    : global::HeroParser.Htbs.Records.HtbDataType.String,
                isNullable: true))
        ]);
        using var htbStream = new MemoryStream();
        HeroParser.Conversion.CsvToHtbConverter.Convert(partialCsv, htbStream, htbSchema);
        htbStream.Position = 0;
        using var sw = new StringWriter();
        HeroParser.Conversion.HtbToCsvConverter.Convert(htbStream, sw);
        Assert.Equal(partialCsv, sw.ToString());
    }

    [Fact]
    public void Excel_RoundTripsEmptyAndNullByteArrays_AcrossAllWritersAndReaders()
    {
        ExcelByteArrayGeneratedRecord[] generatedInput =
        [
            new() { RequiredBytes = [], OptionalBytes = null },
            new() { RequiredBytes = [], OptionalBytes = [] },
            new() { RequiredBytes = [1, 2, 3], OptionalBytes = [4, 5] }
        ];

        byte[] escapedXlsx = HeroParser.Excel.SerializeRecords(
            generatedInput,
            new HeroParser.Excels.Core.ExcelWriteOptions
            {
                InjectionProtection = HeroParser.Excels.Core.ExcelInjectionProtection.EscapeWithApostrophe
            });
        using (var ms = new MemoryStream(escapedXlsx))
        {
            var readBack = HeroParser.Excel.Read<ExcelByteArrayGeneratedRecord>().FromStream(ms);
            Assert.Equal(3, readBack.Count);
            Assert.Equal([], readBack[0].RequiredBytes);
            Assert.Null(readBack[0].OptionalBytes);
            Assert.Equal([], readBack[1].RequiredBytes);
            Assert.Empty(Assert.IsType<byte[]>(readBack[1].OptionalBytes));
            Assert.Equal([1, 2, 3], readBack[2].RequiredBytes);
            Assert.Equal([4, 5], readBack[2].OptionalBytes);
        }

        byte[] builderXlsx = HeroParser.Excel.Write<ExcelByteArrayGeneratedRecord>().ToBytes(generatedInput);
        using (var ms = new MemoryStream(builderXlsx))
        {
            var builderBack = HeroParser.Excel.Read<ExcelByteArrayGeneratedRecord>().FromStream(ms);
            Assert.Equal(3, builderBack.Count);
            Assert.Null(builderBack[0].OptionalBytes);
            Assert.Empty(Assert.IsType<byte[]>(builderBack[1].OptionalBytes));
            Assert.Equal([4, 5], builderBack[2].OptionalBytes);
        }

        ExcelByteArrayReflectionRecord[] reflectionInput =
        [
            new() { RequiredBytes = [], OptionalBytes = null },
            new() { RequiredBytes = [], OptionalBytes = [] },
            new() { RequiredBytes = [7, 8], OptionalBytes = [9] }
        ];
        var csvMap = new CsvMap<ExcelByteArrayReflectionRecord>()
            .Map(x => x.RequiredBytes)
            .Map(x => x.OptionalBytes);
        byte[] mapXlsx = HeroParser.Excel.Write<ExcelByteArrayReflectionRecord>()
            .WithMap(csvMap)
            .ToBytes(reflectionInput);
        using (var mapMs = new MemoryStream(mapXlsx))
        {
            var mapReadBack = HeroParser.Excel.Read<ExcelByteArrayReflectionRecord>().WithMap(csvMap).FromStream(mapMs);
            Assert.Equal(3, mapReadBack.Count);
            Assert.Equal([], mapReadBack[0].RequiredBytes);
            Assert.Null(mapReadBack[0].OptionalBytes);
            Assert.Equal([], mapReadBack[1].RequiredBytes);
            Assert.Empty(Assert.IsType<byte[]>(mapReadBack[1].OptionalBytes));
            Assert.Equal([7, 8], mapReadBack[2].RequiredBytes);
            Assert.Equal([9], mapReadBack[2].OptionalBytes);
        }

        using var table = new System.Data.DataTable();
        table.Columns.Add(nameof(ExcelByteArrayGeneratedRecord.RequiredBytes), typeof(byte[]));
        table.Columns.Add(nameof(ExcelByteArrayGeneratedRecord.OptionalBytes), typeof(byte[]));
        table.Rows.Add(Array.Empty<byte>(), DBNull.Value);
        table.Rows.Add(Array.Empty<byte>(), Array.Empty<byte>());
        table.Rows.Add(new byte[] { 10, 20 }, new byte[] { 30 });

        using var reader = table.CreateDataReader();
        byte[] drXlsx = HeroParser.Excel.WriteFromDataReaderToBytes(reader);
        using (var drMs = new MemoryStream(drXlsx))
        {
            var drBack = HeroParser.Excel.Read<ExcelByteArrayGeneratedRecord>().FromStream(drMs);
            Assert.Equal(3, drBack.Count);
            Assert.Equal([], drBack[0].RequiredBytes);
            Assert.Null(drBack[0].OptionalBytes);
            Assert.Equal([], drBack[1].RequiredBytes);
            Assert.Empty(Assert.IsType<byte[]>(drBack[1].OptionalBytes));
            Assert.Equal([10, 20], drBack[2].RequiredBytes);
            Assert.Equal([30], drBack[2].OptionalBytes);
        }

        // Empty headerless typed Excel export must enforce MaxOutputSize after finalizing sheet XML.
        Assert.Throws<HeroParser.Excels.Core.ExcelException>(() =>
            HeroParser.Excel.Write<ExcelByteArrayGeneratedRecord>()
                .WithoutHeader()
                .WithMaxOutputSize(1)
                .ToBytes([]));
    }

    [Fact]
    public async Task CsvAndFixedWidth_RoundTripEmptyAndNullByteArrays_AcrossAllWritersAndReadersAsync()
    {
        var ct = TestContext.Current.CancellationToken;

        ExcelByteArrayGeneratedRecord[] csvGenInput =
        [
            new() { RequiredBytes = [], OptionalBytes = null },
            new() { RequiredBytes = [], OptionalBytes = [] },
            new() { RequiredBytes = [1, 2, 3], OptionalBytes = [4, 5] }
        ];

        string genCsvText = Csv.WriteToText(csvGenInput);
        AssertCsvByteArrayRecords(Csv.DeserializeRecords<ExcelByteArrayGeneratedRecord>(genCsvText).ToList());
        using (var genCsvMs = new MemoryStream(Encoding.UTF8.GetBytes(genCsvText)))
        {
            AssertCsvByteArrayRecords(ReadCsvStream(genCsvMs));
        }

        using (var asyncCsvMs = new MemoryStream())
        {
            await Csv.Write<ExcelByteArrayGeneratedRecord>().ToStreamFromEnumerableAsync(asyncCsvMs, csvGenInput, leaveOpen: true, ct);
            asyncCsvMs.Position = 0;
            AssertCsvByteArrayRecords(ReadCsvStream(asyncCsvMs));
        }

        static List<ExcelByteArrayGeneratedRecord> ReadCsvStream(MemoryStream stream)
        {
            var list = new List<ExcelByteArrayGeneratedRecord>();
            using var reader = Csv.Read<ExcelByteArrayGeneratedRecord>().FromStream(stream, out _);
            while (reader.MoveNext())
                list.Add(reader.Current);
            reader.ThrowOnStrictErrors();
            return list;
        }

        ExcelByteArrayReflectionRecord[] csvReflInput =
        [
            new() { RequiredBytes = [], OptionalBytes = null },
            new() { RequiredBytes = [], OptionalBytes = [] },
            new() { RequiredBytes = [1, 2, 3], OptionalBytes = [4, 5] }
        ];
        var csvMap = new CsvMap<ExcelByteArrayReflectionRecord>()
            .Map(x => x.RequiredBytes)
            .Map(x => x.OptionalBytes);
        string mapCsvText = Csv.Write<ExcelByteArrayReflectionRecord>().WithMap(csvMap).ToText(csvReflInput);
        AssertCsvByteArrayReflectionRecords(Csv.Read<ExcelByteArrayReflectionRecord>().WithMap(csvMap).FromText(mapCsvText).ToList());
        using (var mapCsvMs = new MemoryStream(Encoding.UTF8.GetBytes(mapCsvText)))
        {
            AssertCsvByteArrayReflectionRecords(Csv.Read<ExcelByteArrayReflectionRecord>().WithMap(csvMap).FromStream(mapCsvMs).ToList());
        }

        using (var table = new System.Data.DataTable())
        {
            table.Columns.Add(nameof(ExcelByteArrayGeneratedRecord.RequiredBytes), typeof(byte[]));
            table.Columns.Add(nameof(ExcelByteArrayGeneratedRecord.OptionalBytes), typeof(byte[]));
            table.Rows.Add(Array.Empty<byte>(), DBNull.Value);
            table.Rows.Add(Array.Empty<byte>(), Array.Empty<byte>());
            table.Rows.Add(new byte[] { 1, 2, 3 }, new byte[] { 4, 5 });

            using var dr = table.CreateDataReader();
            string drCsv = Csv.WriteFromDataReaderToText(dr);
            AssertCsvByteArrayRecords(Csv.DeserializeRecords<ExcelByteArrayGeneratedRecord>(drCsv).ToList());

            using var dr2 = table.CreateDataReader();
            var fwCols = new FixedWidthFieldDefinition[]
            {
                new(nameof(FixedWidthByteArrayGeneratedRecord.RequiredBytes), 12),
                new(nameof(FixedWidthByteArrayGeneratedRecord.OptionalBytes), 12)
            };
            using var fwSw = new StringWriter();
            FixedWidth.WriteFromDataReader(dr2, fwSw, fwCols);
            AssertFwByteArrayGeneratedRecords([.. FixedWidth.DeserializeRecords<FixedWidthByteArrayGeneratedRecord>(fwSw.ToString())]);
        }

        FixedWidthByteArrayGeneratedRecord[] fwGenInput =
        [
            new() { RequiredBytes = [], OptionalBytes = null },
            new() { RequiredBytes = [], OptionalBytes = [] },
            new() { RequiredBytes = [1, 2, 3], OptionalBytes = [4, 5] }
        ];
        string genFwText = FixedWidth.WriteToText(fwGenInput);
        AssertFwByteArrayGeneratedRecords([.. FixedWidth.DeserializeRecords<FixedWidthByteArrayGeneratedRecord>(genFwText)]);
        using (var genFwMs = new MemoryStream(Encoding.UTF8.GetBytes(genFwText)))
        {
            AssertFwByteArrayGeneratedRecords([.. FixedWidth.Read<FixedWidthByteArrayGeneratedRecord>().FromStream(genFwMs)]);
        }

        using (var asyncFwMs = new MemoryStream())
        {
            await FixedWidth.Write<FixedWidthByteArrayGeneratedRecord>().ToStreamAsync(asyncFwMs, fwGenInput, leaveOpen: true, ct);
            asyncFwMs.Position = 0;
            AssertFwByteArrayGeneratedRecords([.. FixedWidth.Read<FixedWidthByteArrayGeneratedRecord>().FromStream(asyncFwMs)]);
        }

        FixedWidthByteArrayReflectionRecord[] fwReflInput =
        [
            new() { RequiredBytes = [], OptionalBytes = null },
            new() { RequiredBytes = [], OptionalBytes = [] },
            new() { RequiredBytes = [1, 2, 3], OptionalBytes = [4, 5] }
        ];
        string reflFwText = FixedWidth.WriteToText(fwReflInput);
        AssertFwByteArrayReflectionRecords([.. FixedWidth.DeserializeRecords<FixedWidthByteArrayReflectionRecord>(reflFwText)]);
        using (var reflFwMs = new MemoryStream(Encoding.UTF8.GetBytes(reflFwText)))
        {
            AssertFwByteArrayReflectionRecords([.. FixedWidth.Read<FixedWidthByteArrayReflectionRecord>().FromStream(reflFwMs)]);
        }

        var fwMap = new FixedWidthMap<ExcelByteArrayReflectionRecord>()
            .Map(x => x.RequiredBytes, c => c.Start(0).Length(12))
            .Map(x => x.OptionalBytes, c => c.Start(12).Length(12));
        string mapFwText = FixedWidth.Write<ExcelByteArrayReflectionRecord>().WithMap(fwMap).ToText(csvReflInput);
        AssertCsvByteArrayReflectionRecords([.. FixedWidth.Read<ExcelByteArrayReflectionRecord>().WithMap(fwMap).FromText(mapFwText)]);
        using var mapFwMs = new MemoryStream(Encoding.UTF8.GetBytes(mapFwText));
        AssertCsvByteArrayReflectionRecords([.. FixedWidth.Read<ExcelByteArrayReflectionRecord>().WithMap(fwMap).FromStream(mapFwMs)]);

        static void AssertCsvByteArrayRecords(List<ExcelByteArrayGeneratedRecord> records)
        {
            Assert.Equal(3, records.Count);
            Assert.Empty(records[0].RequiredBytes);
            Assert.Null(records[0].OptionalBytes);
            Assert.Empty(records[1].RequiredBytes);
            Assert.Empty(Assert.IsType<byte[]>(records[1].OptionalBytes));
            Assert.Equal([1, 2, 3], records[2].RequiredBytes);
            Assert.Equal([4, 5], records[2].OptionalBytes);
        }

        static void AssertCsvByteArrayReflectionRecords(List<ExcelByteArrayReflectionRecord> records)
        {
            Assert.Equal(3, records.Count);
            Assert.Empty(records[0].RequiredBytes);
            Assert.Null(records[0].OptionalBytes);
            Assert.Empty(records[1].RequiredBytes);
            Assert.Empty(Assert.IsType<byte[]>(records[1].OptionalBytes));
            Assert.Equal([1, 2, 3], records[2].RequiredBytes);
            Assert.Equal([4, 5], records[2].OptionalBytes);
        }

        static void AssertFwByteArrayGeneratedRecords(List<FixedWidthByteArrayGeneratedRecord> records)
        {
            Assert.Equal(3, records.Count);
            Assert.Empty(records[0].RequiredBytes);
            Assert.Null(records[0].OptionalBytes);
            Assert.Empty(records[1].RequiredBytes);
            Assert.Empty(Assert.IsType<byte[]>(records[1].OptionalBytes));
            Assert.Equal([1, 2, 3], records[2].RequiredBytes);
            Assert.Equal([4, 5], records[2].OptionalBytes);
        }

        static void AssertFwByteArrayReflectionRecords(List<FixedWidthByteArrayReflectionRecord> records)
        {
            Assert.Equal(3, records.Count);
            Assert.Empty(records[0].RequiredBytes);
            Assert.Null(records[0].OptionalBytes);
            Assert.Empty(records[1].RequiredBytes);
            Assert.Empty(Assert.IsType<byte[]>(records[1].OptionalBytes));
            Assert.Equal([1, 2, 3], records[2].RequiredBytes);
            Assert.Equal([4, 5], records[2].OptionalBytes);
        }
    }

    [Fact]
    public async Task Jsonl_BigIntegerJsonConverter_RoundTripsAotJsonTypeInfoAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        JsonlBigIntegerAotRecord[] records =
        [
            new()
            {
                RequiredBigInt = -BigInteger.Pow(2, 200),
                OptionalBigInt = null
            },
            new()
            {
                RequiredBigInt = BigInteger.Parse("1234567890123456789012345678901234567890", CultureInfo.InvariantCulture),
                OptionalBigInt = BigInteger.Parse("9999999999999999999999999999999999999999", CultureInfo.InvariantCulture)
            }
        ];

        string jsonl = Jsonl.SerializeRecords(records, options: null, JsonlBigIntegerAotContext.Default.JsonlBigIntegerAotRecord);
        Assert.DoesNotContain("IsPowerOfTwo", jsonl, StringComparison.Ordinal);
        Assert.Contains("1234567890123456789012345678901234567890", jsonl, StringComparison.Ordinal);

        var viaTypeInfo = Jsonl.DeserializeRecords(jsonl, JsonlBigIntegerAotContext.Default.JsonlBigIntegerAotRecord).ToList();
        var viaBinder = Jsonl.Read<JsonlBigIntegerAotRecord>().FromText(jsonl).ToList();
        Assert.Equal(2, viaTypeInfo.Count);
        Assert.Equal(2, viaBinder.Count);

        for (int i = 0; i < 2; i++)
        {
            Assert.Equal(records[i].RequiredBigInt, viaTypeInfo[i].RequiredBigInt);
            Assert.Equal(records[i].OptionalBigInt, viaTypeInfo[i].OptionalBigInt);
            Assert.Equal(records[i].RequiredBigInt, viaBinder[i].RequiredBigInt);
            Assert.Equal(records[i].OptionalBigInt, viaBinder[i].OptionalBigInt);
        }

        await using var ms = new MemoryStream();
        await Jsonl.WriteEnumerableToStreamAsync(ms, records, options: null, JsonlBigIntegerAotContext.Default.JsonlBigIntegerAotRecord, leaveOpen: true, cancellationToken: ct);
        ms.Position = 0;
        var asyncRoundTrip = new List<JsonlBigIntegerAotRecord>();
        await foreach (var item in Jsonl.Read<JsonlBigIntegerAotRecord>().FromStreamAsync(ms, JsonlBigIntegerAotContext.Default.JsonlBigIntegerAotRecord, cancellationToken: ct))
        {
            asyncRoundTrip.Add(item);
        }
        Assert.Equal(2, asyncRoundTrip.Count);
        Assert.Equal(records[0].RequiredBigInt, asyncRoundTrip[0].RequiredBigInt);
        Assert.True(asyncRoundTrip[0].OptionalBigInt is null);
        Assert.Equal(records[1].OptionalBigInt, asyncRoundTrip[1].OptionalBigInt);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void NumericRangeCheck_InexactDecimalBounds_PreserveComparisonDirection()
    {
        var inv = CultureInfo.InvariantCulture;

        // RangeMax = 6e-29 sits between 0m and 1e-28m: 0m is valid, 1e-28m must be rejected.
        var maxSmallPos = new NumericRangeCheck(null, 6e-29);
        Assert.False(maxSmallPos.TryEvaluate(0m, inv, out bool below, out bool above, out _) && (below || above));
        Assert.True(maxSmallPos.TryEvaluate(1e-28m, inv, out below, out above, out _));
        Assert.False(below);
        Assert.True(above);

        // RangeMax = 1.4e-28 sits between 1e-28m and 2e-28m: 1e-28m is valid, 2e-28m must be rejected.
        var maxMidPos = new NumericRangeCheck(null, 1.4e-28);
        Assert.True(maxMidPos.TryEvaluate(1e-28m, inv, out below, out above, out _));
        Assert.False(below || above);
        Assert.True(maxMidPos.TryEvaluate(2e-28m, inv, out _, out above, out _));
        Assert.True(above);

        // RangeMin = 1.4e-28 sits between 1e-28m and 2e-28m: 1e-28m must be rejected, 2e-28m is valid.
        var minMidPos = new NumericRangeCheck(1.4e-28, null);
        Assert.True(minMidPos.TryEvaluate(1e-28m, inv, out below, out _, out _));
        Assert.True(below);
        Assert.True(minMidPos.TryEvaluate(2e-28m, inv, out below, out above, out _));
        Assert.False(below || above);

        // RangeMax = -6e-29 sits between -1e-28m and 0m: 0m must be rejected, -1e-28m is valid.
        var maxSmallNeg = new NumericRangeCheck(null, -6e-29);
        Assert.True(maxSmallNeg.TryEvaluate(0m, inv, out _, out above, out _));
        Assert.True(above);
        Assert.True(maxSmallNeg.TryEvaluate(-1e-28m, inv, out below, out above, out _));
        Assert.False(below || above);

        // RangeMin = -1.4e-28 sits between -2e-28m and -1e-28m: -1e-28m is valid, -2e-28m must be rejected.
        var minMidNeg = new NumericRangeCheck(-1.4e-28, null);
        Assert.True(minMidNeg.TryEvaluate(-1e-28m, inv, out below, out above, out _));
        Assert.False(below || above);
        Assert.True(minMidNeg.TryEvaluate(-2e-28m, inv, out below, out _, out _));
        Assert.True(below);

        // Subnormal positive/negative bounds (< 1e-28).
        var minEps = new NumericRangeCheck(double.Epsilon, null);
        Assert.True(minEps.TryEvaluate(0m, inv, out below, out _, out _));
        Assert.True(below);
        Assert.True(minEps.TryEvaluate(1e-28m, inv, out below, out above, out _));
        Assert.False(below || above);

        var maxNegEps = new NumericRangeCheck(null, -double.Epsilon);
        Assert.True(maxNegEps.TryEvaluate(0m, inv, out _, out above, out _));
        Assert.True(above);
        Assert.True(maxNegEps.TryEvaluate(-1e-28m, inv, out below, out above, out _));
        Assert.False(below || above);

        // Exact representable bounds keep inclusive acceptance at the boundary.
        var exactRange = new NumericRangeCheck(0.1, 99.99);
        Assert.True(exactRange.TryEvaluate(0.1m, inv, out below, out above, out _));
        Assert.False(below || above);
        Assert.True(exactRange.TryEvaluate(99.99m, inv, out below, out above, out _));
        Assert.False(below || above);

        // Negative zero (-0.0) treats 0m as valid on both ends.
        var negZeroRange = new NumericRangeCheck(-0.0, -0.0);
        Assert.True(negZeroRange.TryEvaluate(0m, inv, out below, out above, out _));
        Assert.False(below || above);
    }
}
