using System.Threading;
using System.Threading.Tasks;

namespace HeroParser.SeparatedValues.Writing;

/// <summary>
/// Source-generated writer interface for writing CSV records of type T directly without boxing or allocations.
/// </summary>
/// <typeparam name="T">The record type.</typeparam>
public interface ICsvSourceWriter<in T>
{
    /// <summary>
    /// Writes a single record directly to the CsvStreamWriter.
    /// </summary>
    void WriteRecord(CsvStreamWriter writer, T record);

    /// <summary>
    /// Writes a single record asynchronously directly to the CsvAsyncStreamWriter.
    /// </summary>
    ValueTask WriteRecordAsync(CsvAsyncStreamWriter writer, T record, CancellationToken cancellationToken);
}
