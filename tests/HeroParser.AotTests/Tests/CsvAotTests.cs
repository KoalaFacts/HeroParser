using HeroParser.AotTests.Models;
using HeroParser.SeparatedValues.Records;

namespace HeroParser.AotTests.Tests;

/// <summary>
/// AOT compatibility tests for CSV parsing and writing.
/// </summary>
public static class CsvAotTests
{
    public static void Run(TestRunner runner)
    {
        runner.PrintSection("CSV Tests");

        runner.Run("CSV: Generated binder parsing", GeneratedBinderParsing);
        runner.Run("CSV: Generated writer output", GeneratedWriterOutput);
        runner.Run("CSV: Round-trip integrity", RoundTripIntegrity);
        runner.Run("CSV: Basic parsing", BasicParsing);
        runner.Run("CSV: CsvColumn attribute handling", AttributeHandling);
        runner.Run("CSV: Multiple generated types", MultipleGeneratedTypes);
        runner.Run("CSV: Nullable types handling", NullableTypesHandling);
    }

    private static void GeneratedBinderParsing()
    {
        var csv = "Name,Age\nAlice,30\nBob,25";
        var records = Csv.DeserializeRecords<Person>(csv).ToList();

        if (records.Count != 2)
            throw new Exception($"Expected 2 records, got {records.Count}");
        if (records[0].Name != "Alice" || records[0].Age != 30)
            throw new Exception($"First record mismatch: {records[0].Name}, {records[0].Age}");
        if (records[1].Name != "Bob" || records[1].Age != 25)
            throw new Exception($"Second record mismatch: {records[1].Name}, {records[1].Age}");
    }

    private static void GeneratedWriterOutput()
    {
        Person[] records = [new Person { Name = "Charlie", Age = 35 }];
        var csv = Csv.WriteToText(records);

        if (!csv.Contains("Name") || !csv.Contains("Age"))
            throw new Exception($"Header missing in output: {csv}");
        if (!csv.Contains("Charlie") || !csv.Contains("35"))
            throw new Exception($"Data missing in output: {csv}");
    }

    private static void RoundTripIntegrity()
    {
        Person[] original =
        [
            new Person { Name = "Diana", Age = 28 },
            new Person { Name = "Eve", Age = 32 }
        ];

        var csv = Csv.WriteToText(original);
        var parsed = Csv.DeserializeRecords<Person>(csv).ToList();

        if (parsed.Count != 2)
            throw new Exception($"Round-trip count mismatch: expected 2, got {parsed.Count}");
        if (parsed[0].Name != "Diana" || parsed[0].Age != 28)
            throw new Exception("Round-trip data mismatch for first record");
        if (parsed[1].Name != "Eve" || parsed[1].Age != 32)
            throw new Exception("Round-trip data mismatch for second record");
    }

    private static void BasicParsing()
    {
        var csv = "A,B,C\n1,2,3\n4,5,6";
        var reader = Csv.ReadFromText(csv);

        int rowCount = 0;
        while (reader.MoveNext())
        {
            rowCount++;
            var row = reader.Current;
            if (row.ColumnCount != 3)
                throw new Exception($"Expected 3 columns, got {row.ColumnCount}");
        }

        if (rowCount != 3)
            throw new Exception($"Expected 3 rows, got {rowCount}");
    }

    private static void AttributeHandling()
    {
        // When using explicit Index attributes, use headerless mode since positions are known
        var csv = "1,Test User";
        var records = Csv.DeserializeRecords<AttributedPerson>(csv, new CsvRecordOptions { HasHeaderRow = false }).ToList();

        if (records.Count != 1)
            throw new Exception($"Expected 1 record, got {records.Count}");
        if (records[0].Id != 1)
            throw new Exception($"Id mismatch: expected 1, got {records[0].Id}");
        if (records[0].Name != "Test User")
            throw new Exception($"Name mismatch: expected 'Test User', got '{records[0].Name}'");
    }

    private static void MultipleGeneratedTypes()
    {
        var personCsv = Csv.WriteToText([new Person { Name = "A", Age = 1 }]);
        var attributedCsv = Csv.WriteToText([new AttributedPerson { Id = 1, Name = "B" }]);

        if (!personCsv.Contains("Name") || !attributedCsv.Contains("full_name"))
            throw new Exception("Multiple type headers incorrect");
    }

    private static void NullableTypesHandling()
    {
        var csv = "Name,Score\nAlice,100\nBob,";
        var records = Csv.DeserializeRecords<NullableRecord>(csv).ToList();

        if (records.Count != 2)
            throw new Exception($"Expected 2 records, got {records.Count}");
        if (records[0].Score != 100)
            throw new Exception($"First score should be 100, got {records[0].Score}");
        if (records[1].Score != null)
            throw new Exception($"Second score should be null, got {records[1].Score}");
    }
}
