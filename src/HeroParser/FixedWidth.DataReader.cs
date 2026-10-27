using System.Text;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Reading.Data;
using HeroParser.FixedWidths.Streaming;

namespace HeroParser;

public static partial class FixedWidth
{
    /// <summary>
    /// Creates an <see cref="System.Data.IDataReader"/> over a fixed-width stream.
    /// </summary>
    /// <param name="stream">Readable stream containing fixed-width data.</param>
    /// <param name="options">Parser configuration; defaults to <see cref="FixedWidthReadOptions.Default"/>.</param>
    /// <param name="readerOptions">DataReader configuration; defaults to <see cref="FixedWidthDataReaderOptions.Default"/>.</param>
    /// <param name="encoding">Text encoding; defaults to UTF-8 with BOM detection.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, the stream remains open after the reader is disposed.</param>
    /// <param name="bufferSize">Initial pooled buffer size in characters.</param>
    public static FixedWidthDataReader CreateDataReader(
        Stream stream,
        FixedWidthReadOptions? options = null,
        FixedWidthDataReaderOptions? readerOptions = null,
        Encoding? encoding = null,
        bool leaveOpen = true,
        int bufferSize = 16 * 1024)
    {
        ArgumentNullException.ThrowIfNull(stream);

        encoding ??= Encoding.UTF8;
        options ??= FixedWidthReadOptions.Default;
        options.Validate();

        var dataOptions = readerOptions ?? FixedWidthDataReaderOptions.Default;
        if (stream.CanSeek)
        {
            options.ValidateInputSize(stream.Length);
        }

        // Merge DataReader-level SkipRows into parser options
        if (dataOptions.SkipRows > 0)
        {
            options = options with { SkipRows = options.SkipRows + dataOptions.SkipRows };
        }

        var asyncReader = new FixedWidthAsyncStreamReader(stream, options, encoding, leaveOpen, initialBufferSize: bufferSize);
        try
        {
            return new FixedWidthDataReader(asyncReader, options, dataOptions, encoding);
        }
        catch
        {
            // Schema validation can fail after the reader has rented a pooled char buffer.
            // Dispose the failed handoff without changing the caller's stream ownership.
            asyncReader.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }
    }

    /// <summary>
    /// Creates an <see cref="System.Data.IDataReader"/> over a fixed-width file on disk.
    /// </summary>
    /// <param name="path">Filesystem path to the fixed-width file.</param>
    /// <param name="options">Parser configuration; defaults to <see cref="FixedWidthReadOptions.Default"/>.</param>
    /// <param name="readerOptions">DataReader configuration; defaults to <see cref="FixedWidthDataReaderOptions.Default"/>.</param>
    /// <param name="encoding">Text encoding; defaults to UTF-8 with BOM detection.</param>
    /// <param name="bufferSize">Initial pooled buffer size in characters.</param>
    public static FixedWidthDataReader CreateDataReader(
        string path,
        FixedWidthReadOptions? options = null,
        FixedWidthDataReaderOptions? readerOptions = null,
        Encoding? encoding = null,
        int bufferSize = 16 * 1024)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        encoding ??= Encoding.UTF8;
        options ??= FixedWidthReadOptions.Default;
        options.Validate();

        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        try
        {
            return CreateDataReader(stream, options, readerOptions, encoding, leaveOpen: false, bufferSize: bufferSize);
        }
        catch
        {
            // The reader constructor can reject a missing or invalid column schema
            // after the file is opened. Only a successfully created reader owns it.
            stream.Dispose();
            throw;
        }
    }
}

