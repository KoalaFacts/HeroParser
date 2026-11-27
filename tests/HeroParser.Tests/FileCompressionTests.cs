using System.Data;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Serialization;
using HeroParser.Cli;
using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Reading.Data;
using HeroParser.Htbs.Records;
using HeroParser.SeparatedValues.Core;
using HeroParser.SeparatedValues.Detection;
using HeroParser.SeparatedValues.Validation;
using HeroParser.SeparatedValues.Writing;
using HeroParser.Tests.ConsoleUi;
using Xunit;

namespace HeroParser.Tests;

// Some cases run CLI commands (profile, convert) that write through AnsiConsole.Current.
[Collection(AnsiConsoleCurrentCollection.NAME)]
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public partial class FileCompressionTests
{
    [GenerateBinder]
    public sealed class CompressionPerson
    {
        [TabularMap(Name = "Name")]
        public string Name { get; set; } = string.Empty;

        [TabularMap(Name = "Age")]
        public int Age { get; set; }
    }

    [JsonSerializable(typeof(CompressionPerson))]
    internal partial class CompressionJsonContext : JsonSerializerContext;

    [GenerateBinder]
    public sealed class CompressionFixedRecord
    {
        [PositionalMap(Start = 0, Length = 10)]
        public string Name { get; set; } = string.Empty;

        [PositionalMap(Start = 10, Length = 5, Alignment = FieldAlignment.Right)]
        public int Age { get; set; }
    }

    [GenerateBinder]
    public sealed class MultiFoo
    {
        public string? Type { get; set; }
        public int Count { get; set; }
    }

    [GenerateBinder]
    public sealed class MultiBar
    {
        public string? Type { get; set; }
        public string? Label { get; set; }
    }

    [Fact]
    public Task Csv_Gzip_SyncAndAsync_WriteReadDataReaderValidateAndSchema_RoundTrip()
        => AssertCsvCompressedRoundTripAsync(".gz", CompressGzip, AssertGzipMagicHeader);

    [Fact]
    public Task Csv_Brotli_SyncAndAsync_WriteReadDataReaderValidateAndSchema_RoundTrip()
        => AssertCsvCompressedRoundTripAsync(".br", CompressBrotli, AssertBrotliReadable);

    [Fact]
    public Task Jsonl_Gzip_SyncAndAsync_BuildersAndDataReader_RoundTrip()
        => AssertJsonlCompressedRoundTripAsync(".gz", AssertGzipMagicHeader);

    [Fact]
    public Task Jsonl_Brotli_SyncAndAsync_BuildersAndDataReader_RoundTrip()
        => AssertJsonlCompressedRoundTripAsync(".br", AssertBrotliReadable);

    [Fact]
    public void Cli_Convert_Brotli_CsvJsonlRoundTrip()
        => AssertCliConvertCompressedRoundTrip(".br");

    [Fact]
    public Task FixedWidth_Gzip_SyncAndAsync_WriteReadStreamingAndDataReader_RoundTrip()
        => AssertFixedWidthCompressedRoundTripAsync(".gz", AssertGzipMagicHeader);

    [Fact]
    public Task FixedWidth_Brotli_SyncAndAsync_WriteReadStreamingAndDataReader_RoundTrip()
        => AssertFixedWidthCompressedRoundTripAsync(".br", AssertBrotliReadable);

    [Fact]
    public async Task Csv_RowBuilder_MultiSchema_And_CliProfile_Compressed_RoundTrip()
    {
        await AssertCsvRowMultiSchemaAndProfileAsync(".gz", CompressGzipBytes);
        await AssertCsvRowMultiSchemaAndProfileAsync(".br", CompressBrotliBytes);
    }

    [Fact]
    public async Task Htb_Gzip_And_Brotli_BuildersConverterAndDataReader_RoundTrip()
    {
        await AssertHtbCompressedRoundTripAsync(".gz", AssertGzipMagicHeader);
        await AssertHtbCompressedRoundTripAsync(".br", AssertBrotliReadable);
    }

#if NET11_0_OR_GREATER
    [Fact]
    public Task Csv_Zstandard_SyncAndAsync_WriteReadDataReaderValidateAndSchema_RoundTrip()
        => AssertCsvCompressedRoundTripAsync(".zst", CompressZstd, AssertZstdMagicHeader);

    [Fact]
    public void Csv_ZstdLongExtension_SyncWriteAndRead_RoundTrip()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"hero-zstd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            List<CompressionPerson> people =
            [
                new() { Name = "Alice", Age = 30 },
                new() { Name = "Bob", Age = 25 }
            ];

            string zstdPath = Path.Combine(dir, "people.csv.zstd");
            Csv.WriteToFile(zstdPath, people);
            AssertZstdMagicHeader(zstdPath);

            var readBack = new List<CompressionPerson>();
            using (var reader = Csv.Read<CompressionPerson>().FromFile(zstdPath, out _))
            {
                foreach (var item in reader)
                    readBack.Add(item);
            }
            Assert.Equal(2, readBack.Count);
            Assert.Equal("Alice", readBack[0].Name);
            Assert.Equal(25, readBack[1].Age);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public Task Jsonl_Zstandard_SyncAndAsync_BuildersAndDataReader_RoundTrip()
        => AssertJsonlCompressedRoundTripAsync(".zst", AssertZstdMagicHeader);

    [Fact]
    public void Cli_Convert_Zstandard_CsvJsonlRoundTrip()
        => AssertCliConvertCompressedRoundTrip(".zst");

    [Fact]
    public Task FixedWidth_Zstandard_SyncAndAsync_WriteReadStreamingAndDataReader_RoundTrip()
        => AssertFixedWidthCompressedRoundTripAsync(".zst", AssertZstdMagicHeader);

    [Fact]
    public void FixedWidth_ZstdLongExtension_SyncWriteAndRead_RoundTrip()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"hero-fw-zstd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            List<CompressionFixedRecord> records =
            [
                new() { Name = "Alice", Age = 30 },
                new() { Name = "Bob", Age = 25 }
            ];

            string zstdPath = Path.Combine(dir, "people.dat.zstd");
            FixedWidth.WriteToFile(zstdPath, records);
            AssertZstdMagicHeader(zstdPath);

            var readBack = FixedWidth.Read<CompressionFixedRecord>().FromFile(zstdPath).ToList();
            Assert.Equal(2, readBack.Count);
            Assert.Equal("Alice", readBack[0].Name);
            Assert.Equal(25, readBack[1].Age);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public Task Csv_RowBuilder_MultiSchema_And_CliProfile_Zstandard_RoundTrip()
        => AssertCsvRowMultiSchemaAndProfileAsync(".zst", CompressZstdBytes);

    [Fact]
    public Task Htb_Zstandard_BuildersConverterAndDataReader_RoundTrip()
        => AssertHtbCompressedRoundTripAsync(".zst", AssertZstdMagicHeader);

    [Fact]
    public void Htb_ZstdLongExtension_SyncWriteAndRead_RoundTrip()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"hero-htb-zstd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            List<CompressionPerson> people =
            [
                new() { Name = "Alice", Age = 30 },
                new() { Name = "Bob", Age = 25 }
            ];

            string zstdPath = Path.Combine(dir, "people.htb.zstd");
            HeroParser.Htb.Write<CompressionPerson>().ToFile(zstdPath, people);
            AssertZstdMagicHeader(zstdPath);

            var readBack = HeroParser.Htb.Read<CompressionPerson>().FromFile(zstdPath).ToList();
            Assert.Equal(2, readBack.Count);
            Assert.Equal("Alice", readBack[0].Name);
            Assert.Equal(25, readBack[1].Age);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
#else
    [Fact]
    public void Zstandard_BeforeNet11_ThrowsPlatformNotSupported_WithoutCreatingFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"hero-zst-unsupported-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            List<CompressionPerson> people = [new() { Name = "Alice", Age = 30 }];
            List<CompressionFixedRecord> fixedRecords = [new() { Name = "Alice", Age = 30 }];
            foreach (string ext in (string[])[".zst", ".zstd"])
            {
                string csvPath = Path.Combine(dir, $"people.csv{ext}");
                Assert.Throws<PlatformNotSupportedException>(() => Csv.WriteToFile(csvPath, people));
                Assert.False(File.Exists(csvPath));
                Assert.Throws<PlatformNotSupportedException>(() =>
                {
                    using var csvReader = Csv.ReadFromFile(csvPath, out _);
                });

                string jsonlPath = Path.Combine(dir, $"people.jsonl{ext}");
                Assert.Throws<PlatformNotSupportedException>(() =>
                    Jsonl.Write<CompressionPerson>().ToFile(jsonlPath, people, CompressionJsonContext.Default.CompressionPerson));
                Assert.False(File.Exists(jsonlPath));
                Assert.Throws<PlatformNotSupportedException>(() =>
                {
                    using var jsonlReader = Jsonl.Read<CompressionPerson>().FromFile(jsonlPath, CompressionJsonContext.Default.CompressionPerson);
                });

                string fwPath = Path.Combine(dir, $"people.dat{ext}");
                Assert.Throws<PlatformNotSupportedException>(() => FixedWidth.WriteToFile(fwPath, fixedRecords));
                Assert.False(File.Exists(fwPath));
                Assert.Throws<PlatformNotSupportedException>(() => FixedWidth.ReadFromFile(fwPath));

                string htbPath = Path.Combine(dir, $"people.htb{ext}");
                Assert.Throws<PlatformNotSupportedException>(() => HeroParser.Htb.Write<CompressionPerson>().ToFile(htbPath, people));
                Assert.False(File.Exists(htbPath));
                Assert.Throws<PlatformNotSupportedException>(() => { _ = HeroParser.Htb.Read<CompressionPerson>().FromFile(htbPath).ToList(); });
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
#endif

    private static async Task AssertCsvCompressedRoundTripAsync(
        string extension,
        Func<string, byte[]> compress,
        Action<string> assertCompressedFormat)
    {
        var ct = TestContext.Current.CancellationToken;
        string dir = Path.Combine(Path.GetTempPath(), $"hero-comp-csv-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            List<CompressionPerson> people =
            [
                new() { Name = "Alice", Age = 30 },
                new() { Name = "Bob", Age = 25 },
                new() { Name = "Charlie", Age = 41 }
            ];

            string compPath = Path.Combine(dir, $"people.csv{extension}");
            Csv.WriteToFile(compPath, people);
            assertCompressedFormat(compPath);

            // Sync buffered ReadFromFile
            using (var rowReader = Csv.ReadFromFile(compPath, out _))
            {
                var rows = new List<string>();
                while (rowReader.MoveNext())
                    rows.Add($"{rowReader.Current[0].UnquoteToString()}:{rowReader.Current[1].UnquoteToString()}");
                Assert.Equal(["Name:Age", "Alice:30", "Bob:25", "Charlie:41"], rows);
            }

            // Fluent builder sync + async
            var builderSync = new List<CompressionPerson>();
            using (var syncReader = Csv.Read<CompressionPerson>().FromFile(compPath, out _))
            {
                foreach (var item in syncReader)
                    builderSync.Add(item);
            }
            Assert.Equal(3, builderSync.Count);
            Assert.Equal("Alice", builderSync[0].Name);

            var builderAsync = new List<CompressionPerson>();
            await foreach (var item in Csv.Read<CompressionPerson>().FromFileAsync(compPath, ct))
                builderAsync.Add(item);
            Assert.Equal(3, builderAsync.Count);
            Assert.Equal(41, builderAsync[2].Age);

            // Async streaming reader
            await using (var asyncReader = Csv.CreateAsyncStreamReader(compPath))
            {
                int count = 0;
                while (await asyncReader.MoveNextAsync(ct))
                    count++;
                Assert.Equal(4, count);
            }

            // DataReader + ValidateFileAsync (including SkipRows rewind) + InferSchemaFileAsync (without delimiter)
            using (var dr = Csv.CreateDataReader(compPath))
            {
                Assert.True(dr.Read());
                Assert.Equal("Alice", dr.GetString(0));
            }

            var validation = await Csv.ValidateFileAsync(compPath, new CsvValidationOptions { SkipRows = 1, HasHeaderRow = false }, ct);
            Assert.True(validation.IsValid);
            Assert.Equal(4, validation.TotalRows);
            Assert.Equal(2, validation.ColumnCount);
            Assert.Equal(',', validation.Delimiter);

            // CRLF preamble row + semicolon delimiter auto-detection on non-seekable compressed stream
            string semiPath = Path.Combine(dir, $"semicolon.csv{extension}");
            await File.WriteAllBytesAsync(semiPath, compress("# generated export preamble\r\nName;Age\r\nAlice;30\r\nBob;25\r\n"), ct);
            var semiValidation = await Csv.ValidateFileAsync(semiPath, new CsvValidationOptions { SkipRows = 1, HasHeaderRow = true }, ct);
            Assert.True(semiValidation.IsValid);
            Assert.Equal(';', semiValidation.Delimiter);
            Assert.Equal(["Name", "Age"], semiValidation.Headers);
            Assert.Equal(4, semiValidation.TotalRows);

            var inferred = await Csv.InferSchemaFileAsync(compPath, cancellationToken: ct);
            Assert.Equal(2, inferred.Columns.Count);
            Assert.Equal(CsvInferredType.Integer, inferred.Columns[1].InferredType);

            // MaxInputSize zip-bomb guard on decompressed size
            var smallLimit = new CsvReadOptions { MaxInputSize = 10 };
            Assert.Throws<CsvException>(() => Csv.ReadFromFile(compPath, out _, smallLimit));

            // WriteToFileAsync & WriteFromDataReaderToFileAsync -> compressed output
            string asyncPath = Path.Combine(dir, $"people-async.csv{extension}");
            await Csv.WriteToFileAsync(asyncPath, people, cancellationToken: ct);
            assertCompressedFormat(asyncPath);

            using var table = new DataTable();
            table.Columns.Add("Name", typeof(string));
            table.Columns.Add("Age", typeof(int));
            table.Rows.Add("Dana", 29);

            string drPath = Path.Combine(dir, $"dr.csv{extension}");
            await using (var tableReader = table.CreateDataReader())
            {
                await Csv.WriteFromDataReaderToFileAsync(drPath, tableReader, cancellationToken: ct);
            }
            assertCompressedFormat(drPath);
            using var drBack = Csv.CreateDataReader(drPath);
            Assert.True(drBack.Read());
            Assert.Equal("Dana", drBack.GetString(0));
            Assert.Equal("29", drBack.GetString(1));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static async Task AssertJsonlCompressedRoundTripAsync(
        string extension,
        Action<string> assertCompressedFormat)
    {
        var ct = TestContext.Current.CancellationToken;
        string dir = Path.Combine(Path.GetTempPath(), $"hero-comp-jsonl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            List<CompressionPerson> people =
            [
                new() { Name = "Alice", Age = 30 },
                new() { Name = "Bob", Age = 25 }
            ];

            string compPath = Path.Combine(dir, $"people.jsonl{extension}");
            Jsonl.Write<CompressionPerson>().ToFile(compPath, people, CompressionJsonContext.Default.CompressionPerson);
            assertCompressedFormat(compPath);

            using (var reader = Jsonl.Read<CompressionPerson>().FromFile(compPath, CompressionJsonContext.Default.CompressionPerson))
            {
                var readBack = reader.ToList();
                Assert.Equal(2, readBack.Count);
                Assert.Equal("Bob", readBack[1].Name);
            }

            var asyncRead = new List<CompressionPerson>();
            await foreach (var item in Jsonl.Read<CompressionPerson>().FromFileAsync(compPath, CompressionJsonContext.Default.CompressionPerson, ct))
                asyncRead.Add(item);
            Assert.Equal(2, asyncRead.Count);

            using (var dr = Jsonl.CreateDataReader(compPath))
            {
                Assert.True(dr.HasRows);
                Assert.True(dr.Read());
                Assert.Equal("Alice", dr.GetString(0));
            }

            string drJsonlPath = Path.Combine(dir, $"exported.jsonl{extension}");
            using var table = new DataTable();
            table.Columns.Add("Name", typeof(string));
            table.Columns.Add("Age", typeof(int));
            table.Rows.Add("Eve", 22);

            await using (var tableReader = table.CreateDataReader())
            {
                await Jsonl.WriteFromDataReaderToFileAsync(drJsonlPath, tableReader, cancellationToken: ct);
            }
            assertCompressedFormat(drJsonlPath);
            using var drFromJsonl = Jsonl.CreateDataReader(drJsonlPath);
            Assert.True(drFromJsonl.Read());
            Assert.Equal("Eve", drFromJsonl.GetString(0));
            Assert.Equal("22", drFromJsonl.GetString(1));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static async Task AssertFixedWidthCompressedRoundTripAsync(
        string extension,
        Action<string> assertCompressedFormat)
    {
        var ct = TestContext.Current.CancellationToken;
        string dir = Path.Combine(Path.GetTempPath(), $"hero-comp-fw-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            List<CompressionFixedRecord> records =
            [
                new() { Name = "Alice", Age = 30 },
                new() { Name = "Bob", Age = 25 },
                new() { Name = "Charlie", Age = 41 }
            ];

            string compPath = Path.Combine(dir, $"people.dat{extension}");
            FixedWidth.WriteToFile(compPath, records);
            assertCompressedFormat(compPath);

            // Sync facade ReadFromFile + ReadFromFileAsync
            var spanRows = new List<string>();
            foreach (var row in FixedWidth.ReadFromFile(compPath))
                spanRows.Add($"{row.GetField(0, 10).CharSpan.TrimEnd()}:{row.GetField(10, 5).CharSpan.TrimStart()}");
            Assert.Equal(["Alice:30", "Bob:25", "Charlie:41"], spanRows);

            var asyncSource = await FixedWidth.ReadFromFileAsync(compPath, cancellationToken: ct);
            var sourceRows = new List<string>();
            foreach (var row in asyncSource.CreateReader())
                sourceRows.Add($"{row.GetField(0, 10).CharSpan.TrimEnd()}:{row.GetField(10, 5).CharSpan.TrimStart()}");
            Assert.Equal(["Alice:30", "Bob:25", "Charlie:41"], sourceRows);

            // Async streaming reader
            await using (var asyncReader = FixedWidth.CreateAsyncStreamReader(compPath))
            {
                int count = 0;
                while (await asyncReader.MoveNextAsync(ct))
                    count++;
                Assert.Equal(3, count);
            }

            // Fluent builder typed (FromFile, FromFileAsync, ForEachFromFile) and non-generic (FromFile, FromFileAsync)
            var builderSync = FixedWidth.Read<CompressionFixedRecord>().FromFile(compPath).ToList();
            Assert.Equal(3, builderSync.Count);
            Assert.Equal("Alice", builderSync[0].Name);
            Assert.Equal(41, builderSync[2].Age);

            var builderAsync = new List<CompressionFixedRecord>();
            await foreach (var item in FixedWidth.Read<CompressionFixedRecord>().FromFileAsync(compPath, ct))
                builderAsync.Add(item);
            Assert.Equal(3, builderAsync.Count);
            Assert.Equal("Bob", builderAsync[1].Name);

            var forEachList = new List<string>();
            FixedWidth.Read<CompressionFixedRecord>().ForEachFromFile(compPath, r => forEachList.Add(r.Name));
            Assert.Equal(["Alice", "Bob", "Charlie"], forEachList);

            int nonGenericSyncCount = 0;
            foreach (var _ in FixedWidth.Read().FromFile(compPath))
                nonGenericSyncCount++;
            Assert.Equal(3, nonGenericSyncCount);

            await using (var nonGenericAsync = FixedWidth.Read().FromFileAsync(compPath))
            {
                int count = 0;
                while (await nonGenericAsync.MoveNextAsync(ct))
                    count++;
                Assert.Equal(3, count);
            }

            // DataReader
            var drOptions = new FixedWidthDataReaderOptions
            {
                HasHeaderRow = false,
                Columns =
                [
                    new FixedWidthDataReaderColumn { Name = "Name", Start = 0, Length = 10 },
                    new FixedWidthDataReaderColumn { Name = "Age", Start = 10, Length = 5, Alignment = FieldAlignment.Right }
                ]
            };
            using (var dr = FixedWidth.CreateDataReader(compPath, readerOptions: drOptions))
            {
                Assert.True(dr.Read());
                Assert.Equal("Alice", dr.GetString(0));
                Assert.Equal("30", dr.GetString(1));
            }

            // MaxInputSize zip-bomb guard on decompressed size (buffered & streaming)
            var smallLimit = new FixedWidthReadOptions { MaxInputSize = 10 };
            Assert.Throws<FixedWidthException>(() => FixedWidth.ReadFromFile(compPath, smallLimit));
            await Assert.ThrowsAsync<FixedWidthException>(async () =>
            {
                await using var r = FixedWidth.CreateAsyncStreamReader(compPath, smallLimit);
                while (await r.MoveNextAsync(ct)) { }
            });

            // WriteToFileAsync, Write<T>().ToFileAsync, CreateFileWriter & Write().CreateFileWriter
            string facadeAsyncPath = Path.Combine(dir, $"facade-async.dat{extension}");
            await FixedWidth.WriteToFileAsync(facadeAsyncPath, records, cancellationToken: ct);
            assertCompressedFormat(facadeAsyncPath);
            Assert.Equal(3, FixedWidth.Read<CompressionFixedRecord>().FromFile(facadeAsyncPath).Count);

            string builderAsyncPath = Path.Combine(dir, $"builder-async.dat{extension}");
            await FixedWidth.Write<CompressionFixedRecord>().ToFileAsync(builderAsyncPath, records, ct);
            assertCompressedFormat(builderAsyncPath);
            Assert.Equal(3, FixedWidth.Read<CompressionFixedRecord>().FromFile(builderAsyncPath).Count);

            string facadeWriterPath = Path.Combine(dir, $"facade-writer.dat{extension}");
            using (var writer = FixedWidth.CreateFileWriter(facadeWriterPath))
            {
                writer.WriteField("Dana", 10);
                writer.WriteField("29", 5, FieldAlignment.Right);
                writer.EndRow();
            }
            assertCompressedFormat(facadeWriterPath);
            var danaRecord = Assert.Single(FixedWidth.Read<CompressionFixedRecord>().FromFile(facadeWriterPath));
            Assert.Equal("Dana", danaRecord.Name);
            Assert.Equal(29, danaRecord.Age);

            string builderWriterPath = Path.Combine(dir, $"builder-writer.dat{extension}");
            using (var writer = FixedWidth.Write().CreateFileWriter(builderWriterPath))
            {
                writer.WriteField("Eve", 10);
                writer.WriteField("22", 5, FieldAlignment.Right);
                writer.EndRow();
            }
            assertCompressedFormat(builderWriterPath);
            var eveRecord = Assert.Single(FixedWidth.Read<CompressionFixedRecord>().FromFile(builderWriterPath));
            Assert.Equal("Eve", eveRecord.Name);
            Assert.Equal(22, eveRecord.Age);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void AssertCliConvertCompressedRoundTrip(string extension)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"hero-comp-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string csvIn = Path.Combine(dir, $"input.csv{extension}");
            string jsonlMid = Path.Combine(dir, $"mid.jsonl{extension}");
            string csvOut = Path.Combine(dir, $"output.csv{extension}");

            Csv.WriteToFile(csvIn, [new CompressionPerson { Name = "Alice", Age = 30 }]);

            var cli = new CliCommands();
            Assert.True(cli.Convert(csvIn, jsonlMid, null, null, null));
            Assert.True(cli.Convert(jsonlMid, csvOut, null, null, null));

            var roundTripped = new List<CompressionPerson>();
            using (var reader = Csv.Read<CompressionPerson>().FromFile(csvOut, out _))
            {
                foreach (var item in reader)
                    roundTripped.Add(item);
            }
            var single = Assert.Single(roundTripped);
            Assert.Equal("Alice", single.Name);
            Assert.Equal(30, single.Age);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void AssertGzipMagicHeader(string path)
    {
        byte[] header = new byte[2];
        using var fs = File.OpenRead(path);
        int read = fs.Read(header, 0, 2);
        Assert.Equal(2, read);
        Assert.Equal(0x1F, header[0]);
        Assert.Equal(0x8B, header[1]);
    }

    private static void AssertBrotliReadable(string path)
    {
        using var fs = File.OpenRead(path);
        using var br = new BrotliStream(fs, CompressionMode.Decompress);
        using var sr = new StreamReader(br, Encoding.UTF8);
        string decompressed = sr.ReadToEnd();
        Assert.NotEmpty(decompressed);
    }

    private static async Task AssertCsvRowMultiSchemaAndProfileAsync(
        string extension,
        Func<byte[], byte[]> compressBytes)
    {
        var ct = TestContext.Current.CancellationToken;
        string dir = Path.Combine(Path.GetTempPath(), $"hero-comp-rowms-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string utf8Path = Path.Combine(dir, $"rows.csv{extension}");
            await File.WriteAllBytesAsync(utf8Path, compressBytes(Encoding.UTF8.GetBytes("Name,Age\nAlice,30\nBob,25\n")), ct);

            await using (var rowReader = Csv.Read().FromFileAsync(utf8Path))
            {
                int rows = 0;
                while (await rowReader.MoveNextAsync(ct))
                    rows++;
                Assert.Equal(3, rows);
            }

            await using (var textReader = Csv.Read().FromTextFileAsync(utf8Path))
            {
                Assert.True(await textReader.MoveNextAsync(ct));
                Assert.Equal("Name", textReader.Current[0].UnquoteToString());
                Assert.True(await textReader.MoveNextAsync(ct));
                Assert.Equal("Alice", textReader.Current[0].UnquoteToString());
                Assert.True(await textReader.MoveNextAsync(ct));
                Assert.Equal("25", textReader.Current[1].UnquoteToString());
                Assert.False(await textReader.MoveNextAsync(ct));
            }

            // UTF-16LE with BOM compressed
            string utf16Path = Path.Combine(dir, $"utf16.csv{extension}");
            byte[] utf16Preamble = Encoding.Unicode.GetPreamble();
            byte[] utf16Body = Encoding.Unicode.GetBytes("Name,Age\nDana,40\n");
            byte[] utf16Raw = [.. utf16Preamble, .. utf16Body];
            await File.WriteAllBytesAsync(utf16Path, compressBytes(utf16Raw), ct);

            await using (var utf16Reader = Csv.Read().FromTextFileAsync(utf16Path))
            {
                Assert.True(await utf16Reader.MoveNextAsync(ct));
                Assert.Equal("Name", utf16Reader.Current[0].UnquoteToString());
                Assert.True(await utf16Reader.MoveNextAsync(ct));
                Assert.Equal("Dana", utf16Reader.Current[0].UnquoteToString());
                Assert.Equal("40", utf16Reader.Current[1].UnquoteToString());
                Assert.False(await utf16Reader.MoveNextAsync(ct));
            }

            // CLI profile reads through FromTextFileAsync
            Assert.True(await new CliCommands().ProfileAsync(utf8Path, null, null));

            // Multi-schema FromFile
            string multiPath = Path.Combine(dir, $"multi.csv{extension}");
            await File.WriteAllBytesAsync(
                multiPath,
                compressBytes(Encoding.UTF8.GetBytes("Type,Count,Label\nFoo,5,\nBar,,hello\n")),
                ct);

            var multiItems = new List<object>();
            await using (var multiReader = Csv.Read()
                .WithMultiSchema()
                .WithDiscriminator("Type")
                .MapRecord<MultiFoo>("Foo")
                .MapRecord<MultiBar>("Bar")
                .FromFile(multiPath))
            {
                while (await multiReader.MoveNextAsync(ct))
                {
                    if (multiReader.Current is not null)
                        multiItems.Add(multiReader.Current);
                }
            }
            Assert.Equal(2, multiItems.Count);
            Assert.Equal(5, Assert.IsType<MultiFoo>(multiItems[0]).Count);
            Assert.Equal("hello", Assert.IsType<MultiBar>(multiItems[1]).Label);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static async Task AssertHtbCompressedRoundTripAsync(
        string extension,
        Action<string> assertCompressedFormat)
    {
        _ = TestContext.Current.CancellationToken;
        string dir = Path.Combine(Path.GetTempPath(), $"hero-comp-htb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            List<CompressionPerson> people =
            [
                new() { Name = "Alice", Age = 30 },
                new() { Name = "Bob", Age = 25 }
            ];

            string htbSync = Path.Combine(dir, $"sync.htb{extension}");
            HeroParser.Htb.Write<CompressionPerson>().ToFile(htbSync, people);
            assertCompressedFormat(htbSync);
            var readSync = HeroParser.Htb.Read<CompressionPerson>().FromFile(htbSync).ToList();
            Assert.Equal(2, readSync.Count);
            Assert.Equal("Alice", readSync[0].Name);
            Assert.Equal(25, readSync[1].Age);

            string htbAsync = Path.Combine(dir, $"async.htb{extension}");
            await HeroParser.Htb.Write<CompressionPerson>().ToFileAsync(htbAsync, people);
            assertCompressedFormat(htbAsync);
            var readAsync = new List<CompressionPerson>();
            await foreach (var item in HeroParser.Htb.Read<CompressionPerson>().FromFileAsync(htbAsync))
                readAsync.Add(item);
            Assert.Equal(2, readAsync.Count);
            Assert.Equal("Bob", readAsync[1].Name);

            // ConvertFromCsv / ConvertToCsv + CLI convert with compressed HTB paths
            string csvSrc = Path.Combine(dir, $"src.csv{extension}");
            Csv.WriteToFile(csvSrc, people);
            string htbConv = Path.Combine(dir, $"conv.htb{extension}");
            string csvBack = Path.Combine(dir, $"back.csv{extension}");

            var schema = new HtbSchema(
            [
                new HtbColumn("Name", HtbDataType.String, isNullable: false),
                new HtbColumn("Age", HtbDataType.Int32, isNullable: false)
            ]);
            HeroParser.Htb.ConvertFromCsv(csvSrc, htbConv, schema);
            assertCompressedFormat(htbConv);
            HeroParser.Htb.ConvertToCsv(htbConv, csvBack);
            assertCompressedFormat(csvBack);

            using (var dr = HeroParser.Htb.CreateDataReader(htbConv))
            {
                Assert.True(dr.HasRows);
                Assert.True(dr.Read());
                Assert.Equal("Alice", dr.GetString(0));
                Assert.Equal(30, dr.GetInt32(1));
            }

            string cliHtb = Path.Combine(dir, $"cli.htb{extension}");
            string cliCsv = Path.Combine(dir, $"cli-out.csv{extension}");
            var cli = new CliCommands();
            Assert.True(cli.Convert(csvSrc, cliHtb, null, null, null));
            assertCompressedFormat(cliHtb);
            Assert.True(cli.Convert(cliHtb, cliCsv, null, null, null));
            assertCompressedFormat(cliCsv);

            using var drCsv = Csv.CreateDataReader(cliCsv);
            Assert.True(drCsv.Read());
            Assert.Equal("Alice", drCsv.GetString(0));
            Assert.Equal("30", drCsv.GetString(1));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static byte[] CompressGzip(string content)
        => CompressGzipBytes(Encoding.UTF8.GetBytes(content));

    private static byte[] CompressGzipBytes(byte[] bytes)
    {
        using var ms = new MemoryStream();
        using (var gzip = new GZipStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            gzip.Write(bytes, 0, bytes.Length);
        return ms.ToArray();
    }

    private static byte[] CompressBrotli(string content)
        => CompressBrotliBytes(Encoding.UTF8.GetBytes(content));

    private static byte[] CompressBrotliBytes(byte[] bytes)
    {
        using var ms = new MemoryStream();
        using (var brotli = new BrotliStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            brotli.Write(bytes, 0, bytes.Length);
        return ms.ToArray();
    }

#if NET11_0_OR_GREATER
    private static void AssertZstdMagicHeader(string path)
    {
        byte[] header = new byte[4];
        using var fs = File.OpenRead(path);
        int read = fs.Read(header, 0, 4);
        Assert.Equal(4, read);
        Assert.Equal(0x28, header[0]);
        Assert.Equal(0xB5, header[1]);
        Assert.Equal(0x2F, header[2]);
        Assert.Equal(0xFD, header[3]);
    }

    private static byte[] CompressZstd(string content)
        => CompressZstdBytes(Encoding.UTF8.GetBytes(content));

    private static byte[] CompressZstdBytes(byte[] bytes)
    {
        using var ms = new MemoryStream();
        using (var zstd = new ZstandardStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            zstd.Write(bytes, 0, bytes.Length);
        return ms.ToArray();
    }
#endif

    [Fact]
    public async Task EmptyGzipWrite_ViaLibraryWriters_WritesValidGzipFrameSyncAndAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        string dir = Path.Combine(Path.GetTempPath(), $"hero-empty-gz-lib-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string jsonlSync = Path.Combine(dir, "empty-sync.jsonl.gz");
            string jsonlAsync = Path.Combine(dir, "empty-async.jsonl.gz");
            string csvSync = Path.Combine(dir, "empty-sync.csv.gz");
            string csvAsync = Path.Combine(dir, "empty-async.csv.gz");

            var noBomUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            Jsonl.WriteToFile(jsonlSync, Array.Empty<CompressionPerson>());
            await Jsonl.WriteEnumerableToFileAsync(jsonlAsync, Array.Empty<CompressionPerson>(), cancellationToken: ct);
            Csv.WriteToFile(csvSync, Array.Empty<CompressionPerson>(), new CsvWriteOptions { WriteHeader = false }, noBomUtf8);
            await Csv.WriteToFileAsync(csvAsync, Array.Empty<CompressionPerson>(), new CsvWriteOptions { WriteHeader = false }, noBomUtf8, cancellationToken: ct);

            foreach (string gzPath in (string[])[jsonlSync, jsonlAsync, csvSync, csvAsync])
            {
                byte[] raw = File.ReadAllBytes(gzPath);
                Assert.True(raw.Length >= 20, $"Expected valid gzip frame for {gzPath}, got {raw.Length} bytes.");
                Assert.Equal(0x1F, raw[0]);
                Assert.Equal(0x8B, raw[1]);

                using var gz = new GZipStream(File.OpenRead(gzPath), CompressionMode.Decompress);
                using var ms = new MemoryStream();
                gz.CopyTo(ms);
                Assert.Equal(0, ms.Length);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task CompressedCsv_MaxInputSize_CountsDecompressedBytesSyncAndAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        string dir = Path.Combine(Path.GetTempPath(), $"hero-comp-maxsize-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            // 4-byte CSV compresses into a ~24-byte .gz frame on disk.
            string rowPath = Path.Combine(dir, "tiny.csv.gz");
            await File.WriteAllBytesAsync(rowPath, CompressGzip("a,b\n"), ct);
            Assert.True(new FileInfo(rowPath).Length > 4);

            using (var reader = Csv.Read().WithMaxInputSize(4).FromFile(rowPath, out _))
            {
                Assert.True(reader.MoveNext());
                Assert.Equal("a", reader.Current[0].ToString());
                Assert.Equal("b", reader.Current[1].ToString());
                Assert.False(reader.MoveNext());
            }

            Assert.Throws<CsvException>(() => Csv.Read().WithMaxInputSize(3).FromFile(rowPath, out _));

            await using (var okAsync = Csv.Read().WithMaxInputSize(4).FromFileAsync(rowPath))
            {
                Assert.True(await okAsync.MoveNextAsync(ct));
                Assert.False(await okAsync.MoveNextAsync(ct));
            }

            await Assert.ThrowsAsync<CsvException>(async () =>
            {
                await using var tooSmallAsync = Csv.Read().WithMaxInputSize(3).FromFileAsync(rowPath);
                while (await tooSmallAsync.MoveNextAsync(ct)) { }
            });

            string recPath = Path.Combine(dir, "person.csv.gz");
            await File.WriteAllBytesAsync(recPath, CompressGzip("Name,Age\nAlice,30\n"), ct);

            var okRecords = new List<CompressionPerson>();
            await foreach (var person in Csv.Read<CompressionPerson>().WithMaxInputSize(18).FromFileAsync(recPath, ct))
            {
                okRecords.Add(person);
            }
            Assert.Single(okRecords);
            Assert.Equal("Alice", okRecords[0].Name);

            await Assert.ThrowsAsync<CsvException>(async () =>
            {
                await foreach (var _ in Csv.Read<CompressionPerson>().WithMaxInputSize(17).FromFileAsync(recPath, ct)) { }
            });

            await Assert.ThrowsAsync<CsvException>(async () =>
            {
                await foreach (var _ in Csv.Read<CompressionPerson>().WithMaxInputSize(3).FromFileAsync(recPath, ct)) { }
            });

            // ValidateFileAsync must enforce MaxInputSize during compressed sampling, whitespace scanning, and SkipRows preamble skipping.
            string largeWsPath = Path.Combine(dir, "whitespace.csv.gz");
            await File.WriteAllBytesAsync(largeWsPath, CompressGzip(new string(' ', 100_000)), ct);

            var smallCapWs = await Csv.ValidateFileAsync(
                largeWsPath,
                new CsvValidationOptions { AllowEmptyFile = true, ParseOptions = new CsvReadOptions { MaxInputSize = 4096 } },
                ct);
            Assert.False(smallCapWs.IsValid);
            Assert.Contains(smallCapWs.Errors, e => e.ErrorType == CsvValidationErrorType.ParseError);

            var midCapWs = await Csv.ValidateFileAsync(
                largeWsPath,
                new CsvValidationOptions { AllowEmptyFile = true, ParseOptions = new CsvReadOptions { MaxInputSize = 70_000 } },
                ct);
            Assert.False(midCapWs.IsValid);
            Assert.Contains(midCapWs.Errors, e => e.ErrorType == CsvValidationErrorType.ParseError);

            string skipRowsPath = Path.Combine(dir, "skiprows.csv.gz");
            await File.WriteAllBytesAsync(skipRowsPath, CompressGzip("# metadata preamble\nName,Age\nAlice,30\n"), ct);
            var skipRowsExceeded = await Csv.ValidateFileAsync(
                skipRowsPath,
                new CsvValidationOptions { SkipRows = 1, ParseOptions = new CsvReadOptions { MaxInputSize = 10 } },
                ct);
            Assert.False(skipRowsExceeded.IsValid);
            Assert.Contains(skipRowsExceeded.Errors, e => e.ErrorType == CsvValidationErrorType.ParseError);

            string okWsPath = Path.Combine(dir, "ok-ws.csv.gz");
            await File.WriteAllBytesAsync(okWsPath, CompressGzip("   \r\n  "), ct);
            var okWsResult = await Csv.ValidateFileAsync(
                okWsPath,
                new CsvValidationOptions { AllowEmptyFile = true, ParseOptions = new CsvReadOptions { MaxInputSize = 1024 } },
                ct);
            Assert.True(okWsResult.IsValid);

            // FromTextFileAsync must enforce MaxInputSize on raw decompressed UTF-16 bytes BEFORE transcoding:
            // "A,B\n1,2\n" in UTF-16LE with BOM is 18 raw bytes (8 UTF-8 bytes after transcoding), so MaxInputSize = 10 must reject it.
            string utf16ExceededPath = Path.Combine(dir, "utf16-exceeded.csv.gz");
            byte[] utf16ExceededBytes = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("A,B\n1,2\n")];
            await File.WriteAllBytesAsync(utf16ExceededPath, CompressGzipBytes(utf16ExceededBytes), ct);

            await Assert.ThrowsAsync<CsvException>(async () =>
            {
                await using var reader = Csv.Read().WithMaxInputSize(10).FromTextFileAsync(utf16ExceededPath);
                while (await reader.MoveNextAsync(ct)) { }
            });

            // CJK UTF-16LE with BOM: 22 chars = 46 raw UTF-16 bytes, but 62 UTF-8 bytes after transcoding.
            // MaxInputSize = 50 must succeed because the raw byte budget is 46 <= 50.
            string cjkCol1 = new('中', 10);
            string cjkCol2 = new('文', 10);
            string cjkPath = Path.Combine(dir, "utf16-cjk.csv.gz");
            byte[] cjkBytes = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes($"{cjkCol1},{cjkCol2}\n")];
            await File.WriteAllBytesAsync(cjkPath, CompressGzipBytes(cjkBytes), ct);

            await using var cjkReader = Csv.Read().WithMaxInputSize(50).FromTextFileAsync(cjkPath);
            Assert.True(await cjkReader.MoveNextAsync(ct));
            Assert.Equal(cjkCol1, cjkReader.Current[0].UnquoteToString());
            Assert.Equal(cjkCol2, cjkReader.Current[1].UnquoteToString());
            Assert.False(await cjkReader.MoveNextAsync(ct));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
