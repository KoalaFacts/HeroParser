using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Binders;
using HeroParser.SeparatedValues.Reading.Records;
using HeroParser.SeparatedValues.Reading.Rows;

namespace HeroParser;

public static partial class Csv
{
    private const int PIPE_BIND_STACKALLOC_THRESHOLD = 1024;

    /// <summary>
    /// Asynchronously deserializes CSV data from a <see cref="PipeReader"/> into strongly typed records.
    /// </summary>
    /// <remarks>
    /// This path uses the borrowed <see cref="CsvPipeSequenceReader"/> fast path and only copies a row
    /// when it spans multiple pipe segments.
    /// </remarks>
    public static async IAsyncEnumerable<T> DeserializeRecordsAsync<T>(
        PipeReader reader,
        CsvRecordOptions? recordOptions = null,
        CsvReadOptions? parserOptions = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where T : new()
    {
        ArgumentNullException.ThrowIfNull(reader);

        parserOptions ??= CsvReadOptions.Default;
        recordOptions ??= CsvRecordOptions.Default;

        var binder = CsvRecordBinderFactory.GetByteBinder<T>(recordOptions);
        int dataRowCount = 0;
        int progressInterval = recordOptions.ProgressIntervalRows > 0
            ? recordOptions.ProgressIntervalRows
            : 1000;

        await using var pipeReader = new CsvPipeSequenceReader(reader, parserOptions, recordOptions.SkipRows);

        while (await pipeReader.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = pipeReader.Current;

            if (binder.NeedsHeaderResolution)
            {
                BindPipeSequenceHeader(row, binder);
                continue;
            }

            if (!TryBindPipeSequenceRow(row, binder, out var result))
            {
                continue;
            }

            dataRowCount++;
            if (recordOptions.Progress is not null && dataRowCount % progressInterval == 0)
            {
                ReportProgress(recordOptions.Progress, dataRowCount);
            }

            yield return result;
        }

        if (recordOptions.Progress is not null && dataRowCount > 0)
        {
            ReportProgress(recordOptions.Progress, dataRowCount);
        }
    }

    private static void BindPipeSequenceHeader<T>(
        CsvPipeSequenceRow row,
        ICsvBinder<byte, T> binder)
        where T : new()
    {
        if (row.TryGetContiguousRow(out var contiguousRow))
        {
            binder.BindHeader(contiguousRow, row.RowNumber);
            return;
        }

        int rowLength = checked((int)row.RawRecord.Length);
        if (rowLength <= PIPE_BIND_STACKALLOC_THRESHOLD)
        {
            Span<byte> scratch = stackalloc byte[rowLength];
            row.RawRecord.CopyTo(scratch);
            binder.BindHeader(row.CreateContiguousRow(scratch), row.RowNumber);
            return;
        }

        byte[] rented = ArrayPool<byte>.Shared.Rent(rowLength);
        try
        {
            var scratch = rented.AsSpan(0, rowLength);
            row.RawRecord.CopyTo(scratch);
            binder.BindHeader(row.CreateContiguousRow(scratch), row.RowNumber);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static bool TryBindPipeSequenceRow<T>(
        CsvPipeSequenceRow row,
        ICsvBinder<byte, T> binder,
        out T result)
        where T : new()
    {
        if (row.TryGetContiguousRow(out var contiguousRow))
        {
            return binder.TryBind(contiguousRow, row.RowNumber, out result, errors: null);
        }

        int rowLength = checked((int)row.RawRecord.Length);
        if (rowLength <= PIPE_BIND_STACKALLOC_THRESHOLD)
        {
            Span<byte> scratch = stackalloc byte[rowLength];
            row.RawRecord.CopyTo(scratch);
            return binder.TryBind(row.CreateContiguousRow(scratch), row.RowNumber, out result, errors: null);
        }

        byte[] rented = ArrayPool<byte>.Shared.Rent(rowLength);
        try
        {
            var scratch = rented.AsSpan(0, rowLength);
            row.RawRecord.CopyTo(scratch);
            return binder.TryBind(row.CreateContiguousRow(scratch), row.RowNumber, out result, errors: null);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static void ReportProgress(IProgress<CsvProgress> progress, int dataRowCount)
    {
        progress.Report(new CsvProgress
        {
            RowsProcessed = dataRowCount,
            BytesProcessed = 0,
            TotalBytes = -1
        });
    }
}
