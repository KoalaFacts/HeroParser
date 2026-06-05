using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;
using HeroParser.Htbs;
using HeroParser.Htbs.Reading;
using HeroParser.Htbs.Writing;
using HeroParser.Htbs.Records;
using HeroParser.SeparatedValues;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Detection;
using HeroParser.SeparatedValues.Validation;
using HeroParser.SeparatedValues.Reading.Records.MultiSchema;
using HeroParser.SeparatedValues.Reading.Rows;
using HeroParser;

namespace HeroParser.Tests.Fuzzing;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class ParserFuzzHarness
{
    private const int ITERATIONS_PER_PARSER = 500; // Moderate default for test suite execution

    [GenerateBinder]
    public class FuzzTestRecord
    {
        [TabularMap(Name = "Id")]
        public int Id { get; set; }

        [TabularMap(Name = "Name")]
        [Validate(NotNull = true, NotEmpty = true, MaxLength = 20, MinLength = 2)]
        public string? Name { get; set; }

        [TabularMap(Name = "Score")]
        [Validate(RangeMin = -10.0, RangeMax = 100.0)]
        public double Score { get; set; }

        [TabularMap(Name = "Flag")]
        public bool Flag { get; set; }
    }

    private static readonly string[] seedCsvRows =
    [
        "Id,Name,Score,Flag\r\n",
        "1,Alice,95.5,true\r\n",
        "2,Bob,88.0,false\r\n",
        "3,Charlie,-5.0,true\r\n",
        "4,\"David, The Great\",42.1,false\r\n",
        "5,\"Emma\r\nNewline\",78.9,true\r\n"
    ];

    [Fact]
    public void FuzzCsvParserRobustness()
    {
        var rand = new Random(1337);
        var baseCsv = string.Concat(seedCsvRows);

        for (int i = 0; i < ITERATIONS_PER_PARSER; i++)
        {
            string mutatedText = MutateChars(baseCsv, rand);

            // Execute CSV parser and assert robustness
            try
            {
                var options = new CsvReadOptions { AllowNewlinesInsideQuotes = rand.Next(2) == 0 };
                using var reader = Csv.ReadFromText(mutatedText, options);
                while (reader.MoveNext())
                {
                    var row = reader.Current;
                    for (int col = 0; col < row.ColumnCount; col++)
                    {
                        _ = row[col].UnquoteToString();
                    }
                }
            }
            catch (Exception ex) when (ex is FormatException or CsvException or InvalidOperationException)
            {
                // Expected structured parsing exceptions
            }
            catch (Exception ex)
            {
                // Unhandled catastrophic crash!
                throw new Exception($"CSV Fuzz failure on iteration {i} with mutated input:\n{mutatedText}\nError: {ex}", ex);
            }
        }
    }

    [Fact]
    public void FuzzCsvParserOptionsAndBoundaries()
    {
        var rand = new Random(5678);

        char[] delimiters = [',', ';', '|', '\t'];
        char[] quotes = ['"', '\''];
        char?[] escapes = ['\\', '/', null];
        char?[] comments = ['#', ';', null];

        for (int i = 0; i < 500; i++)
        {
            char delim = delimiters[rand.Next(delimiters.Length)];
            char quote = quotes[rand.Next(quotes.Length)];
            char? escape = escapes[rand.Next(escapes.Length)];
            char? comment = comments[rand.Next(comments.Length)];

            // Ensure no collision
            if (comment.HasValue && (comment.Value == delim || comment.Value == quote))
                comment = null;
            if (escape.HasValue && (escape.Value == delim || escape.Value == quote || (comment.HasValue && escape.Value == comment.Value)))
                escape = null;

            var options = new CsvReadOptions
            {
                Delimiter = delim,
                Quote = quote,
                EscapeCharacter = escape,
                CommentCharacter = comment,
                AllowNewlinesInsideQuotes = rand.Next(2) == 0,
                EnableQuotedFields = true,
                TrimFields = rand.Next(2) == 0,
                TrackSourceLineNumbers = rand.Next(2) == 0,
                UseSimdIfAvailable = rand.Next(2) == 0,
                MaxFieldSize = rand.Next(2) == 0 ? 50 : null,
                MaxRowSize = rand.Next(2) == 0 ? 1024 : null
            };

            // Generate specialized boundary aligned CSV strings
            string boundaryCsv = GenerateBoundaryString(rand, delim, quote, escape, comment);
            string mutatedText = MutateChars(boundaryCsv, rand);

            // 1. Text Parsing Fuzz
            try
            {
                using var reader = Csv.ReadFromText(mutatedText, options);
                while (reader.MoveNext())
                {
                    var row = reader.Current;
                    for (int col = 0; col < row.ColumnCount; col++)
                    {
                        _ = row[col].ToString();
                        _ = row[col].UnquoteToString(quote, escape);
                    }
                }
            }
            catch (Exception ex) when (ex is FormatException or CsvException or InvalidOperationException)
            {
                // Expected parsing/validation exceptions
            }
            catch (Exception ex)
            {
                throw new Exception($"CsvParser boundary fuzz failure (text) at iteration {i}.\nOptions: Delim={delim}, Quote={quote}, Escape={escape}, Comment={comment}\nInput: {mutatedText}\nError: {ex}", ex);
            }

            // 2. Stream Parsing Fuzz
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(mutatedText);
                using var ms = new MemoryStream(bytes);
                using var reader = Csv.ReadFromStream(ms, out var streamBytes, options);
                while (reader.MoveNext())
                {
                    var row = reader.Current;
                    for (int col = 0; col < row.ColumnCount; col++)
                    {
                        _ = row[col].ToString();
                        _ = row[col].UnquoteToString((byte)quote, escape.HasValue ? (byte)escape.Value : null);
                    }
                }
            }
            catch (Exception ex) when (ex is FormatException or CsvException or InvalidOperationException)
            {
                // Expected parsing/validation exceptions
            }
            catch (Exception ex)
            {
                throw new Exception($"CsvParser boundary fuzz failure (stream) at iteration {i}.\nOptions: Delim={delim}, Quote={quote}, Escape={escape}, Comment={comment}\nInput: {mutatedText}\nError: {ex}", ex);
            }
        }
    }

    [Fact]
    public async Task FuzzCsvMultiSchema()
    {
        var rand = new Random(9876);
        string[] types = ["H", "D", "T", "X", "", " "];

        for (int i = 0; i < 300; i++)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Type,Data");
            for (int r = 0; r < 8; r++)
            {
                string type = types[rand.Next(types.Length)];
                string data = rand.Next(2) == 0 ? "some_data" : "\"quoted, data\"";
                sb.AppendLine($"{type},{data}");
            }
            string csvText = sb.ToString();

            try
            {
                var behavior = rand.Next(2) == 0 ? UnmatchedRowBehavior.Skip : UnmatchedRowBehavior.Throw;
                var builder = Csv.Read()
                    .WithMultiSchema()
                    .WithDiscriminator("Type")
                    .MapRecord<MultiSchemaTests.SimpleHeader>("H")
                    .MapRecord<MultiSchemaTests.SimpleDetail>("D")
                    .MapRecord<MultiSchemaTests.SimpleTrailer>("T")
                    .OnUnmatchedRow(behavior);

                if (rand.Next(2) == 0)
                {
                    foreach (var record in builder.FromText(csvText))
                    {
                        _ = record;
                    }
                }
                else
                {
                    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvText));
                    await using var reader = builder.FromStream(stream);
                    while (await reader.MoveNextAsync(TestContext.Current.CancellationToken))
                    {
                        _ = reader.Current;
                    }
                }
            }
            catch (Exception ex) when (ex is CsvException or FormatException or InvalidOperationException)
            {
                // Expected parsing/validation exceptions
            }
            catch (Exception ex)
            {
                throw new Exception($"Multi-schema fuzz failure at iteration {i}.\nInput: {csvText}\nError: {ex}", ex);
            }
        }
    }

    [Fact]
    public void FuzzCsvDataReader()
    {
        var rand = new Random(1212);
        var baseCsv = string.Concat(seedCsvRows);

        for (int i = 0; i < 300; i++)
        {
            string mutatedText = MutateChars(baseCsv, rand);
            byte[] bytes = Encoding.UTF8.GetBytes(mutatedText);

            try
            {
                var options = new CsvReadOptions
                {
                    AllowNewlinesInsideQuotes = rand.Next(2) == 0,
                    CommentCharacter = rand.Next(2) == 0 ? '#' : null
                };
                using var ms = new MemoryStream(bytes);
                using var dr = Csv.CreateDataReader(ms, options: options);

                while (dr.Read())
                {
                    for (int col = 0; col < dr.FieldCount; col++)
                    {
                        _ = dr.GetName(col);
                        _ = dr.GetFieldType(col);
                        _ = dr.GetValue(col);
                        _ = dr.IsDBNull(col);
                    }
                }
            }
            catch (Exception ex) when (ex is CsvException or FormatException or InvalidOperationException)
            {
                // Expected structured parser exceptions
            }
            catch (Exception ex)
            {
                throw new Exception($"CsvDataReader fuzz failure at iteration {i}. Error: {ex}", ex);
            }
        }
    }

    [Fact]
    public void FuzzCsvValidatorAndDetector()
    {
        var rand = new Random(9999);
        var baseCsv = string.Concat(seedCsvRows);

        for (int i = 0; i < 300; i++)
        {
            string mutatedText = MutateChars(baseCsv, rand);

            // Test CsvDelimiterDetector
            try
            {
                _ = CsvDelimiterDetector.DetectDelimiter(mutatedText);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                // Expected delimiter detection failures under heavy corruption
            }
            catch (Exception ex)
            {
                throw new Exception($"CsvDelimiterDetector fuzz failure at iteration {i}. Error: {ex}", ex);
            }

            // Test CsvValidator
            try
            {
                var validationOptions = new CsvValidationOptions
                {
                    AllowEmptyFile = rand.Next(2) == 0,
                    CheckConsistentColumnCount = rand.Next(2) == 0,
                    HasHeaderRow = rand.Next(2) == 0,
                    MaxRows = rand.Next(1, 100)
                };
                _ = CsvValidator.Validate(mutatedText, validationOptions);
            }
            catch (Exception ex) when (ex is CsvException or FormatException or InvalidOperationException)
            {
                // Expected validation exceptions
            }
            catch (Exception ex)
            {
                throw new Exception($"CsvValidator fuzz failure at iteration {i}. Error: {ex}", ex);
            }
        }
    }

    [Fact]
    public void FuzzHtbParserRobustness()
    {
        var rand = new Random(4242);

        // Generate baseline valid HTB bytes
        byte[] validHtbBytes;
        using (var ms = new MemoryStream())
        {
            var records = new[]
            {
                new FuzzTestRecord { Id = 1, Name = "Alice", Score = 95.5, Flag = true },
                new FuzzTestRecord { Id = 2, Name = "Bob", Score = 88.0, Flag = false }
            };
            global::HeroParser.Htb.Write<FuzzTestRecord>().ToStream(ms, records, leaveOpen: true);
            validHtbBytes = ms.ToArray();
        }

        for (int i = 0; i < ITERATIONS_PER_PARSER; i++)
        {
            byte[] mutatedBytes = MutateBytes(validHtbBytes, rand);

            // Execute HTB reader and assert robustness
            try
            {
                using var ms = new MemoryStream(mutatedBytes);
                var records = global::HeroParser.Htb.Read<FuzzTestRecord>().FromStream(ms).ToList();
            }
            catch (Exception ex) when (ex is HtbException or FormatException or IndexOutOfRangeException or EndOfStreamException)
            {
                // Expected structured or low-level parser exceptions under heavy mutative fuzzing
            }
            catch (Exception ex)
            {
                // Catastrophic or unhandled crash!
                throw new Exception($"HTB Fuzz failure on iteration {i} with mutated bytes length {mutatedBytes.Length}.\nError: {ex}", ex);
            }
        }
    }

    private static string GenerateBoundaryString(Random rand, char delimiter, char quote, char? escape, char? comment)
    {
        var sb = new StringBuilder();
        // We want to align special characters at 15, 16, 31, 32, 63, 64 offsets to stress SIMD scanner chunking
        int[] boundaries = [15, 16, 31, 32, 63, 64];
        foreach (int b in boundaries)
        {
            sb.Append(new string('a', b));
            int r = rand.Next(6);
            if (r == 0) sb.Append(delimiter);
            else if (r == 1) sb.Append(quote);
            else if (r == 2) sb.Append("\r\n");
            else if (r == 3 && comment.HasValue) sb.Append(comment.Value);
            else if (r == 4 && escape.HasValue) sb.Append(escape.Value);
            else sb.Append("🌟👑");
        }
        return sb.ToString();
    }

    private static string MutateChars(string original, Random rand)
    {
        var sb = new StringBuilder(original);
        int mutations = rand.Next(1, 5);

        for (int i = 0; i < mutations; i++)
        {
            int op = rand.Next(6);
            int idx = rand.Next(sb.Length + 1);

#pragma warning disable IDE0010
            switch (op)
            {
                case 0: // Replace char with random delimiter / quote / newline
                    if (sb.Length > 0)
                    {
                        int targetIdx = rand.Next(sb.Length);
                        char[] chars = [',', ';', '|', '"', '\r', '\n', '\\', 'a', '1'];
                        sb[targetIdx] = chars[rand.Next(chars.Length)];
                    }
                    break;
                case 1: // Insert random string segment
                    char[] insertChars = [',', ';', '"', '\r', '\n', '\\'];
                    int lenSegment = rand.Next(1, 5);
                    char[] charsSegment = new char[lenSegment];
                    for (int c = 0; c < lenSegment; c++)
                    {
                        charsSegment[c] = insertChars[rand.Next(insertChars.Length)];
                    }
                    var segment = new string(charsSegment);
                    sb.Insert(idx, segment);
                    break;
                case 2: // Delete block of chars
                    if (sb.Length > 0)
                    {
                        int len = Math.Min(rand.Next(1, 10), sb.Length - idx);
                        if (len > 0) sb.Remove(idx, len);
                    }
                    break;
                case 3: // Insert Unicode / Emoji characters
                    sb.Insert(idx, "🌟👑皇后👑🌟");
                    break;
                case 4: // Exaggerated column size (DoS injection)
                    sb.Insert(idx, new string('A', rand.Next(10, 100)));
                    break;
                case 5: // Shuffled duplicate block
                    if (sb.Length > 5)
                    {
                        int sourceIdx = rand.Next(sb.Length - 5);
                        var block = sb.ToString(sourceIdx, 5);
                        sb.Insert(idx, block);
                    }
                    break;
            }
#pragma warning restore IDE0010
        }

        return sb.ToString();
    }

    private static byte[] MutateBytes(byte[] original, Random rand)
    {
        if (original.Length == 0) return original;

        int newLen = original.Length;
        int lenOp = rand.Next(3);
        if (lenOp == 0) // Truncate
        {
            newLen = rand.Next(Math.Max(1, original.Length - 10), original.Length);
        }
        else if (lenOp == 1) // Expand
        {
            newLen = rand.Next(original.Length, original.Length + 15);
        }

        var res = new byte[newLen];
        Array.Copy(original, res, Math.Min(original.Length, res.Length));

        // Perform byte mutations
        int mutations = rand.Next(1, 4);
        for (int i = 0; i < mutations; i++)
        {
            int idx = rand.Next(res.Length);
            int op = rand.Next(4);

#pragma warning disable IDE0010
            switch (op)
            {
                case 0: // Single bit flip
                    res[idx] ^= (byte)(1 << rand.Next(8));
                    break;
                case 1: // Direct byte override (extreme sizes or tags)
                    byte[] overrideBytes = [0x00, 0xFF, 0x7F, 0x80, 0x01, 0x08, 0x0A];
                    res[idx] = overrideBytes[rand.Next(overrideBytes.Length)];
                    break;
                case 2: // Random byte value
                    res[idx] = (byte)rand.Next(256);
                    break;
                case 3: // Replicate small chunk
                    if (res.Length > 4)
                    {
                        int src = rand.Next(res.Length - 4);
                        int dest = rand.Next(res.Length - 4);
                        Array.Copy(res, src, res, dest, 4);
                    }
                    break;
            }
#pragma warning restore IDE0010
        }

        return res;
    }
}
