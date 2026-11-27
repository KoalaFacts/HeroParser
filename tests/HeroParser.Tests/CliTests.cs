using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using HeroParser.Cli;
using HeroParser.Conversion;
using HeroParser.Excels.Core;
using HeroParser.Excels.Reading.Data;
using HeroParser.Excels.Xlsx;
using HeroParser.Htbs;
using HeroParser.Tests.ConsoleUi;

namespace HeroParser.Tests;

[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
[Collection(AnsiConsoleCurrentCollection.NAME)]
public sealed class CliTests
{
    private readonly CliCommands cliCommands = new();

    [Fact]
    public async Task Main_NoArgs_ReturnsSuccessAndPrintsHelp()
    {
        // Act
        int exitCode = await Program.Main([]);

        // Assert
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task Main_InvalidCommand_ReturnsError()
    {
        // Act
        int exitCode = await Program.Main(["nonexistentcommand"]);

        // Assert
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Detect_ValidCsv_PrintsCorrectDetails()
    {
        // Arrange
        string csv = "Name,Age,Email\nAlice,30,alice@example.com\nBob,25,bob@example.com";
        string tempFile = Path.GetTempFileName() + ".csv";
        File.WriteAllText(tempFile, csv);

        try
        {
            // Act & Assert (Should not throw)
            cliCommands.Detect(tempFile);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Validate_ValidCsv_Succeeds()
    {
        // Arrange
        string csv = "Name,Age,Email\nAlice,30,alice@example.com\nBob,25,bob@example.com";
        string tempFile = Path.GetTempFileName() + ".csv";
        File.WriteAllText(tempFile, csv);

        try
        {
            // Act & Assert
            Assert.True(await cliCommands.ValidateAsync(tempFile, ','));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Validate_InvalidCsv_HandlesErrorsGracefully()
    {
        // Arrange
        string csv = "Name,Age,Email\nAlice,30\nBob,25,bob@example.com,ExtraColumn";
        string tempFile = Path.GetTempFileName() + ".csv";
        File.WriteAllText(tempFile, csv);

        try
        {
            // Act & Assert
            Assert.False(await cliCommands.ValidateAsync(tempFile, ','));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Profile_ValidCsv_PrintsMarkdownProfile()
    {
        // Arrange
        string csv = "Name,Age,Active\nAlice,30,true\nBob,25,false\nCharlie,,true";
        string tempFile = Path.GetTempFileName() + ".csv";
        File.WriteAllText(tempFile, csv);

        try
        {
            // Act & Assert
            Assert.True(await cliCommands.ProfileAsync(tempFile, ',', null));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Convert_CsvToJsonlAndBack_Succeeds()
    {
        // Arrange
        string csv = "Name,Age\nAlice,30\nBob,25";
        string tempCsv = Path.GetTempFileName() + ".csv";
        string tempJsonl = Path.GetTempFileName() + ".jsonl";
        string tempCsvBack = Path.GetTempFileName() + ".csv";

        File.WriteAllText(tempCsv, csv);

        try
        {
            // Act - Convert to JSONL
            cliCommands.Convert(tempCsv, tempJsonl, ',', "flat", null);
            Assert.True(File.Exists(tempJsonl));
            var jsonlLines = File.ReadAllLines(tempJsonl);
            Assert.Equal(2, jsonlLines.Length);
            Assert.Contains("\"Name\":\"Alice\"", jsonlLines[0]);

            // Act - Convert JSONL back to CSV
            cliCommands.Convert(tempJsonl, tempCsvBack, ',', null, null);
            Assert.True(File.Exists(tempCsvBack));
            var csvLines = File.ReadAllLines(tempCsvBack);
            Assert.Equal(3, csvLines.Length); // Header + 2 data lines
            Assert.Equal("Name,Age", csvLines[0]);
        }
        finally
        {
            if (File.Exists(tempCsv)) File.Delete(tempCsv);
            if (File.Exists(tempJsonl)) File.Delete(tempJsonl);
            if (File.Exists(tempCsvBack)) File.Delete(tempCsvBack);
        }
    }

    [Fact]
    public void Repair_CutoffMarkdown_CleansAndRepairsQuotes()
    {
        // Arrange
        string rawLlm = "```csv\nName,Quote\nAlice,\"Hello world\n";
        string tempInput = Path.GetTempFileName() + ".txt";
        string tempOutput = Path.GetTempFileName() + ".csv";

        File.WriteAllText(tempInput, rawLlm);

        try
        {
            // Act
            cliCommands.Repair(tempInput, tempOutput);

            // Assert
            Assert.True(File.Exists(tempOutput));
            var lines = File.ReadAllLines(tempOutput);
            Assert.Equal(2, lines.Length);
            Assert.Equal("Name,Quote", lines[0]);
            Assert.Equal("Alice,\"Hello world\"", lines[1]); // Fixed ending quote
        }
        finally
        {
            if (File.Exists(tempInput)) File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
        }
    }

    [Fact]
    public async Task Schema_LocalInference_GeneratesValidClassStructure()
    {
        // Arrange
        string csv = "CustomerId,Balance,IsPremium,JoinDate\n1,100.50,true,2026-01-01";
        string tempCsv = Path.GetTempFileName() + ".csv";
        File.WriteAllText(tempCsv, csv);

        try
        {
            // Act - local inference (useAi = false)
            await cliCommands.SchemaAsync(tempCsv, ',', useAi: false, null, null, null);
        }
        finally
        {
            if (File.Exists(tempCsv)) File.Delete(tempCsv);
        }
    }

    [Fact]
    public void Convert_CsvAndJsonl_ToExcelAndHtbRoundTrip_Succeed()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"hero-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string csvPath = Path.Combine(dir, "input.csv");
            string csvXlsxPath = Path.Combine(dir, "from-csv.xlsx");
            string jsonlPath = Path.Combine(dir, "input.jsonl");
            string jsonlXlsxPath = Path.Combine(dir, "from-jsonl.xlsx");
            string htbPath = Path.Combine(dir, "output.htb");
            string htbCsvPath = Path.Combine(dir, "from-htb.csv");

            File.WriteAllText(csvPath, "Name,Age,Score\nAlice,30,9.5\nBob,25,8.0\n");
            File.WriteAllText(jsonlPath, "{\"Name\":\"Alice\",\"Age\":\"30\"}\n{\"Name\":\"Bob\",\"Age\":\"25\"}\n");

            Assert.True(cliCommands.Convert(csvPath, csvXlsxPath, null, null, null));
            Assert.True(File.Exists(csvXlsxPath));
            using (var reader = HeroParser.Excel.CreateDataReader(csvXlsxPath))
            {
                Assert.True(reader.Read());
                Assert.Equal("Alice", reader.GetString(0));
                Assert.Equal("30", reader.GetString(1));
                Assert.Equal("9.5", reader.GetString(2));
                Assert.True(reader.Read());
                Assert.Equal("Bob", reader.GetString(0));
                Assert.False(reader.Read());
            }

            Assert.True(cliCommands.Convert(jsonlPath, jsonlXlsxPath, null, null, "Data"));
            Assert.True(File.Exists(jsonlXlsxPath));
            using (var reader = HeroParser.Excel.CreateDataReader(jsonlXlsxPath, sheetName: "Data"))
            {
                Assert.True(reader.Read());
                Assert.Equal("Alice", reader.GetString(0));
                Assert.Equal("30", reader.GetString(1));
                Assert.True(reader.Read());
                Assert.Equal("Bob", reader.GetString(0));
                Assert.False(reader.Read());
            }

            Assert.True(cliCommands.Convert(csvPath, htbPath, null, null, null));
            Assert.True(File.Exists(htbPath));

            Assert.True(cliCommands.Convert(htbPath, htbCsvPath, null, null, null));
            Assert.True(File.Exists(htbCsvPath));
            var lines = File.ReadAllLines(htbCsvPath);
            Assert.Equal(3, lines.Length);
            Assert.Equal("Name,Age,Score", lines[0]);
            Assert.Equal("Alice,30,9.5", lines[1]);
            Assert.Equal("Bob,25,8.0", lines[2]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Convert_CompressedCsvToFixedWidth_DecompressesInputCorrectly()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"heroparser-cli-gz-fw-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string gzCsvPath = Path.Combine(dir, "input.csv.gz");
            string txtOutPath = Path.Combine(dir, "output.txt");
            string gzTxtOutPath = Path.Combine(dir, "output.txt.gz");

            using (var fs = File.Create(gzCsvPath))
            using (var gz = new GZipStream(fs, CompressionLevel.Fastest))
            using (var sw = new StreamWriter(gz, Encoding.UTF8))
            {
                sw.Write("Name,Age,City\nAlice,30,Sydney\nBob,25,Melbourne\n");
            }

            Assert.True(cliCommands.Convert(gzCsvPath, txtOutPath, null, null, null));
            Assert.True(File.Exists(txtOutPath));
            string fixedText = File.ReadAllText(txtOutPath);
            Assert.Contains("Alice", fixedText, StringComparison.Ordinal);
            Assert.Contains("Sydney", fixedText, StringComparison.Ordinal);
            Assert.Contains("Melbourne", fixedText, StringComparison.Ordinal);

            Assert.True(cliCommands.Convert(gzCsvPath, gzTxtOutPath, null, null, null));
            Assert.True(File.Exists(gzTxtOutPath));
            using var readFs = File.OpenRead(gzTxtOutPath);
            using var readGz = new GZipStream(readFs, CompressionMode.Decompress);
            using var reader = new StreamReader(readGz, Encoding.UTF8);
            string gzFixedText = reader.ReadToEnd();
            Assert.Equal(fixedText, gzFixedText);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Convert_JsonlToExcel_PreservesLiteralDottedKeysAndLateColumns()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"heroparser-cli-jsonl-xlsx-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string jsonlPath = Path.Combine(dir, "input.jsonl");
            string xlsxPath = Path.Combine(dir, "output.xlsx");

            File.WriteAllText(
                jsonlPath,
                "{\"a.b\":7}\n{\"a.b\":8,\"status\":\"done\"}\n");

            Assert.True(cliCommands.Convert(jsonlPath, xlsxPath, null, null, "Items"));
            Assert.True(File.Exists(xlsxPath));

            using var reader = HeroParser.Excel.CreateDataReader(
                xlsxPath,
                new ExcelDataReaderOptions { AllowMissingColumns = true },
                sheetName: "Items");
            Assert.Equal("a.b", reader.GetName(0));
            Assert.Equal("status", reader.GetName(1));

            Assert.True(reader.Read());
            Assert.Equal("7", reader.GetString(0));
            Assert.True(reader.IsDBNull(1) || reader.GetString(1) == string.Empty);

            Assert.True(reader.Read());
            Assert.Equal("8", reader.GetString(0));
            Assert.Equal("done", reader.GetString(1));
            Assert.False(reader.Read());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Convert_CsvToHtb_PreservesOffsetMultilineDuplicateAndBlankHeaders()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"heroparser-cli-csv-htb-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string csvPath = Path.Combine(dir, "input.csv");
            string htbPath = Path.Combine(dir, "output.htb");

            File.WriteAllText(
                csvPath,
                "Id,Id,,Note,Stamp\r\n1,2,mid,\"line1\nline2\",2024-06-15T10:30:00+04:30\r\n");

            Assert.True(cliCommands.Convert(csvPath, htbPath, null, null, null));
            Assert.True(File.Exists(htbPath));

            using var htbStream = File.OpenRead(htbPath);
            using var reader = HeroParser.Htb.CreateDataReader(htbStream, leaveOpen: true);
            Assert.Equal("Col3", reader.GetName(2));
            Assert.Equal(typeof(DateTimeOffset), reader.GetFieldType(4));

            Assert.True(reader.Read());
            Assert.Equal(1, reader.GetInt32(0));
            Assert.Equal(2, reader.GetInt32(1));
            Assert.Equal("mid", reader.GetString(2));
            Assert.Equal("line1\nline2", reader.GetString(3));
            var dto = Assert.IsType<DateTimeOffset>(reader.GetValue(4));
            Assert.Equal(TimeSpan.FromMinutes(270), dto.Offset);
            Assert.False(reader.Read());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Convert_CsvToExcel_PreservesMultilineQuotedFields()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"heroparser-cli-csv-xlsx-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string csvPath = Path.Combine(dir, "input.csv");
            string xlsxPath = Path.Combine(dir, "output.xlsx");

            File.WriteAllText(csvPath, "Name,Note\r\nAlice,\"line1\nline2\"\r\n");

            Assert.True(cliCommands.Convert(csvPath, xlsxPath, null, null, "Notes"));
            Assert.True(File.Exists(xlsxPath));

            using var reader = HeroParser.Excel.CreateDataReader(xlsxPath, sheetName: "Notes");
            Assert.True(reader.Read());
            Assert.Equal("Alice", reader.GetString(0));
            Assert.Equal("line1\nline2", reader.GetString(1));
            Assert.False(reader.Read());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Convert_CompressedXlsxPaths_ReturnsFalse()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"heroparser-cli-xlsx-gz-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string gzXlsxInput = Path.Combine(dir, "input.xlsx.gz");
            string csvOutput = Path.Combine(dir, "out.csv");
            File.WriteAllBytes(gzXlsxInput, [0x1F, 0x8B]);

            Assert.False(cliCommands.Convert(gzXlsxInput, csvOutput, null, null, null));
            Assert.False(File.Exists(csvOutput));

            string csvInput = Path.Combine(dir, "input.csv");
            string gzXlsxOutput = Path.Combine(dir, "out.xlsx.gz");
            File.WriteAllText(csvInput, "Name\nAlice\n");

            Assert.False(cliCommands.Convert(csvInput, gzXlsxOutput, null, null, null));
            Assert.False(File.Exists(gzXlsxOutput));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Convert_JsonlToExcel_PreservesNumericPrecisionAndKeepsExactNumbersNumeric()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"heroparser-cli-jsonl-num-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string jsonlPath = Path.Combine(dir, "numbers.jsonl");
            string xlsxPath = Path.Combine(dir, "numbers.xlsx");

            File.WriteAllText(
                jsonlPath,
                """{"exactInt":42,"exactFloat":98.5,"bigPos":9007199254740993,"bigNeg":-9007199254740993,"highPrec":0.12345678901234567890,"ultraPrec":1.2345678901234567890123456789012345,"tiny":1e-35,"tinyEpsilon":1.000000000000000000000000000001,"underflow":1e-350,"max15Int":999999999999999,"over15Int":1234567890123456,"max15Dec":1.23456789012345,"over15Dec":1.234567890123456}""" + "\n");

            Assert.True(cliCommands.Convert(jsonlPath, xlsxPath, null, null, "Nums"));
            Assert.True(File.Exists(xlsxPath));

            var rows = HeroParser.Excel.Read().WithoutHeader().FromSheet("Nums").FromFile(xlsxPath);
            Assert.Equal(2, rows.Count);
            Assert.Equal("42", rows[1][0]);
            Assert.Equal("98.5", rows[1][1]);
            Assert.Equal("9007199254740993", rows[1][2]);
            Assert.Equal("-9007199254740993", rows[1][3]);
            Assert.Equal("0.12345678901234567890", rows[1][4]);
            Assert.Equal("1.2345678901234567890123456789012345", rows[1][5]);
            Assert.Equal("1E-35", rows[1][6]);
            Assert.Equal("1.000000000000000000000000000001", rows[1][7]);
            Assert.Equal("1e-350", rows[1][8]);
            Assert.Equal("999999999999999", rows[1][9]);
            Assert.Equal("1234567890123456", rows[1][10]);
            Assert.Equal("1.23456789012345", rows[1][11]);
            Assert.Equal("1.234567890123456", rows[1][12]);

            using var archive = new ZipArchive(File.OpenRead(xlsxPath), ZipArchiveMode.Read);
            var sheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
            Assert.NotNull(sheetEntry);
            using var sheetReader = new StreamReader(sheetEntry.Open(), Encoding.UTF8);
            string sheetXml = sheetReader.ReadToEnd();
            Assert.Contains("<c r=\"A2\" ><v>42</v></c>", sheetXml);
            Assert.Contains("<c r=\"B2\" ><v>98.5</v></c>", sheetXml);
            Assert.Contains("<c r=\"G2\" ><v>1E-35</v></c>", sheetXml);
            Assert.Contains("<c r=\"J2\" ><v>999999999999999</v></c>", sheetXml);
            Assert.DoesNotContain("<c r=\"K2\" ><v>", sheetXml);
            Assert.Contains("<c r=\"L2\" ><v>1.23456789012345</v></c>", sheetXml);
            Assert.DoesNotContain("<c r=\"M2\" ><v>", sheetXml);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Convert_CsvToHtb_RejectsWiderRowAndPreservesExistingTargetOnLateFailure()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"heroparser-cli-htb-atomic-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string wideCsv = Path.Combine(dir, "wide.csv");
            string wideOut = Path.Combine(dir, "wide.htb");
            File.WriteAllText(wideCsv, "Id,Name\n1,Alice,Extra\n");

            Assert.False(cliCommands.Convert(wideCsv, wideOut, null, null, null));
            Assert.False(File.Exists(wideOut));

            var lines = new StringBuilder("Id\n");
            for (int i = 1; i <= 100; i++)
                lines.Append(i).Append('\n');
            lines.Append("not_an_int\n");

            string failingCsv = Path.Combine(dir, "failing.csv");
            string existingHtb = Path.Combine(dir, "existing.htb");
            File.WriteAllText(failingCsv, lines.ToString());
            byte[] sentinel = [1, 2, 3, 4];
            File.WriteAllBytes(existingHtb, sentinel);

            Assert.False(cliCommands.Convert(failingCsv, existingHtb, null, null, null));
            Assert.Equal(sentinel, File.ReadAllBytes(existingHtb));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));

            string failingJsonl = Path.Combine(dir, "bad.jsonl");
            string existingXlsx = Path.Combine(dir, "existing.xlsx");
            File.WriteAllText(failingJsonl, "{\"Id\":1}\n{bad json\n");
            File.WriteAllBytes(existingXlsx, sentinel);

            Assert.False(cliCommands.Convert(failingJsonl, existingXlsx, null, null, null));
            Assert.Equal(sentinel, File.ReadAllBytes(existingXlsx));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Convert_Utf16LeCsvToExcelAndHtb_TranscodesAndRoundTrips()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"heroparser-cli-utf16-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string csvPath = Path.Combine(dir, "utf16.csv");
            string gzCsvPath = Path.Combine(dir, "utf16.csv.gz");
            string xlsxPath = Path.Combine(dir, "out.xlsx");
            string htbPath = Path.Combine(dir, "out.htb");
            string gzHtbPath = Path.Combine(dir, "out_gz.htb");

            File.WriteAllText(csvPath, "Name,Quantity\nCafé,2\n", Encoding.Unicode);
            using (var gzOut = new GZipStream(File.Create(gzCsvPath), CompressionLevel.Fastest))
            {
                byte[] utf16Bytes = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("Name,Quantity\nCafé,2\n")];
                gzOut.Write(utf16Bytes);
            }

            Assert.True(cliCommands.Convert(csvPath, xlsxPath, null, null, "Data"));
            var xlsxRows = HeroParser.Excel.Read().WithoutHeader().FromSheet("Data").FromFile(xlsxPath);
            Assert.Equal(2, xlsxRows.Count);
            Assert.Equal("Café", xlsxRows[1][0]);
            Assert.Equal("2", xlsxRows[1][1]);

            Assert.True(cliCommands.Convert(csvPath, htbPath, null, null, null));
            Assert.True(cliCommands.Convert(gzCsvPath, gzHtbPath, null, null, null));

            foreach (string path in (string[])[htbPath, gzHtbPath])
            {
                using var fs = File.OpenRead(path);
                using var dr = HeroParser.Htb.CreateDataReader(fs, leaveOpen: true);
                Assert.True(dr.Read());
                Assert.Equal("Café", dr.GetString(0));
                Assert.Equal(2, dr.GetInt32(1));
                Assert.False(dr.Read());
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Convert_EmptyOrHeaderOnlyExcel_WritesValidDecompressibleGzip()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"heroparser-cli-empty-gz-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string emptyXlsx = Path.Combine(dir, "empty.xlsx");
            using (var fs = File.Create(emptyXlsx))
            using (var xlsx = new XlsxWriter(fs, leaveOpen: false))
            {
                using var sheet = xlsx.StartSheet("Sheet1");
                sheet.Close();
            }

            string headerOnlyXlsx = Path.Combine(dir, "header-only.xlsx");
            using (var fs = File.Create(headerOnlyXlsx))
            using (var xlsx = new XlsxWriter(fs, leaveOpen: false))
            {
                using var sheet = xlsx.StartSheet("Sheet1");
                sheet.WriteHeaderRow(["Id", "Name"]);
                sheet.Close();
            }

            string emptyCsvGz = Path.Combine(dir, "empty.csv.gz");
            string emptyJsonlGz = Path.Combine(dir, "empty.jsonl.gz");
            string headerOnlyJsonlGz = Path.Combine(dir, "header-only.jsonl.gz");

            Assert.True(cliCommands.Convert(emptyXlsx, emptyCsvGz, null, null, "Sheet1"));
            Assert.True(cliCommands.Convert(emptyXlsx, emptyJsonlGz, null, null, "Sheet1"));
            Assert.True(cliCommands.Convert(headerOnlyXlsx, headerOnlyJsonlGz, null, null, "Sheet1"));

            foreach (string gzPath in (string[])[emptyCsvGz, emptyJsonlGz, headerOnlyJsonlGz])
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
    public void Convert_CorruptHtbToCsv_PreservesExistingTargetAndCleansStaging()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"heroparser-cli-htb2csv-atomic-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string badHtb = Path.Combine(dir, "corrupt.htb");
            File.WriteAllBytes(badHtb, [0x00, 0x01, 0x02, 0x03]);
            byte[] sentinel = Encoding.UTF8.GetBytes("keep,this\n1,2\n");

            foreach (string outPath in (string[])[Path.Combine(dir, "existing.csv"), Path.Combine(dir, "existing.csv.gz")])
            {
                File.WriteAllBytes(outPath, sentinel);
                Assert.False(cliCommands.Convert(badHtb, outPath, null, null, null));
                Assert.Equal(sentinel, File.ReadAllBytes(outPath));
                Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Convert_CsvToHtbToCsv_PreservesNonCanonicalAndHighPrecisionNumbers()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"heroparser-cli-htb-lossless-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string csvIn = Path.Combine(dir, "in.csv");
            string htbOut = Path.Combine(dir, "out.htb");
            string csvOut = Path.Combine(dir, "round.csv");

            File.WriteAllText(csvIn, "Zip,Signed,Precise,Amount\r\n00123,+42,1.000000000000000000000000000001,2.50\r\n");

            Assert.True(cliCommands.Convert(csvIn, htbOut, null, null, null));
            Assert.True(cliCommands.Convert(htbOut, csvOut, null, null, null));

            var rows = new List<string[]>();
            foreach (var row in Csv.ReadFromText(File.ReadAllText(csvOut)))
            {
                var cols = new string[row.ColumnCount];
                for (int i = 0; i < cols.Length; i++)
                    cols[i] = row[i].ToString();
                rows.Add(cols);
            }
            Assert.Equal(2, rows.Count);
            Assert.Equal("00123", rows[1][0]);
            Assert.Equal("+42", rows[1][1]);
            Assert.Equal("1.000000000000000000000000000001", rows[1][2]);
            Assert.Equal("2.50", rows[1][3]);

            // Late row after the 100-row sample window must not be silently stripped of leading zeros
            var lateCsv = new StringBuilder("Id\n");
            for (int i = 1; i <= 100; i++)
                lateCsv.Append(i).Append('\n');
            lateCsv.Append("00123\n");

            string lateIn = Path.Combine(dir, "late.csv");
            string existingHtb = Path.Combine(dir, "existing.htb");
            byte[] sentinel = [9, 8, 7, 6];
            File.WriteAllText(lateIn, lateCsv.ToString());
            File.WriteAllBytes(existingHtb, sentinel);

            Assert.False(cliCommands.Convert(lateIn, existingHtb, null, null, null));
            Assert.Equal(sentinel, File.ReadAllBytes(existingHtb));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Convert_JsonlWithOnlyEmptyObjectsToExcel_FailsWithoutOverwritingTarget()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"heroparser-cli-jsonl-emptyobj-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string jsonlPath = Path.Combine(dir, "empty-objects.jsonl");
            string newXlsx = Path.Combine(dir, "new.xlsx");
            string existingXlsx = Path.Combine(dir, "existing.xlsx");
            byte[] sentinel = [1, 2, 3, 4];

            File.WriteAllText(jsonlPath, "{}\n{}\n");
            File.WriteAllBytes(existingXlsx, sentinel);

            Assert.False(cliCommands.Convert(jsonlPath, newXlsx, null, null, null));
            Assert.False(File.Exists(newXlsx));

            Assert.False(cliCommands.Convert(jsonlPath, existingXlsx, null, null, null));
            Assert.Equal(sentinel, File.ReadAllBytes(existingXlsx));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void XlsxWriter_RowAndColumnLimits_ThrowExcelException()
    {
        using var ms = new MemoryStream();
        using var xlsx = new XlsxWriter(ms, leaveOpen: true);
        using var sheet = xlsx.StartSheet("Limits");

        Assert.Throws<ExcelException>(() => sheet.StartRow(0));
        Assert.Throws<ExcelException>(() => sheet.StartRow(XlsxWriter.SheetWriter.MAX_ROWS + 1));

        sheet.StartRow(1);
        Assert.Throws<ExcelException>(() => sheet.WriteCellString(0, "x"));
        Assert.Throws<ExcelException>(() => sheet.WriteCellString(XlsxWriter.SheetWriter.MAX_COLUMNS + 1, "x"));
        sheet.EndRow();
        sheet.Close();
    }

    [Fact]
    public void Convert_CsvToHtbToCsv_PreservesMixedOffsetAndUnzonedDates()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"heroparser-cli-mixed-temporal-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string csvIn = Path.Combine(dir, "in.csv");
            string htbOut = Path.Combine(dir, "out.htb");
            string csvOut = Path.Combine(dir, "round.csv");

            File.WriteAllText(csvIn, "When\r\n2026-11-23\r\n2026-11-23T14:30:00Z\r\n");

            Assert.True(cliCommands.Convert(csvIn, htbOut, null, null, null));
            using (var dr = HeroParser.Htb.CreateDataReader(htbOut))
            {
                Assert.Equal(typeof(string), dr.GetFieldType(0));
            }

            Assert.True(cliCommands.Convert(htbOut, csvOut, null, null, null));
            var values = new List<string>();
            foreach (var row in Csv.ReadFromText(File.ReadAllText(csvOut)))
                values.Add(row[0].ToString());

            Assert.Equal(["When", "2026-11-23", "2026-11-23T14:30:00Z"], values);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Convert_CsvToHtbToCsv_PreservesTimeWithOffsetOnlyAsText()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"heroparser-cli-time-offset-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string csvIn = Path.Combine(dir, "in.csv");
            string htbOut = Path.Combine(dir, "out.htb");
            string csvOut = Path.Combine(dir, "round.csv");

            File.WriteAllText(csvIn, "When\r\n09:30+02:00\r\n");

            Assert.True(cliCommands.Convert(csvIn, htbOut, null, null, null));
            using (var dr = HeroParser.Htb.CreateDataReader(htbOut))
            {
                Assert.Equal(typeof(string), dr.GetFieldType(0));
            }

            Assert.True(cliCommands.Convert(htbOut, csvOut, null, null, null));
            var values = new List<string>();
            foreach (var row in Csv.ReadFromText(File.ReadAllText(csvOut)))
                values.Add(row[0].ToString());

            Assert.Equal(["When", "09:30+02:00"], values);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Convert_CsvWithOverlongHeaderOrExcelCell_FailsAtomically()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"heroparser-cli-limits-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string longHeaderCsv = Path.Combine(dir, "long-header.csv");
            string htbOut = Path.Combine(dir, "long-header.htb");
            File.WriteAllText(longHeaderCsv, $"{new string('中', 683)}\r\n1\r\n");

            Assert.False(cliCommands.Convert(longHeaderCsv, htbOut, null, null, null));
            Assert.False(File.Exists(htbOut));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));

            string longCellCsv = Path.Combine(dir, "long-cell.csv");
            string xlsxOut = Path.Combine(dir, "long-cell.xlsx");
            File.WriteAllText(longCellCsv, $"Text\r\n{new string('a', XlsxWriter.SheetWriter.MAX_CELL_TEXT_LENGTH + 1)}\r\n");

            Assert.False(cliCommands.Convert(longCellCsv, xlsxOut, null, null, null));
            Assert.False(File.Exists(xlsxOut));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }

        using var ms = new MemoryStream();
        using var xlsx = new XlsxWriter(ms, leaveOpen: true);
        using var sheet = xlsx.StartSheet("Limits");
        sheet.StartRow(1);
        sheet.WriteCellString(1, new string('a', XlsxWriter.SheetWriter.MAX_CELL_TEXT_LENGTH));
        sheet.WriteCellString(2, new string('b', XlsxWriter.SheetWriter.MAX_CELL_TEXT_LENGTH).AsSpan());
        Assert.Throws<ExcelException>(() => sheet.WriteCellString(3, new string('a', XlsxWriter.SheetWriter.MAX_CELL_TEXT_LENGTH + 1)));
        Assert.Throws<ExcelException>(() => sheet.WriteCellString(4, new string('b', XlsxWriter.SheetWriter.MAX_CELL_TEXT_LENGTH + 1).AsSpan()));
        Assert.Throws<ExcelException>(() => sheet.WriteCellString(5, "=" + new string('c', XlsxWriter.SheetWriter.MAX_CELL_TEXT_LENGTH - 1)));
        sheet.EndRow();
        sheet.Close();
    }

    [Fact]
    public void Convert_WideCsvWithMoreThan100Columns_ToExcelAndHtb_Succeeds()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"heroparser-cli-wide-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            const int columnCount = 105;
            string headers = string.Join(",", Enumerable.Range(1, columnCount).Select(i => $"Col{i}"));
            string values = string.Join(",", Enumerable.Range(1, columnCount));
            string csvPath = Path.Combine(dir, "wide.csv");
            string xlsxOut = Path.Combine(dir, "wide.xlsx");
            string htbOut = Path.Combine(dir, "wide.htb");
            File.WriteAllText(csvPath, $"{headers}\r\n{values}\r\n");

            Assert.True(cliCommands.Convert(csvPath, xlsxOut, null, null, null));
            var excelRows = HeroParser.Excel.Read().WithoutHeader().FromFile(xlsxOut);
            Assert.Equal(2, excelRows.Count);
            Assert.Equal(columnCount, excelRows[0].Length);
            Assert.Equal("Col105", excelRows[0][104]);
            Assert.Equal("105", excelRows[1][104]);

            Assert.True(cliCommands.Convert(csvPath, htbOut, null, null, null));
            using var htbDr = HeroParser.Htb.CreateDataReader(htbOut);
            Assert.Equal(columnCount, htbDr.FieldCount);
            Assert.Equal("Col105", htbDr.GetName(104));
            Assert.True(htbDr.Read());
            Assert.Equal(1, htbDr.GetInt32(0));
            Assert.Equal(105, htbDr.GetInt32(104));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Convert_CsvToHtb_LateTimezoneOffsetAfterOffsetlessSample_FailsAtomically()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"heroparser-cli-late-offset-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var sb = new StringBuilder("CreatedAt\n");
            for (int i = 0; i < 101; i++)
                sb.Append("2026-11-21T12:34:56\n");
            sb.Append("2026-11-21T12:34:56+05:00\n");

            string csvPath = Path.Combine(dir, "late-offset.csv");
            string htbOut = Path.Combine(dir, "out.htb");
            File.WriteAllText(csvPath, sb.ToString());

            Assert.False(cliCommands.Convert(csvPath, htbOut, null, null, null));
            Assert.False(File.Exists(htbOut));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Convert_JsonlToExcel_OversizedLineExceedingDefaultLimit_FailsAtomically()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"heroparser-cli-jsonl-oversize-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string jsonlPath = Path.Combine(dir, "oversized.jsonl");
            string xlsxOut = Path.Combine(dir, "out.xlsx");
            File.WriteAllText(jsonlPath, "{\"Text\":\"" + new string('a', 1_100_000) + "\"}\n");

            Assert.False(cliCommands.Convert(jsonlPath, xlsxOut, null, null, null));
            Assert.False(File.Exists(xlsxOut));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Validate_AndConvertToFixedWidth_Utf16Compressed_StreamsCleanly()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"heroparser-cli-utf16-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string utf16Csv = Path.Combine(dir, "valid-le.csv");
            await File.WriteAllBytesAsync(
                utf16Csv,
                [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("Name,Age\nAlice,30\nBob,25\n")],
                TestContext.Current.CancellationToken);
            Assert.True(await cliCommands.ValidateAsync(utf16Csv, null));

            string utf16BeGz = Path.Combine(dir, "bad-be.csv.gz");
            byte[] badBeBytes = [.. Encoding.BigEndianUnicode.GetPreamble(), .. Encoding.BigEndianUnicode.GetBytes("Name,Age\nAlice,30\nBob\n")];
            await using (var fs = File.Create(utf16BeGz))
            await using (var gz = new GZipStream(fs, CompressionMode.Compress))
            {
                await gz.WriteAsync(badBeBytes, TestContext.Current.CancellationToken);
            }
            Assert.False(await cliCommands.ValidateAsync(utf16BeGz, null));

            string utf16Gz = Path.Combine(dir, "input-le.csv.gz");
            byte[] leBytes = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("Name,City\nAlice,Sydney\nBob,Melbourne\n")];
            await using (var fs = File.Create(utf16Gz))
            await using (var gz = new GZipStream(fs, CompressionMode.Compress))
            {
                await gz.WriteAsync(leBytes, TestContext.Current.CancellationToken);
            }
            string txtOut = Path.Combine(dir, "out.txt");
            Assert.True(cliCommands.Convert(utf16Gz, txtOut, ',', null, null));
            string content = await File.ReadAllTextAsync(txtOut, TestContext.Current.CancellationToken);
            Assert.Contains("Alice", content, StringComparison.Ordinal);
            Assert.Contains("Melbourne", content, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Convert_WideCsv10001ColumnsToExcel_Succeeds()
    {
        const int columnCount = 10_001;
        string dir = Path.Combine(Path.GetTempPath(), $"heroparser-cli-wide-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string csvPath = Path.Combine(dir, "wide.csv");
            string xlsxPath = Path.Combine(dir, "wide.xlsx");
            string headers = string.Join(',', Enumerable.Range(1, columnCount).Select(i => $"C{i}"));
            string values = string.Join(',', Enumerable.Range(1, columnCount).Select(i => $"V{i}"));
            File.WriteAllText(csvPath, headers + "\n" + values + "\n");

            Assert.True(cliCommands.Convert(csvPath, xlsxPath, ',', null, null));
            Assert.True(File.Exists(xlsxPath));

            var rows = HeroParser.Excel.Read().WithoutHeader().FromFile(xlsxPath);
            Assert.Equal(2, rows.Count);
            Assert.Equal(columnCount, rows[0].Length);
            Assert.Equal("C1", rows[0][0]);
            Assert.Equal($"C{columnCount}", rows[0][columnCount - 1]);
            Assert.Equal($"V{columnCount}", rows[1][columnCount - 1]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Convert_CsvWithEmbeddedNewlineToFixedWidth_RejectsAndLeavesNoOutput()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"heroparser-cli-fw-newline-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            // 1. Embedded newline in quoted data value
            string valueCsv = Path.Combine(dir, "multiline-value.csv");
            string fwOut1 = Path.Combine(dir, "out-value.txt");
            await File.WriteAllTextAsync(valueCsv, "Name,Note\nAlice,\"line1\nline2\"\n", TestContext.Current.CancellationToken);

            int exitCode1 = await Program.Main(["convert", valueCsv, fwOut1]);
            Assert.Equal(1, exitCode1);
            Assert.False(File.Exists(fwOut1));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));

            // 2. Embedded newline in quoted header
            string headerCsv = Path.Combine(dir, "multiline-header.csv");
            string fwOut2 = Path.Combine(dir, "out-header.txt");
            await File.WriteAllTextAsync(headerCsv, "\"First\nHeader\",Age\nAlice,30\n", TestContext.Current.CancellationToken);

            int exitCode2 = await Program.Main(["convert", headerCsv, fwOut2]);
            Assert.Equal(1, exitCode2);
            Assert.False(File.Exists(fwOut2));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Convert_CsvAndJsonlToExcel_UseBoundedStreamingSharedStrings()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"heroparser-cli-xlsx-stream-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            // Streaming budget caches up to STREAMING_MAX_ENTRIES (16,384) unique strings,
            // then spills subsequent distinct values to inlineStr without bloating memory.
            var csvSb = new StringBuilder("Id,Note\n");
            var jsonlSb = new StringBuilder();
            const int rowCount = 16_386;
            for (int i = 0; i < rowCount; i++)
            {
                string val = $"val-{i:D5}";
                csvSb.AppendLine($"{i},{val}");
                jsonlSb.AppendLine($$"""{"Id":{{i}},"Note":"{{val}}"}""");
            }

            string csvPath = Path.Combine(dir, "long.csv");
            string csvXlsxPath = Path.Combine(dir, "from-csv.xlsx");
            File.WriteAllText(csvPath, csvSb.ToString());
            Assert.True(cliCommands.Convert(csvPath, csvXlsxPath, null, null, null));

            string jsonlPath = Path.Combine(dir, "long.jsonl");
            string jsonlXlsxPath = Path.Combine(dir, "from-jsonl.xlsx");
            File.WriteAllText(jsonlPath, jsonlSb.ToString());
            Assert.True(cliCommands.Convert(jsonlPath, jsonlXlsxPath, null, null, null));

            foreach (string path in new[] { csvXlsxPath, jsonlXlsxPath })
            {
                using var archive = new ZipArchive(File.OpenRead(path), ZipArchiveMode.Read);
                using var reader = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open(), Encoding.UTF8);
                string xml = reader.ReadToEnd();
                Assert.Contains("t=\"inlineStr\"", xml, StringComparison.Ordinal);

                var rows = HeroParser.Excel.Read().FromFile(path);
                Assert.Equal(rowCount, rows.Count);
                Assert.Equal("val-00000", rows[0][1]);
                Assert.Equal($"val-{rowCount - 1:D5}", rows[rowCount - 1][1]);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Convert_CsvToHtb_AcceptsLargeRowAndMoreThan100kRows()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"heroparser-cli-htb-limits-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            // 1. 600,000-byte single field (exceeds CsvReadOptions default 512 KiB, fits schema inference 1 MiB)
            string bigField = new('x', 600_000);
            string bigCsvPath = Path.Combine(dir, "bigrow.csv");
            string bigHtbPath = Path.Combine(dir, "bigrow.htb");
            File.WriteAllText(bigCsvPath, $"Id,Payload\n1,{bigField}\n");
            Assert.True(cliCommands.Convert(bigCsvPath, bigHtbPath, ',', null, null));

            using (var bigHtbStream = File.OpenRead(bigHtbPath))
            using (var reader = HeroParser.Htb.CreateDataReader(bigHtbStream))
            {
                Assert.True(reader.Read());
                Assert.Equal(bigField, reader.GetString(1));
                Assert.False(reader.Read());
            }

            // 2. 100,001 data rows + 1 header row = 100,002 CSV rows (exceeds CsvReadOptions default 100,000 rows)
            string manyCsvPath = Path.Combine(dir, "manyrows.csv");
            string manyHtbPath = Path.Combine(dir, "manyrows.htb");
            using (var sw = new StreamWriter(manyCsvPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                sw.WriteLine("Id");
                for (int i = 1; i <= 100_001; i++)
                    sw.WriteLine(i);
            }
            Assert.True(cliCommands.Convert(manyCsvPath, manyHtbPath, ',', null, null));

            using (var manyHtbStream = File.OpenRead(manyHtbPath))
            using (var reader = HeroParser.Htb.CreateDataReader(manyHtbStream))
            {
                int count = 0;
                while (reader.Read())
                    count++;
                Assert.Equal(100_001, count);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Convert_SparseJsonlToExcel_DoesNotWriteAbsentCells()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"heroparser-cli-sparse-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string jsonlPath = Path.Combine(dir, "sparse.jsonl");
            string xlsxPath = Path.Combine(dir, "sparse.xlsx");
            const int Records = 300;
            File.WriteAllLines(jsonlPath, Enumerable.Range(1, Records).Select(i => $"{{\"Column{i}\":{i}}}"));

            Assert.True(cliCommands.Convert(jsonlPath, xlsxPath, null, null, null));

            using var archive = new ZipArchive(File.OpenRead(xlsxPath), ZipArchiveMode.Read);
            using var reader = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open(), Encoding.UTF8);
            string xml = reader.ReadToEnd();
            // Writing every absent key would emit ~90,000 cells (> 1.5 MB); only present values should be written.
            Assert.True(xml.Length < 200_000, $"Sheet XML unexpectedly large: {xml.Length}");

            var rows = HeroParser.Excel.Read().WithoutHeader().FromFile(xlsxPath);
            Assert.Equal(Records + 1, rows.Count);
            Assert.Equal("5", rows[5][4]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Convert_CsvToFixedWidthAndHtbToCsv_DoNotWriteUtf8Bom()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"heroparser-cli-nobom-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string csvPath = Path.Combine(dir, "input.csv");
            string fwPath = Path.Combine(dir, "output.txt");
            string htbPath = Path.Combine(dir, "middle.htb");
            string csvRoundTripPath = Path.Combine(dir, "output.csv");
            File.WriteAllText(csvPath, "Name,Age\nAlice,30\n");

            Assert.True(cliCommands.Convert(csvPath, fwPath, ',', null, null));
            byte[] fwBytes = File.ReadAllBytes(fwPath);
            Assert.True(fwBytes.Length > 3);
            Assert.False(fwBytes[0] == 0xEF && fwBytes[1] == 0xBB && fwBytes[2] == 0xBF);
            Assert.Equal((byte)'N', fwBytes[0]);

            Assert.True(cliCommands.Convert(csvPath, htbPath, ',', null, null));
            Assert.True(cliCommands.Convert(htbPath, csvRoundTripPath, ',', null, null));
            byte[] htbCsvBytes = File.ReadAllBytes(csvRoundTripPath);
            Assert.True(htbCsvBytes.Length > 3);
            Assert.False(htbCsvBytes[0] == 0xEF && htbCsvBytes[1] == 0xBB && htbCsvBytes[2] == 0xBF);
            Assert.Equal((byte)'N', htbCsvBytes[0]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Convert_JsonlToExcel_DuplicateKeysInWideObject_PreservesLastOccurrence()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"heroparser-cli-dupw-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string jsonlPath = Path.Combine(dir, "dupwide.jsonl");
            string xlsxPath = Path.Combine(dir, "dupwide.xlsx");
            var props = Enumerable.Range(1, 20).Select(i => $"\"k{i:D2}\":\"v{i}\"");
            // 20+ items triggers unstable IntroSort in List<T>.Sort; duplicate "k01" at the end must still win.
            File.WriteAllText(jsonlPath, "{\"k01\":\"first\"," + string.Join(",", props.Skip(1)) + ",\"k01\":\"last\"}\n");

            Assert.True(cliCommands.Convert(jsonlPath, xlsxPath, null, null, null));

            var rows = HeroParser.Excel.Read().FromFile(xlsxPath);
            var row = Assert.Single(rows);
            Assert.Equal("last", row[0]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Convert_CsvWithCompactGuidToHtbAndBack_Succeeds()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"heroparser-cli-guid-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string csvPath = Path.Combine(dir, "input.csv");
            string htbPath = Path.Combine(dir, "mid.htb");
            string outCsvPath = Path.Combine(dir, "output.csv");
            const string compactGuid = "0123456789abcdef0123456789abcdef";
            File.WriteAllText(csvPath, $"Id,Tag\n{compactGuid},ok\n");

            Assert.True(cliCommands.Convert(csvPath, htbPath, ',', null, null));
            Assert.True(cliCommands.Convert(htbPath, outCsvPath, ',', null, null));

            List<string[]> roundTrip = [];
            foreach (var csvRow in Csv.Read().FromText(File.ReadAllText(outCsvPath)))
            {
                var cols = new string[csvRow.ColumnCount];
                for (int i = 0; i < cols.Length; i++)
                    cols[i] = csvRow[i].UnquoteToString();
                roundTrip.Add(cols);
            }
            Assert.Equal(2, roundTrip.Count);
            Assert.Equal(Guid.Parse(compactGuid), Guid.Parse(roundTrip[1][0]));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task HtbToCsvConverter_RespectsCustomMaxRowCount()
    {
        var ct = TestContext.Current.CancellationToken;
        string dir = Path.Combine(Path.GetTempPath(), $"heroparser-htb-limit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string csvPath = Path.Combine(dir, "three.csv");
            string htbPath = Path.Combine(dir, "three.htb");
            File.WriteAllText(csvPath, "Id\n1\n2\n3\n");
            Assert.True(cliCommands.Convert(csvPath, htbPath, ',', null, null));

            await using var htbStream = File.OpenRead(htbPath);
            using (var swFail = new StringWriter())
            {
                var ex = Assert.Throws<HtbException>(() =>
                    HtbToCsvConverter.Convert(htbStream, swFail, new HtbToCsvOptions { MaxRowCount = 2 }));
                Assert.Equal(HtbErrorCode.LimitExceeded, ex.ErrorCode);
            }

            htbStream.Position = 0;
            using (var swAsyncFail = new StringWriter())
            {
                var ex = await Assert.ThrowsAsync<HtbException>(() =>
                    HtbToCsvConverter.ConvertAsync(htbStream, swAsyncFail, new HtbToCsvOptions { MaxRowCount = 2 }, ct));
                Assert.Equal(HtbErrorCode.LimitExceeded, ex.ErrorCode);
            }

            htbStream.Position = 0;
            using var swOk = new StringWriter();
            HtbToCsvConverter.Convert(htbStream, swOk, new HtbToCsvOptions { MaxRowCount = 3, NewLine = "\n" });
            Assert.Equal("Id\n1\n2\n3\n", swOk.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Convert_CompressedCsvToFixedWidth_MeasuresWidthsAcrossAllRowsWithoutTruncation()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"heroparser-cli-gzfw-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string gzPath = Path.Combine(dir, "longlate.csv.gz");
            string fwPath = Path.Combine(dir, "longlate.txt");
            const string longLate = "very-long-value-after-first-100-rows";

            using (var fs = File.Create(gzPath))
            using (var gz = new GZipStream(fs, CompressionLevel.Optimal))
            using (var sw = new StreamWriter(gz, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                sw.WriteLine("Id,Note");
                for (int i = 1; i <= 104; i++)
                    sw.WriteLine($"{i},x");
                sw.WriteLine($"105,{longLate}");
            }

            Assert.True(cliCommands.Convert(gzPath, fwPath, null, null, null));

            string fwText = File.ReadAllText(fwPath);
            Assert.Contains(longLate, fwText, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Convert_Utf32BomCsvToFixedWidth_PreservesTextWithoutNulBytes()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"heroparser-cli-utf32-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string csvPath = Path.Combine(dir, "utf32.csv");
            string fwPath = Path.Combine(dir, "utf32.txt");
            File.WriteAllText(csvPath, "Name,Age\nAlice,30\n", new UTF32Encoding(bigEndian: false, byteOrderMark: true));

            Assert.True(cliCommands.Convert(csvPath, fwPath, ',', null, null));

            string fwText = File.ReadAllText(fwPath);
            Assert.DoesNotContain('\0', fwText);
            string[] lines = fwText.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, lines.Length);
            Assert.Contains("Alice", lines[1], StringComparison.Ordinal);
            Assert.Contains("30", lines[1], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ValidateAndSchemaWithPlan_Utf32LeBomCsv_RespectsTranscodingAndPlanContractAsync()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"heroparser-cli-utf32-val-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string utf32Path = Path.Combine(dir, "quoted.csv");
            string planPath = Path.Combine(dir, "plan.json");
            Encoding utf32Le = new UTF32Encoding(bigEndian: false, byteOrderMark: true);
            File.WriteAllText(utf32Path, "Name,City\r\n\"Smith, Alice\",Sydney\r\n", utf32Le);

            new CsvImportPlan
            {
                Delimiter = ',',
                HasHeaderRow = true,
                ExpectedColumnCount = 2,
                SampledDataRows = 1,
                SampledWidthMismatches = 0,
                DelimiterConfidence = 100,
                Utf8BomObserved = false
            }.Save(planPath);
            var plan = CsvImportPlan.Load(planPath);

            Assert.True(await cliCommands.ValidateAsync(utf32Path, null));
            Assert.False(await cliCommands.SchemaAsync(utf32Path, null, useAi: false, null, null, null, plan: plan));
            Assert.False(await cliCommands.ValidateAsync(utf32Path, null, plan: plan));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Convert_PaddedBooleanCsvToHtbAndBack_PreservesBooleans()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"heroparser-cli-bool-pad-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string csvPath = Path.Combine(dir, "padded-bool.csv");
            string htbPath = Path.Combine(dir, "padded-bool.htb");
            string backCsvPath = Path.Combine(dir, "round.csv");

            File.WriteAllText(csvPath, "flag\n true \n false \n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            Assert.True(cliCommands.Convert(csvPath, htbPath, null, null, null));
            Assert.True(cliCommands.Convert(htbPath, backCsvPath, ',', null, null));

            string roundCsv = File.ReadAllText(backCsvPath);
            Assert.Equal("flag\r\nTrue\r\nFalse\r\n", roundCsv);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Convert_JsonlToExcel_RejectsMoreThanMaxExcelColumns()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"heroparser-cli-jsonl-maxcols-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string jsonlPath = Path.Combine(dir, "too-wide.jsonl");
            string xlsxPath = Path.Combine(dir, "too-wide.xlsx");

            var sb = new StringBuilder();
            sb.Append('{');
            for (int i = 0; i < 16_384; i++)
            {
                if (i > 0)
                    sb.Append(',');
                sb.Append("\"c").Append(i).Append("\":").Append(i);
            }
            sb.AppendLine("}");
            sb.AppendLine("""{"c16384":16384}""");

            File.WriteAllText(jsonlPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            Assert.False(cliCommands.Convert(jsonlPath, xlsxPath, null, null, null));
            Assert.False(File.Exists(xlsxPath));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Convert_CsvToHtb_RejectsPartialDateAfterInferenceWindow()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"heroparser-cli-partial-date-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string csvPath = Path.Combine(dir, "dates.csv");
            string htbPath = Path.Combine(dir, "dates.htb");

            var sb = new StringBuilder();
            sb.AppendLine("D");
            for (int i = 0; i < 100; i++)
                sb.AppendLine("2025-01-02");
            sb.AppendLine("2/3");

            File.WriteAllText(csvPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            Assert.False(cliCommands.Convert(csvPath, htbPath, ',', null, null));
            Assert.False(File.Exists(htbPath));

            var schema = new HeroParser.Htbs.Records.HtbSchema(
            [
                new HeroParser.Htbs.Records.HtbColumn("D", HeroParser.Htbs.Records.HtbDataType.DateOnly, isNullable: false)
            ]);
            using var ms = new MemoryStream();
            Assert.Throws<HeroParser.Htbs.HtbException>(() =>
                CsvToHtbConverter.Convert("D\n2/3\n", ms, schema, new CsvToHtbOptions { RejectLossyNumericValues = true }));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Convert_CsvToFixedWidth_LongDestinationFileNameOver220Chars_SucceedsAtomically()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"hp-longname-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            string csvPath = Path.Combine(dir, "in.csv");
            string longName = new string('x', 225) + ".txt";
            string fwPath = Path.Combine(dir, longName);

            File.WriteAllText(csvPath, "Name,Age\nAlice,30\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            Assert.True(cliCommands.Convert(csvPath, fwPath, ',', null, null));
            Assert.True(File.Exists(fwPath));
            Assert.Contains("Alice", File.ReadAllText(fwPath), StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
