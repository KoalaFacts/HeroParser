using HeroParser.Configuration;

#if NET6_0_OR_GREATER
using System.Runtime.CompilerServices;
#endif

namespace HeroParser.Core;

/// <summary>
/// Provides methods for reading and parsing CSV data with high performance and RFC 4180 compliance.
/// </summary>
public interface ICsvReader : IDisposable
{
    /// <summary>
    /// Gets the configuration used by this CSV reader.
    /// </summary>
    CsvReadConfiguration Configuration { get; }

    /// <summary>
    /// Gets the headers if available and configuration specifies header row.
    /// </summary>
    IReadOnlyList<string>? Headers { get; }

    /// <summary>
    /// Gets a value indicating whether this reader has reached the end of the CSV data.
    /// </summary>
    bool EndOfCsv { get; }

    /// <summary>
    /// Reads all remaining CSV records as an enumerable sequence.
    /// </summary>
    /// <returns>An enumerable sequence of string arrays representing CSV rows.</returns>
    IEnumerable<string[]> ReadAll();

    /// <summary>
    /// Reads all remaining CSV records asynchronously.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous read operation.</returns>
    Task<IEnumerable<string[]>> ReadAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the next CSV record.
    /// </summary>
    /// <returns>A string array representing the next CSV row, or null if end of data.</returns>
    string[]? ReadRecord();

    /// <summary>
    /// Reads the next CSV record asynchronously.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous read operation.</returns>
    Task<string[]?> ReadRecordAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the value of a field by column name (requires headers).
    /// </summary>
    /// <param name="record">The CSV record.</param>
    /// <param name="columnName">The column name.</param>
    /// <returns>The field value.</returns>
    /// <exception cref="ArgumentException">Thrown when column name is not found.</exception>
    string GetField(string[] record, string columnName);

    /// <summary>
    /// Tries to get the value of a field by column name.
    /// </summary>
    /// <param name="record">The CSV record.</param>
    /// <param name="columnName">The column name.</param>
    /// <param name="value">The field value if found.</param>
    /// <returns>True if the column was found; otherwise, false.</returns>
    bool TryGetField(string[] record, string columnName, out string? value);
}

#if NET6_0_OR_GREATER
/// <summary>
/// Extension methods for ICsvReader providing modern enumerable patterns.
/// </summary>
public static class CsvReaderExtensions
{
    /// <summary>
    /// Reads all CSV records as an async enumerable sequence.
    /// </summary>
    /// <param name="reader">The CSV reader.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async enumerable sequence of string arrays.</returns>
    public static async IAsyncEnumerable<string[]> ReadAllAsyncEnumerable(
        this ICsvReader reader,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (reader == null) throw new ArgumentNullException(nameof(reader));

        while (!reader.EndOfCsv)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = await reader.ReadRecordAsync(cancellationToken).ConfigureAwait(false);
            if (record != null)
                yield return record;
        }
    }

    /// <summary>
    /// Maps CSV records to objects using a custom mapper function.
    /// </summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="reader">The CSV reader.</param>
    /// <param name="mapper">The mapper function.</param>
    /// <param name="cancellationToken">A token to cancel the enumeration.</param>
    /// <returns>An async enumerable sequence of mapped objects.</returns>
    public static async IAsyncEnumerable<T> ReadAllAsyncEnumerable<T>(
        this ICsvReader reader,
        Func<string[], T> mapper,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (reader == null) throw new ArgumentNullException(nameof(reader));
        if (mapper == null) throw new ArgumentNullException(nameof(mapper));

        await foreach (var record in reader.ReadAllAsyncEnumerable(cancellationToken))
        {
            yield return mapper(record);
        }
    }
}
#endif