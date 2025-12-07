using HeroParser.Generators.Tests.Generated;
using HeroParser.SeparatedValues.Reading.Binders;
using HeroParser.SeparatedValues.Reading.Records;
using HeroParser.SeparatedValues.Reading.Shared;
using HeroParser.SeparatedValues.Writing;
using Xunit;

namespace HeroParser.Generators.Tests;

/// <summary>
/// Integration tests that verify the source generator produces working binders and writers.
/// These tests compile with the generator as an analyzer, so the generated code is available at runtime.
/// </summary>
public class GeneratorIntegrationTests
{
    private const string CATEGORY = "Category";
    private const string UNIT = "Unit";
    private const string INTEGRATION = "Integration";

    [Fact]
    [Trait(CATEGORY, UNIT)]
    public void GeneratedBinder_IsRegistered_AndBindsRows()
    {
        // Using GetCharBinder to verify binder is registered
        var binder = CsvRecordBinderFactory.GetCharBinder<GeneratedPerson>(null);
        Assert.NotNull(binder);

        var csv = "Name,Age\nJane,42\nBob,25";
        var reader = Csv.ReadFromText(csv);

        int row = 0;
        while (reader.MoveNext())
        {
            row++;
            var current = reader.Current;
            if (binder.NeedsHeaderResolution)
            {
                binder.BindHeader(current, row);
                continue;
            }

            var person = binder.Bind(current, row);
            Assert.NotNull(person);
            if (row == 2)
            {
                Assert.Equal("Jane", person!.Name);
                Assert.Equal(42, person.Age);
            }

            if (row == 3)
            {
                Assert.Equal("Bob", person!.Name);
                Assert.Equal(25, person.Age);
            }
        }
    }

    [Fact]
    [Trait(CATEGORY, UNIT)]
    public void GeneratedBinder_RespectsAttributes_AndHeaderless()
    {
        var binder = CsvRecordBinderFactory.GetCharBinder<GeneratedAttributed>(new CsvRecordOptions { HasHeaderRow = false });
        Assert.NotNull(binder);

        var csv = "1,full_name\n2,other";
        var reader = Csv.ReadFromText(csv);

        int row = 0;
        while (reader.MoveNext())
        {
            row++;
            var entity = binder.Bind(reader.Current, row);
            Assert.NotNull(entity);
            if (row == 1)
            {
                Assert.Equal(1, entity.Id);
                Assert.Equal("full_name", entity.Name);
            }
            else
            {
                Assert.Equal(2, entity.Id);
                Assert.Equal("other", entity.Name);
            }
        }
    }

    [Fact]
    [Trait(CATEGORY, UNIT)]
    public void NonAnnotatedType_ThrowsInvalidOperation()
    {
        var csv = "Name,Age\nJane,42";
        Assert.Throws<InvalidOperationException>(() => Csv.DeserializeRecords<NonAnnotatedRecord>(csv));
    }

    [Fact]
    [Trait(CATEGORY, INTEGRATION)]
    public void SyncStreaming_UsesGeneratedBinder()
    {
        var binder = CsvRecordBinderFactory.GetCharBinder<GeneratedPerson>(null);
        Assert.NotNull(binder);

        var csv = "Name,Age\nAlice,9\nBob,7";

        var results = new List<GeneratedPerson>();
        foreach (var person in Csv.DeserializeRecords<GeneratedPerson>(csv))
        {
            results.Add(person);
        }

        Assert.Equal(2, results.Count);
        Assert.Equal("Alice", results[0].Name);
        Assert.Equal(9, results[0].Age);
        Assert.Equal("Bob", results[1].Name);
        Assert.Equal(7, results[1].Age);
    }

    [Fact]
    [Trait(CATEGORY, UNIT)]
    public void UnsupportedType_SkipsGeneration_NoBinder()
    {
        // Array property should be unsupported by generator, so no binder should be registered
        var options = new CsvRecordOptions { AllowMissingColumns = true };
        Assert.Throws<InvalidOperationException>(() => CsvRecordBinderFactory.GetCharBinder<UnsupportedProperty>(options));
    }

    #region Generated Writer Tests

    [Fact]
    [Trait(CATEGORY, UNIT)]
    public void GeneratedWriter_IsRegistered_ForAnnotatedType()
    {
        // Verify the generated writer is registered
        Assert.True(CsvRecordWriterFactory.TryGetWriter<GeneratedPerson>(null, out var writer));
        Assert.NotNull(writer);
    }

    [Fact]
    [Trait(CATEGORY, UNIT)]
    public void GeneratedWriter_WritesRecords()
    {
        var records = new[]
        {
            new GeneratedPerson { Name = "Alice", Age = 30 },
            new GeneratedPerson { Name = "Bob", Age = 25 }
        };

        var csv = Csv.WriteToText(records);

        Assert.Contains("Name,Age", csv);
        Assert.Contains("Alice,30", csv);
        Assert.Contains("Bob,25", csv);
    }

    [Fact]
    [Trait(CATEGORY, UNIT)]
    public void GeneratedWriter_RespectsAttributeNames()
    {
        var records = new[]
        {
            new GeneratedAttributed { Id = 1, Name = "Test" }
        };

        var csv = Csv.WriteToText(records);

        // Should use the attribute-defined header name "full_name"
        Assert.Contains("full_name", csv);
        Assert.Contains("Id", csv);
        Assert.Contains("Test", csv);
    }

    [Fact]
    [Trait(CATEGORY, INTEGRATION)]
    public void RoundTrip_GeneratedBinderAndWriter()
    {
        // Create records
        var original = new[]
        {
            new GeneratedPerson { Name = "Alice", Age = 30 },
            new GeneratedPerson { Name = "Bob", Age = 25 }
        };

        // Write to CSV using generated writer
        var csv = Csv.WriteToText(original);

        // Read back using generated binder
        var parsed = Csv.DeserializeRecords<GeneratedPerson>(csv).ToList();

        Assert.Equal(2, parsed.Count);
        Assert.Equal("Alice", parsed[0].Name);
        Assert.Equal(30, parsed[0].Age);
        Assert.Equal("Bob", parsed[1].Name);
        Assert.Equal(25, parsed[1].Age);
    }

    #endregion

    #region Test Types

    private sealed class NonAnnotatedRecord
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
    }

    [CsvGenerateBinder]
    private sealed class UnsupportedProperty
    {
        public string Name { get; set; } = string.Empty;
        public int[] Scores { get; set; } = [];
    }

    #endregion
}
