using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Detection;
using HeroParser.SeparatedValues.Reading.Streaming;

namespace HeroParser.SeparatedValues.Validation;

public static partial class CsvValidator
{
    private const int DETECTION_SAMPLE_BYTES = 64 * 1024;

    /// <summary>
    /// Validates a UTF-8 CSV file while retaining at most <see cref="CsvValidationOptions.MaxErrors"/> errors.
    /// </summary>
    /// <param name="path">Path to the file.</param>
    /// <param name="options">Validation options.</param>
    /// <param name="cancellationToken">Token used to cancel file reads.</param>
    /// <returns>A result describing the rows inspected and errors found.</returns>
    public static async Task<CsvValidationResult> ValidateFileAsync(
        string path,
        CsvValidationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        options ??= new CsvValidationOptions();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxErrors);

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var sample = new byte[DETECTION_SAMPLE_BYTES];
        int sampleLength = await ReadSampleAsync(stream, sample, cancellationToken).ConfigureAwait(false);

        char delimiter = options.Delimiter ?? ',';
        bool whitespaceOnly = sampleLength == 0 || (IsWhiteSpaceOnly(sample.AsSpan(0, sampleLength)) &&
            await IsRemainingWhiteSpaceAsync(stream, cancellationToken).ConfigureAwait(false));
        if (whitespaceOnly)
        {
            return new CsvValidationResult
            {
                Delimiter = delimiter,
                Errors = options.AllowEmptyFile ? [] : [new CsvValidationError
                {
                    ErrorType = CsvValidationErrorType.EmptyFile,
                    Message = "CSV file is empty"
                }]
            };
        }

        if (!options.Delimiter.HasValue && options.ExpectedColumnCount != 1)
        {
            try
            {
                if (options.SkipRows > 0)
                {
                    stream.Position = 0;
                    await SkipLogicalRowsAsync(stream, options.SkipRows, options.GetEffectiveParseOptions().MaxRowSize,
                        sample, cancellationToken).ConfigureAwait(false);
                    sampleLength = await ReadSampleAsync(stream, sample, cancellationToken).ConfigureAwait(false);
                }
                if (sampleLength > 0)
                    delimiter = CsvDelimiterDetector.DetectDelimiter(sample.AsSpan(0, sampleLength));
            }
            catch (CsvException ex)
            {
                return new CsvValidationResult
                {
                    Delimiter = delimiter,
                    Errors = [new CsvValidationError
                    {
                        ErrorType = CsvValidationErrorType.ParseError,
                        Message = $"Parse error: {ex.Message}"
                    }]
                };
            }
            catch (InvalidOperationException ex)
            {
                return new CsvValidationResult
                {
                    Delimiter = delimiter,
                    Errors = [new CsvValidationError
                    {
                        ErrorType = CsvValidationErrorType.DelimiterDetectionFailed,
                        Message = $"Could not detect delimiter: {ex.Message}"
                    }]
                };
            }
        }

        stream.Position = 0;
        var parseOptions = options.GetEffectiveParseOptions() with { Delimiter = delimiter };
        if (options.ParseOptions is null)
            parseOptions = parseOptions with { MaxRowCount = int.MaxValue };
        parseOptions.Validate();

        var errors = new List<CsvValidationError>();
        var headers = new List<string>();
        int totalRows = 0;
        int detectedColumnCount = 0;
        int expectedColumnCount = options.ExpectedColumnCount ?? 0;
        bool stoppedEarly = false;

        CsvValidationResult Result() => new()
        {
            Errors = errors,
            Headers = headers,
            TotalRows = totalRows,
            ColumnCount = detectedColumnCount,
            Delimiter = delimiter,
            StoppedEarly = stoppedEarly
        };

        await using var reader = new CsvAsyncStreamReader(stream, parseOptions, leaveOpen: true,
            initialBufferSize: 16 * 1024);
        try
        {
            for (int i = 0; i < options.SkipRows && await reader.MoveNextAsync(cancellationToken).ConfigureAwait(false); i++)
                totalRows++;

            if (options.HasHeaderRow && await reader.MoveNextAsync(cancellationToken).ConfigureAwait(false))
            {
                totalRows++;
                var header = reader.Current;
                detectedColumnCount = header.ColumnCount;
                for (int i = 0; i < header.ColumnCount; i++)
                    headers.Add(header.GetString(i));

                if (options.RequiredHeaders is not null)
                {
                    foreach (var required in options.RequiredHeaders)
                    {
                        if (!headers.Contains(required, StringComparer.OrdinalIgnoreCase))
                            errors.Add(new CsvValidationError
                            {
                                ErrorType = CsvValidationErrorType.MissingHeader,
                                Message = $"Required header '{required}' is missing",
                                RowNumber = 1,
                                Expected = required
                            });
                        if (errors.Count >= options.MaxErrors)
                        {
                            stoppedEarly = true;
                            return Result();
                        }
                    }
                }

                if (expectedColumnCount > 0 && header.ColumnCount != expectedColumnCount)
                    errors.Add(new CsvValidationError
                    {
                        ErrorType = CsvValidationErrorType.ColumnCountMismatch,
                        Message = $"Header has {header.ColumnCount} columns, expected {expectedColumnCount}",
                        RowNumber = 1,
                        Expected = expectedColumnCount.ToString(),
                        Actual = header.ColumnCount.ToString()
                    });
            }

            while (errors.Count < options.MaxErrors && await reader.MoveNextAsync(cancellationToken).ConfigureAwait(false))
            {
                totalRows++;
                int columnCount = reader.Current.ColumnCount;
                if (detectedColumnCount == 0)
                    detectedColumnCount = columnCount;

                if (options.CheckConsistentColumnCount)
                {
                    int expected = expectedColumnCount > 0 ? expectedColumnCount : detectedColumnCount;
                    if (columnCount != expected)
                        errors.Add(new CsvValidationError
                        {
                            ErrorType = CsvValidationErrorType.InconsistentColumnCount,
                            Message = $"Row has {columnCount} columns, expected {expected}",
                            RowNumber = totalRows,
                            Expected = expected.ToString(),
                            Actual = columnCount.ToString()
                        });
                }

                if (errors.Count >= options.MaxErrors)
                {
                    stoppedEarly = true;
                    break;
                }

                if (options.MaxRows > 0 && totalRows > options.MaxRows)
                {
                    errors.Add(new CsvValidationError
                    {
                        ErrorType = CsvValidationErrorType.TooManyRows,
                        Message = $"CSV exceeds maximum allowed rows ({options.MaxRows})",
                        RowNumber = totalRows
                    });
                    break;
                }
            }

            stoppedEarly = errors.Count >= options.MaxErrors;
        }
        catch (CsvException ex)
        {
            if (errors.Count < options.MaxErrors)
                errors.Add(new CsvValidationError
                {
                    ErrorType = CsvValidationErrorType.ParseError,
                    Message = $"Parse error: {ex.Message}",
                    RowNumber = ex.Row ?? 0,
                    ColumnNumber = ex.Column ?? 0
                });
        }

        int nonDataRows = options.SkipRows + (options.HasHeaderRow ? 1 : 0);
        if (totalRows <= nonDataRows && !options.AllowEmptyFile && errors.Count < options.MaxErrors)
            errors.Add(new CsvValidationError
            {
                ErrorType = CsvValidationErrorType.EmptyFile,
                Message = "CSV contains no data rows"
            });

        return Result();
    }

    private static async Task<int> ReadSampleAsync(Stream stream, byte[] sample, CancellationToken cancellationToken)
    {
        int length = 0;
        while (length < sample.Length)
        {
            int read = await stream.ReadAsync(sample.AsMemory(length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            length += read;
        }
        return length;
    }

    private static async Task<bool> IsRemainingWhiteSpaceAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (!IsWhiteSpaceOnly(buffer.AsSpan(0, read)))
                return false;
        }
        return true;
    }

    private static async Task SkipLogicalRowsAsync(Stream stream, int rows, int? maxRowSize, byte[] buffer,
        CancellationToken cancellationToken)
    {
        bool quoted = false;
        bool afterCr = false;
        int skipped = 0;
        int rowLimit = maxRowSize ?? CsvAsyncStreamReader.ABSOLUTE_MAX_BUFFER_SIZE;
        long rowBytes = 0;
        int read;
        while (skipped < rows && (read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            for (int i = 0; i < read; i++)
            {
                byte value = buffer[i];
                if (afterCr)
                {
                    afterCr = false;
                    if (value == (byte)'\n')
                        continue;
                }
                if (++rowBytes > rowLimit)
                    throw new CsvException(CsvErrorCode.ParseError,
                        $"Row exceeds maximum size of {rowLimit:N0} bytes while skipping preamble.", skipped + 1);
                if (value == (byte)'"')
                    quoted = !quoted;
                if (!quoted && (value == (byte)'\r' || value == (byte)'\n'))
                {
                    skipped++;
                    rowBytes = 0;
                    if (value == (byte)'\r')
                        afterCr = true;
                    if (skipped == rows)
                    {
                        stream.Position -= read - i - 1;
                        if (afterCr && stream.Position < stream.Length)
                        {
                            int next = stream.ReadByte();
                            if (next != (byte)'\n')
                                stream.Position--;
                        }
                        return;
                    }
                }
            }
        }
    }
}
