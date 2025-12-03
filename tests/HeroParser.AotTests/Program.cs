using HeroParser;
using HeroParser.SeparatedValues.Records;
using HeroParser.SeparatedValues.Records.Binding;

// AOT Compatibility Tests for HeroParser
// This project compiles with Native AOT to verify the library works without reflection

Console.WriteLine("HeroParser AOT Compatibility Tests");
Console.WriteLine("===================================");

var failures = new List<string>();

// Test 1: Generated binder can parse CSV (implicitly tests registration)
Test("Generated binder parsing", () =>
{
    var csv = "Name,Age\nAlice,30\nBob,25";
    var records = Csv.DeserializeRecords<Person>(csv).ToList();

    if (records.Count != 2)
        throw new Exception($"Expected 2 records, got {records.Count}");
    if (records[0].Name != "Alice" || records[0].Age != 30)
        throw new Exception($"First record mismatch: {records[0].Name}, {records[0].Age}");
    if (records[1].Name != "Bob" || records[1].Age != 25)
        throw new Exception($"Second record mismatch: {records[1].Name}, {records[1].Age}");
});

// Test 2: Generated writer can write CSV (implicitly tests registration)
Test("Generated writer output", () =>
{
    Person[] records = [new Person { Name = "Charlie", Age = 35 }];
    var csv = Csv.WriteToText(records);

    if (!csv.Contains("Name") || !csv.Contains("Age"))
        throw new Exception($"Header missing in output: {csv}");
    if (!csv.Contains("Charlie") || !csv.Contains("35"))
        throw new Exception($"Data missing in output: {csv}");
});

// Test 3: Round-trip (write then read)
Test("Round-trip integrity", () =>
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
});

// Test 4: Basic CSV parsing (non-generated)
Test("Basic CSV parsing", () =>
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
});

// Test 5: CsvColumn attribute with custom name and explicit index (headerless mode)
Test("CsvColumn attribute handling", () =>
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
});

// Test 6: Multiple types with generated binders
Test("Multiple generated types", () =>
{
    var personCsv = Csv.WriteToText([new Person { Name = "A", Age = 1 }]);
    var attributedCsv = Csv.WriteToText([new AttributedPerson { Id = 1, Name = "B" }]);

    if (!personCsv.Contains("Name") || !attributedCsv.Contains("full_name"))
        throw new Exception("Multiple type headers incorrect");
});

// Test 7: Nullable types
Test("Nullable types handling", () =>
{
    var csv = "Name,Score\nAlice,100\nBob,";
    var records = Csv.DeserializeRecords<NullableRecord>(csv).ToList();

    if (records.Count != 2)
        throw new Exception($"Expected 2 records, got {records.Count}");
    if (records[0].Score != 100)
        throw new Exception($"First score should be 100, got {records[0].Score}");
    if (records[1].Score != null)
        throw new Exception($"Second score should be null, got {records[1].Score}");
});

// Summary
Console.WriteLine();
Console.WriteLine("===================================");
if (failures.Count == 0)
{
    Console.WriteLine("All AOT tests PASSED!");
    return 0;
}
else
{
    Console.WriteLine($"{failures.Count} test(s) FAILED:");
    foreach (var failure in failures)
        Console.WriteLine($"  - {failure}");
    return 1;
}

void Test(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine($"  [PASS] {name}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  [FAIL] {name}: {ex.Message}");
        failures.Add(name);
    }
}

// Test types with source-generated binders
[CsvGenerateBinder]
public class Person
{
    public string Name { get; set; } = "";
    public int Age { get; set; }
}

[CsvGenerateBinder]
public class AttributedPerson
{
    [CsvColumn(Name = "id", Index = 0)]
    public int Id { get; set; }

    [CsvColumn(Name = "full_name", Index = 1)]
    public string Name { get; set; } = "";
}

[CsvGenerateBinder]
public class NullableRecord
{
    public string Name { get; set; } = "";
    public int? Score { get; set; }
}
