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
            if (row == 2)
            {
                Assert.Equal("Jane", person.Name);
                Assert.Equal(42, person.Age);
            }

            if (row == 3)
            {
                Assert.Equal("Bob", person.Name);
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
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void NonAnnotatedType_FallsBackToReflection()
    {
        var csv = "Name,Age\nJane,42";
        var reader = Csv.ParseRecords<ReflectionOnly>(csv);
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
        await foreach (var person in Csv.ParseRecordsAsync<GeneratedPerson>(stream, cancellationToken: TestContext.Current.CancellationToken))
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
        var reader = Csv.ParseRecords<UnsupportedProperty>(csv, options);
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
}
