using HeroParser.SeparatedValues.Records;
using HeroParser.SeparatedValues.Records.Binding;
using HeroParser.SeparatedValues.Writing;
using HeroParser.Tests.Generated;
using Xunit;

namespace HeroParser.Tests;

public class SourceGeneratorTests
{
    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void GeneratedBinder_IsRegistered_AndBindsRows()
    {
        Assert.True(CsvRecordBinderFactory.TryGetBinder<GeneratedPerson>(null, out var binder));
        Assert.NotNull(binder);
        var notNullBinder = binder!;

        var csv = "Name,Age\nJane,42\nBob,25";
        var reader = Csv.ReadFromText(csv);

        int row = 0;
        while (reader.MoveNext())
        {
            row++;
            var current = reader.Current;
            if (notNullBinder.NeedsHeaderResolution)
            {
                notNullBinder.BindHeader(current, row);
                continue;
            }

            var person = notNullBinder.Bind(current, row);
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
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void GeneratedBinder_RespectsAttributes_AndHeaderless()
    {
        Assert.True(CsvRecordBinderFactory.TryGetBinder<GeneratedAttributed>(new CsvRecordOptions { HasHeaderRow = false }, out var binder));
        Assert.NotNull(binder);
        var b = binder!;

        var csv = "1,full_name\n2,other";
        var reader = Csv.ReadFromText(csv);

        int row = 0;
        while (reader.MoveNext())
        {
            row++;
            var entity = b.Bind(reader.Current, row);
            Assert.NotNull(entity);
            if (row == 1)
            {
                Assert.Equal(1, entity!.Id);
                Assert.Equal("full_name", entity.Name);
            }
            else
            {
                Assert.Equal(2, entity!.Id);
                Assert.Equal("other", entity.Name);
            }
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void NonAnnotatedType_FallsBackToReflection()
    {
        var csv = "Name,Age\nJane,42";
        var reader = Csv.DeserializeRecords<ReflectionOnly>(csv);
        Assert.True(reader.MoveNext());
        var item = reader.Current;
        Assert.Equal("Jane", item.Name);
        Assert.Equal(42, item.Age);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task AsyncStreaming_UsesGeneratedBinder()
    {
        Assert.True(CsvRecordBinderFactory.TryGetBinder<GeneratedPerson>(null, out var binder));
        Assert.NotNull(binder);

        var csv = "Name,Age\nAlice,9\nBob,7";
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));

        var results = new List<GeneratedPerson>();
        await foreach (var person in Csv.DeserializeRecordsAsync<GeneratedPerson>(stream, cancellationToken: TestContext.Current.CancellationToken))
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
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void UnsupportedType_SkipsGeneration_FallsBackToReflection()
    {
        // Array property should be unsupported by generator, so TryGetBinder should fail and reflection should still work.
        var options = new CsvRecordOptions { AllowMissingColumns = true };
        Assert.False(CsvRecordBinderFactory.TryGetBinder<UnsupportedProperty>(options, out _));

        var csv = "Name\nJane";
        var reader = Csv.DeserializeRecords<UnsupportedProperty>(csv, options);
        Assert.True(reader.MoveNext());
        Assert.Equal("Jane", reader.Current.Name);
    }

    private sealed class ReflectionOnly
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

    #region Generated Writer Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void GeneratedWriter_IsRegistered_ForAnnotatedType()
    {
        // Verify the generated writer is registered
        Assert.True(CsvRecordWriterFactory.TryGetWriter<GeneratedPerson>(null, out var writer));
        Assert.NotNull(writer);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void GeneratedWriter_WritesRecords()
    {
        var records = new[]
        {
            new GeneratedPerson { Name = "Alice", Age = 30 },
            new GeneratedPerson { Name = "Bob", Age = 25 }
        };

        var csv = Csv.WriteToText<GeneratedPerson>(records);

        Assert.Contains("Name,Age", csv);
        Assert.Contains("Alice,30", csv);
        Assert.Contains("Bob,25", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void GeneratedWriter_RespectsAttributeNames()
    {
        var records = new[]
        {
            new GeneratedAttributed { Id = 1, Name = "Test" }
        };

        var csv = Csv.WriteToText<GeneratedAttributed>(records);

        // Should use the attribute-defined header name "full_name"
        Assert.Contains("full_name", csv);
        Assert.Contains("Id", csv);
        Assert.Contains("Test", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void NonAnnotatedType_Writer_FallsBackToReflection()
    {
        // ReflectionOnly doesn't have [CsvGenerateBinder], so no generated writer should exist
        Assert.False(CsvRecordWriterFactory.TryGetWriter<ReflectionOnly>(null, out _));

        // But writing should still work via reflection
        var records = new[] { new ReflectionOnly { Name = "Jane", Age = 42 } };
        var csv = Csv.WriteToText<ReflectionOnly>(records);

        Assert.Contains("Name,Age", csv);
        Assert.Contains("Jane,42", csv);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public void RoundTrip_GeneratedBinderAndWriter()
    {
        // Create records
        var original = new[]
        {
            new GeneratedPerson { Name = "Alice", Age = 30 },
            new GeneratedPerson { Name = "Bob", Age = 25 }
        };

        // Write to CSV using generated writer
        var csv = Csv.WriteToText<GeneratedPerson>(original);

        // Read back using generated binder
        var parsed = Csv.DeserializeRecords<GeneratedPerson>(csv).ToList();

        Assert.Equal(2, parsed.Count);
        Assert.Equal("Alice", parsed[0].Name);
        Assert.Equal(30, parsed[0].Age);
        Assert.Equal("Bob", parsed[1].Name);
        Assert.Equal(25, parsed[1].Age);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void GeneratedWriter_MatchesReflectionWriter_Output()
    {
        var generatedRecords = new[]
        {
            new GeneratedPerson { Name = "Alice", Age = 30 },
            new GeneratedPerson { Name = "Bob", Age = 25 }
        };

        var reflectionRecords = new[]
        {
            new ReflectionOnly { Name = "Alice", Age = 30 },
            new ReflectionOnly { Name = "Bob", Age = 25 }
        };

        var generatedCsv = Csv.WriteToText<GeneratedPerson>(generatedRecords);
        var reflectionCsv = Csv.WriteToText<ReflectionOnly>(reflectionRecords);

        // Both should have the same structure (same number of rows)
        var generatedLines = generatedCsv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var reflectionLines = reflectionCsv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(generatedLines.Length, reflectionLines.Length);

        // Both should have the same content since properties are the same
        Assert.Equal(generatedCsv, reflectionCsv);
    }

    #endregion
}
