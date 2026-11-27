using HeroParser.Internal;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Detection;
using HeroParser.SeparatedValues.Reading.Streaming;
using System.Globalization;
using System.Text;

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

        var parseOptions = options.GetEffectiveParseOptions();
        if (options.ParseOptions is null)
            parseOptions = parseOptions with { MaxRowCount = int.MaxValue };
        if (options.Delimiter.HasValue)
        {
            parseOptions = parseOptions with { Delimiter = options.Delimiter.Value };
            parseOptions.Validate();
        }
        else
        {
            ValidateSamplingOptions(parseOptions);
        }

        var samplingParseOptions = parseOptions.MaxInputSize is not null
            ? parseOptions with { MaxInputSize = null }
            : parseOptions;

        Stream? stream = null;
        try
        {
            char delimiter = options.Delimiter ?? ',';
            var sample = new byte[DETECTION_SAMPLE_BYTES];
            int sampleLength;
            try
            {
                stream = OpenValidationStream(path, parseOptions);
                sampleLength = await ReadSampleAsync(stream, sample, samplingParseOptions, cancellationToken).ConfigureAwait(false);

                bool whitespaceOnly = sampleLength == 0 || (IsWhiteSpaceOnly(sample.AsSpan(0, sampleLength)) &&
                    await IsRemainingWhiteSpaceAsync(stream, sampleLength, samplingParseOptions, cancellationToken).ConfigureAwait(false));
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
            }
            catch (CsvException ex)
            {
                return new CsvValidationResult
                {
                    Delimiter = delimiter,
                    Errors = [new CsvValidationError
                    {
                        ErrorType = CsvValidationErrorType.ParseError,
                        Message = $"Parse error: {ex.Message}",
                        RowNumber = ex.Row ?? 0,
                        ColumnNumber = ex.Column ?? 0
                    }]
                };
            }

            if (!options.Delimiter.HasValue && options.ExpectedColumnCount != 1)
            {
                try
                {
                    if (options.SkipRows > 0)
                    {
                        stream = await RewindOrReopenAsync(stream, path, parseOptions).ConfigureAwait(false);
                        sampleLength = await ReadSampleAfterSkippingRowsAsync(stream, options.SkipRows,
                            samplingParseOptions, sample, cancellationToken).ConfigureAwait(false);
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

            stream = await RewindOrReopenAsync(stream, path, parseOptions).ConfigureAwait(false);
            parseOptions = parseOptions with { Delimiter = delimiter };
            parseOptions.Validate();
            var readerParseOptions = parseOptions.MaxInputSize is not null
                ? parseOptions with { MaxInputSize = null }
                : parseOptions;

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

            await using var reader = new CsvAsyncStreamReader(stream, readerParseOptions, leaveOpen: true,
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
                        headers.Add(HeaderName(header, i, parseOptions));

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
        finally
        {
            if (stream is not null)
                await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static Stream OpenValidationStream(string path, CsvReadOptions parseOptions)
    {
        Stream rawStream = new CompressedFile(path).OpenRead(
            bufferSize: 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        Stream countingStream = parseOptions.MaxInputSize is not null
            ? new MaxInputSizeReadStream(rawStream, parseOptions)
            : rawStream;
        try
        {
            byte[] bom = new byte[4];
            int read = 0;
            while (read < bom.Length)
            {
                int n = countingStream.Read(bom, read, bom.Length - read);
                if (n == 0)
                    break;
                read += n;
            }

            if (read == 4 && bom[0] == 0xFF && bom[1] == 0xFE && bom[2] == 0x00 && bom[3] == 0x00)
                return new Utf16ToUtf8ReadStream(countingStream, Encoding.UTF32);

            if (read == 4 && bom[0] == 0x00 && bom[1] == 0x00 && bom[2] == 0xFE && bom[3] == 0xFF)
                return new Utf16ToUtf8ReadStream(countingStream, new UTF32Encoding(bigEndian: true, byteOrderMark: true));

            if (read >= 2 && ((bom[0] == 0xFF && bom[1] == 0xFE) || (bom[0] == 0xFE && bom[1] == 0xFF)))
            {
                Encoding utf16 = bom[0] == 0xFF ? Encoding.Unicode : Encoding.BigEndianUnicode;
                Stream remainder = read > 2
                    ? new PrefixReadStream(countingStream, bom[2..read], leaveOpen: false)
                    : countingStream;
                return new Utf16ToUtf8ReadStream(remainder, utf16);
            }

            if (countingStream.CanSeek)
            {
                countingStream.Position = 0;
                return countingStream;
            }

            if (read > 0)
                return new PrefixReadStream(countingStream, bom[..read], leaveOpen: false);

            return countingStream;
        }
        catch
        {
            countingStream.Dispose();
            throw;
        }
    }

    private static async ValueTask<Stream> RewindOrReopenAsync(
        Stream stream, string path, CsvReadOptions parseOptions)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
            return stream;
        }

        await stream.DisposeAsync().ConfigureAwait(false);
        return OpenValidationStream(path, parseOptions);
    }

    private static int GetCappedReadCount(int bufferAvailable, long bytesReadSoFar, long? maxInputSize)
    {
        if (maxInputSize is not { } limit)
            return bufferAvailable;

        long remainingAllowed = Math.Max(0L, limit - bytesReadSoFar);
        long maxToRequest = remainingAllowed < bufferAvailable ? remainingAllowed + 1 : bufferAvailable;
        return (int)Math.Min(bufferAvailable, maxToRequest);
    }

    private static async Task<int> ReadSampleAsync(
        Stream stream,
        byte[] sample,
        CsvReadOptions parseOptions,
        CancellationToken cancellationToken)
    {
        int length = 0;
        while (length < sample.Length)
        {
            int toRead = GetCappedReadCount(sample.Length - length, length, parseOptions.MaxInputSize);
            int read = await stream.ReadAsync(sample.AsMemory(length, toRead), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            length += read;
            parseOptions.ValidateInputSize(length);
        }
        return length;
    }

    private static async Task<bool> IsRemainingWhiteSpaceAsync(
        Stream stream,
        long initialBytesRead,
        CsvReadOptions parseOptions,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[16 * 1024];
        long totalBytesRead = initialBytesRead;
        while (true)
        {
            int toRead = GetCappedReadCount(buffer.Length, totalBytesRead, parseOptions.MaxInputSize);
            int read = await stream.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
                break;
            totalBytesRead += read;
            parseOptions.ValidateInputSize(totalBytesRead);
            if (!IsWhiteSpaceOnly(buffer.AsSpan(0, read)))
                return false;
        }
        return true;
    }

    private static async Task<int> ReadSampleAfterSkippingRowsAsync(
        Stream stream,
        int rows,
        CsvReadOptions parseOptions,
        byte[] sample,
        CancellationToken cancellationToken)
    {
        bool quoted = false;
        bool afterCr = false;
        bool hasQuote = parseOptions.EnableQuotedFields && parseOptions.Quote <= 127;
        byte quoteByte = (byte)parseOptions.Quote;
        int skipped = 0;
        int rowLimit = parseOptions.MaxRowSize ?? CsvAsyncStreamReader.ABSOLUTE_MAX_BUFFER_SIZE;
        long rowBytes = 0;
        long totalBytesRead = 0;
        while (skipped < rows)
        {
            int toRead = GetCappedReadCount(sample.Length, totalBytesRead, parseOptions.MaxInputSize);
            int read = await stream.ReadAsync(sample.AsMemory(0, toRead), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;

            totalBytesRead += read;
            parseOptions.ValidateInputSize(totalBytesRead);

            for (int i = 0; i < read; i++)
            {
                byte value = sample[i];
                if (afterCr)
                {
                    afterCr = false;
                    if (value == (byte)'\n')
                        continue;
                }
                if (++rowBytes > rowLimit)
                    throw new CsvException(CsvErrorCode.ParseError,
                        string.Create(CultureInfo.InvariantCulture, $"Row exceeds maximum size of {rowLimit:N0} bytes while skipping preamble."), skipped + 1);
                if (hasQuote && value == quoteByte)
                    quoted = !quoted;
                if (!quoted && (value == (byte)'\r' || value == (byte)'\n'))
                {
                    skipped++;
                    rowBytes = 0;
                    if (value == (byte)'\r')
                        afterCr = true;
                    if (skipped == rows)
                    {
                        int start = i + 1;
                        if (value == (byte)'\r')
                        {
                            if (start < read)
                            {
                                if (sample[start] == (byte)'\n')
                                    start++;
                            }
                            else
                            {
                                int peekToRead = GetCappedReadCount(sample.Length, totalBytesRead, parseOptions.MaxInputSize);
                                read = await stream.ReadAsync(sample.AsMemory(0, peekToRead), cancellationToken).ConfigureAwait(false);
                                if (read == 0)
                                    return 0;
                                totalBytesRead += read;
                                parseOptions.ValidateInputSize(totalBytesRead);
                                start = sample[0] == (byte)'\n' ? 1 : 0;
                            }
                        }

                        int remaining = read - start;
                        if (remaining > 0 && start > 0)
                            Buffer.BlockCopy(sample, start, sample, 0, remaining);

                        while (remaining < sample.Length)
                        {
                            int fillToRead = GetCappedReadCount(sample.Length - remaining, totalBytesRead, parseOptions.MaxInputSize);
                            int extra = await stream.ReadAsync(sample.AsMemory(remaining, fillToRead), cancellationToken).ConfigureAwait(false);
                            if (extra == 0)
                                break;
                            totalBytesRead += extra;
                            parseOptions.ValidateInputSize(totalBytesRead);
                            remaining += extra;
                        }

                        return remaining;
                    }
                }
            }
        }

        return 0;
    }

    private static void ValidateSamplingOptions(CsvReadOptions parseOptions)
    {
        if (parseOptions.MaxInputSize is <= 0)
        {
            throw new CsvException(
                CsvErrorCode.InvalidOptions,
                $"MaxInputSize must be positive when specified, got {parseOptions.MaxInputSize.Value}");
        }

        if (parseOptions.MaxRowSize is <= 0)
        {
            throw new CsvException(
                CsvErrorCode.InvalidOptions,
                $"MaxRowSize must be positive when specified, got {parseOptions.MaxRowSize.Value}");
        }
    }
}
