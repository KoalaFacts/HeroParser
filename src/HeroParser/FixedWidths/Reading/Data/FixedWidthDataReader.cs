using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Streaming;

namespace HeroParser.FixedWidths.Reading.Data;

/// <summary>
/// Streams fixed-width data through <see cref="IDataReader"/> for database bulk loading.
/// </summary>
public sealed class FixedWidthDataReader : TabularDataReader
{
    private readonly FixedWidthAsyncStreamReader reader;
    private readonly FixedWidthReadOptions parserOptions;
    private readonly FixedWidthDataReaderOptions options;
    private readonly Encoding encoding;
    private readonly string[]? nullValues;
    private readonly StringComparer headerComparer;
    private readonly int[] columnStarts;
    private readonly int[] columnLengths;
    private readonly char[] columnPadChars;
    private readonly FieldAlignment[] columnAlignments;
    private readonly int requiredRecordLength;

    private readonly bool hasExplicitColumnNames;

    internal FixedWidthDataReader(
        FixedWidthAsyncStreamReader reader,
        FixedWidthReadOptions parserOptions,
        FixedWidthDataReaderOptions options,
        Encoding encoding)
    {
        this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
        this.parserOptions = parserOptions ?? FixedWidthReadOptions.Default;
        this.options = options ?? FixedWidthDataReaderOptions.Default;
        this.encoding = encoding ?? Encoding.UTF8;
        headerComparer = this.options.HeaderComparer;
        nullValues = PrepareNullValues(this.options.NullValues);

        if (this.options.Columns is not { Count: > 0 })
        {
            throw new FixedWidthException(
                FixedWidthErrorCode.InvalidOptions,
                "Fixed-width data reader requires at least one column definition.");
        }

        var columnCount = this.options.Columns.Count;
        var columnNameOverrides = this.options.ColumnNames;
        hasExplicitColumnNames = columnNameOverrides is { Count: > 0 };
        if (hasExplicitColumnNames && columnNameOverrides!.Count != columnCount)
        {
            throw new FixedWidthException(
                FixedWidthErrorCode.InvalidOptions,
                $"ColumnNames defines {columnNameOverrides.Count} columns but {columnCount} definitions were provided.");
        }

        columnNames = new string[columnCount];
        columnStarts = new int[columnCount];
        columnLengths = new int[columnCount];
        columnPadChars = new char[columnCount];
        columnAlignments = new FieldAlignment[columnCount];

        var maxEnd = 0;
        for (int i = 0; i < columnCount; i++)
        {
            var column = this.options.Columns[i]
                ?? throw new FixedWidthException(
                    FixedWidthErrorCode.InvalidOptions,
                    "Fixed-width column definitions cannot be null.");

            if (column.Start < 0)
            {
                throw new FixedWidthException(
                    FixedWidthErrorCode.InvalidOptions,
                    $"Column start must be non-negative. Value: {column.Start}.");
            }

            if (column.Length <= 0)
            {
                throw new FixedWidthException(
                    FixedWidthErrorCode.InvalidOptions,
                    $"Column length must be positive. Value: {column.Length}.");
            }

            var end = (long)column.Start + column.Length;
            if (end > int.MaxValue)
            {
                throw new FixedWidthException(
                    FixedWidthErrorCode.InvalidOptions,
                    "Column start/length overflowed the allowed range.");
            }

            if (end > maxEnd)
                maxEnd = (int)end;

            columnStarts[i] = column.Start;
            columnLengths[i] = column.Length;
            columnPadChars[i] = column.PadChar ?? this.parserOptions.DefaultPadChar;
            columnAlignments[i] = column.Alignment ?? this.parserOptions.DefaultAlignment;

            var name = hasExplicitColumnNames ? columnNameOverrides![i] : column.Name;
            if (hasExplicitColumnNames && name is null)
            {
                throw new FixedWidthException(
                    FixedWidthErrorCode.InvalidOptions,
                    "ColumnNames cannot contain null entries.");
            }
            columnNames[i] = hasExplicitColumnNames
                ? name!
                : string.IsNullOrWhiteSpace(name) ? string.Empty : name;
        }

        requiredRecordLength = maxEnd;
        fieldCount = columnCount;
    }

    private protected override Encoding ByteEncoding => encoding;

    private protected override int GetSchemaColumnSize(int ordinal) => columnLengths[ordinal];

    /// <inheritdoc />
    public override bool Read()
    {
        EnsureNotClosed();
        EnsureInitialized();

        if (hasPendingRow)
        {
            hasPendingRow = false;
            hasCurrentRow = true;
            return true;
        }

        if (!Advance())
        {
            hasCurrentRow = false;
            return false;
        }

        ValidateRow(reader.Current);
        hasCurrentRow = true;
        return true;
    }

    /// <inheritdoc />
    public override async Task<bool> ReadAsync(CancellationToken cancellationToken)
    {
        EnsureNotClosed();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        if (hasPendingRow)
        {
            hasPendingRow = false;
            hasCurrentRow = true;
            return true;
        }

        if (!await AdvanceAsync(cancellationToken).ConfigureAwait(false))
        {
            hasCurrentRow = false;
            return false;
        }

        ValidateRow(reader.Current);
        hasCurrentRow = true;
        return true;
    }

    private async Task<bool> AdvanceAsync(CancellationToken cancellationToken)
    {
        return await reader.MoveNextAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task<bool> IsDBNullAsync(int ordinal, CancellationToken cancellationToken)
    {
        EnsureNotClosed();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        return IsDBNull(ordinal);
    }

    /// <inheritdoc />
    public override async Task<T> GetFieldValueAsync<T>(int ordinal, CancellationToken cancellationToken)
    {
        EnsureNotClosed();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        return await base.GetFieldValueAsync<T>(ordinal, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override object GetValue(int ordinal)
    {
        EnsureCurrentRow();
        ValidateOrdinal(ordinal);

        var row = reader.Current;
        if (!TryGetColumnSpan(row, ordinal, out var span))
            return DBNull.Value;

        if (IsNullValue(span))
            return DBNull.Value;

        return new string(span);
    }

    /// <inheritdoc />
    public override bool IsDBNull(int ordinal)
    {
        EnsureCurrentRow();
        ValidateOrdinal(ordinal);

        var row = reader.Current;
        if (!TryGetColumnSpan(row, ordinal, out var span))
            return true;

        return IsNullValue(span);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (closed)
            return;

        closed = true;
        if (disposing)
        {
            reader.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        if (closed)
            return;

        closed = true;
        await reader.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    private protected override void EnsureInitialized()
    {
        if (initialized)
            return;

        initialized = true;

        if (options.HasHeaderRow)
        {
            if (!Advance())
            {
                if (!hasExplicitColumnNames)
                    FillDefaultNames();
                BuildOrdinalMap(headerComparer);
                return;
            }

            var headerRow = reader.Current;
            ValidateRow(headerRow);
            if (!hasExplicitColumnNames)
            {
                for (int i = 0; i < fieldCount; i++)
                {
                    if (!string.IsNullOrEmpty(columnNames[i]))
                        continue;

                    columnNames[i] = TryGetColumnSpan(headerRow, i, out var span) && !span.IsEmpty
                        ? new string(span)
                        : $"Column{i + 1}";
                }
            }
        }
        else
        {
            if (!hasExplicitColumnNames)
                FillDefaultNames();
        }

        if (Advance())
        {
            ValidateRow(reader.Current);
            hasPendingRow = true;
            hasAnyRow = true;
        }

        BuildOrdinalMap(headerComparer);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (initialized)
            return;

        initialized = true;

        if (options.HasHeaderRow)
        {
            if (!await AdvanceAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!hasExplicitColumnNames)
                    FillDefaultNames();
                BuildOrdinalMap(headerComparer);
                return;
            }

            var headerRow = reader.Current;
            ValidateRow(headerRow);
            if (!hasExplicitColumnNames)
            {
                for (int i = 0; i < fieldCount; i++)
                {
                    if (!string.IsNullOrEmpty(columnNames[i]))
                        continue;

                    columnNames[i] = TryGetColumnSpan(headerRow, i, out var span) && !span.IsEmpty
                        ? new string(span)
                        : $"Column{i + 1}";
                }
            }
        }
        else
        {
            if (!hasExplicitColumnNames)
                FillDefaultNames();
        }

        if (await AdvanceAsync(cancellationToken).ConfigureAwait(false))
        {
            ValidateRow(reader.Current);
            hasPendingRow = true;
            hasAnyRow = true;
        }

        BuildOrdinalMap(headerComparer);
    }

    private void FillDefaultNames()
    {
        for (int i = 0; i < columnNames.Length; i++)
        {
            if (string.IsNullOrEmpty(columnNames[i]))
            {
                columnNames[i] = $"Column{i + 1}";
            }
        }
    }

    private bool Advance()
        => reader.MoveNextAsync().AsTask().GetAwaiter().GetResult();

    private void ValidateRow(FixedWidthCharSpanRow row)
    {
        if (options.AllowMissingColumns)
            return;

        if (row.RawRecord.Length < requiredRecordLength)
        {
            throw new FixedWidthException(
                FixedWidthErrorCode.FieldOutOfBounds,
                $"Row has length {row.RawRecord.Length} but schema expects at least {requiredRecordLength} characters.",
                row.RecordNumber,
                row.SourceLineNumber);
        }
    }

    private bool TryGetColumnSpan(FixedWidthCharSpanRow row, int ordinal, out ReadOnlySpan<char> span)
    {
        var start = columnStarts[ordinal];
        var length = columnLengths[ordinal];
        var record = row.RawRecord;
        var end = start + length;

        if (end > record.Length)
        {
            if (options.AllowMissingColumns)
            {
                span = default;
                return false;
            }

            throw new FixedWidthException(
                FixedWidthErrorCode.FieldOutOfBounds,
                $"Row has length {record.Length} but schema expects at least {requiredRecordLength} characters.",
                row.RecordNumber,
                row.SourceLineNumber);
        }

        span = GetColumnSpan(row, ordinal);
        return true;
    }

    private ReadOnlySpan<char> GetColumnSpan(FixedWidthCharSpanRow row, int ordinal)
        => row.GetField(
            columnStarts[ordinal],
            columnLengths[ordinal],
            columnPadChars[ordinal],
            columnAlignments[ordinal]).CharSpan;

    private bool IsNullValue(ReadOnlySpan<char> span)
    {
        if (nullValues is null)
            return false;

        foreach (var nullValue in nullValues)
        {
            if (span.SequenceEqual(nullValue.AsSpan()))
                return true;
        }
        return false;
    }

    private static string[]? PrepareNullValues(IReadOnlyList<string>? nullValues)
    {
        if (nullValues is not { Count: > 0 })
            return null;

        return [.. nullValues];
    }
}

