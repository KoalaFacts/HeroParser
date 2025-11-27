using HeroParser.SeparatedValues.Records;
using Xunit;

namespace HeroParser.Tests;

public class LinqExtensionsTests
{
    private const string TEST_CSV = "Name,Age,Score\nAlice,25,100\nBob,30,85\nCharlie,25,95\nDiana,35,90";

    #region ToList / ToArray Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ToList_ReturnsAllRecords()
    {
        var reader = Csv.DeserializeRecords<Person>(TEST_CSV);
        var list = reader.ToList();

        Assert.Equal(4, list.Count);
        Assert.Equal("Alice", list[0].Name);
        Assert.Equal("Diana", list[3].Name);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ToArray_ReturnsAllRecords()
    {
        var reader = Csv.DeserializeRecords<Person>(TEST_CSV);
        var array = reader.ToArray();

        Assert.Equal(4, array.Length);
        Assert.Equal("Bob", array[1].Name);
    }

    #endregion

    #region First / FirstOrDefault Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void First_ReturnsFirstRecord()
    {
        var reader = Csv.DeserializeRecords<Person>(TEST_CSV);
        var first = reader.First();

        Assert.Equal("Alice", first.Name);
        Assert.Equal(25, first.Age);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void First_ThrowsOnEmpty()
    {
        InvalidOperationException? ex = null;
        try
        {
            var reader = Csv.DeserializeRecords<Person>("Name,Age,Score");
            reader.First();
        }
        catch (InvalidOperationException e)
        {
            ex = e;
        }

        Assert.NotNull(ex);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FirstOrDefault_ReturnsFirstRecord()
    {
        var reader = Csv.DeserializeRecords<Person>(TEST_CSV);
        var first = reader.FirstOrDefault();

        Assert.NotNull(first);
        Assert.Equal("Alice", first!.Name);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FirstOrDefault_ReturnsNullOnEmpty()
    {
        var reader = Csv.DeserializeRecords<Person>("Name,Age,Score");
        var first = reader.FirstOrDefault();

        Assert.Null(first);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void First_WithPredicate_ReturnsFirstMatch()
    {
        var reader = Csv.DeserializeRecords<Person>(TEST_CSV);
        var first = reader.First(p => p.Age == 30);

        Assert.Equal("Bob", first.Name);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void First_WithPredicate_ThrowsOnNoMatch()
    {
        InvalidOperationException? ex = null;
        try
        {
            var reader = Csv.DeserializeRecords<Person>(TEST_CSV);
            reader.First(p => p.Age == 99);
        }
        catch (InvalidOperationException e)
        {
            ex = e;
        }

        Assert.NotNull(ex);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FirstOrDefault_WithPredicate_ReturnsFirstMatch()
    {
        var reader = Csv.DeserializeRecords<Person>(TEST_CSV);
        var first = reader.FirstOrDefault(p => p.Score > 90);

        Assert.NotNull(first);
        Assert.Equal("Alice", first!.Name);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void FirstOrDefault_WithPredicate_ReturnsNullOnNoMatch()
    {
        var reader = Csv.DeserializeRecords<Person>(TEST_CSV);
        var first = reader.FirstOrDefault(p => p.Age == 99);

        Assert.Null(first);
    }

    #endregion

    #region Single / SingleOrDefault Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Single_ReturnsSingleRecord()
    {
        var reader = Csv.DeserializeRecords<Person>("Name,Age,Score\nAlice,25,100");
        var single = reader.Single();

        Assert.Equal("Alice", single.Name);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Single_ThrowsOnEmpty()
    {
        InvalidOperationException? ex = null;
        try
        {
            var reader = Csv.DeserializeRecords<Person>("Name,Age,Score");
            reader.Single();
        }
        catch (InvalidOperationException e)
        {
            ex = e;
        }

        Assert.NotNull(ex);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Single_ThrowsOnMultiple()
    {
        InvalidOperationException? ex = null;
        try
        {
            var reader = Csv.DeserializeRecords<Person>(TEST_CSV);
            reader.Single();
        }
        catch (InvalidOperationException e)
        {
            ex = e;
        }

        Assert.NotNull(ex);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void SingleOrDefault_ReturnsSingleRecord()
    {
        var reader = Csv.DeserializeRecords<Person>("Name,Age,Score\nAlice,25,100");
        var single = reader.SingleOrDefault();

        Assert.NotNull(single);
        Assert.Equal("Alice", single!.Name);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void SingleOrDefault_ReturnsNullOnEmpty()
    {
        var reader = Csv.DeserializeRecords<Person>("Name,Age,Score");
        var single = reader.SingleOrDefault();

        Assert.Null(single);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void SingleOrDefault_ThrowsOnMultiple()
    {
        InvalidOperationException? ex = null;
        try
        {
            var reader = Csv.DeserializeRecords<Person>(TEST_CSV);
            reader.SingleOrDefault();
        }
        catch (InvalidOperationException e)
        {
            ex = e;
        }

        Assert.NotNull(ex);
    }

    #endregion

    #region Count Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Count_ReturnsCorrectCount()
    {
        var reader = Csv.DeserializeRecords<Person>(TEST_CSV);
        var count = reader.Count();

        Assert.Equal(4, count);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Count_WithPredicate_CountsMatches()
    {
        var reader = Csv.DeserializeRecords<Person>(TEST_CSV);
        var count = reader.Count(p => p.Age == 25);

        Assert.Equal(2, count);
    }

    #endregion

    #region Any / All Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Any_ReturnsTrueWhenRecordsExist()
    {
        var reader = Csv.DeserializeRecords<Person>(TEST_CSV);
        Assert.True(reader.Any());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Any_ReturnsFalseWhenEmpty()
    {
        var reader = Csv.DeserializeRecords<Person>("Name,Age,Score");
        Assert.False(reader.Any());
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Any_WithPredicate_ReturnsTrueOnMatch()
    {
        var reader = Csv.DeserializeRecords<Person>(TEST_CSV);
        Assert.True(reader.Any(p => p.Score == 100));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Any_WithPredicate_ReturnsFalseOnNoMatch()
    {
        var reader = Csv.DeserializeRecords<Person>(TEST_CSV);
        Assert.False(reader.Any(p => p.Score > 200));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void All_ReturnsTrueWhenAllMatch()
    {
        var reader = Csv.DeserializeRecords<Person>(TEST_CSV);
        Assert.True(reader.All(p => p.Age > 0));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void All_ReturnsFalseWhenAnyDoesNotMatch()
    {
        var reader = Csv.DeserializeRecords<Person>(TEST_CSV);
        Assert.False(reader.All(p => p.Age > 30));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void All_ReturnsTrueOnEmpty()
    {
        var reader = Csv.DeserializeRecords<Person>("Name,Age,Score");
        Assert.True(reader.All(p => p.Age > 100)); // Vacuous truth
    }

    #endregion

    #region Where / Select Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Where_FiltersRecords()
    {
        var reader = Csv.DeserializeRecords<Person>(TEST_CSV);
        var filtered = reader.Where(p => p.Age >= 30);

        Assert.Equal(2, filtered.Count);
        Assert.All(filtered, p => Assert.True(p.Age >= 30));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Select_ProjectsRecords()
    {
        var reader = Csv.DeserializeRecords<Person>(TEST_CSV);
        var names = reader.Select(p => p.Name);

        Assert.Equal(4, names.Count);
        Assert.Contains("Alice", names);
        Assert.Contains("Diana", names);
    }

    #endregion

    #region Skip / Take Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Skip_SkipsRecords()
    {
        var reader = Csv.DeserializeRecords<Person>(TEST_CSV);
        var skipped = reader.Skip(2);

        Assert.Equal(2, skipped.Count);
        Assert.Equal("Charlie", skipped[0].Name);
        Assert.Equal("Diana", skipped[1].Name);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Take_TakesRecords()
    {
        var reader = Csv.DeserializeRecords<Person>(TEST_CSV);
        var taken = reader.Take(2);

        Assert.Equal(2, taken.Count);
        Assert.Equal("Alice", taken[0].Name);
        Assert.Equal("Bob", taken[1].Name);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void Take_ReturnsAllWhenCountExceedsRecords()
    {
        var reader = Csv.DeserializeRecords<Person>(TEST_CSV);
        var taken = reader.Take(100);

        Assert.Equal(4, taken.Count);
    }

    #endregion

    #region ForEach Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ForEach_ExecutesActionOnEachRecord()
    {
        var reader = Csv.DeserializeRecords<Person>(TEST_CSV);
        var names = new List<string>();

        reader.ForEach(p => names.Add(p.Name));

        Assert.Equal(4, names.Count);
        Assert.Equal(["Alice", "Bob", "Charlie", "Diana"], names);
    }

    #endregion

    #region ToDictionary / GroupBy Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ToDictionary_CreatesDictionaryByKey()
    {
        var reader = Csv.DeserializeRecords<Person>(TEST_CSV);
        var dict = reader.ToDictionary(p => p.Name);

        Assert.Equal(4, dict.Count);
        Assert.Equal(25, dict["Alice"].Age);
        Assert.Equal(35, dict["Diana"].Age);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void ToDictionary_WithValueSelector_CreatesProjectedDictionary()
    {
        var reader = Csv.DeserializeRecords<Person>(TEST_CSV);
        var dict = reader.ToDictionary(p => p.Name, p => p.Score);

        Assert.Equal(4, dict.Count);
        Assert.Equal(100, dict["Alice"]);
        Assert.Equal(90, dict["Diana"]);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void GroupBy_GroupsRecordsByKey()
    {
        var reader = Csv.DeserializeRecords<Person>(TEST_CSV);
        var groups = reader.GroupBy(p => p.Age);

        Assert.Equal(3, groups.Count); // Ages: 25, 30, 35
        Assert.Equal(2, groups[25].Count); // Alice and Charlie
        Assert.Single(groups[30]); // Bob
        Assert.Single(groups[35]); // Diana
    }

    #endregion

    #region Streaming Reader Tests

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void StreamingReader_ToList_Works()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(TEST_CSV));
        var reader = Csv.DeserializeRecords<Person>(stream);
        var list = reader.ToList();

        Assert.Equal(4, list.Count);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void StreamingReader_First_Works()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(TEST_CSV));
        var reader = Csv.DeserializeRecords<Person>(stream);
        var first = reader.First();

        Assert.Equal("Alice", first.Name);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void StreamingReader_Where_FiltersRecords()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(TEST_CSV));
        var reader = Csv.DeserializeRecords<Person>(stream);
        var filtered = reader.Where(p => p.Score >= 95);

        Assert.Equal(2, filtered.Count);
    }

    #endregion

    private sealed class Person
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
        public int Score { get; set; }
    }
}
