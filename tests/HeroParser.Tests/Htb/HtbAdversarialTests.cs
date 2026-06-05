using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using HeroParser.Htbs;
using HeroParser.Htbs.Reading;
using HeroParser.Htbs.Writing;
using HeroParser.Htbs.Records;
using HeroParser.Conversion;
using HeroParser.SeparatedValues.Core;

namespace HeroParser.Tests.Htb;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class HtbAdversarialTests
{
    public class SimpleNonNullableRecord
    {
        public int Id { get; set; }
        public bool IsActive { get; set; }
    }

    [Fact]
    public void TestBytesReadAccuracy()
    {
        var schema = new HtbSchema([
            new HtbColumn("Id", HtbDataType.Int32, isNullable: false),
            new HtbColumn("IsActive", HtbDataType.Boolean, isNullable: false)
        ]);

        using var ms = new MemoryStream();
        using (var writer = new HtbStreamWriter(ms, leaveOpen: true))
        {
            writer.WriteHeader(schema);
            writer.IncrementRecordCount();
            writer.WriteMask([0]);
            writer.WriteInt32(42);
            writer.WriteBoolean(true);
            writer.Flush();
        }

        long actualStreamLength = ms.Length;
        ms.Position = 0;

        using var reader = new HtbStreamReader(ms);
        _ = reader.ParseHeader();

        Assert.False(reader.IsEndOfStream());
        reader.IncrementRecordCount();

        Span<byte> mask = stackalloc byte[1];
        reader.ReadMask(mask);
        _ = reader.ReadInt32();
        _ = reader.ReadBoolean();

        Assert.Equal(actualStreamLength, reader.BytesRead);
    }

    [Fact]
    public void TestTruncatedStreamDuringReadMask()
    {
        var columns = Enumerable.Range(0, 16)
            .Select(i => new HtbColumn($"Col{i}", HtbDataType.Int32, isNullable: true))
            .ToList();
        var schema = new HtbSchema(columns);

        using var ms = new MemoryStream();
        using (var writer = new HtbStreamWriter(ms, leaveOpen: true))
        {
            writer.WriteHeader(schema);
            writer.Flush();
        }

        byte[] headerBytes = ms.ToArray();
        var truncatedBytes = new byte[headerBytes.Length + 1];
        Array.Copy(headerBytes, truncatedBytes, headerBytes.Length);
        truncatedBytes[^1] = 0xAA;

        using var truncatedMs = new MemoryStream(truncatedBytes);
        using var reader = new HtbStreamReader(truncatedMs);
        reader.ParseHeader();

        Assert.False(reader.IsEndOfStream());

        Assert.Throws<HtbException>(() =>
        {
            Span<byte> mask = stackalloc byte[2];
            reader.ReadMask(mask);
        });
    }

    [Fact]
    public void TestNonNullableColumnNullValidationInConverter()
    {
        var schema = new HtbSchema([
            new HtbColumn("Id", HtbDataType.Int32, isNullable: false),
            new HtbColumn("Name", HtbDataType.String, isNullable: true)
        ]);

        string csvData = "Id,Name\r\n,Alice";

        using var htbStream = new MemoryStream();

        Assert.Throws<HtbException>(() =>
        {
            CsvToHtbConverter.Convert(csvData, htbStream, schema);
        });
    }

    [Fact]
    public void TestComplexCsvToHtbRoundtripWithQuotesAndNewlines()
    {
        var schema = new HtbSchema([
            new HtbColumn("Id", HtbDataType.Int32, isNullable: false),
            new HtbColumn("Name", HtbDataType.String, isNullable: true),
            new HtbColumn("Notes", HtbDataType.String, isNullable: true),
            new HtbColumn("Embedding", HtbDataType.FloatArray, isNullable: true)
        ]);

        // Construct CSV containing multiline string, escaped quotes, commas, and a vector
        string csvData = "Id,Name,Notes,Embedding\r\n" +
                         "1,\"Alice \"\"The Queen\"\" of Hearts\",\"Line 1\r\nLine 2\nLine 3\",\"[0.1,-0.2,3.14e-5]\"\r\n" +
                         "2,\"C:\\\\Folder\\\\File.txt\",\"Comma, Semicolon; and \"\"Quotes\"\" included.\",[]\r\n" +
                         "3,Bob,,";

        using var htbStream = new MemoryStream();
        var convOptions = new CsvToHtbOptions { AllowNewlinesInsideQuotes = true };
        CsvToHtbConverter.Convert(csvData, htbStream, schema, convOptions);

        htbStream.Position = 0;

        using var csvWriter = new StringWriter();
        HtbToCsvConverter.Convert(htbStream, csvWriter);

        string roundTrippedCsv = csvWriter.ToString();

        // Validate both CSVs parse to the exact same data
        var csvReadOptions = new CsvReadOptions { AllowNewlinesInsideQuotes = true };
        using var reader1 = Csv.ReadFromText(csvData, csvReadOptions);
        using var reader2 = Csv.ReadFromText(roundTrippedCsv, csvReadOptions);

        bool isHeader = true;
        while (true)
        {
            bool has1 = reader1.MoveNext();
            bool has2 = reader2.MoveNext();
            Assert.Equal(has1, has2);
            if (!has1) break;

            var r1 = reader1.Current;
            var r2 = reader2.Current;

            Assert.Equal(r1.ColumnCount, r2.ColumnCount);
            for (int i = 0; i < r1.ColumnCount; i++)
            {
                string s1 = r1[i].UnquoteToString();
                string s2 = r2[i].UnquoteToString();

                if (isHeader)
                {
                    Assert.Equal(s1, s2);
                }
                else
                {
                    if (i == 3 && !string.IsNullOrEmpty(s1))
                    {
                        // Vector parsing equivalence
                        var v1 = HeroParser.Vectors.VectorParser.ParseFloats(s1);
                        var v2 = HeroParser.Vectors.VectorParser.ParseFloats(s2);
                        Assert.Equal(v1, v2);
                    }
                    else
                    {
                        Assert.Equal(s1, s2);
                    }
                }
            }
            isHeader = false;
        }
    }

    [Theory]
    [InlineData("[0.1, 0.2")]
    [InlineData("0.1, 0.2]")]
    [InlineData("[0.1, [0.2], 0.3]")]
    [InlineData("[1.0, 2.0] extra")]
    [InlineData("[abc, def]")]
    [InlineData("not a vector")]
    public void TestVectorParserMalformedBoundaryInputs(string input)
    {
        Assert.Throws<FormatException>(() => HeroParser.Vectors.VectorParser.ParseFloats(input));
        Assert.False(HeroParser.Vectors.VectorParser.TryParseFloats(input, out _));

        Assert.Throws<FormatException>(() => HeroParser.Vectors.VectorParser.ParseDoubles(input));
        Assert.False(HeroParser.Vectors.VectorParser.TryParseDoubles(input, out _));
    }

    [Fact]
    public void TestVectorParserConsecutiveSeparators()
    {
        float[] result = HeroParser.Vectors.VectorParser.ParseFloats("[1.0,,2.0]");
        Assert.Equal([1.0f, 2.0f], result);
    }

    [Fact]
    public async Task TestAsyncWriterFlushStressWideSchema()
    {
        // 1. Create a wide schema with 100 columns
        int colCount = 100;
        var columns = Enumerable.Range(0, colCount)
            .Select(i => new HtbColumn($"Col{i}", i % 2 == 0 ? HtbDataType.Int32 : HtbDataType.String, isNullable: true))
            .ToList();
        var schema = new HtbSchema(columns);

        // 2. Generate a large wide record and convert to CSV
        var sbCsv = new StringBuilder();
        // Header
        sbCsv.AppendLine(string.Join(",", Enumerable.Range(0, colCount).Select(i => $"Col{i}")));
        // Rows
        for (int row = 0; row < 50; row++)
        {
            var rowValues = Enumerable.Range(0, colCount)
                .Select(col => col % 2 == 0 ? col.ToString() : $"\"StringValue_{row}_{col}\"");
            sbCsv.AppendLine(string.Join(",", rowValues));
        }

        string csvData = sbCsv.ToString();
        using var csvStream = new MemoryStream(Encoding.UTF8.GetBytes(csvData));
        using var htbStream = new MemoryStream();

        // 3. Asynchronously convert from CSV to HTB
        await CsvToHtbConverter.ConvertAsync(csvStream, htbStream, schema, options: null, TestContext.Current.CancellationToken);

        // 4. Asynchronously convert from HTB back to CSV
        htbStream.Position = 0;
        using var csvWriter = new StringWriter();
        await HtbToCsvConverter.ConvertAsync(htbStream, csvWriter, options: null, TestContext.Current.CancellationToken);

        string roundTrippedCsv = csvWriter.ToString();

        // 5. Compare structure and values
        using var reader1 = Csv.ReadFromText(csvData);
        using var reader2 = Csv.ReadFromText(roundTrippedCsv);

        while (true)
        {
            bool has1 = reader1.MoveNext();
            bool has2 = reader2.MoveNext();
            Assert.Equal(has1, has2);
            if (!has1) break;

            var r1 = reader1.Current;
            var r2 = reader2.Current;

            Assert.Equal(r1.ColumnCount, r2.ColumnCount);
            for (int i = 0; i < r1.ColumnCount; i++)
            {
                Assert.Equal(r1[i].UnquoteToString(), r2[i].UnquoteToString());
            }
        }
    }

    [GenerateBinder]
    public class ValidatedHtbRecord
    {
        [TabularMap(Name = "Id")]
        public int Id { get; set; }

        [TabularMap(Name = "Name")]
        [Validate(NotNull = true, NotEmpty = true, MaxLength = 10, MinLength = 3)]
        public string? Name { get; set; }

        [TabularMap(Name = "Score")]
        [Validate(RangeMin = 0.0, RangeMax = 100.0)]
        public double Score { get; set; }

        [TabularMap(Name = "Code")]
        [Validate(Pattern = "^[A-Z]{3}$")]
        public string? Code { get; set; }
    }

    [Fact]
    public void TestHtbSourceGeneratedValidationParity()
    {
        // 1. Test happy path
        using (var ms = new MemoryStream())
        {
            var records = new[] { new ValidatedHtbRecord { Id = 1, Name = "Alice", Score = 95.5, Code = "USA" } };
            global::HeroParser.Htb.Write<ValidatedHtbRecord>().ToStream(ms, records, leaveOpen: true);

            ms.Position = 0;
            var reader = global::HeroParser.Htb.Read<ValidatedHtbRecord>().FromStream(ms).ToList();
            Assert.Single(reader);
            Assert.Equal("Alice", reader[0].Name);
            Assert.Equal(95.5, reader[0].Score);
            Assert.Equal("USA", reader[0].Code);
        }

        // 2. Test NotNull violation
        using (var ms = new MemoryStream())
        {
            var records = new[] { new ValidatedHtbRecord { Id = 1, Name = null, Score = 95.5, Code = "USA" } };
            global::HeroParser.Htb.Write<ValidatedHtbRecord>().ToStream(ms, records, leaveOpen: true);

            ms.Position = 0;
            Assert.Throws<HtbException>(() => global::HeroParser.Htb.Read<ValidatedHtbRecord>().FromStream(ms).ToList());
        }

        // 3. Test NotEmpty violation (string only)
        using (var ms = new MemoryStream())
        {
            var records = new[] { new ValidatedHtbRecord { Id = 1, Name = "   ", Score = 95.5, Code = "USA" } };
            global::HeroParser.Htb.Write<ValidatedHtbRecord>().ToStream(ms, records, leaveOpen: true);

            ms.Position = 0;
            Assert.Throws<HtbException>(() => global::HeroParser.Htb.Read<ValidatedHtbRecord>().FromStream(ms).ToList());
        }

        // 4. Test MinLength violation (string only)
        using (var ms = new MemoryStream())
        {
            var records = new[] { new ValidatedHtbRecord { Id = 1, Name = "Ab", Score = 95.5, Code = "USA" } };
            global::HeroParser.Htb.Write<ValidatedHtbRecord>().ToStream(ms, records, leaveOpen: true);

            ms.Position = 0;
            Assert.Throws<HtbException>(() => global::HeroParser.Htb.Read<ValidatedHtbRecord>().FromStream(ms).ToList());
        }

        // 5. Test MaxLength violation (string only)
        using (var ms = new MemoryStream())
        {
            var records = new[] { new ValidatedHtbRecord { Id = 1, Name = "SuperLongNameHere", Score = 95.5, Code = "USA" } };
            global::HeroParser.Htb.Write<ValidatedHtbRecord>().ToStream(ms, records, leaveOpen: true);

            ms.Position = 0;
            Assert.Throws<HtbException>(() => global::HeroParser.Htb.Read<ValidatedHtbRecord>().FromStream(ms).ToList());
        }

        // 6. Test RangeMin violation (numeric only)
        using (var ms = new MemoryStream())
        {
            var records = new[] { new ValidatedHtbRecord { Id = 1, Name = "Alice", Score = -5.0, Code = "USA" } };
            global::HeroParser.Htb.Write<ValidatedHtbRecord>().ToStream(ms, records, leaveOpen: true);

            ms.Position = 0;
            Assert.Throws<HtbException>(() => global::HeroParser.Htb.Read<ValidatedHtbRecord>().FromStream(ms).ToList());
        }

        // 7. Test RangeMax violation (numeric only)
        using (var ms = new MemoryStream())
        {
            var records = new[] { new ValidatedHtbRecord { Id = 1, Name = "Alice", Score = 150.0, Code = "USA" } };
            global::HeroParser.Htb.Write<ValidatedHtbRecord>().ToStream(ms, records, leaveOpen: true);

            ms.Position = 0;
            Assert.Throws<HtbException>(() => global::HeroParser.Htb.Read<ValidatedHtbRecord>().FromStream(ms).ToList());
        }

        // 8. Test Pattern violation (string only)
        using (var ms = new MemoryStream())
        {
            var records = new[] { new ValidatedHtbRecord { Id = 1, Name = "Alice", Score = 95.5, Code = "US1" } };
            global::HeroParser.Htb.Write<ValidatedHtbRecord>().ToStream(ms, records, leaveOpen: true);

            ms.Position = 0;
            Assert.Throws<HtbException>(() => global::HeroParser.Htb.Read<ValidatedHtbRecord>().FromStream(ms).ToList());
        }
    }

    [Fact]
    public void TestHtbCorruptStreamCannotBeSkipped()
    {
        var schema = new HtbSchema([
            new HtbColumn("Id", HtbDataType.Int32, isNullable: false)
        ]);

        // Create corrupt stream (truncated during Int32 read)
        using var ms = new MemoryStream();
        using (var writer = new HtbStreamWriter(ms, leaveOpen: true))
        {
            writer.WriteHeader(schema);
            writer.IncrementRecordCount();
            writer.WriteMask([0]);
            // Only write 2 bytes of the Int32 instead of 4
            ms.Write([1, 0]);
            writer.Flush();
        }

        ms.Position = 0;

        // Custom OnError handler that tries to return SkipRecord
        var options = new HtbReadOptions
        {
            OnError = (context, ex) => HtbDeserializeErrorAction.SkipRecord
        };

        // Assert that even if OnError returns SkipRecord, the reader strictly throws due to stream corruption!
        using var reader = new HtbRecordReader<SimpleNonNullableRecord>(ms, options);
        Assert.Throws<HtbException>(() =>
        {
            while (reader.ReadNext(out _)) { }
        });
    }
}

