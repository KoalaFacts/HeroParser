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
    /// <param name="options">Parser configuration; defaults to <see cref="FixedWidthParserOptions.Default"/>.</param>
    /// <param name="readerOptions">DataReader configuration; defaults to <see cref="FixedWidthDataReaderOptions.Default"/>.</param>
    /// <param name="encoding">Text encoding; defaults to UTF-8 with BOM detection.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, the stream remains open after the reader is disposed.</param>
    /// <param name="bufferSize">Initial pooled buffer size in characters.</param>
    public static FixedWidthDataReader CreateDataReader(
        Stream stream,
        FixedWidthParserOptions? options = null,
        FixedWidthDataReaderOptions? readerOptions = null,
        Encoding? encoding = null,
        bool leaveOpen = true,
        int bufferSize = 16 * 1024)
    {
        ArgumentNullException.ThrowIfNull(stream);

        encoding ??= Encoding.UTF8;
        options ??= FixedWidthParserOptions.Default;
        options.Validate();

        var dataOptions = readerOptions ?? FixedWidthDataReaderOptions.Default;
        var asyncReader = new FixedWidthAsyncStreamReader(stream, options, encoding, leaveOpen, initialBufferSize: bufferSize);

        return new FixedWidthDataReader(asyncReader, options, dataOptions);
    }

    /// <summary>
    /// Creates an <see cref="System.Data.IDataReader"/> over a fixed-width file on disk.
    /// </summary>
    /// <param name="path">Filesystem path to the fixed-width file.</param>
    /// <param name="options">Parser configuration; defaults to <see cref="FixedWidthParserOptions.Default"/>.</param>
    /// <param name="readerOptions">DataReader configuration; defaults to <see cref="FixedWidthDataReaderOptions.Default"/>.</param>
    /// <param name="encoding">Text encoding; defaults to UTF-8 with BOM detection.</param>
    /// <param name="bufferSize">Initial pooled buffer size in characters.</param>
    public static FixedWidthDataReader CreateDataReader(
        string path,
        FixedWidthParserOptions? options = null,
        FixedWidthDataReaderOptions? readerOptions = null,
        Encoding? encoding = null,
        int bufferSize = 16 * 1024)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        encoding ??= Encoding.UTF8;
        options ??= FixedWidthParserOptions.Default;
        options.Validate();

        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        return CreateDataReader(stream, options, readerOptions, encoding, leaveOpen: false, bufferSize: bufferSize);
    }
}
