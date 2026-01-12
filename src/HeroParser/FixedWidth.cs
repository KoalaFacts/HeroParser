using HeroParser.FixedWidths.Records;

namespace HeroParser;

/// <summary>
/// Factory methods for fixed-width (fixed-length) file parsing.
/// </summary>
public static partial class FixedWidth
{
    /// <summary>
    /// Creates a builder for reading and deserializing fixed-width records to a typed object.
    /// </summary>
    /// <typeparam name="T">The record type to deserialize to.</typeparam>
    /// <returns>A fluent builder for configuring the read operation.</returns>
    /// <example>
    /// <code>
    /// // Define record type with column mappings
    /// [FixedWidthGenerateBinder]
    /// public class Employee
    /// {
    ///     [FixedWidthColumn(Start = 0, Length = 10)]
    ///     public string Id { get; set; }
    ///
    ///     [FixedWidthColumn(Start = 10, Length = 30)]
    ///     public string Name { get; set; }
    ///
    ///     [FixedWidthColumn(Start = 40, Length = 10, Alignment = FieldAlignment.Right, PadChar = '0')]
    ///     public decimal Salary { get; set; }
    /// }
    ///
    /// // Read records
    /// foreach (var employee in FixedWidth.Read&lt;Employee&gt;().FromFile("employees.dat"))
    /// {
    ///     Console.WriteLine($"{employee.Name}: {employee.Salary:C}");
    /// }
    /// </code>
    /// </example>
    public static FixedWidthReaderBuilder<T> Read<T>() where T : new()
        => new();

    /// <summary>
    /// Creates a builder for manual row-by-row fixed-width file reading.
    /// </summary>
    /// <returns>A fluent builder for configuring the read operation.</returns>
    /// <example>
    /// <code>
    /// // Configure and read manually
    /// foreach (var row in FixedWidth.Read()
    ///     .WithRecordLength(80)
    ///     .WithDefaultPadChar(' ')
    ///     .FromFile("legacy.dat"))
    /// {
    ///     var id = row.GetField(0, 10).ToString();
    ///     var name = row.GetField(10, 30).ToString();
    ///     Console.WriteLine($"{id}: {name}");
    /// }
    /// </code>
    /// </example>
    public static FixedWidthReaderBuilder Read()
        => new();
}
