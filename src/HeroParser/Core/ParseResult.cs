using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace HeroParser.Core
{
    /// <summary>
    /// Comprehensive result container providing records, error information, performance statistics,
    /// and source metadata with lazy enumeration support for streaming scenarios.
    /// </summary>
    /// <typeparam name="T">Type of parsed records</typeparam>
    public sealed class ParseResult<T> : IEnumerable<T>
    {
        private readonly IEnumerable<T> _records;
        private readonly ParseError[] _errors;
        private readonly ParseStatistics _statistics;
        private readonly SourceMetadata _metadata;

        /// <summary>
        /// Initializes a new parse result with records and diagnostics.
        /// </summary>
        /// <param name="records">Parsed records (lazy enumerable)</param>
        /// <param name="errors">Collection of parse errors</param>
        /// <param name="statistics">Performance statistics</param>
        /// <param name="metadata">Source metadata</param>
        public ParseResult(IEnumerable<T> records, ParseError[] errors, ParseStatistics statistics, SourceMetadata metadata)
        {
            _records = records ?? throw new ArgumentNullException(nameof(records));
            _errors = errors ?? Array.Empty<ParseError>();
            _statistics = statistics ?? throw new ArgumentNullException(nameof(statistics));
            _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        }

        /// <summary>
        /// Gets the parsed records as a lazy enumerable.
        /// Records are materialized only when enumerated.
        /// </summary>
        public IEnumerable<T> Records => _records;

        /// <summary>
        /// Gets the collection of parse errors encountered during processing.
        /// </summary>
        public IReadOnlyList<ParseError> Errors => _errors;

        /// <summary>
        /// Gets performance statistics for the parsing operation.
        /// </summary>
        public ParseStatistics Statistics => _statistics;

        /// <summary>
        /// Gets metadata about the source data.
        /// </summary>
        public SourceMetadata Metadata => _metadata;

        /// <summary>
        /// Gets whether the parsing operation completed successfully without fatal errors.
        /// </summary>
        public bool IsSuccess => !_errors.Any(e => e.IsFatal);

        /// <summary>
        /// Gets whether any non-fatal errors were encountered during parsing.
        /// </summary>
        public bool HasWarnings => _errors.Any(e => !e.IsFatal);

        /// <summary>
        /// Gets the total number of errors and warnings.
        /// </summary>
        public int ErrorCount => _errors.Length;

        /// <summary>
        /// Gets fatal errors that halted parsing.
        /// </summary>
        public IEnumerable<ParseError> FatalErrors => _errors.Where(e => e.IsFatal);

        /// <summary>
        /// Gets non-fatal warnings that did not halt parsing.
        /// </summary>
        public IEnumerable<ParseError> Warnings => _errors.Where(e => !e.IsFatal);

        /// <summary>
        /// Returns an enumerator for the parsed records.
        /// </summary>
        public IEnumerator<T> GetEnumerator() => _records.GetEnumerator();

        /// <summary>
        /// Returns a non-generic enumerator for the parsed records.
        /// </summary>
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Creates a successful parse result with no errors.
        /// </summary>
        /// <param name="records">Parsed records</param>
        /// <param name="statistics">Performance statistics</param>
        /// <param name="metadata">Source metadata</param>
        /// <returns>Successful parse result</returns>
        public static ParseResult<T> Success(IEnumerable<T> records, ParseStatistics statistics, SourceMetadata metadata)
        {
            return new ParseResult<T>(records, Array.Empty<ParseError>(), statistics, metadata);
        }

        /// <summary>
        /// Creates a failed parse result with fatal errors.
        /// </summary>
        /// <param name="errors">Fatal errors that prevented parsing</param>
        /// <param name="statistics">Partial statistics</param>
        /// <param name="metadata">Source metadata</param>
        /// <returns>Failed parse result</returns>
        public static ParseResult<T> Failure(ParseError[] errors, ParseStatistics statistics, SourceMetadata metadata)
        {
            return new ParseResult<T>(Enumerable.Empty<T>(), errors, statistics, metadata);
        }

        /// <summary>
        /// Creates a partial parse result with records and non-fatal errors.
        /// </summary>
        /// <param name="records">Parsed records</param>
        /// <param name="warnings">Non-fatal warnings</param>
        /// <param name="statistics">Performance statistics</param>
        /// <param name="metadata">Source metadata</param>
        /// <returns>Partial parse result</returns>
        public static ParseResult<T> Partial(IEnumerable<T> records, ParseError[] warnings, ParseStatistics statistics, SourceMetadata metadata)
        {
            return new ParseResult<T>(records, warnings, statistics, metadata);
        }
    }

    /// <summary>
    /// Represents a parse error with location and recovery information.
    /// </summary>
    public sealed class ParseError
    {
        /// <summary>
        /// Gets the error message describing what went wrong.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Gets the line number where the error occurred (1-based).
        /// </summary>
        public int LineNumber { get; }

        /// <summary>
        /// Gets the column position where the error occurred (1-based).
        /// </summary>
        public int ColumnPosition { get; }

        /// <summary>
        /// Gets the raw data that caused the error.
        /// </summary>
        public string RawData { get; }

        /// <summary>
        /// Gets whether this error is fatal and halted parsing.
        /// </summary>
        public bool IsFatal { get; }

        /// <summary>
        /// Gets the error code for programmatic handling.
        /// </summary>
        public string ErrorCode { get; }

        /// <summary>
        /// Gets the recovery strategy used to continue parsing.
        /// </summary>
        public string RecoveryStrategy { get; }

        /// <summary>
        /// Initializes a new parse error.
        /// </summary>
        public ParseError(string message, int lineNumber, int columnPosition, string rawData,
                         bool isFatal = false, string errorCode = "", string recoveryStrategy = "")
        {
            Message = message ?? throw new ArgumentNullException(nameof(message));
            LineNumber = lineNumber;
            ColumnPosition = columnPosition;
            RawData = rawData ?? "";
            IsFatal = isFatal;
            ErrorCode = errorCode ?? "";
            RecoveryStrategy = recoveryStrategy ?? "";
        }

        /// <summary>
        /// Creates a fatal error that halts parsing.
        /// </summary>
        public static ParseError Fatal(string message, int lineNumber, int columnPosition, string rawData, string errorCode = "")
        {
            return new ParseError(message, lineNumber, columnPosition, rawData, true, errorCode);
        }

        /// <summary>
        /// Creates a warning that allows parsing to continue.
        /// </summary>
        public static ParseError Warning(string message, int lineNumber, int columnPosition, string rawData,
                                        string errorCode = "", string recoveryStrategy = "")
        {
            return new ParseError(message, lineNumber, columnPosition, rawData, false, errorCode, recoveryStrategy);
        }

        /// <summary>
        /// Returns a string representation of the error.
        /// </summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(IsFatal ? "FATAL" : "WARNING");
            sb.Append($" at line {LineNumber}, column {ColumnPosition}: {Message}");

            if (!string.IsNullOrEmpty(ErrorCode))
                sb.Append($" (Code: {ErrorCode})");

            if (!string.IsNullOrEmpty(RecoveryStrategy))
                sb.Append($" [Recovery: {RecoveryStrategy}]");

            return sb.ToString();
        }
    }

    /// <summary>
    /// Performance statistics for parsing operations.
    /// </summary>
    public sealed class ParseStatistics
    {
        /// <summary>
        /// Gets the total parsing time.
        /// </summary>
        public TimeSpan ParseTime { get; }

        /// <summary>
        /// Gets the throughput in bytes per second.
        /// </summary>
        public double ThroughputBytesPerSecond { get; }

        /// <summary>
        /// Gets the throughput in records per second.
        /// </summary>
        public double ThroughputRecordsPerSecond { get; }

        /// <summary>
        /// Gets the total number of bytes processed.
        /// </summary>
        public long BytesProcessed { get; }

        /// <summary>
        /// Gets the total number of records processed.
        /// </summary>
        public long RecordsProcessed { get; }

        /// <summary>
        /// Gets the peak memory usage during parsing.
        /// </summary>
        public long PeakMemoryUsage { get; }

        /// <summary>
        /// Gets the total memory allocated during parsing.
        /// </summary>
        public long TotalMemoryAllocated { get; }

        /// <summary>
        /// Gets the number of GC collections that occurred during parsing.
        /// </summary>
        public int GCCollections { get; }

        /// <summary>
        /// Initializes new parse statistics.
        /// </summary>
        public ParseStatistics(TimeSpan parseTime, long bytesProcessed, long recordsProcessed,
                              long peakMemoryUsage = 0, long totalMemoryAllocated = 0, int gcCollections = 0)
        {
            ParseTime = parseTime;
            BytesProcessed = bytesProcessed;
            RecordsProcessed = recordsProcessed;
            PeakMemoryUsage = peakMemoryUsage;
            TotalMemoryAllocated = totalMemoryAllocated;
            GCCollections = gcCollections;

            ThroughputBytesPerSecond = parseTime.TotalSeconds > 0 ? bytesProcessed / parseTime.TotalSeconds : 0;
            ThroughputRecordsPerSecond = parseTime.TotalSeconds > 0 ? recordsProcessed / parseTime.TotalSeconds : 0;
        }

        /// <summary>
        /// Creates statistics from a stopwatch measurement.
        /// </summary>
        public static ParseStatistics FromStopwatch(Stopwatch stopwatch, long bytesProcessed, long recordsProcessed)
        {
            return new ParseStatistics(stopwatch.Elapsed, bytesProcessed, recordsProcessed);
        }

        /// <summary>
        /// Returns a string representation of the statistics.
        /// </summary>
        public override string ToString()
        {
            return $"Processed {RecordsProcessed:N0} records ({BytesProcessed:N0} bytes) in {ParseTime.TotalMilliseconds:F1}ms " +
                   $"({ThroughputBytesPerSecond / (1024 * 1024):F1} MB/s, {ThroughputRecordsPerSecond:F0} records/s)";
        }
    }

    /// <summary>
    /// Metadata about the source data and parsing characteristics.
    /// </summary>
    public sealed class SourceMetadata
    {
        /// <summary>
        /// Gets the detected or specified text encoding.
        /// </summary>
        public Encoding Encoding { get; }

        /// <summary>
        /// Gets the detected line ending format.
        /// </summary>
        public LineEndingFormat LineEndingFormat { get; }

        /// <summary>
        /// Gets the estimated total number of records in the source.
        /// </summary>
        public long EstimatedRecordCount { get; }

        /// <summary>
        /// Gets the source file size in bytes (if applicable).
        /// </summary>
        public long SourceSizeBytes { get; }

        /// <summary>
        /// Gets whether the source has a header row.
        /// </summary>
        public bool HasHeaderRow { get; }

        /// <summary>
        /// Gets additional metadata properties.
        /// </summary>
        public IReadOnlyDictionary<string, object> Properties { get; }

        /// <summary>
        /// Initializes new source metadata.
        /// </summary>
        public SourceMetadata(Encoding encoding, LineEndingFormat lineEndingFormat, long estimatedRecordCount,
                             long sourceSizeBytes = 0, bool hasHeaderRow = false,
                             IReadOnlyDictionary<string, object>? properties = null)
        {
            Encoding = encoding ?? throw new ArgumentNullException(nameof(encoding));
            LineEndingFormat = lineEndingFormat;
            EstimatedRecordCount = estimatedRecordCount;
            SourceSizeBytes = sourceSizeBytes;
            HasHeaderRow = hasHeaderRow;
            Properties = properties ?? new Dictionary<string, object>();
        }
    }

    /// <summary>
    /// Line ending format detection results.
    /// </summary>
    public enum LineEndingFormat
    {
        Unknown,
        Windows,    // CRLF
        Unix,       // LF
        MacClassic, // CR
        Mixed       // Multiple formats detected
    }
}