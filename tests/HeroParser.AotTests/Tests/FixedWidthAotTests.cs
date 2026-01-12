using HeroParser.AotTests.Models;
using HeroParser.FixedWidths;

namespace HeroParser.AotTests.Tests;

/// <summary>
/// AOT compatibility tests for fixed-width parsing and writing.
/// </summary>
public static class FixedWidthAotTests
{
    public static void Run(TestRunner runner)
    {
        runner.PrintSection("Fixed-Width Tests");

        runner.Run("FixedWidth: Generated binder parsing", GeneratedBinderParsing);
        runner.Run("FixedWidth: Generated writer output", GeneratedWriterOutput);
        runner.Run("FixedWidth: Round-trip integrity", RoundTripIntegrity);
        runner.Run("FixedWidth: Basic parsing", BasicParsing);
        runner.Run("FixedWidth: Alignment handling", AlignmentHandling);
        runner.Run("FixedWidth: Multiple generated types", MultipleGeneratedTypes);
    }

    private static void GeneratedBinderParsing()
    {
        // Each record is 30 chars: Name (20 chars) + Age (10 chars, right-aligned)
        // Fixed-width files often concatenate records without newlines
        var data = "Alice                       30Bob                         25";
        var options = new FixedWidthReadOptions { RecordLength = 30 };
        var records = FixedWidth.DeserializeRecords<FixedWidthPerson>(data, options).ToList();

        if (records.Count != 2)
            throw new Exception($"Expected 2 records, got {records.Count}");
        if (records[0].Name != "Alice" || records[0].Age != 30)
            throw new Exception($"First record mismatch: '{records[0].Name}', {records[0].Age}");
        if (records[1].Name != "Bob" || records[1].Age != 25)
            throw new Exception($"Second record mismatch: '{records[1].Name}', {records[1].Age}");
    }

    private static void GeneratedWriterOutput()
    {
        FixedWidthPerson[] records = [new FixedWidthPerson { Name = "Charlie", Age = 35 }];
        var output = FixedWidth.WriteToText(records);

        // Check the output contains the data
        if (!output.Contains("Charlie"))
            throw new Exception($"Name missing in output: '{output}'");
        if (!output.Contains("35"))
            throw new Exception($"Age missing in output: '{output}'");
    }

    private static void RoundTripIntegrity()
    {
        FixedWidthPerson[] original =
        [
            new FixedWidthPerson { Name = "Diana", Age = 28 },
            new FixedWidthPerson { Name = "Eve", Age = 32 }
        ];

        var output = FixedWidth.WriteToText(original);

        // The writer adds newlines - determine record length from output
        // Record length = 20 (Name) + 10 (Age) = 30 chars
        // With newlines, we need to account for that in the parser
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 1)
            throw new Exception("No output lines");
        var recordLength = lines[0].Length;

        // Remove newlines for parsing since RecordLength doesn't include them
        var dataWithoutNewlines = string.Concat(lines);
        var options = new FixedWidthReadOptions { RecordLength = recordLength };
        var parsed = FixedWidth.DeserializeRecords<FixedWidthPerson>(dataWithoutNewlines, options).ToList();

        if (parsed.Count != 2)
            throw new Exception($"Round-trip count mismatch: expected 2, got {parsed.Count}");
        if (parsed[0].Name != "Diana" || parsed[0].Age != 28)
            throw new Exception($"Round-trip data mismatch for first record: '{parsed[0].Name}', {parsed[0].Age}");
        if (parsed[1].Name != "Eve" || parsed[1].Age != 32)
            throw new Exception($"Round-trip data mismatch for second record: '{parsed[1].Name}', {parsed[1].Age}");
    }

    private static void BasicParsing()
    {
        var data = "ABCDEFGHIJ1234567890";
        var options = new FixedWidthReadOptions { RecordLength = 20 };
        var reader = FixedWidth.ReadFromText(data, options);

        int rowCount = 0;
        while (reader.MoveNext())
        {
            rowCount++;
            var row = reader.Current;
            var field1 = row.GetField(0, 10).ToString();
            var field2 = row.GetField(10, 10).ToString();

            if (field1 != "ABCDEFGHIJ")
                throw new Exception($"Field1 mismatch: expected 'ABCDEFGHIJ', got '{field1}'");
            if (field2 != "1234567890")
                throw new Exception($"Field2 mismatch: expected '1234567890', got '{field2}'");
        }

        if (rowCount != 1)
            throw new Exception($"Expected 1 row, got {rowCount}");
    }

    private static void AlignmentHandling()
    {
        // Right-aligned ID (5 chars, padded with spaces), Left-aligned Name (15 chars)
        var data = "   42Test Name      ";
        var options = new FixedWidthReadOptions { RecordLength = 20 };
        var records = FixedWidth.DeserializeRecords<FixedWidthAligned>(data, options).ToList();

        if (records.Count != 1)
            throw new Exception($"Expected 1 record, got {records.Count}");
        if (records[0].Id != 42)
            throw new Exception($"Id mismatch: expected 42, got {records[0].Id}");
        if (records[0].Name != "Test Name")
            throw new Exception($"Name mismatch: expected 'Test Name', got '{records[0].Name}'");
    }

    private static void MultipleGeneratedTypes()
    {
        var person = new FixedWidthPerson { Name = "Test", Age = 99 };
        var aligned = new FixedWidthAligned { Id = 1, Name = "Other" };

        var personOutput = FixedWidth.WriteToText([person]);
        var alignedOutput = FixedWidth.WriteToText([aligned]);

        if (!personOutput.Contains("Test"))
            throw new Exception("Person output missing name");
        if (!alignedOutput.Contains("Other"))
            throw new Exception("Aligned output missing name");
    }
}

