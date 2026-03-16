using System.Collections.Generic;
using HeroParser.SeparatedValues.Reading.Binders;
using HeroParser.SeparatedValues.Reading.Rows;
using HeroParser.Validation;

namespace HeroParser.SeparatedValues.Reading.Records;

/// <summary>
/// Reads CSV rows as strongly typed records from an in-memory span.
/// </summary>
/// <typeparam name="TElement">The element type: <see cref="char"/> for UTF-16 or <see cref="byte"/> for UTF-8.</typeparam>
/// <typeparam name="T">The record type to deserialize.</typeparam>
public ref struct CsvRecordReader<TElement, T>
    where TElement : unmanaged, IEquatable<TElement>
    where T : new()
{
    private CsvRowReader<TElement> reader;
    private CsvRowReader<byte> byteReader;
    private readonly ICsvBinder<TElement, T>? binder;
    private readonly ICsvBinder<byte, T>? byteBinder;
    // Keeps encoded text alive for byte-backed string readers.
    private readonly byte[]? ownedBuffer;
    private readonly bool useByteReader;
    private readonly int skipRows;
    private readonly IProgress<CsvProgress>? progress;
    private readonly int progressInterval;
    private readonly ValidationMode validationMode;
    private int rowNumber;
    private int skippedCount;
    private int dataRowCount;
    private readonly List<ValidationError> errors = [];

    internal CsvRecordReader(CsvRowReader<TElement> reader, ICsvBinder<TElement, T> binder, int skipRows = 0,
        IProgress<CsvProgress>? progress = null, int progressInterval = 1000,
        ValidationMode validationMode = ValidationMode.Strict)
    {
        this.reader = reader;
        byteReader = default;
        this.binder = binder;
        byteBinder = null;
        ownedBuffer = null;
        useByteReader = false;
        this.skipRows = skipRows;
        this.progress = progress;
        this.progressInterval = progressInterval > 0 ? progressInterval : 1000;
        this.validationMode = validationMode;
        Current = default!;
        rowNumber = 0;
        skippedCount = 0;
        dataRowCount = 0;
    }

    private CsvRecordReader(
        CsvRowReader<byte> byteReader,
        ICsvBinder<byte, T> byteBinder,
        byte[] ownedBuffer,
        int skipRows = 0,
        IProgress<CsvProgress>? progress = null,
        int progressInterval = 1000,
        ValidationMode validationMode = ValidationMode.Strict)
    {
        reader = default;
        this.byteReader = byteReader;
        binder = default;
        this.byteBinder = byteBinder;
        this.ownedBuffer = ownedBuffer;
        useByteReader = true;
        this.skipRows = skipRows;
        this.progress = progress;
        this.progressInterval = progressInterval > 0 ? progressInterval : 1000;
        this.validationMode = validationMode;
        Current = default!;
        rowNumber = 0;
        skippedCount = 0;
        dataRowCount = 0;
    }

    internal static CsvRecordReader<char, T> CreateByteBacked(
        CsvRowReader<byte> byteReader,
        ICsvBinder<byte, T> byteBinder,
        byte[] ownedBuffer,
        int skipRows = 0,
        IProgress<CsvProgress>? progress = null,
        int progressInterval = 1000,
        ValidationMode validationMode = ValidationMode.Strict)
    {
        return new CsvRecordReader<char, T>(
            byteReader,
            byteBinder,
            ownedBuffer,
            skipRows,
            progress,
            progressInterval,
            validationMode);
    }

    /// <summary>Gets the current mapped record.</summary>
    public T Current { get; private set; }

    /// <summary>Gets the validation errors collected during iteration.</summary>
    public readonly IReadOnlyList<ValidationError> Errors => errors;

    /// <summary>
    /// Gets the validation mode for this reader.
    /// </summary>
    public readonly ValidationMode ValidationMode => validationMode;

    /// <summary>
    /// Throws a <see cref="ValidationException"/> if the reader is in <see cref="ValidationMode.Strict"/>
    /// mode and any validation errors were collected during iteration.
    /// Called automatically by terminal methods like <c>ToList()</c>.
    /// </summary>
    internal readonly void ThrowOnStrictErrors()
    {
        if (validationMode == ValidationMode.Strict && errors.Count > 0)
            throw new ValidationException(errors);
    }

    /// <summary>Returns this instance for <c>foreach</c> support.</summary>
    public readonly CsvRecordReader<TElement, T> GetEnumerator() => this;

    /// <summary>
    /// Advances to the next mapped record, parsing and binding the underlying row.
    /// </summary>
    public bool MoveNext()
    {
        if (useByteReader)
        {
            return MoveNextByteReader();
        }

        return MoveNextGenericReader();
    }

    private bool MoveNextGenericReader()
    {
        while (reader.MoveNext())
        {
            rowNumber++;
            var row = reader.Current;

            // Skip rows if requested
            if (skippedCount < skipRows)
            {
                skippedCount++;
                continue;
            }

            if (binder!.NeedsHeaderResolution)
            {
                binder.BindHeader(row, rowNumber);
                continue;
            }

            if (!binder!.TryBind(row, rowNumber, out var result, errors))
            {
                // Row was skipped due to error handling
                continue;
            }

            dataRowCount++;
            ReportProgress();

            Current = result;
            return true;
        }

        ReportFinalProgress();
        Current = default!;
        return false;
    }

    private bool MoveNextByteReader()
    {
        _ = ownedBuffer;

        while (byteReader.MoveNext())
        {
            rowNumber++;
            var row = byteReader.Current;

            if (skippedCount < skipRows)
            {
                skippedCount++;
                continue;
            }

            if (byteBinder!.NeedsHeaderResolution)
            {
                byteBinder.BindHeader(row, rowNumber);
                continue;
            }

            if (!byteBinder.TryBind(row, rowNumber, out var result, errors))
            {
                continue;
            }

            dataRowCount++;
            ReportProgress();

            Current = result;
            return true;
        }

        ReportFinalProgress();
        Current = default!;
        return false;
    }

    private void ReportProgress()
    {
        if (progress is not null && dataRowCount % progressInterval == 0)
        {
            progress.Report(new CsvProgress
            {
                RowsProcessed = dataRowCount,
                BytesProcessed = 0, // Not available for span-based parsing
                TotalBytes = -1
            });
        }
    }

    private void ReportFinalProgress()
    {
        if (progress is not null && dataRowCount > 0)
        {
            progress.Report(new CsvProgress
            {
                RowsProcessed = dataRowCount,
                BytesProcessed = 0,
                TotalBytes = -1
            });
        }
    }

    /// <summary>Releases pooled buffers from the underlying CSV reader.</summary>
    public readonly void Dispose()
    {
        if (useByteReader)
        {
            byteReader.Dispose();
        }
        else
        {
            reader.Dispose();
        }
    }
}
