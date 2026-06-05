#pragma warning disable IDE0010 // Populate switch
#pragma warning disable IDE0066 // Use 'switch' expression
#pragma warning disable IDE0302 // Simplify collection initialization

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
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
    public async ValueTask<bool> ReadNextAsync()
    {
        if (!recordHeaderParsed)
        {
            await ParseRecordHeaderAsync().ConfigureAwait(false);
        }

        while (RecordsRead < options.MaxRowCount)
        {
            try
            {
                if (await IsEndOfStreamAsync().ConfigureAwait(false))
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
                    await ReadMaskAsync(mask).ConfigureAwait(false);

                    for (int i = 0; i < colCount; i++)
                    {
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
                            await SkipColumnValueAsync(Schema.Columns[i].DataType).ConfigureAwait(false);
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

        if (RecordsRead >= options.MaxRowCount && !await IsEndOfStreamAsync().ConfigureAwait(false))
        {
            throw new HtbException(
                HtbErrorCode.LimitExceeded,
                $"MaxRowCount limit of {options.MaxRowCount} was exceeded.");
        }

        CurrentRecord = default;
        return false;
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
    private async Task ParseRecordHeaderAsync()
    {
        var fileSchema = await ParseHeaderAsync().ConfigureAwait(false);
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

    #region Reflection Fallback Helper Methods

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
            default:
                throw new HtbException(HtbErrorCode.CorruptData, $"Unsupported Column DataType: {type}");
        }
    }

    #endregion

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
                        throw new HtbException(HtbErrorCode.DeserializationError, $"Property '{col.Property.Name}' violated validation constraint: length must be at least {validate.MinLength}.");
                    }

                    // 4. MaxLength validation
                    if (validate.MaxLength > 0 && val is string sMax && sMax.Length > validate.MaxLength)
                    {
                        throw new HtbException(HtbErrorCode.DeserializationError, $"Property '{col.Property.Name}' violated validation constraint: length cannot exceed {validate.MaxLength}.");
                    }

                    // 5. RangeMin validation
                    if (!double.IsNaN(validate.RangeMin))
                    {
                        double numericVal = Convert.ToDouble(val);
                        if (numericVal < validate.RangeMin)
                        {
                            throw new HtbException(HtbErrorCode.DeserializationError, $"Property '{col.Property.Name}' violated validation constraint: value {numericVal} must be at least {validate.RangeMin}.");
                        }
                    }

                    // 6. RangeMax validation
                    if (!double.IsNaN(validate.RangeMax))
                    {
                        double numericVal = Convert.ToDouble(val);
                        if (numericVal > validate.RangeMax)
                        {
                            throw new HtbException(HtbErrorCode.DeserializationError, $"Property '{col.Property.Name}' violated validation constraint: value {numericVal} cannot exceed {validate.RangeMax}.");
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
        }
    }
}
