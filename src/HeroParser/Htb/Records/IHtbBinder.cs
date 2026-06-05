using System.Threading.Tasks;
using HeroParser.Htbs.Reading;

namespace HeroParser.Htbs.Records;

/// <summary>
/// Interface for High-Throughput Tabular Binary (HTB) binders that map binary columns directly to records.
/// </summary>
/// <typeparam name="T">The record type to bind to.</typeparam>
public interface IHtbBinder<T> where T : new()
{
    /// <summary>
    /// Checks if a column at the specified index is bound to a record property.
    /// </summary>
    bool IsColumnBound(int columnIndex);

    /// <summary>
    /// Binds a column value from the reader directly to the record property.
    /// </summary>
    void BindField(T instance, int columnIndex, HtbRecordReader<T> reader, bool isNull);

    /// <summary>
    /// Binds a column value asynchronously from the reader directly to the record property.
    /// </summary>
    ValueTask BindFieldAsync(T instance, int columnIndex, HtbRecordReader<T> reader, bool isNull);
}
