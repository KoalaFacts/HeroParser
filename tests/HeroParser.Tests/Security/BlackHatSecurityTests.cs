using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;
using HeroParser.Htbs;
using HeroParser.Htbs.Reading;
using HeroParser.Htbs.Writing;
using HeroParser.Htbs.Records;
using HeroParser.Conversion;
using HeroParser.SeparatedValues.Core;

namespace HeroParser.Tests.Security;

/// <summary>
/// Hardened security verification suite checking against stackalloc-exhaustion DoS crash exploits.
/// </summary>
[Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
public class BlackHatSecurityTests
{
    private class DummyRecord
    {
        public int Id { get; set; }
    }

    [Fact]
    public void Test_HtbRecordReader_PreventsStackOverflow()
    {
        // 1. Arrange a 2000-column schema (triggers maskLen = 250, exceeding stackalloc threshold 128)
        var columns = Enumerable.Range(0, 2000)
            .Select(i => new HtbColumn($"Col{i}", HtbDataType.Int32, isNullable: true))
            .ToList();
        var schema = new HtbSchema(columns);

        using var ms = new MemoryStream();
        using (var writer = new HtbStreamWriter(ms, leaveOpen: true))
        {
            writer.WriteHeader(schema);
            writer.IncrementRecordCount();

            // Mask length is 250 bytes
            var mask = new byte[250];
            // Mark all columns as null
            for (int i = 0; i < mask.Length; i++)
            {
                mask[i] = 0xFF;
            }
            writer.WriteMask(mask);
            writer.Flush();
        }

        ms.Position = 0;

        // 2. Act & Assert: Read the wide schema HTB stream.
        // It must NOT crash the process with StackOverflowException, and should gracefully fall back to secure heap memory.
        var reader = new HtbRecordReader<DummyRecord>(ms);
        bool success = reader.ReadNext(out var record);

        Assert.True(success);
        Assert.NotNull(record);
        Assert.Equal(1, reader.RecordsRead);
    }

    [Fact]
    public void Test_CsvToHtbConverter_HandlesExtremeColumns()
    {
        // 1. Arrange a 1500-column CSV input (maskLen = 188, exceeding 128 bytes threshold)
        int colCount = 1500;
        var columns = Enumerable.Range(0, colCount)
            .Select(i => new HtbColumn($"Col{i}", HtbDataType.Int32, isNullable: true))
            .ToList();
        var schema = new HtbSchema(columns);

        var csvBuilder = new StringBuilder();
        // Headers
        csvBuilder.AppendLine(string.Join(",", Enumerable.Range(0, colCount).Select(i => $"Col{i}")));
        // Data row (all empty/null values)
        csvBuilder.AppendLine(string.Join(",", Enumerable.Repeat("", colCount)));

        string csvData = csvBuilder.ToString();
        using var htbStream = new MemoryStream();

        // 2. Act
        // Must complete successfully without throwing StackOverflowException or out-of-bounds error.
        CsvToHtbConverter.Convert(csvData, htbStream, schema, new CsvToHtbOptions { HasHeaderRow = true });

        // 3. Assert
        Assert.True(htbStream.Length > 0);
    }

    [Fact]
    public void Test_HtbToCsvConverter_HandlesExtremeColumns()
    {
        // 1. Arrange a 1500-column HTB stream (maskLen = 188, exceeding 128 bytes threshold)
        int colCount = 1500;
        var columns = Enumerable.Range(0, colCount)
            .Select(i => new HtbColumn($"Col{i}", HtbDataType.Int32, isNullable: true))
            .ToList();
        var schema = new HtbSchema(columns);

        using var htbStream = new MemoryStream();
        using (var writer = new HtbStreamWriter(htbStream, leaveOpen: true))
        {
            writer.WriteHeader(schema);
            writer.IncrementRecordCount();

            var mask = new byte[188];
            // Mark all columns as null
            for (int i = 0; i < mask.Length; i++)
            {
                mask[i] = 0xFF;
            }
            writer.WriteMask(mask);
            writer.Flush();
        }

        htbStream.Position = 0;
        using var csvWriter = new StringWriter();

        // 2. Act
        // Must complete successfully without throwing StackOverflowException or out-of-bounds error.
        HtbToCsvConverter.Convert(htbStream, csvWriter, new HtbToCsvOptions { IncludeHeaderRow = true });

        // 3. Assert
        string csvOutput = csvWriter.ToString();
        Assert.NotEmpty(csvOutput);

        // CSV output should contain the headers and one empty values row
        var lines = csvOutput.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Contains("Col0", lines[0]);
        Assert.Contains("Col1499", lines[0]);
    }

    [Fact]
    public void Test_XlsxXmlSettings_ProhibitsDtdAndResolver()
    {
        var settings = HeroParser.Excels.Xlsx.XlsxXml.CreateReaderSettings();
        Assert.NotNull(settings);
        Assert.Equal(System.Xml.DtdProcessing.Prohibit, settings.DtdProcessing);
        Assert.True(settings.MaxCharactersInDocument > 0);
        Assert.True(settings.MaxCharactersFromEntities > 0);
    }

    [Fact]
    public void Test_HtbRecordReader_SkipsUnboundColumns()
    {
        var schema = new HtbSchema([
            new HtbColumn("Id", HtbDataType.Int32, isNullable: false),
            new HtbColumn("UnboundStr", HtbDataType.String, isNullable: true),
            new HtbColumn("UnboundArr", HtbDataType.FloatArray, isNullable: true)
        ]);

        using var ms = new MemoryStream();
        using (var writer = new HtbStreamWriter(ms, leaveOpen: true))
        {
            writer.WriteHeader(schema);
            writer.IncrementRecordCount();

            writer.WriteMask([0]);
            writer.WriteInt32(42);
            writer.WriteString(new string('s', 5000));
            writer.WriteFloatArray(new float[2000]);
            writer.Flush();
        }

        ms.Position = 0;

        var reader = new HtbRecordReader<DummyRecord>(ms);
        bool success = reader.ReadNext(out var record);

        Assert.True(success);
        Assert.NotNull(record);
        Assert.Equal(42, record.Id);
    }

    [Fact]
    public void Test_HtbRecordReader_CachesMalformedLengths()
    {
        var schema = new HtbSchema([
            new HtbColumn("Name", HtbDataType.String, isNullable: false)
        ]);

        // Case A: Negative string length
        using (var ms = new MemoryStream())
        {
            using (var writer = new HtbStreamWriter(ms, leaveOpen: true))
            {
                writer.WriteHeader(schema);
                writer.IncrementRecordCount();
                writer.WriteMask([0]);
                writer.WriteInt32(-100);
                writer.Flush();
            }

            ms.Position = 0;
            var reader = new HtbRecordReader<DummyRecord>(ms);
            var ex = Assert.Throws<HtbException>(() => reader.ReadNext(out _));
            Assert.Contains("Invalid string length", ex.Message);
        }

        // Case B: Excessive string length (> 64MB)
        using (var ms = new MemoryStream())
        {
            using (var writer = new HtbStreamWriter(ms, leaveOpen: true))
            {
                writer.WriteHeader(schema);
                writer.IncrementRecordCount();
                writer.WriteMask([0]);
                writer.WriteInt32(70 * 1024 * 1024);
                writer.Flush();
            }

            ms.Position = 0;
            var reader = new HtbRecordReader<DummyRecord>(ms);
            var ex = Assert.Throws<HtbException>(() => reader.ReadNext(out _));
            Assert.Contains("Invalid string length", ex.Message);
        }
    }
}
