using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeroParser.Conversion;
using HeroParser.Htbs.Reading;
using HeroParser.Htbs.Records;
using HeroParser.Htbs.Writing;

namespace HeroParser;

/// <summary>
/// Gateway for High-Throughput Tabular Binary (HTB) high-performance binary tabular parsing and serialization.
/// </summary>
public static partial class Htb
{
    /// <summary>
    /// Creates a fluent reader builder for deserializing HTB binary streams into strongly-typed records.
    /// </summary>
    /// <typeparam name="T">The record type to deserialize.</typeparam>
    public static HtbRecordReaderBuilder<T> Read<T>() where T : new() => new();

    /// <summary>
    /// Creates a fluent writer builder for serializing strongly-typed records into the HTB binary format.
    /// </summary>
    /// <typeparam name="T">The record type to serialize.</typeparam>
    public static HtbRecordWriterBuilder<T> Write<T>() where T : new() => new();

    /// <summary>
    /// Converts a CSV file directly to the HTB binary format.
    /// </summary>
    public static void ConvertFromCsv(string csvPath, string htbPath, HtbSchema schema, CsvToHtbOptions? options = null)
        => CsvToHtbConverter.ConvertFile(csvPath, htbPath, schema, options);

    /// <summary>
    /// Converts an HTB binary file directly to the CSV text format.
    /// </summary>
    public static void ConvertToCsv(string htbPath, string csvPath, HtbToCsvOptions? options = null)
        => HtbToCsvConverter.ConvertFile(htbPath, csvPath, options);

    /// <summary>
    /// Asynchronously converts a CSV stream directly to the HTB binary format.
    /// </summary>
    public static Task ConvertFromCsvAsync(
        Stream csvStream,
        Stream htbStream,
        HtbSchema schema,
        CsvToHtbOptions? options = null,
        CancellationToken cancellationToken = default)
        => CsvToHtbConverter.ConvertAsync(csvStream, htbStream, schema, options, cancellationToken);

    /// <summary>
    /// Asynchronously converts an HTB binary stream directly to the CSV text format written into an output TextWriter.
    /// </summary>
    public static Task ConvertToCsvAsync(
        Stream htbStream,
        TextWriter csvWriter,
        HtbToCsvOptions? options = null,
        CancellationToken cancellationToken = default)
        => HtbToCsvConverter.ConvertAsync(htbStream, csvWriter, options, cancellationToken);
}
