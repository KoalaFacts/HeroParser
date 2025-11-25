using System.Globalization;
using System.Text;

namespace HeroParser.SeparatedValues;

/// <summary>
/// Writes CSV data to a TextWriter or Stream following RFC 4180 conventions.
/// </summary>
public sealed class CsvWriter : IDisposable
{
    private readonly TextWriter writer;
    private readonly CsvWriterOptions options;
    private readonly bool leaveOpen;
    private bool disposed;
    private bool isFirstField;

    /// <summary>
    /// Initializes a new instance of the <see cref="CsvWriter"/> class.
    /// </summary>
    /// <param name="writer">The TextWriter to write CSV data to.</param>
    /// <param name="options">Optional writer configuration.</param>
    /// <param name="leaveOpen">When true, the writer remains open after disposal.</param>
    public CsvWriter(TextWriter writer, CsvWriterOptions? options = null, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(writer);
        this.writer = writer;
        this.options = options ?? CsvWriterOptions.Default;
        this.options.Validate();
        this.leaveOpen = leaveOpen;
        isFirstField = true;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CsvWriter"/> class from a Stream.
    /// </summary>
    /// <param name="stream">The stream to write CSV data to.</param>
    /// <param name="options">Optional writer configuration.</param>
    /// <param name="encoding">Text encoding; defaults to UTF-8.</param>
    /// <param name="leaveOpen">When true, the stream remains open after disposal.</param>
    public CsvWriter(Stream stream, CsvWriterOptions? options = null, Encoding? encoding = null, bool leaveOpen = false)
        : this(new StreamWriter(stream, encoding ?? Encoding.UTF8, bufferSize: 1024, leaveOpen: leaveOpen), options, leaveOpen: false)
    {
    }

    /// <summary>
    /// Writes a single field value.
    /// </summary>
    /// <param name="value">The field value to write.</param>
    public void WriteField(string? value)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (!isFirstField)
        {
            writer.Write(options.Delimiter);
        }

        if (value == null)
        {
            isFirstField = false;
            return;
        }

        bool needsQuoting = options.AlwaysQuote || NeedsQuoting(value);

        if (needsQuoting)
        {
            writer.Write(options.Quote);
            WriteEscaped(value);
            writer.Write(options.Quote);
        }
        else
        {
            writer.Write(value);
        }

        isFirstField = false;
    }

    /// <summary>
    /// Writes a single field value by converting it to a string.
    /// </summary>
    /// <param name="value">The field value to write.</param>
    public void WriteField<T>(T? value)
    {
        string? stringValue = value switch
        {
            null => null,
            string s => s,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };

        WriteField(stringValue);
    }

    /// <summary>
    /// Writes an entire row of fields and terminates the line.
    /// </summary>
    /// <param name="fields">The field values to write.</param>
    public void WriteRow(params string?[] fields)
    {
        WriteRow((IEnumerable<string?>)fields);
    }

    /// <summary>
    /// Writes an entire row of fields and terminates the line.
    /// </summary>
    /// <param name="fields">The field values to write.</param>
    public void WriteRow(IEnumerable<string?> fields)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        foreach (var field in fields)
        {
            WriteField(field);
        }

        EndRow();
    }

    /// <summary>
    /// Writes an entire row of strongly typed fields and terminates the line.
    /// </summary>
    /// <param name="fields">The field values to write.</param>
    public void WriteRow<T>(IEnumerable<T?> fields)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        foreach (var field in fields)
        {
            WriteField(field);
        }

        EndRow();
    }

    /// <summary>
    /// Ends the current row and writes the newline sequence.
    /// </summary>
    public void EndRow()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        writer.Write(options.NewLine);
        isFirstField = true;
    }

    /// <summary>
    /// Flushes the underlying writer.
    /// </summary>
    public void Flush()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        writer.Flush();
    }

    /// <summary>
    /// Asynchronously flushes the underlying writer.
    /// </summary>
    public async Task FlushAsync()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await writer.FlushAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Disposes the writer and optionally the underlying TextWriter.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
            return;

        if (!leaveOpen)
        {
            writer.Dispose();
        }

        disposed = true;
    }

    private bool NeedsQuoting(string value)
    {
        foreach (char c in value)
        {
            if (c == options.Delimiter || c == options.Quote || c == '\r' || c == '\n')
            {
                return true;
            }
        }

        return false;
    }

    private void WriteEscaped(string value)
    {
        // RFC 4180: quotes inside quoted fields are escaped by doubling them
        foreach (char c in value)
        {
            writer.Write(c);
            if (c == options.Quote)
            {
                writer.Write(options.Quote);
            }
        }
    }
}
