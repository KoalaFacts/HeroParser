#pragma warning disable IDE0010 // Populate switch
#pragma warning disable IDE0066 // Use 'switch' expression
#pragma warning disable IDE0302 // Simplify collection initialization

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HeroParser.Htbs.Records;
using HeroParser.Validation;

namespace HeroParser.Htbs.Reading;

/// <summary>
/// Streams and deserializes records from a High-Throughput Tabular Binary (HTB) stream.
/// </summary>
public sealed class HtbRecordReader<T> : HtbStreamReader where T : new()
{
    private readonly HtbReadOptions options;
    private IHtbBinder<T>? binder;
    private bool recordHeaderParsed;

    /// <summary>
    /// Gets the most recently read record (for async enumerable consumption).
    /// </summary>
    public T? CurrentRecord { get; private set; }

    /// <summary>
    /// Initializes a new instance of <see cref="HtbRecordReader{T}"/>.
    /// </summary>
    public HtbRecordReader(Stream stream, HtbReadOptions? options = null, bool leaveOpen = false)
        : base(stream, options, leaveOpen)
    {
        this.options = options ?? HtbReadOptions.Default;
    }

    /// <summary>
    /// Reads the next record synchronously from the stream.
    /// </summary>
    [RequiresUnreferencedCode("Reflection-based HTB deserialization requires dynamic binding which is not Native AOT-safe.")]
    public bool ReadNext([NotNullWhen(true)] out T? record)
    {
        if (!recordHeaderParsed)
        {
            ParseRecordHeader();
        }

        while (RecordsRead < options.MaxRowCount)
        {
            try
            {
                if (IsEndOfStream())
                {
                    record = default;
                    return false;
                }

                T rec = new();
                int colCount = Schema!.Columns.Count;
                int maskLen = (colCount + 7) / 8;

                byte[]? rented = null;
                Span<byte> mask = maskLen <= 128
                    ? stackalloc byte[maskLen]
                    : (rented = ArrayPool<byte>.Shared.Rent(maskLen)).AsSpan(0, maskLen);
                try
                {
                    ReadMask(mask);

                    for (int i = 0; i < colCount; i++)
                    {
                        bool isNull = (mask[i / 8] & (1 << (i % 8))) != 0;
                        if (isNull)
                        {
                            if (binder!.IsColumnBound(i))
                            {
                                binder!.BindField(rec, i, this, true);
                            }
                            continue;
                        }

                        if (!binder!.IsColumnBound(i))
                        {
                            SkipColumnValue(Schema.Columns[i].DataType);
                        }
                        else
                        {
                            binder!.BindField(rec, i, this, false);
                        }
                    }
                }
                finally
                {
                    if (rented != null)
                    {
                        ArrayPool<byte>.Shared.Return(rented);
                    }
                }

                IncrementRecordCount();

                // Enforce SkipRows
                if (RecordsRead <= options.SkipRows)
                {
                    continue;
                }

                // Run field validations
                ValidateRecord(rec);

                record = rec;
                return true;
            }
            catch (Exception ex)
            {
                if (ex is HtbException htbEx && (htbEx.ErrorCode == HtbErrorCode.CorruptData || htbEx.ErrorCode == HtbErrorCode.LimitExceeded))
                {
                    throw;
                }

                var context = new HtbDeserializeErrorContext
                {
                    RecordIndex = RecordsRead,
                    TargetType = typeof(T)
                };

                var defaultAction = (options.ValidationMode == ValidationMode.Lenient && ex is HtbException hex && hex.ErrorCode == HtbErrorCode.DeserializationError)
                    ? HtbDeserializeErrorAction.SkipRecord
                    : HtbDeserializeErrorAction.Throw;

                var action = options.OnError?.Invoke(context, ex) ?? defaultAction;
                if (action == HtbDeserializeErrorAction.Throw)
                {
                    throw;
                }
            }
        }

        if (RecordsRead >= options.MaxRowCount && !IsEndOfStream())
        {
            throw new HtbException(
                HtbErrorCode.LimitExceeded,
                $"MaxRowCount limit of {options.MaxRowCount} was exceeded.");
        }

        record = default;
        return false;
    }

    /// <summary>
    /// Reads the next record asynchronously from the stream.
    /// </summary>
    [RequiresUnreferencedCode("Reflection-based HTB deserialization requires dynamic binding which is not Native AOT-safe.")]
    public ValueTask<bool> ReadNextAsync()
        => ReadNextAsync(CancellationToken.None);

    /// <summary>
    /// Reads the next record asynchronously from the stream with cancellation support.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel async I/O and stop reading.</param>
    [RequiresUnreferencedCode("Reflection-based HTB deserialization requires dynamic binding which is not Native AOT-safe.")]
    public async ValueTask<bool> ReadNextAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        activeCancellationToken = cancellationToken;
        try
        {
            if (!recordHeaderParsed)
            {
                await ParseRecordHeaderAsync(cancellationToken).ConfigureAwait(false);
            }

            while (RecordsRead < options.MaxRowCount)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (await IsEndOfStreamAsync(cancellationToken).ConfigureAwait(false))
                    {
                        CurrentRecord = default;
                        return false;
                    }

                    T rec = new();
                    int colCount = Schema!.Columns.Count;
                    int maskLen = (colCount + 7) / 8;

                    byte[] rented = ArrayPool<byte>.Shared.Rent(maskLen);
                    try
                    {
                        var mask = rented.AsMemory(0, maskLen);
                        await ReadMaskAsync(mask, cancellationToken).ConfigureAwait(false);

                        for (int i = 0; i < colCount; i++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            bool isNull = (rented[i / 8] & (1 << (i % 8))) != 0;
                            if (isNull)
                            {
                                if (binder!.IsColumnBound(i))
                                {
                                    await binder!.BindFieldAsync(rec, i, this, true).ConfigureAwait(false);
                                }
                                continue;
                            }

                            if (!binder!.IsColumnBound(i))
                            {
                                await SkipColumnValueAsync(Schema.Columns[i].DataType, cancellationToken).ConfigureAwait(false);
                            }
                            else
                            {
                                await binder!.BindFieldAsync(rec, i, this, false).ConfigureAwait(false);
                            }
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(rented);
                    }

                    IncrementRecordCount();

                    // Enforce SkipRows
                    if (RecordsRead <= options.SkipRows)
                    {
                        continue;
                    }

                    // Run field validations
                    ValidateRecord(rec);

                    CurrentRecord = rec;
                    return true;
                }
                catch (Exception ex)
                {
                    if (ex is OperationCanceledException)
                    {
                        throw;
                    }

                    if (ex is HtbException htbEx && (htbEx.ErrorCode == HtbErrorCode.CorruptData || htbEx.ErrorCode == HtbErrorCode.LimitExceeded))
                    {
                        throw;
                    }

                    var context = new HtbDeserializeErrorContext
                    {
                        RecordIndex = RecordsRead,
                        TargetType = typeof(T)
                    };

                    var defaultAction = (options.ValidationMode == ValidationMode.Lenient && ex is HtbException hex && hex.ErrorCode == HtbErrorCode.DeserializationError)
                        ? HtbDeserializeErrorAction.SkipRecord
                        : HtbDeserializeErrorAction.Throw;

                    var action = options.OnError?.Invoke(context, ex) ?? defaultAction;
                    if (action == HtbDeserializeErrorAction.Throw)
                    {
                        throw;
                    }
                }
            }

            if (RecordsRead >= options.MaxRowCount && !await IsEndOfStreamAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new HtbException(
                    HtbErrorCode.LimitExceeded,
                    $"MaxRowCount limit of {options.MaxRowCount} was exceeded.");
            }

            CurrentRecord = default;
            return false;
        }
        finally
        {
            activeCancellationToken = default;
        }
    }

    [RequiresUnreferencedCode("Reflection schema extraction is not safe for Native AOT.")]
    private void ParseRecordHeader()
    {
        var fileSchema = ParseHeader();
        var registeredBinder = HtbRecordBinderFactory.TryGetBinder<T>(fileSchema);
        if (registeredBinder != null)
        {
            schema = fileSchema;
            binder = registeredBinder;
            recordHeaderParsed = true;
            return;
        }

        var targetSchema = HtbSchema.FromType<T>();
        var finalColumns = new List<HtbColumn>();

        foreach (var col in fileSchema.Columns)
        {
            var match = targetSchema.Columns.FirstOrDefault(c => string.Equals(c.Name, col.Name, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                finalColumns.Add(new HtbColumn(col.Name, col.DataType, isNullable: true));
            }
            else
            {
                if (match.DataType != col.DataType)
                {
                    throw new HtbException(
                        HtbErrorCode.SchemaMismatch,
                        $"Schema mismatch on column '{col.Name}': File has '{col.DataType}', Model has '{match.DataType}'.");
                }
                finalColumns.Add(new HtbColumn(col.Name, col.DataType, match.IsNullable, match.Property));
            }
        }

        schema = new HtbSchema(finalColumns);
        binder = HtbRecordBinderFactory.GetBinder<T>(schema);
        recordHeaderParsed = true;
    }

    [RequiresUnreferencedCode("Reflection schema extraction is not safe for Native AOT.")]
    private async Task ParseRecordHeaderAsync(CancellationToken cancellationToken)
    {
        var fileSchema = await ParseHeaderAsync(cancellationToken).ConfigureAwait(false);
        var registeredBinder = HtbRecordBinderFactory.TryGetBinder<T>(fileSchema);
        if (registeredBinder != null)
        {
            schema = fileSchema;
            binder = registeredBinder;
            recordHeaderParsed = true;
            return;
        }

        var targetSchema = HtbSchema.FromType<T>();
        var finalColumns = new List<HtbColumn>();

        foreach (var col in fileSchema.Columns)
        {
            var match = targetSchema.Columns.FirstOrDefault(c => string.Equals(c.Name, col.Name, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                finalColumns.Add(new HtbColumn(col.Name, col.DataType, isNullable: true));
            }
            else
            {
                if (match.DataType != col.DataType)
                {
                    throw new HtbException(
                        HtbErrorCode.SchemaMismatch,
                        $"Schema mismatch on column '{col.Name}': File has '{col.DataType}', Model has '{match.DataType}'.");
                }
                finalColumns.Add(new HtbColumn(col.Name, col.DataType, match.IsNullable, match.Property));
            }
        }

        schema = new HtbSchema(finalColumns);
        binder = HtbRecordBinderFactory.GetBinder<T>(schema);
        recordHeaderParsed = true;
    }

    internal object ReadColumnValueInternal(HtbDataType type)
    {
        switch (type)
        {
            case HtbDataType.Int32: return ReadInt32();
            case HtbDataType.Int64: return ReadInt64();
            case HtbDataType.Float: return ReadFloat();
            case HtbDataType.Double: return ReadDouble();
            case HtbDataType.Decimal: return ReadDecimal();
            case HtbDataType.Boolean: return ReadBoolean();
            case HtbDataType.DateTime: return ReadDateTime();
            case HtbDataType.Guid: return ReadGuid();
            case HtbDataType.String: return ReadString();
            case HtbDataType.FloatArray: return ReadFloatArray();
            case HtbDataType.DateTimeOffset: return ReadDateTimeOffset();
            case HtbDataType.DateOnly: return ReadDateOnly();
            case HtbDataType.TimeOnly: return ReadTimeOnly();
            case HtbDataType.TimeSpan: return ReadTimeSpan();
            case HtbDataType.Int16: return ReadInt16();
            case HtbDataType.UInt16: return ReadUInt16();
            case HtbDataType.Byte: return ReadByte();
            case HtbDataType.SByte: return ReadSByte();
            case HtbDataType.UInt32: return ReadUInt32();
            case HtbDataType.UInt64: return ReadUInt64();
            case HtbDataType.Char: return ReadChar();
            case HtbDataType.Half: return ReadHalf();
            case HtbDataType.Int128: return ReadInt128();
            case HtbDataType.UInt128: return ReadUInt128();
            case HtbDataType.ByteArray: return ReadByteArray();
            case HtbDataType.BigInteger: return ReadBigInteger();
            case HtbDataType.Uri: return ReadUri();
            default:
                throw new HtbException(HtbErrorCode.CorruptData, $"Unsupported Column DataType: {type}");
        }
    }

    internal async ValueTask<object> ReadColumnValueInternalAsync(HtbDataType type)
    {
        switch (type)
        {
            case HtbDataType.Int32: return await ReadInt32Async().ConfigureAwait(false);
            case HtbDataType.Int64: return await ReadInt64Async().ConfigureAwait(false);
            case HtbDataType.Float: return await ReadFloatAsync().ConfigureAwait(false);
            case HtbDataType.Double: return await ReadDoubleAsync().ConfigureAwait(false);
            case HtbDataType.Decimal: return await ReadDecimalAsync().ConfigureAwait(false);
            case HtbDataType.Boolean: return await ReadBooleanAsync().ConfigureAwait(false);
            case HtbDataType.DateTime: return await ReadDateTimeAsync().ConfigureAwait(false);
            case HtbDataType.Guid: return await ReadGuidAsync().ConfigureAwait(false);
            case HtbDataType.String: return await ReadStringAsync().ConfigureAwait(false);
            case HtbDataType.FloatArray: return await ReadFloatArrayAsync().ConfigureAwait(false);
            case HtbDataType.DateTimeOffset: return await ReadDateTimeOffsetAsync().ConfigureAwait(false);
            case HtbDataType.DateOnly: return await ReadDateOnlyAsync().ConfigureAwait(false);
            case HtbDataType.TimeOnly: return await ReadTimeOnlyAsync().ConfigureAwait(false);
            case HtbDataType.TimeSpan: return await ReadTimeSpanAsync().ConfigureAwait(false);
            case HtbDataType.Int16: return await ReadInt16Async().ConfigureAwait(false);
            case HtbDataType.UInt16: return await ReadUInt16Async().ConfigureAwait(false);
            case HtbDataType.Byte: return await ReadByteAsync().ConfigureAwait(false);
            case HtbDataType.SByte: return await ReadSByteAsync().ConfigureAwait(false);
            case HtbDataType.UInt32: return await ReadUInt32Async().ConfigureAwait(false);
            case HtbDataType.UInt64: return await ReadUInt64Async().ConfigureAwait(false);
            case HtbDataType.Char: return await ReadCharAsync().ConfigureAwait(false);
            case HtbDataType.Half: return await ReadHalfAsync().ConfigureAwait(false);
            case HtbDataType.Int128: return await ReadInt128Async().ConfigureAwait(false);
            case HtbDataType.UInt128: return await ReadUInt128Async().ConfigureAwait(false);
            case HtbDataType.ByteArray: return await ReadByteArrayAsync().ConfigureAwait(false);
            case HtbDataType.BigInteger: return await ReadBigIntegerAsync().ConfigureAwait(false);
            case HtbDataType.Uri: return await ReadUriAsync().ConfigureAwait(false);
            default:
                throw new HtbException(HtbErrorCode.CorruptData, $"Unsupported Column DataType: {type}");
        }
    }

    [RequiresUnreferencedCode("Validation using attributes requires reflection.")]
    private void ValidateRecord(T record)
    {
        if (binder is HtbRecordBinder<T> refBinder)
        {
            int colCount = Schema!.Columns.Count;
            for (int i = 0; i < colCount; i++)
            {
                var col = Schema.Columns[i];
                if (col.Property == null)
                    continue;

                var validate = col.Property.GetCustomAttribute<ValidateAttribute>();
                if (validate == null)
                    continue;

                object? val = refBinder.GetValue(record, i);

                // 1. NotNull validation
                if (validate.NotNull && val == null)
                {
                    throw new HtbException(HtbErrorCode.DeserializationError, $"Property '{col.Property.Name}' violated validation constraint: value cannot be null.");
                }

                if (val != null)
                {
                    // 2. NotEmpty validation
                    if (validate.NotEmpty && val is string s && string.IsNullOrWhiteSpace(s))
                    {
                        throw new HtbException(HtbErrorCode.DeserializationError, $"Property '{col.Property.Name}' violated validation constraint: value cannot be empty.");
                    }

                    // 3. MinLength validation
                    if (validate.MinLength > 0 && val is string sMin && sMin.Length < validate.MinLength)
                    {
                        throw new HtbException(HtbErrorCode.DeserializationError, string.Create(CultureInfo.InvariantCulture, $"Property '{col.Property.Name}' violated validation constraint: length must be at least {validate.MinLength}."));
                    }

                    // 4. MaxLength validation
                    if (validate.MaxLength > 0 && val is string sMax && sMax.Length > validate.MaxLength)
                    {
                        throw new HtbException(HtbErrorCode.DeserializationError, string.Create(CultureInfo.InvariantCulture, $"Property '{col.Property.Name}' violated validation constraint: length cannot exceed {validate.MaxLength}."));
                    }

                    // 5 & 6. RangeMin / RangeMax validation
                    // A value that is not a number (e.g. an empty or non-numeric string) is not range-checked,
                    // as in the CSV, Fixed-Width and write-side validation runners.
                    var rangeCheck = new NumericRangeCheck(
                        double.IsNaN(validate.RangeMin) ? null : validate.RangeMin,
                        double.IsNaN(validate.RangeMax) ? null : validate.RangeMax);
                    if (rangeCheck.HasRange &&
                        rangeCheck.TryEvaluate(val, CultureInfo.InvariantCulture, out bool belowMin, out bool aboveMax, out string formattedValue))
                    {
                        if (belowMin)
                        {
                            throw new HtbException(HtbErrorCode.DeserializationError, string.Create(CultureInfo.InvariantCulture, $"Property '{col.Property.Name}' violated validation constraint: value {formattedValue} must be at least {validate.RangeMin}."));
                        }

                        if (aboveMax)
                        {
                            throw new HtbException(HtbErrorCode.DeserializationError, string.Create(CultureInfo.InvariantCulture, $"Property '{col.Property.Name}' violated validation constraint: value {formattedValue} cannot exceed {validate.RangeMax}."));
                        }
                    }

                    // 7. Pattern validation
                    if (!string.IsNullOrEmpty(validate.Pattern) && val is string sPat)
                    {
                        var regex = validate.PatternTimeoutMs > 0
                            ? new Regex(validate.Pattern, RegexOptions.None, TimeSpan.FromMilliseconds(validate.PatternTimeoutMs))
                            : new Regex(validate.Pattern);

                        if (!regex.IsMatch(sPat))
                        {
                            throw new HtbException(HtbErrorCode.DeserializationError, $"Property '{col.Property.Name}' violated validation constraint: value must match pattern '{validate.Pattern}'.");
                        }
                    }
                }
            }
            return;
        }

        binder!.ValidateRecord(record);
    }
}
