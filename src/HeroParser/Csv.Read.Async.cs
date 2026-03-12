using System.Buffers;
using System.IO.Pipelines;
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
    public static IAsyncEnumerable<T> DeserializeRecordsAsync<T>(
        PipeReader reader,
        CsvRecordOptions? recordOptions = null,
        CsvReadOptions? parserOptions = null,
        CancellationToken cancellationToken = default)
        where T : new()
    {
        ArgumentNullException.ThrowIfNull(reader);
        parserOptions ??= CsvReadOptions.Default;
        recordOptions ??= CsvRecordOptions.Default;
        return new CsvPipeRecordAsyncEnumerable<T>(reader, recordOptions, parserOptions, cancellationToken);
    }

    internal static void BindPipeSequenceHeader<T>(
        CsvPipeSequenceRow row,
        ICsvBinder<byte, T> binder,
        ref byte[]? scratchBuffer)
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

        scratchBuffer = EnsureScratchBuffer(scratchBuffer, rowLength);
        var rentedScratch = scratchBuffer.AsSpan(0, rowLength);
        row.RawRecord.CopyTo(rentedScratch);
        binder.BindHeader(row.CreateContiguousRow(rentedScratch), row.RowNumber);
    }

    internal static bool TryBindPipeSequenceRow<T>(
        CsvPipeSequenceRow row,
        ICsvBinder<byte, T> binder,
        ref byte[]? scratchBuffer,
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

        scratchBuffer = EnsureScratchBuffer(scratchBuffer, rowLength);
        var rentedScratch = scratchBuffer.AsSpan(0, rowLength);
        row.RawRecord.CopyTo(rentedScratch);
        return binder.TryBind(row.CreateContiguousRow(rentedScratch), row.RowNumber, out result, errors: null);
    }

    private static byte[] EnsureScratchBuffer(byte[]? scratchBuffer, int requiredLength)
    {
        if (scratchBuffer is not null && scratchBuffer.Length >= requiredLength)
        {
            return scratchBuffer;
        }

        if (scratchBuffer is not null)
        {
            ArrayPool<byte>.Shared.Return(scratchBuffer);
        }

        return ArrayPool<byte>.Shared.Rent(requiredLength);
    }

    internal static void ReportProgress(IProgress<CsvProgress> progress, int dataRowCount)
    {
        progress.Report(new CsvProgress
        {
            RowsProcessed = dataRowCount,
            BytesProcessed = 0,
            TotalBytes = -1
        });
    }
}

internal sealed class CsvPipeRecordAsyncEnumerable<T> : IAsyncEnumerable<T>
    where T : new()
{
    private readonly PipeReader reader;
    private readonly CsvRecordOptions recordOptions;
    private readonly CsvReadOptions parserOptions;
    private readonly CancellationToken cancellationToken;

    public CsvPipeRecordAsyncEnumerable(
        PipeReader reader,
        CsvRecordOptions recordOptions,
        CsvReadOptions parserOptions,
        CancellationToken cancellationToken)
    {
        this.reader = reader;
        this.recordOptions = recordOptions;
        this.parserOptions = parserOptions;
        this.cancellationToken = cancellationToken;
    }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        => new Enumerator(reader, recordOptions, parserOptions, this.cancellationToken, cancellationToken);

    private sealed class Enumerator : IAsyncEnumerator<T>
    {
        private readonly PipeReader reader;
        private readonly CsvRecordOptions recordOptions;
        private readonly CsvReadOptions parserOptions;
        private readonly CancellationTokenSource? linkedCancellationSource;
        private readonly CancellationToken cancellationToken;
        private readonly int progressInterval;

        private CsvPipeSequenceReader? pipeReader;
        private ICsvBinder<byte, T>? binder;
        private byte[]? scratchBuffer;
        private bool completed;
        private int dataRowCount;

        public Enumerator(
            PipeReader reader,
            CsvRecordOptions recordOptions,
            CsvReadOptions parserOptions,
            CancellationToken methodCancellationToken,
            CancellationToken enumeratorCancellationToken)
        {
            this.reader = reader;
            this.recordOptions = recordOptions;
            this.parserOptions = parserOptions;
            progressInterval = recordOptions.ProgressIntervalRows > 0
                ? recordOptions.ProgressIntervalRows
                : 1000;

            if (!methodCancellationToken.CanBeCanceled)
            {
                cancellationToken = enumeratorCancellationToken;
            }
            else if (!enumeratorCancellationToken.CanBeCanceled || enumeratorCancellationToken == methodCancellationToken)
            {
                cancellationToken = methodCancellationToken;
            }
            else
            {
                linkedCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(
                    methodCancellationToken,
                    enumeratorCancellationToken);
                cancellationToken = linkedCancellationSource.Token;
            }

            Current = new T();
        }

        public T Current { get; private set; }

        public async ValueTask<bool> MoveNextAsync()
        {
            if (completed)
            {
                return false;
            }

            cancellationToken.ThrowIfCancellationRequested();

            binder ??= CsvRecordBinderFactory.GetByteBinder<T>(recordOptions);
            pipeReader ??= new CsvPipeSequenceReader(reader, parserOptions, recordOptions.SkipRows);

            while (await pipeReader.MoveNextAsync(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = pipeReader.Current;

                if (binder.NeedsHeaderResolution)
                {
                    Csv.BindPipeSequenceHeader(row, binder, ref scratchBuffer);
                    continue;
                }

                if (!Csv.TryBindPipeSequenceRow(row, binder, ref scratchBuffer, out var result))
                {
                    continue;
                }

                dataRowCount++;
                if (recordOptions.Progress is not null && dataRowCount % progressInterval == 0)
                {
                    Csv.ReportProgress(recordOptions.Progress, dataRowCount);
                }

                Current = result;
                return true;
            }

            completed = true;
            if (recordOptions.Progress is not null && dataRowCount > 0)
            {
                Csv.ReportProgress(recordOptions.Progress, dataRowCount);
            }

            return false;
        }

        public async ValueTask DisposeAsync()
        {
            if (pipeReader is not null)
            {
                await pipeReader.DisposeAsync().ConfigureAwait(false);
                pipeReader = null;
            }

            if (scratchBuffer is not null)
            {
                ArrayPool<byte>.Shared.Return(scratchBuffer);
                scratchBuffer = null;
            }

            linkedCancellationSource?.Dispose();
        }
    }
}
