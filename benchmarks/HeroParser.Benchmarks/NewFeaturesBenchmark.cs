using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using HeroParser.SeparatedValues;
using System.Text;

namespace HeroParser.Benchmarks;

/// <summary>
/// Benchmarks for P2 features: CsvWriter, comment line skipping, TrimFields, and MaxFieldLength validation.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput, iterationCount: 10, warmupCount: 3)]
public class NewFeaturesBenchmark
{
    private string csvNormal = null!;
    private string csvWithComments = null!;
    private string csvWithWhitespace = null!;
    private string csvWithLongFields = null!;
    private List<List<string>> rowsToWrite = null!;

    [Params(1_000, 10_000)]
    public int Rows { get; set; }

    [Params(10, 25)]
    public int Columns { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Generate normal CSV
        csvNormal = GenerateCsv(Rows, Columns, includeComments: false, addWhitespace: false);

        // Generate CSV with comment lines (every 5th line is a comment)
        csvWithComments = GenerateCsv(Rows, Columns, includeComments: true, addWhitespace: false);

        // Generate CSV with whitespace around fields
        csvWithWhitespace = GenerateCsv(Rows, Columns, includeComments: false, addWhitespace: true);

        // Generate CSV with long field values
        csvWithLongFields = GenerateCsvWithLongFields(Rows, Columns);

        // Generate rows for write benchmarks
        rowsToWrite = new List<List<string>>(Rows);
        for (int r = 0; r < Rows; r++)
        {
            var row = new List<string>(Columns);
            for (int c = 0; c < Columns; c++)
            {
                row.Add($"val{r}_{c}");
            }
            rowsToWrite.Add(row);
        }
    }

    private static string GenerateCsv(int rows, int columns, bool includeComments, bool addWhitespace)
    {
        var sb = new StringBuilder();
        for (int r = 0; r < rows; r++)
        {
            // Add comment line every 5th line
            if (includeComments && r > 0 && r % 5 == 0)
            {
                sb.AppendLine("# This is a comment line");
            }

            for (int c = 0; c < columns; c++)
            {
                if (c > 0) sb.Append(',');

                if (addWhitespace)
                {
                    sb.Append("  ");  // leading whitespace
                }

                sb.Append($"val{r}_{c}");

                if (addWhitespace)
                {
                    sb.Append("  ");  // trailing whitespace
                }
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string GenerateCsvWithLongFields(int rows, int columns)
    {
        var sb = new StringBuilder();
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                if (c > 0) sb.Append(',');
                // Create fields with varying lengths (50-200 characters)
                sb.Append(new string('x', 50 + (r + c) % 150));
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    // ============================================================
    // CsvWriter Throughput Benchmarks
    // ============================================================

    /// <summary>
    /// Baseline: Measures CsvWriter write performance (rows/sec)
    /// </summary>
    [Benchmark]
    public string CsvWriter_WriteToString()
    {
        using var stringWriter = new StringWriter();
        using var csvWriter = Csv.WriteToTextWriter(stringWriter, leaveOpen: false);

        foreach (var row in rowsToWrite)
        {
            csvWriter.WriteRow(row);
        }

        return stringWriter.ToString();
    }

    /// <summary>
    /// Measures CsvWriter write performance to a MemoryStream
    /// </summary>
    [Benchmark]
    public int CsvWriter_WriteToStream()
    {
        using var stream = new MemoryStream();
        using var csvWriter = Csv.WriteToStream(stream, leaveOpen: false);

        foreach (var row in rowsToWrite)
        {
            csvWriter.WriteRow(row);
        }

        return (int)stream.Length;
    }

    // ============================================================
    // Comment Line Skipping Overhead Benchmarks
    // ============================================================

    /// <summary>
    /// Baseline: Parse CSV without comment support
    /// </summary>
    [Benchmark]
    public int Parse_WithoutCommentSupport()
    {
        var options = new CsvParserOptions
        {
            MaxColumns = Columns + 4,
            MaxRows = Rows + 100,
            CommentCharacter = null
        };

        using var reader = Csv.ReadFromText(csvNormal, options);
        int total = 0;
        foreach (var row in reader)
        {
            total += row.ColumnCount;
        }
        return total;
    }

    /// <summary>
    /// Compare: Parse CSV with comment support enabled (but no comments in data)
    /// </summary>
    [Benchmark]
    public int Parse_WithCommentSupport_NoComments()
    {
        var options = new CsvParserOptions
        {
            MaxColumns = Columns + 4,
            MaxRows = Rows + 100,
            CommentCharacter = '#'
        };

        using var reader = Csv.ReadFromText(csvNormal, options);
        int total = 0;
        foreach (var row in reader)
        {
            total += row.ColumnCount;
        }
        return total;
    }

    /// <summary>
    /// Compare: Parse CSV with comment support and actual comment lines
    /// </summary>
    [Benchmark]
    public int Parse_WithCommentSupport_WithComments()
    {
        var options = new CsvParserOptions
        {
            MaxColumns = Columns + 4,
            MaxRows = Rows + 100,
            CommentCharacter = '#'
        };

        using var reader = Csv.ReadFromText(csvWithComments, options);
        int total = 0;
        foreach (var row in reader)
        {
            total += row.ColumnCount;
        }
        return total;
    }

    // ============================================================
    // TrimFields Performance Impact Benchmarks
    // ============================================================

    /// <summary>
    /// Baseline: Parse CSV without field trimming
    /// </summary>
    [Benchmark]
    public int Parse_WithoutTrimFields()
    {
        var options = new CsvParserOptions
        {
            MaxColumns = Columns + 4,
            MaxRows = Rows + 100,
            TrimFields = false
        };

        using var reader = Csv.ReadFromText(csvWithWhitespace, options);
        int total = 0;
        foreach (var row in reader)
        {
            total += row.ColumnCount;
        }
        return total;
    }

    /// <summary>
    /// Compare: Parse CSV with field trimming enabled
    /// </summary>
    [Benchmark]
    public int Parse_WithTrimFields()
    {
        var options = new CsvParserOptions
        {
            MaxColumns = Columns + 4,
            MaxRows = Rows + 100,
            TrimFields = true
        };

        using var reader = Csv.ReadFromText(csvWithWhitespace, options);
        int total = 0;
        foreach (var row in reader)
        {
            total += row.ColumnCount;
        }
        return total;
    }

    // ============================================================
    // MaxFieldLength Validation Overhead Benchmarks
    // ============================================================

    /// <summary>
    /// Baseline: Parse CSV without MaxFieldLength validation
    /// </summary>
    [Benchmark]
    public int Parse_WithoutMaxFieldLength()
    {
        var options = new CsvParserOptions
        {
            MaxColumns = Columns + 4,
            MaxRows = Rows + 100,
            MaxFieldLength = null
        };

        using var reader = Csv.ReadFromText(csvWithLongFields, options);
        int total = 0;
        foreach (var row in reader)
        {
            total += row.ColumnCount;
        }
        return total;
    }

    /// <summary>
    /// Compare: Parse CSV with MaxFieldLength validation (set to 500, won't trigger)
    /// </summary>
    [Benchmark]
    public int Parse_WithMaxFieldLength()
    {
        var options = new CsvParserOptions
        {
            MaxColumns = Columns + 4,
            MaxRows = Rows + 100,
            MaxFieldLength = 500
        };

        using var reader = Csv.ReadFromText(csvWithLongFields, options);
        int total = 0;
        foreach (var row in reader)
        {
            total += row.ColumnCount;
        }
        return total;
    }

    /// <summary>
    /// Compare: Parse CSV with strict MaxFieldLength validation (set to 100, won't trigger)
    /// </summary>
    [Benchmark]
    public int Parse_WithStrictMaxFieldLength()
    {
        var options = new CsvParserOptions
        {
            MaxColumns = Columns + 4,
            MaxRows = Rows + 100,
            MaxFieldLength = 300
        };

        using var reader = Csv.ReadFromText(csvWithLongFields, options);
        int total = 0;
        foreach (var row in reader)
        {
            total += row.ColumnCount;
        }
        return total;
    }
}
