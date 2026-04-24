using System.Diagnostics.CodeAnalysis;
using HeroParser.AotTests.Models;

namespace HeroParser.AotTests.Tests;

/// <summary>
/// AOT compatibility tests for Excel (.xlsx) reading and writing.
/// Exercises the source-generated template paths for both directions so that
/// any hidden reflection or dynamic-code dependency in the Excel pipeline
/// surfaces as an ILCompiler warning or runtime failure under PublishAot=true.
/// </summary>
/// <remarks>
/// The <see cref="UnconditionalSuppressMessageAttribute"/>s below are required
/// because the public <see cref="Excel"/> write facades are annotated with
/// <c>[RequiresUnreferencedCode]</c> + <c>[RequiresDynamicCode]</c> to warn users
/// who might call them with types lacking <c>[GenerateBinder]</c>. Every record
/// type used in this test class carries <c>[GenerateBinder]</c>, so the runtime
/// hits the source-generated template path, never reflection, and the warnings
/// do not represent real AOT hazards here.
/// </remarks>
[UnconditionalSuppressMessage(
    "Trimming",
    "IL2026:Members attributed with RequiresUnreferencedCode may break when trimming",
    Justification = "All record types in this test class are decorated with [GenerateBinder]; the reflection fallback in Excel.Write is never taken.")]
[UnconditionalSuppressMessage(
    "AOT",
    "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling.",
    Justification = "All record types in this test class are decorated with [GenerateBinder]; the reflection fallback in Excel.Write is never taken.")]
public static class ExcelAotTests
{
    public static void Run(TestRunner runner)
    {
        runner.PrintSection("Excel Tests");

        runner.Run("Excel: Generated writer output", GeneratedWriterOutput);
        runner.Run("Excel: Generated reader parsing", GeneratedReaderParsing);
        runner.Run("Excel: Round-trip integrity", RoundTripIntegrity);
        runner.Run("Excel: TabularMap attribute handling", AttributeHandling);
        runner.Run("Excel: Multiple generated types", MultipleGeneratedTypes);
        runner.Run("Excel: Nullable types handling", NullableTypesHandling);
    }

    private static void GeneratedWriterOutput()
    {
        ExcelPerson[] records = [new ExcelPerson { Name = "Charlie", Age = 35 }];
        var bytes = Excel.Write<ExcelPerson>().ToBytes(records);

        if (bytes.Length == 0)
            throw new Exception("Excel writer produced zero bytes");

        // XLSX is a ZIP archive; the first two bytes are the local-file-header magic "PK".
        if (bytes[0] != 0x50 || bytes[1] != 0x4B)
            throw new Exception($"Output is not a valid ZIP/XLSX stream: 0x{bytes[0]:X2} 0x{bytes[1]:X2}");
    }

    private static void GeneratedReaderParsing()
    {
        ExcelPerson[] original =
        [
            new ExcelPerson { Name = "Alice", Age = 30 },
            new ExcelPerson { Name = "Bob", Age = 25 },
        ];

        var bytes = Excel.Write<ExcelPerson>().ToBytes(original);
        using var ms = new MemoryStream(bytes);
        var records = Excel.Read<ExcelPerson>().FromStream(ms);

        if (records.Count != 2)
            throw new Exception($"Expected 2 records, got {records.Count}");
        if (records[0].Name != "Alice" || records[0].Age != 30)
            throw new Exception($"First record mismatch: {records[0].Name}, {records[0].Age}");
        if (records[1].Name != "Bob" || records[1].Age != 25)
            throw new Exception($"Second record mismatch: {records[1].Name}, {records[1].Age}");
    }

    private static void RoundTripIntegrity()
    {
        ExcelPerson[] original =
        [
            new ExcelPerson { Name = "Diana", Age = 28 },
            new ExcelPerson { Name = "Eve", Age = 32 },
        ];

        var bytes = Excel.Write<ExcelPerson>().ToBytes(original);
        using var ms = new MemoryStream(bytes);
        var parsed = Excel.Read<ExcelPerson>().FromStream(ms);

        if (parsed.Count != original.Length)
            throw new Exception($"Round-trip count mismatch: expected {original.Length}, got {parsed.Count}");
        for (int i = 0; i < original.Length; i++)
        {
            if (parsed[i].Name != original[i].Name || parsed[i].Age != original[i].Age)
                throw new Exception($"Round-trip data mismatch at index {i}: got ({parsed[i].Name}, {parsed[i].Age})");
        }
    }

    private static void AttributeHandling()
    {
        ExcelOrder[] original =
        [
            new ExcelOrder { Id = 1001, Customer = "Acme", Amount = 99.95m },
            new ExcelOrder { Id = 1002, Customer = "Globex", Amount = 250.00m },
        ];

        var bytes = Excel.Write<ExcelOrder>().ToBytes(original);
        using var ms = new MemoryStream(bytes);
        var parsed = Excel.Read<ExcelOrder>().FromStream(ms);

        if (parsed.Count != 2)
            throw new Exception($"Expected 2 records, got {parsed.Count}");
        if (parsed[0].Id != 1001 || parsed[0].Customer != "Acme" || parsed[0].Amount != 99.95m)
            throw new Exception($"First record mismatch: {parsed[0].Id}, {parsed[0].Customer}, {parsed[0].Amount}");
        if (parsed[1].Id != 1002 || parsed[1].Customer != "Globex" || parsed[1].Amount != 250.00m)
            throw new Exception($"Second record mismatch: {parsed[1].Id}, {parsed[1].Customer}, {parsed[1].Amount}");
    }

    private static void MultipleGeneratedTypes()
    {
        var personBytes = Excel.Write<ExcelPerson>().ToBytes([new ExcelPerson { Name = "A", Age = 1 }]);
        var orderBytes = Excel.Write<ExcelOrder>().ToBytes([new ExcelOrder { Id = 42, Customer = "B", Amount = 1.00m }]);

        if (personBytes.Length == 0 || orderBytes.Length == 0)
            throw new Exception("One or both generated-type writes produced empty output");

        using var personMs = new MemoryStream(personBytes);
        var persons = Excel.Read<ExcelPerson>().FromStream(personMs);
        if (persons.Count != 1 || persons[0].Name != "A")
            throw new Exception("Person round-trip failed");

        using var orderMs = new MemoryStream(orderBytes);
        var orders = Excel.Read<ExcelOrder>().FromStream(orderMs);
        if (orders.Count != 1 || orders[0].Id != 42 || orders[0].Customer != "B")
            throw new Exception("Order round-trip failed");
    }

    private static void NullableTypesHandling()
    {
        ExcelNullableRecord[] original =
        [
            new ExcelNullableRecord { Name = "Alice", Score = 100 },
            new ExcelNullableRecord { Name = "Bob", Score = null },
        ];

        var bytes = Excel.Write<ExcelNullableRecord>().ToBytes(original);
        using var ms = new MemoryStream(bytes);
        var parsed = Excel.Read<ExcelNullableRecord>().FromStream(ms);

        if (parsed.Count != 2)
            throw new Exception($"Expected 2 records, got {parsed.Count}");
        if (parsed[0].Score != 100)
            throw new Exception($"First score should be 100, got {parsed[0].Score}");
        if (parsed[1].Score != null)
            throw new Exception($"Second score should be null, got {parsed[1].Score}");
    }
}
