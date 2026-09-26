using HeroParser.SeparatedValues.Validation;
using Xunit;

namespace HeroParser.Tests;

public sealed class CsvStreamingValidationTests : IDisposable
{
    private readonly List<string> files = [];

    private string TempFile(string content)
    {
        string path = Path.GetTempFileName();
        File.WriteAllText(path, content);
        files.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (string path in files)
            File.Delete(path);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ValidateFileAsync_DetectsDelimiterAndChecksHeaders()
    {
        string path = TempFile("Name;Age\nAlice;30\nBob;25\n");

        var result = await Csv.ValidateFileAsync(path,
            new CsvValidationOptions { RequiredHeaders = ["name", "Email"] },
            TestContext.Current.CancellationToken);

        Assert.Equal(';', result.Delimiter);
        Assert.Equal(3, result.TotalRows);
        Assert.Equal(2, result.ColumnCount);
        Assert.Equal(["Name", "Age"], result.Headers);
        Assert.Contains(result.Errors, error => error.ErrorType == CsvValidationErrorType.MissingHeader);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ValidateFileAsync_QuotedNewlineAcrossBufferBoundaryIsOneRow()
    {
        string field = new('a', 20_000);
        string path = TempFile($"Name,Note\nAlice,\"{field}\nnext line\"\nBob,done\n");

        var result = await Csv.ValidateFileAsync(path,
            new CsvValidationOptions { Delimiter = ',' },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Equal(3, result.TotalRows);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ValidateFileAsync_StopsAtConfiguredErrorLimit()
    {
        string path = TempFile("A,B\n1\n2\n3\n4\n");

        var result = await Csv.ValidateFileAsync(path,
            new CsvValidationOptions { Delimiter = ',', MaxErrors = 2 },
            TestContext.Current.CancellationToken);

        Assert.False(result.IsValid);
        Assert.True(result.StoppedEarly);
        Assert.Equal(2, result.Errors.Count);
        Assert.Equal(3, result.TotalRows);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ValidateFileAsync_WhitespaceAtSampleBoundaryIsEmpty()
    {
        string path = TempFile(new string(' ', 64 * 1024));

        var result = await Csv.ValidateFileAsync(path, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(result.Errors, error => error.ErrorType == CsvValidationErrorType.EmptyFile);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task ValidateFileAsync_HandlesFileBeyondInMemoryValidationLimit()
    {
        string path = TempFile(string.Empty);
        using (var writer = new StreamWriter(path))
        {
            writer.WriteLine("Name,Age");
            string row = new string('a', 180) + ",1";
            for (int i = 0; i < 100_001; i++)
                writer.WriteLine(row);
        }

        Assert.True(new FileInfo(path).Length > CsvValidator.MAX_UTF8_INPUT_BYTES);
        var result = await Csv.ValidateFileAsync(path, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
        Assert.Equal(100_002, result.TotalRows);
    }
}
