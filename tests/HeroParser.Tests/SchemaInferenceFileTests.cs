using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Detection;
using Xunit;

namespace HeroParser.Tests;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public sealed class SchemaInferenceFileTests : IDisposable
{
    private readonly List<string> paths = [];

    public void Dispose()
    {
        foreach (string path in paths)
            File.Delete(path);
    }

    private string FileWith(string content)
    {
        string path = Path.Join(Path.GetTempPath(), Path.GetRandomFileName() + ".csv");
        File.WriteAllText(path, content);
        paths.Add(path);
        return path;
    }

    [Fact]
    public async Task SmallFile_MatchesExistingStringInference()
    {
        const string csv = "Name,Age,Amount,Active\nAlice,30,1.5,true\nBob,,2.25,false";

        var expected = Csv.InferSchema(csv);
        var actual = await Csv.InferSchemaFileAsync(FileWith(csv), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(expected.SampledRowCount, actual.SampledRowCount);
        Assert.Equal(expected.Columns, actual.Columns);
    }

    [Fact]
    public async Task FileInference_StopsBeforeOversizedTail()
    {
        string csv = "Id,Value\n" + string.Concat(Enumerable.Range(0, 100).Select(i => $"{i},true\n")) +
            new string('x', 600_000);

        var result = await Csv.InferSchemaFileAsync(FileWith(csv), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(100, result.SampledRowCount);
        Assert.Equal(CsvInferredType.Integer, result.Columns[0].InferredType);
        Assert.Equal(CsvInferredType.Boolean, result.Columns[1].InferredType);
    }

    [Fact]
    public async Task ExplicitDelimiterAndWideRows_AreSupported()
    {
        string csv = string.Join(';', Enumerable.Range(0, 101).Select(i => $"C{i}")) + "\n" +
            string.Join(';', Enumerable.Repeat("1", 101));

        var result = await Csv.InferSchemaFileAsync(FileWith(csv), new CsvSchemaInferenceOptions
        {
            Delimiter = ';',
            MaxColumnCount = 101
        }, TestContext.Current.CancellationToken);

        Assert.Equal(101, result.Columns.Count);
        Assert.All(result.Columns, column => Assert.Equal(CsvInferredType.Integer, column.InferredType));
    }

    [Fact]
    public async Task Utf16File_IsRejectedEvenWithExplicitDelimiter()
    {
        string path = FileWith("");
        File.WriteAllText(path, "A,B\n1,2", System.Text.Encoding.Unicode);

        await Assert.ThrowsAsync<CsvException>(async () =>
            await Csv.InferSchemaFileAsync(path, new CsvSchemaInferenceOptions { Delimiter = ',' },
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TrailingBlankRows_MarkColumnsNullable()
    {
        var result = await Csv.InferSchemaFileAsync(FileWith("A,B\n1,2\n\n"),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.SampledRowCount);
        Assert.All(result.Columns, column => Assert.True(column.IsNullable));
    }

    [Fact]
    public async Task MalformedSampleRow_ReturnsObservedRows()
    {
        var result = await Csv.InferSchemaFileAsync(FileWith("Id,Value\n1,true\n\"unterminated"),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, result.SampledRowCount);
        Assert.Equal(CsvInferredType.Integer, result.Columns[0].InferredType);
        Assert.Equal(CsvInferredType.Boolean, result.Columns[1].InferredType);
    }

    [Fact]
    public async Task LargeRow_UsesStreamingHardLimitInsteadOfDefaultLimit()
    {
        var result = await Csv.InferSchemaFileAsync(FileWith("Value\n" + new string('x', 600_000)),
            new CsvSchemaInferenceOptions { Delimiter = ',' }, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.SampledRowCount);
        Assert.Equal(600_000, result.Columns[0].MaxLength);
    }

    [Fact]
    public async Task TruncatedDelimiterSample_RequiresExplicitDelimiter()
    {
        string csv = "\"" + new string('x', 70_000) + "\";B\n1;2";
        string path = FileWith(csv);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Csv.InferSchemaFileAsync(path, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("specify a delimiter", error.Message, StringComparison.OrdinalIgnoreCase);

        var result = await Csv.InferSchemaFileAsync(path, new CsvSchemaInferenceOptions { Delimiter = ';' },
            TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Columns.Count);
    }
}
