using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Validation;
using System.IO.Compression;
using System.Text;
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
    public async Task QuotedHeaders_SatisfyRequiredHeadersOnEveryInputPath()
    {
        // Quoting is CSV encoding: "Name" is the header Name, and Headers reports the value.
        const string csv = "\"Name\",\"Unit \"\"cm\"\"\"\nAlice,30\nBob,25\n";
        var options = new CsvValidationOptions { Delimiter = ',', RequiredHeaders = ["Name", "Unit \"cm\""] };

        var results = new[]
        {
            Csv.Validate(csv, options),
            Csv.Validate(System.Text.Encoding.UTF8.GetBytes(csv), options),
            await Csv.ValidateFileAsync(TempFile(csv), options, TestContext.Current.CancellationToken),
        };

        foreach (var result in results)
        {
            Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(error => error.Message)));
            Assert.Equal(["Name", "Unit \"cm\""], result.Headers);
        }
    }

    [Theory]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    [InlineData("'Name','It''s'\nAlice,30\n", '\'', null, true, "Name", "It's")]
    [InlineData("\"Na\\\"me\",Age\nAlice,30\n", '"', '\\', true, "Na\"me", "Age")]
    [InlineData("\"Name\",Age\nAlice,30\n", '\'', null, true, "\"Name\"", "Age")]
    [InlineData("\"Name\",Age\nAlice,30\n", '"', null, false, "\"Name\"", "Age")]
    public async Task Headers_FollowTheConfiguredQuoting(
        string csv, char quote, char? escape, bool quotedFields, string first, string second)
    {
        var options = new CsvValidationOptions
        {
            Delimiter = ',',
            RequiredHeaders = [first, second],
            ParseOptions = new CsvReadOptions { Quote = quote, EscapeCharacter = escape, EnableQuotedFields = quotedFields },
        };

        var results = new[]
        {
            Csv.Validate(csv, options),
            Csv.Validate(System.Text.Encoding.UTF8.GetBytes(csv), options),
            await Csv.ValidateFileAsync(TempFile(csv), options, TestContext.Current.CancellationToken),
        };

        foreach (var result in results)
        {
            Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(error => error.Message)));
            Assert.Equal([first, second], result.Headers);
        }
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ValidateFileAsync_TreatsLargeWhitespaceFileAsEmpty()
    {
        string path = TempFile(new string(' ', 70_000));

        var rejected = await Csv.ValidateFileAsync(path, cancellationToken: TestContext.Current.CancellationToken);
        var allowed = await Csv.ValidateFileAsync(path, new CsvValidationOptions { AllowEmptyFile = true },
            TestContext.Current.CancellationToken);

        Assert.Contains(rejected.Errors, error => error.ErrorType == CsvValidationErrorType.EmptyFile);
        Assert.True(allowed.IsValid);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ValidateFileAsync_DetectsDelimiterAfterSkippedRows()
    {
        string path = TempFile("metadata,with,commas\nmore,metadata,here\nName;Age\nAlice;30\nBob;25\n");

        var result = await Csv.ValidateFileAsync(path, new CsvValidationOptions
        {
            SkipRows = 2,
            RequiredHeaders = ["Name", "Age"]
        }, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Equal(';', result.Delimiter);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ValidateFileAsync_SkipsQuotedPreambleAcrossSampleBoundary()
    {
        string preamble = "\"metadata," + new string('x', 70_000) + "\ncontinued\",note\n";
        string path = TempFile(preamble + "Name;Age\nAlice;30\n");

        var result = await Csv.ValidateFileAsync(path, new CsvValidationOptions { SkipRows = 1 },
            TestContext.Current.CancellationToken);

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(error => error.Message)));
        Assert.Equal(';', result.Delimiter);
        Assert.Equal(["Name", "Age"], result.Headers);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ValidateFileAsync_SkippingAllRowsHonorsAllowEmptyFile()
    {
        string path = TempFile("metadata,only\n");

        var result = await Csv.ValidateFileAsync(path, new CsvValidationOptions
        {
            SkipRows = 1,
            AllowEmptyFile = true
        }, TestContext.Current.CancellationToken);

        Assert.True(result.IsValid);
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ValidateFileAsync_RejectsOversizedQuotedPreamble()
    {
        string path = TempFile("\"metadata," + new string('x', 600_000));

        var result = await Csv.ValidateFileAsync(path, new CsvValidationOptions { SkipRows = 1 },
            TestContext.Current.CancellationToken);

        Assert.Contains(result.Errors, error => error.ErrorType == CsvValidationErrorType.ParseError);
        Assert.Contains("maximum size", result.Errors[0].Message, StringComparison.Ordinal);
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

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public async Task ValidateFileAsync_AllowsCommaQuoteWithSemicolonDelimiter_ExplicitAndAutoDetected()
    {
        string path = TempFile("Name;Note\nAlice;,hello;world,\nBob;plain\n");

        var explicitResult = await Csv.ValidateFileAsync(path, new CsvValidationOptions
        {
            Delimiter = ';',
            ParseOptions = new CsvReadOptions { Quote = ',' }
        }, TestContext.Current.CancellationToken);

        Assert.True(explicitResult.IsValid, string.Join("; ", explicitResult.Errors.Select(e => e.Message)));
        Assert.Equal(';', explicitResult.Delimiter);
        Assert.Equal(3, explicitResult.TotalRows);

        var detectedResult = await Csv.ValidateFileAsync(path, new CsvValidationOptions
        {
            ParseOptions = new CsvReadOptions { Quote = ',' }
        }, TestContext.Current.CancellationToken);

        Assert.True(detectedResult.IsValid, string.Join("; ", detectedResult.Errors.Select(e => e.Message)));
        Assert.Equal(';', detectedResult.Delimiter);
        Assert.Equal(3, detectedResult.TotalRows);

        await Assert.ThrowsAsync<CsvException>(() => Csv.ValidateFileAsync(path, new CsvValidationOptions
        {
            ParseOptions = new CsvReadOptions { MaxInputSize = 0 }
        }, TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<CsvException>(() => Csv.ValidateFileAsync(path, new CsvValidationOptions
        {
            ParseOptions = new CsvReadOptions { MaxRowSize = 0 }
        }, TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.INTEGRATION)]
    public async Task ValidateFileAsync_Utf16BomAndCompressed_EnforcesRawMaxInputSize()
    {
        var ct = TestContext.Current.CancellationToken;
        string dir = Path.Combine(Path.GetTempPath(), $"hp-val-utf16-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            // UTF-16LE auto-delimiter detection (';')
            string utf16LePath = Path.Combine(dir, "valid-le.csv");
            await File.WriteAllBytesAsync(
                utf16LePath,
                [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("Name;Age\nAlice;30\nBob;25\n")],
                ct);
            var leResult = await Csv.ValidateFileAsync(utf16LePath, cancellationToken: ct);
            Assert.True(leResult.IsValid);
            Assert.Equal(';', leResult.Delimiter);
            Assert.Equal(3, leResult.TotalRows);

            // UTF-16BE .csv.gz with ragged row (>= 80% valid comma rows so auto-delimiter detects ',')
            string utf16BeGzPath = Path.Combine(dir, "ragged-be.csv.gz");
            byte[] beBytes = [.. Encoding.BigEndianUnicode.GetPreamble(), .. Encoding.BigEndianUnicode.GetBytes("Name,Age\nAlice,30\nCarol,28\nDave,35\nBob\n")];
            using (var fs = File.Create(utf16BeGzPath))
            using (var gz = new GZipStream(fs, CompressionMode.Compress))
            {
                await gz.WriteAsync(beBytes, ct);
            }
            var beResult = await Csv.ValidateFileAsync(utf16BeGzPath, cancellationToken: ct);
            Assert.False(beResult.IsValid);
            Assert.Contains(beResult.Errors, e => e.ErrorType == CsvValidationErrorType.InconsistentColumnCount);

            // UTF-16LE .csv.gz raw MaxInputSize: 18 raw bytes ("A,B\n1,2\n") must fail at MaxInputSize = 10
            string utf16ExceededGz = Path.Combine(dir, "exceeded-le.csv.gz");
            byte[] exceededBytes = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("A,B\n1,2\n")];
            using (var fs = File.Create(utf16ExceededGz))
            using (var gz = new GZipStream(fs, CompressionMode.Compress))
            {
                await gz.WriteAsync(exceededBytes, ct);
            }
            var exceededResult = await Csv.ValidateFileAsync(
                utf16ExceededGz,
                new CsvValidationOptions { ParseOptions = new CsvReadOptions { MaxInputSize = 10 } },
                ct);
            Assert.False(exceededResult.IsValid);
            Assert.Contains(exceededResult.Errors, e => e.ErrorType == CsvValidationErrorType.ParseError);

            // CJK UTF-16LE .csv.gz: 42 raw bytes -> 54 UTF-8 bytes must pass at MaxInputSize = 50
            string cjkShort1 = new('中', 8);
            string cjkShort2 = new('文', 8);
            string cjkGz = Path.Combine(dir, "cjk-le.csv.gz");
            byte[] cjkBytes = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes($"{cjkShort1},{cjkShort2}\n1,2\n")];
            using (var fs = File.Create(cjkGz))
            using (var gz = new GZipStream(fs, CompressionMode.Compress))
            {
                await gz.WriteAsync(cjkBytes, ct);
            }
            var cjkResult = await Csv.ValidateFileAsync(
                cjkGz,
                new CsvValidationOptions { ParseOptions = new CsvReadOptions { MaxInputSize = 50 } },
                ct);
            Assert.True(cjkResult.IsValid);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
