using System.Threading.Tasks;
using HeroParser.Htbs.Writing;

namespace HeroParser.Htbs.Records;

/// <summary>
/// Interface for High-Throughput Tabular Binary (HTB) writers that serialize records directly without boxing.
/// </summary>
/// <typeparam name="T">The record type to write.</typeparam>
public interface IHtbWriter<T> where T : new()
{
    /// <summary>
    /// Writes a single record directly to the writer.
    /// </summary>
    void WriteRecord(HtbRecordWriter<T> writer, T record);

    /// <summary>
    /// Writes a single record asynchronously directly to the writer.
    /// </summary>
    Task WriteRecordAsync(HtbRecordWriter<T> writer, T record);
}
