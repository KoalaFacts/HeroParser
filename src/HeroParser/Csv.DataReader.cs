using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Reading.Data;
using HeroParser.SeparatedValues.Reading.Streaming;

namespace HeroParser;

public static partial class Csv
{
    /// <summary>
    /// Creates an <see cref="System.Data.IDataReader"/> over a CSV stream (UTF-8).
    /// </summary>
    /// <param name="stream">Readable stream containing CSV data.</param>
    /// <param name="options">Parser configuration; defaults to <see cref="CsvReadOptions.Default"/>.</param>
    /// <param name="readerOptions">DataReader configuration; defaults to <see cref="CsvDataReaderOptions.Default"/>.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, the stream remains open after the reader is disposed.</param>
    /// <param name="bufferSize">Initial pooled buffer size in bytes.</param>
    public static CsvDataReader CreateDataReader(
        Stream stream,
        CsvReadOptions? options = null,
        CsvDataReaderOptions? readerOptions = null,
        bool leaveOpen = true,
        int bufferSize = 16 * 1024)
    {
        ArgumentNullException.ThrowIfNull(stream);

        options ??= CsvReadOptions.Default;
        options.Validate();

        var dataOptions = readerOptions ?? CsvDataReaderOptions.Default;
        var asyncReader = new CsvAsyncStreamReader(stream, options, leaveOpen, bufferSize, dataOptions.SkipRows);

        return new CsvDataReader(asyncReader, options, dataOptions);
    }

    /// <summary>
    /// Creates an <see cref="System.Data.IDataReader"/> over a CSV file on disk (UTF-8).
    /// </summary>
    /// <param name="path">Filesystem path to the CSV file.</param>
    /// <param name="options">Parser configuration; defaults to <see cref="CsvReadOptions.Default"/>.</param>
    /// <param name="readerOptions">DataReader configuration; defaults to <see cref="CsvDataReaderOptions.Default"/>.</param>
    /// <param name="bufferSize">Initial pooled buffer size in bytes.</param>
    public static CsvDataReader CreateDataReader(
        string path,
        CsvReadOptions? options = null,
        CsvDataReaderOptions? readerOptions = null,
        int bufferSize = 16 * 1024)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.SequentialScan);

        return CreateDataReader(stream, options, readerOptions, leaveOpen: false, bufferSize: bufferSize);
    }
}
