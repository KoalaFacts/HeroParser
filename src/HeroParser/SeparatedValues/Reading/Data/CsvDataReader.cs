using System;
using System.Collections.Generic;
using System.Data;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Rows;
using HeroParser.SeparatedValues.Reading.Streaming;

namespace HeroParser.SeparatedValues.Reading.Data;

/// <summary>
/// Streams CSV data through <see cref="IDataReader"/> for database bulk loading.
/// </summary>
public sealed class CsvDataReader : TabularDataReader
{
    private readonly CsvAsyncStreamReader reader;
    private readonly CsvReadOptions parserOptions;
    private readonly CsvDataReaderOptions options;
    private readonly string[]? nullValues;
    private readonly StringComparer headerComparer;

    internal CsvDataReader(CsvAsyncStreamReader reader, CsvReadOptions parserOptions, CsvDataReaderOptions options)
    {
        this.reader = reader ?? throw new ArgumentNullException(nameof(reader));
        this.parserOptions = parserOptions ?? CsvReadOptions.Default;
        this.options = options ?? CsvDataReaderOptions.Default;
        headerComparer = this.options.HeaderComparer;
        nullValues = PrepareNullValues(this.options.NullValues);

        if (this.options.ColumnNames is { Count: > 0 })
        {
            columnNames = new string[this.options.ColumnNames.Count];
            for (int i = 0; i < columnNames.Length; i++)
            {
                var name = this.options.ColumnNames[i]
                    ?? throw new CsvException(CsvErrorCode.InvalidOptions, "ColumnNames cannot contain null entries.");
                columnNames[i] = name;
            }
        }
    }

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
    public override async ValueTask DisposeAsync()
    {
        if (closed)
            return;

        closed = true;
        await reader.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override object GetValue(int ordinal)
    {
        EnsureCurrentRow();
        ValidateOrdinal(ordinal);

        var row = reader.Current;
        if (!row.TryGetColumnSpan(ordinal, out _))
        {
            if (options.AllowMissingColumns)
                return DBNull.Value;

            throw new CsvException(
                CsvErrorCode.ParseError,
                $"Row has only {row.ColumnCount} columns but requested index {ordinal}.",
                row.LineNumber,
                ordinal + 1);
        }

        var value = GetColumnString(row, ordinal);
        if (IsNullValue(value))
            return DBNull.Value;

        return value;
    }

    /// <inheritdoc />
    public override bool IsDBNull(int ordinal)
    {
        EnsureCurrentRow();
        ValidateOrdinal(ordinal);

        var row = reader.Current;
        if (!row.TryGetColumnSpan(ordinal, out _))
        {
            if (options.AllowMissingColumns)
                return true;

            throw new CsvException(
                CsvErrorCode.ParseError,
                $"Row has only {row.ColumnCount} columns but requested index {ordinal}.",
                row.LineNumber,
                ordinal + 1);
        }

        var value = GetColumnString(row, ordinal);
        return IsNullValue(value);
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

    private protected override void EnsureInitialized()
    {
        if (initialized)
            return;

        initialized = true;

        if (options.HasHeaderRow)
        {
            if (!Advance())
            {
                fieldCount = columnNames.Length;
                BuildOrdinalMap(headerComparer);
                return;
            }

            var headerRow = reader.Current;
            fieldCount = headerRow.ColumnCount;

            if (columnNames.Length == 0)
            {
                columnNames = new string[fieldCount];
                for (int i = 0; i < fieldCount; i++)
                {
                    columnNames[i] = GetColumnString(headerRow, i);
                }
            }
            else if (columnNames.Length != fieldCount)
            {
                throw new CsvException(
                    CsvErrorCode.ParseError,
                    $"Header contains {fieldCount} columns but ColumnNames defines {columnNames.Length}.",
                    headerRow.LineNumber,
                    0);
            }
        }
        else if (columnNames.Length > 0)
        {
            fieldCount = columnNames.Length;
        }

        if (fieldCount == 0)
        {
            if (!Advance())
            {
                BuildOrdinalMap(headerComparer);
                return;
            }

            fieldCount = reader.Current.ColumnCount;
            columnNames = columnNames.Length > 0
                ? columnNames
                : CreateDefaultNames(fieldCount);

            hasPendingRow = true;
            hasAnyRow = true;
            BuildOrdinalMap(headerComparer);
            return;
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
                fieldCount = columnNames.Length;
                BuildOrdinalMap(headerComparer);
                return;
            }

            var headerRow = reader.Current;
            fieldCount = headerRow.ColumnCount;

            if (columnNames.Length == 0)
            {
                columnNames = new string[fieldCount];
                for (int i = 0; i < fieldCount; i++)
                {
                    columnNames[i] = GetColumnString(headerRow, i);
                }
            }
            else if (columnNames.Length != fieldCount)
            {
                throw new CsvException(
                    CsvErrorCode.ParseError,
                    $"Header contains {fieldCount} columns but ColumnNames defines {columnNames.Length}.",
                    headerRow.LineNumber,
                    0);
            }
        }
        else if (columnNames.Length > 0)
        {
            fieldCount = columnNames.Length;
        }

        if (fieldCount == 0)
        {
            if (!await AdvanceAsync(cancellationToken).ConfigureAwait(false))
            {
                BuildOrdinalMap(headerComparer);
                return;
            }

            fieldCount = reader.Current.ColumnCount;
            columnNames = columnNames.Length > 0
                ? columnNames
                : CreateDefaultNames(fieldCount);

            hasPendingRow = true;
            hasAnyRow = true;
            BuildOrdinalMap(headerComparer);
            return;
        }

        if (await AdvanceAsync(cancellationToken).ConfigureAwait(false))
        {
            ValidateRow(reader.Current);
            hasPendingRow = true;
            hasAnyRow = true;
        }

        BuildOrdinalMap(headerComparer);
    }

    private async Task<bool> AdvanceAsync(CancellationToken cancellationToken)
    {
        return await reader.MoveNextAsync(cancellationToken).ConfigureAwait(false);
    }

    private bool Advance()
        => reader.MoveNextAsync().AsTask().GetAwaiter().GetResult();

    private void ValidateRow(CsvRow<byte> row)
    {
        if (row.ColumnCount > fieldCount)
        {
            throw new CsvException(
                CsvErrorCode.ParseError,
                $"Row has {row.ColumnCount} columns but schema expects {fieldCount}.",
                row.LineNumber,
                fieldCount + 1);
        }

        if (!options.AllowMissingColumns && row.ColumnCount < fieldCount)
        {
            throw new CsvException(
                CsvErrorCode.ParseError,
                $"Row has {row.ColumnCount} columns but schema expects {fieldCount}.",
                row.LineNumber,
                row.ColumnCount + 1);
        }
    }

    private string GetColumnString(CsvRow<byte> row, int ordinal)
    {
        var column = row[ordinal];
        if (!parserOptions.EnableQuotedFields)
            return column.ToString();

        if (parserOptions.EscapeCharacter.HasValue)
            return column.UnquoteToString((byte)parserOptions.Quote, (byte)parserOptions.EscapeCharacter.Value);

        return column.UnquoteToString((byte)parserOptions.Quote);
    }

    private bool IsNullValue(string value)
    {
        if (nullValues is null)
            return false;

        foreach (var nullValue in nullValues)
        {
            if (string.Equals(value, nullValue, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static string[]? PrepareNullValues(IReadOnlyList<string>? nullValues)
    {
        if (nullValues is not { Count: > 0 })
            return null;

        var result = new string[nullValues.Count];
        for (int i = 0; i < nullValues.Count; i++)
        {
            result[i] = nullValues[i]
                ?? throw new CsvException(CsvErrorCode.InvalidOptions, "NullValues cannot contain null entries.");
        }
        return result;
    }
}
