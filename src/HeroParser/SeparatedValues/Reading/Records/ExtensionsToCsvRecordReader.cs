namespace HeroParser.SeparatedValues.Reading.Records;

/// <summary>
/// Provides LINQ-style extension methods for CSV record readers.
/// </summary>
/// <remarks>
/// Since CSV readers are ref structs, they cannot implement <see cref="IEnumerable{T}"/>.
/// These extension methods provide common operations that consume the reader and return materialized results.
/// </remarks>
public static class ExtensionsToCsvRecordReader
{
    /// <summary>
    /// Materializes all records from the reader into a <see cref="List{T}"/>.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="reader">The record reader to consume.</param>
    /// <returns>A list containing all records.</returns>
    /// <remarks>This method consumes the reader. The reader should not be used after calling this method.</remarks>
    public static List<T> ToList<T>(this CsvRecordReader<char, T> reader) where T : new()
    {
        var list = new List<T>();
        while (reader.MoveNext())
        {
            list.Add(reader.Current);
        }
        return list;
    }

    /// <summary>
    /// Materializes all records from the reader into an array.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="reader">The record reader to consume.</param>
    /// <returns>An array containing all records.</returns>
    /// <remarks>This method consumes the reader. The reader should not be used after calling this method.</remarks>
    public static T[] ToArray<T>(this CsvRecordReader<char, T> reader) where T : new()
        => reader.ToList().ToArray();

    /// <summary>
    /// Returns the first record from the reader.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="reader">The record reader to consume.</param>
    /// <returns>The first record.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the reader contains no records.</exception>
    /// <remarks>This method consumes only the first record. The reader can continue to be used after calling this method.</remarks>
    public static T First<T>(this CsvRecordReader<char, T> reader) where T : new()
    {
        if (reader.MoveNext())
        {
            return reader.Current;
        }
        throw new InvalidOperationException("The CSV contains no records.");
    }

    /// <summary>
    /// Returns the first record from the reader, or the default value if empty.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="reader">The record reader to consume.</param>
    /// <returns>The first record, or the default value if no records exist.</returns>
    /// <remarks>This method consumes only the first record. The reader can continue to be used after calling this method.</remarks>
    public static T? FirstOrDefault<T>(this CsvRecordReader<char, T> reader) where T : new()
    {
        if (reader.MoveNext())
        {
            return reader.Current;
        }
        return default;
    }

    /// <summary>
    /// Returns the first record matching a predicate.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="reader">The record reader to consume.</param>
    /// <param name="predicate">A function to test each record.</param>
    /// <returns>The first record that matches the predicate.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no record matches the predicate.</exception>
    public static T First<T>(this CsvRecordReader<char, T> reader, Func<T, bool> predicate) where T : new()
    {
        while (reader.MoveNext())
        {
            if (predicate(reader.Current))
            {
                return reader.Current;
            }
        }
        throw new InvalidOperationException("No record matches the specified predicate.");
    }

    /// <summary>
    /// Returns the first record matching a predicate, or the default value if none match.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="reader">The record reader to consume.</param>
    /// <param name="predicate">A function to test each record.</param>
    /// <returns>The first matching record, or the default value if no records match.</returns>
    public static T? FirstOrDefault<T>(this CsvRecordReader<char, T> reader, Func<T, bool> predicate) where T : new()
    {
        while (reader.MoveNext())
        {
            if (predicate(reader.Current))
            {
                return reader.Current;
            }
        }
        return default;
    }

    /// <summary>
    /// Returns the only record from the reader.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="reader">The record reader to consume.</param>
    /// <returns>The single record.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the reader contains zero or more than one record.</exception>
    /// <remarks>This method consumes the reader to verify exactly one record exists.</remarks>
    public static T Single<T>(this CsvRecordReader<char, T> reader) where T : new()
    {
        if (!reader.MoveNext())
        {
            throw new InvalidOperationException("The CSV contains no records.");
        }

        var result = reader.Current;

        if (reader.MoveNext())
        {
            throw new InvalidOperationException("The CSV contains more than one record.");
        }

        return result;
    }

    /// <summary>
    /// Returns the only record from the reader, or the default value if empty.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="reader">The record reader to consume.</param>
    /// <returns>The single record, or the default value if no records exist.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the reader contains more than one record.</exception>
    /// <remarks>This method consumes the reader to verify at most one record exists.</remarks>
    public static T? SingleOrDefault<T>(this CsvRecordReader<char, T> reader) where T : new()
    {
        if (!reader.MoveNext())
        {
            return default;
        }

        var result = reader.Current;

        if (reader.MoveNext())
        {
            throw new InvalidOperationException("The CSV contains more than one record.");
        }

        return result;
    }

    /// <summary>
    /// Counts the total number of records in the reader.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="reader">The record reader to consume.</param>
    /// <returns>The number of records.</returns>
    /// <remarks>This method consumes the entire reader.</remarks>
    public static int Count<T>(this CsvRecordReader<char, T> reader) where T : new()
    {
        int count = 0;
        while (reader.MoveNext())
        {
            count++;
        }
        return count;
    }

    /// <summary>
    /// Counts the number of records matching a predicate.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="reader">The record reader to consume.</param>
    /// <param name="predicate">A function to test each record.</param>
    /// <returns>The number of matching records.</returns>
    /// <remarks>This method consumes the entire reader.</remarks>
    public static int Count<T>(this CsvRecordReader<char, T> reader, Func<T, bool> predicate) where T : new()
    {
        int count = 0;
        while (reader.MoveNext())
        {
            if (predicate(reader.Current))
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// Determines whether the reader contains any records.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="reader">The record reader to consume.</param>
    /// <returns><see langword="true"/> if the reader contains at least one record; otherwise, <see langword="false"/>.</returns>
    /// <remarks>This method consumes only the first record if one exists.</remarks>
    public static bool Any<T>(this CsvRecordReader<char, T> reader) where T : new()
        => reader.MoveNext();

    /// <summary>
    /// Determines whether any record matches a predicate.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="reader">The record reader to consume.</param>
    /// <param name="predicate">A function to test each record.</param>
    /// <returns><see langword="true"/> if any record matches the predicate; otherwise, <see langword="false"/>.</returns>
    public static bool Any<T>(this CsvRecordReader<char, T> reader, Func<T, bool> predicate) where T : new()
    {
        while (reader.MoveNext())
        {
            if (predicate(reader.Current))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Determines whether all records match a predicate.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="reader">The record reader to consume.</param>
    /// <param name="predicate">A function to test each record.</param>
    /// <returns><see langword="true"/> if all records match the predicate; otherwise, <see langword="false"/>.</returns>
    /// <remarks>This method returns <see langword="true"/> for an empty reader (vacuous truth).</remarks>
    public static bool All<T>(this CsvRecordReader<char, T> reader, Func<T, bool> predicate) where T : new()
    {
        while (reader.MoveNext())
        {
            if (!predicate(reader.Current))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Filters records based on a predicate and returns all matches.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="reader">The record reader to consume.</param>
    /// <param name="predicate">A function to test each record.</param>
    /// <returns>A list containing all records that match the predicate.</returns>
    /// <remarks>This method consumes the entire reader.</remarks>
    public static List<T> Where<T>(this CsvRecordReader<char, T> reader, Func<T, bool> predicate) where T : new()
    {
        var results = new List<T>();
        while (reader.MoveNext())
        {
            if (predicate(reader.Current))
            {
                results.Add(reader.Current);
            }
        }
        return results;
    }

    /// <summary>
    /// Projects each record into a new form.
    /// </summary>
    /// <typeparam name="TSource">The source record type.</typeparam>
    /// <typeparam name="TResult">The projected type.</typeparam>
    /// <param name="reader">The record reader to consume.</param>
    /// <param name="selector">A transform function to apply to each record.</param>
    /// <returns>A list containing the projected results.</returns>
    /// <remarks>This method consumes the entire reader.</remarks>
    public static List<TResult> Select<TSource, TResult>(this CsvRecordReader<char, TSource> reader, Func<TSource, TResult> selector)
        where TSource : new()
    {
        var results = new List<TResult>();
        while (reader.MoveNext())
        {
            results.Add(selector(reader.Current));
        }
        return results;
    }

    /// <summary>
    /// Skips a specified number of records and returns the rest.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="reader">The record reader to consume.</param>
    /// <param name="count">The number of records to skip.</param>
    /// <returns>A list containing all records after skipping the specified count.</returns>
    /// <remarks>This method consumes the entire reader.</remarks>
    public static List<T> Skip<T>(this CsvRecordReader<char, T> reader, int count) where T : new()
    {
        var results = new List<T>();
        int skipped = 0;
        while (reader.MoveNext())
        {
            if (skipped < count)
            {
                skipped++;
                continue;
            }
            results.Add(reader.Current);
        }
        return results;
    }

    /// <summary>
    /// Takes a specified number of records from the start.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="reader">The record reader to consume.</param>
    /// <param name="count">The number of records to take.</param>
    /// <returns>A list containing up to the specified number of records.</returns>
    /// <remarks>This method consumes only the records needed, up to <paramref name="count"/>.</remarks>
    public static List<T> Take<T>(this CsvRecordReader<char, T> reader, int count) where T : new()
    {
        var results = new List<T>(count);
        int taken = 0;
        while (taken < count && reader.MoveNext())
        {
            results.Add(reader.Current);
            taken++;
        }
        return results;
    }

    /// <summary>
    /// Performs an action on each record.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="reader">The record reader to consume.</param>
    /// <param name="action">The action to perform on each record.</param>
    /// <remarks>This method consumes the entire reader.</remarks>
    public static void ForEach<T>(this CsvRecordReader<char, T> reader, Action<T> action) where T : new()
    {
        while (reader.MoveNext())
        {
            action(reader.Current);
        }
    }

    /// <summary>
    /// Creates a dictionary from the records using a key selector.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <typeparam name="TKey">The type of the dictionary key.</typeparam>
    /// <param name="reader">The record reader to consume.</param>
    /// <param name="keySelector">A function to extract a key from each record.</param>
    /// <returns>A dictionary mapping keys to records.</returns>
    /// <exception cref="ArgumentException">Thrown when duplicate keys are encountered.</exception>
    /// <remarks>This method consumes the entire reader.</remarks>
    public static Dictionary<TKey, T> ToDictionary<T, TKey>(
        this CsvRecordReader<char, T> reader,
        Func<T, TKey> keySelector)
        where T : new()
        where TKey : notnull
    {
        var dict = new Dictionary<TKey, T>();
        while (reader.MoveNext())
        {
            dict.Add(keySelector(reader.Current), reader.Current);
        }
        return dict;
    }

    /// <summary>
    /// Creates a dictionary from the records using key and value selectors.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <typeparam name="TKey">The type of the dictionary key.</typeparam>
    /// <typeparam name="TValue">The type of the dictionary value.</typeparam>
    /// <param name="reader">The record reader to consume.</param>
    /// <param name="keySelector">A function to extract a key from each record.</param>
    /// <param name="valueSelector">A function to extract a value from each record.</param>
    /// <returns>A dictionary mapping keys to values.</returns>
    /// <exception cref="ArgumentException">Thrown when duplicate keys are encountered.</exception>
    /// <remarks>This method consumes the entire reader.</remarks>
    public static Dictionary<TKey, TValue> ToDictionary<T, TKey, TValue>(
        this CsvRecordReader<char, T> reader,
        Func<T, TKey> keySelector,
        Func<T, TValue> valueSelector)
        where T : new()
        where TKey : notnull
    {
        var dict = new Dictionary<TKey, TValue>();
        while (reader.MoveNext())
        {
            dict.Add(keySelector(reader.Current), valueSelector(reader.Current));
        }
        return dict;
    }

    /// <summary>
    /// Groups records by a key selector.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <typeparam name="TKey">The type of the grouping key.</typeparam>
    /// <param name="reader">The record reader to consume.</param>
    /// <param name="keySelector">A function to extract a key from each record.</param>
    /// <returns>A dictionary mapping keys to lists of records.</returns>
    /// <remarks>This method consumes the entire reader.</remarks>
    public static Dictionary<TKey, List<T>> GroupBy<T, TKey>(
        this CsvRecordReader<char, T> reader,
        Func<T, TKey> keySelector)
        where T : new()
        where TKey : notnull
    {
        var groups = new Dictionary<TKey, List<T>>();
        while (reader.MoveNext())
        {
            var key = keySelector(reader.Current);
            if (!groups.TryGetValue(key, out var list))
            {
                list = [];
                groups[key] = list;
            }
            list.Add(reader.Current);
        }
        return groups;
    }

}
