using System;
using System.Text;
using System.Collections.Generic;
using System.IO;

namespace HeroParser.ComplianceTests.Utilities
{
    /// <summary>
    /// Specialized test data generator for RFC 4180 compliance and fixed-length format testing.
    /// Generates edge cases, malformed data, and compliance validation scenarios.
    /// </summary>
    public static class ComplianceTestDataGenerator
    {
        /// <summary>
        /// Generates RFC 4180 compliant CSV data for validation testing.
        /// </summary>
        public static string GenerateRfc4180Compliant()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Field1,Field2,Field3");
            sb.AppendLine("value1,value2,value3");
            sb.AppendLine("\"quoted field\",\"field with, comma\",\"field with \"\"quotes\"\"\"");
            sb.AppendLine("\"field with\r\nnewline\",normal,\"trailing comma,\"");
            sb.AppendLine("empty,,fields");
            sb.AppendLine("trailing,fields,");
            return sb.ToString();
        }

        /// <summary>
        /// Generates edge case CSV data for stress testing.
        /// </summary>
        public static string GenerateEdgeCases()
        {
            var sb = new StringBuilder();
            sb.AppendLine("EdgeCase,Description,Data");
            sb.AppendLine("\"Empty quotes\",\"\",\"value\"");
            sb.AppendLine("\"Quote escape\",\"He said \"\"Hello\"\"\",\"test\"");
            sb.AppendLine("\"Newline in field\",\"Line 1\r\nLine 2\",\"value\"");
            sb.AppendLine("\"Unicode\",\"Café ñ 你好 🚀\",\"test\"");
            sb.AppendLine("\"Special chars\",\"Tab:\t Return:\r Newline:\n\",\"end\"");
            return sb.ToString();
        }

        /// <summary>
        /// Generates malformed CSV data for error handling tests.
        /// </summary>
        public static string GenerateMalformed()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Field1,Field2,Field3");
            sb.AppendLine("unclosed,\"quote,field");  // Unclosed quote
            sb.AppendLine("extra\"quote,middle,field");  // Quote in middle
            sb.AppendLine("field1,field2,field3,extra"); // Extra field
            sb.AppendLine("field1,field2"); // Missing field
            return sb.ToString();
        }

        /// <summary>
        /// Generates COBOL copybook test data with PICTURE clauses.
        /// </summary>
        public static string GenerateCobolFixedLength()
        {
            var sb = new StringBuilder();

            // Customer record: ID(8) + Name(20) + Amount(10) + Date(8) = 46 chars
            sb.AppendLine("12345678JOHN DOE            0000123456" + DateTime.Now.ToString("yyyyMMdd"));
            sb.AppendLine("87654321JANE SMITH          0000567890" + DateTime.Now.AddDays(-1).ToString("yyyyMMdd"));
            sb.AppendLine("11111111LONG CUSTOMER NAME  0001000000" + DateTime.Now.AddDays(-2).ToString("yyyyMMdd"));

            return sb.ToString();
        }

        /// <summary>
        /// Generates NACHA format test data for financial compliance.
        /// </summary>
        public static string GenerateNachaFormat()
        {
            var sb = new StringBuilder();

            // File Header Record (94 characters)
            sb.AppendLine("101 121000248 1234567890240923A094101BANK NAME              COMPANY NAME           12345678");

            // Batch Header Record (94 characters)
            sb.AppendLine("5200COMPANY NAME                        1234567890PPDDESCRIPTIO240923   1121000240000001");

            // Entry Detail Record (94 characters)
            sb.AppendLine("6271210002481234567890000001500027JOHN DOE                0121000240000001");

            return sb.ToString();
        }

        /// <summary>
        /// Generates test data with different line ending formats.
        /// </summary>
        public static string GenerateMixedLineEndings()
        {
            var sb = new StringBuilder();
            sb.Append("Field1,Field2,Field3\r\n");    // Windows CRLF
            sb.Append("value1,value2,value3\n");      // Unix LF
            sb.Append("more,data,here\r");            // Mac CR
            sb.Append("final,row,data\r\n");          // Windows CRLF
            return sb.ToString();
        }

        /// <summary>
        /// Generates very large field data for memory testing.
        /// </summary>
        public static string GenerateLargeFields(int fieldSize = 10000)
        {
            var sb = new StringBuilder();
            sb.AppendLine("SmallField,LargeField,AnotherSmall");

            var largeContent = new string('A', fieldSize);
            sb.AppendLine($"small,\"{largeContent}\",end");

            return sb.ToString();
        }

        /// <summary>
        /// Generates CSV with many columns for wide record testing.
        /// </summary>
        public static string GenerateWideRecords(int columnCount = 100)
        {
            var sb = new StringBuilder();

            // Header
            var headers = new List<string>();
            for (int i = 0; i < columnCount; i++)
            {
                headers.Add($"Column{i:D3}");
            }
            sb.AppendLine(string.Join(",", headers));

            // Data rows
            for (int row = 0; row < 5; row++)
            {
                var values = new List<string>();
                for (int col = 0; col < columnCount; col++)
                {
                    values.Add($"R{row}C{col}");
                }
                sb.AppendLine(string.Join(",", values));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Generates binary data patterns for EBCDIC testing.
        /// </summary>
        public static byte[] GenerateEbcdicData()
        {
            // Simple EBCDIC pattern - convert ASCII to EBCDIC encoding
            var asciiData = "HELLO WORLD     12345";
            var encoding = Encoding.GetEncoding("IBM037"); // EBCDIC
            return encoding.GetBytes(asciiData);
        }

        /// <summary>
        /// Generates packed decimal (COMP-3) test data.
        /// </summary>
        public static byte[] GeneratePackedDecimalData()
        {
            // Packed decimal representation of 123456 (positive)
            // Each digit takes 4 bits, with sign in last nibble
            return new byte[] { 0x12, 0x34, 0x5C }; // 123456 positive
        }

        /// <summary>
        /// Generates test data for different encoding types.
        /// </summary>
        public static Dictionary<string, byte[]> GenerateEncodingTests()
        {
            var testData = new Dictionary<string, byte[]>();
            var testString = "Test,Data,Ñoño,Café";

            testData["UTF-8"] = Encoding.UTF8.GetBytes(testString);
            testData["UTF-16"] = Encoding.Unicode.GetBytes(testString);
            testData["ISO-8859-1"] = Encoding.GetEncoding("ISO-8859-1").GetBytes(testString);
            testData["Windows-1252"] = Encoding.GetEncoding("Windows-1252").GetBytes(testString);

            return testData;
        }

        /// <summary>
        /// Generates performance stress test data with configurable characteristics.
        /// </summary>
        public static string GeneratePerformanceStressTest(int recordCount, int fieldsPerRecord = 10, int avgFieldLength = 50)
        {
            var sb = new StringBuilder(recordCount * fieldsPerRecord * avgFieldLength);

            // Header
            var headers = new List<string>();
            for (int i = 0; i < fieldsPerRecord; i++)
            {
                headers.Add($"Field{i:D2}");
            }
            sb.AppendLine(string.Join(",", headers));

            // Data
            for (int record = 0; record < recordCount; record++)
            {
                var fields = new List<string>();
                for (int field = 0; field < fieldsPerRecord; field++)
                {
                    var length = avgFieldLength + (record % 20) - 10; // Vary length ±10
                    var content = GenerateFieldContent(record, field, Math.Max(5, length));
                    fields.Add(content);
                }
                sb.AppendLine(string.Join(",", fields));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Writes compliance test data to a temporary file in the bin directory for debugging.
        /// File will be automatically ignored by git and cleaned up on build.
        /// </summary>
        /// <param name="testData">Test data content</param>
        /// <param name="filename">Optional filename (will be prefixed with timestamp)</param>
        /// <returns>Full path to the temporary file</returns>
        public static string WriteComplianceDataToTempFile(string testData, string filename = "compliance-test-data.csv")
        {
            var tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp-compliance");
            Directory.CreateDirectory(tempDir);

            var timestampedFilename = $"{DateTime.Now:yyyyMMdd-HHmmss}-{filename}";
            var filePath = Path.Combine(tempDir, timestampedFilename);

            File.WriteAllText(filePath, testData);
            return filePath;
        }

        /// <summary>
        /// Writes binary test data (EBCDIC, packed decimal) to temp file for debugging.
        /// </summary>
        /// <param name="binaryData">Binary test data</param>
        /// <param name="filename">Filename for the output</param>
        /// <returns>Full path to the temporary file</returns>
        public static string WriteBinaryDataToTempFile(byte[] binaryData, string filename = "binary-test-data.dat")
        {
            var tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp-compliance");
            Directory.CreateDirectory(tempDir);

            var timestampedFilename = $"{DateTime.Now:yyyyMMdd-HHmmss}-{filename}";
            var filePath = Path.Combine(tempDir, timestampedFilename);

            File.WriteAllBytes(filePath, binaryData);
            return filePath;
        }

        private static string GenerateFieldContent(int record, int field, int length)
        {
            var baseContent = $"R{record}F{field}";
            if (baseContent.Length >= length)
                return baseContent.Substring(0, length);

            var padding = new string('x', length - baseContent.Length);
            return baseContent + padding;
        }
    }
}