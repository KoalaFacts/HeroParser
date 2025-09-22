using System;
using System.Text;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace HeroParser.BenchmarkTests.Utilities
{
    /// <summary>
    /// High-performance CSV data generator for benchmarks and tests.
    /// Generates deterministic test data without requiring large files in git.
    /// Optimized for memory efficiency and realistic data patterns.
    ///
    /// Note: .gitignore is configured to exclude any generated CSV files to prevent
    /// accidental commits of large test data. See docs/test-data-generation.md for details.
    /// </summary>
    public static class CsvDataGenerator
    {
        // Pre-computed random data pools for efficient generation
        private static readonly string[] FirstNames = {
            "John", "Jane", "Michael", "Sarah", "David", "Emily", "Robert", "Jessica",
            "William", "Ashley", "Christopher", "Amanda", "Matthew", "Stephanie", "Joshua",
            "Jennifer", "Andrew", "Elizabeth", "Daniel", "Kimberly", "James", "Lisa"
        };

        private static readonly string[] LastNames = {
            "Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia", "Miller", "Davis",
            "Rodriguez", "Martinez", "Hernandez", "Lopez", "Gonzalez", "Wilson", "Anderson",
            "Thomas", "Taylor", "Moore", "Jackson", "Martin", "Lee", "Perez"
        };

        private static readonly string[] Departments = {
            "Engineering", "Marketing", "Sales", "Human Resources", "Finance",
            "Operations", "Customer Service", "IT", "Research & Development", "Legal"
        };

        private static readonly string[] EmailDomains = {
            "company.com", "enterprise.org", "business.net", "corp.com", "firm.co"
        };

        /// <summary>
        /// Generates realistic CSV data with specified number of records.
        /// Optimized for benchmark testing with consistent field sizes.
        /// </summary>
        /// <param name="recordCount">Number of data records to generate</param>
        /// <param name="schema">Schema type for different test scenarios</param>
        /// <returns>Complete CSV string with headers</returns>
        public static string GenerateRealistic(int recordCount, CsvSchema schema = CsvSchema.Employee)
        {
            return schema switch
            {
                CsvSchema.Employee => GenerateEmployeeData(recordCount),
                CsvSchema.Financial => GenerateFinancialData(recordCount),
                CsvSchema.IoT => GenerateIoTData(recordCount),
                CsvSchema.Ecommerce => GenerateEcommerceData(recordCount),
                CsvSchema.LogData => GenerateLogData(recordCount),
                _ => GenerateEmployeeData(recordCount)
            };
        }

        /// <summary>
        /// Generates CSV data designed to stress test specific parsing scenarios.
        /// </summary>
        /// <param name="recordCount">Number of records</param>
        /// <param name="stress">Stress test scenario</param>
        /// <returns>CSV string optimized for stress testing</returns>
        public static string GenerateStressTest(int recordCount, StressTestType stress)
        {
            return stress switch
            {
                StressTestType.QuotedFields => GenerateQuotedFieldsStress(recordCount),
                StressTestType.EscapeSequences => GenerateEscapeSequenceStress(recordCount),
                StressTestType.LargeFields => GenerateLargeFieldStress(recordCount),
                StressTestType.ManyColumns => GenerateManyColumnsStress(recordCount),
                StressTestType.MixedLineEndings => GenerateMixedLineEndingsStress(recordCount),
                _ => GenerateEmployeeData(recordCount)
            };
        }

        /// <summary>
        /// Generates fixed-length data for COBOL copybook testing.
        /// </summary>
        /// <param name="recordCount">Number of records</param>
        /// <param name="recordLength">Fixed record length in characters</param>
        /// <returns>Fixed-length formatted data</returns>
        public static string GenerateFixedLength(int recordCount, int recordLength = 80)
        {
            var sb = new StringBuilder(recordCount * (recordLength + 2)); // +2 for CRLF

            for (int i = 0; i < recordCount; i++)
            {
                var record = GenerateFixedLengthRecord(i, recordLength);
                sb.AppendLine(record);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Writes generated CSV data to a temporary file in the bin directory for debugging.
        /// File will be automatically ignored by git and cleaned up on build.
        /// </summary>
        /// <param name="csvData">Generated CSV data</param>
        /// <param name="filename">Optional filename (will be prefixed with timestamp)</param>
        /// <returns>Full path to the temporary file</returns>
        public static string WriteToTempFile(string csvData, string filename = "generated-data.csv")
        {
            var tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp-csv");
            Directory.CreateDirectory(tempDir);

            var timestampedFilename = $"{DateTime.Now:yyyyMMdd-HHmmss}-{filename}";
            var filePath = Path.Combine(tempDir, timestampedFilename);

            File.WriteAllText(filePath, csvData);
            return filePath;
        }

        /// <summary>
        /// Writes generated CSV data to a file in the bin/Debug output directory.
        /// Useful for debugging benchmark data during development.
        /// </summary>
        /// <param name="csvData">Generated CSV data</param>
        /// <param name="filename">Filename for the output</param>
        /// <returns>Full path to the output file</returns>
        public static string WriteToOutputDirectory(string csvData, string filename)
        {
            var outputDir = AppDomain.CurrentDomain.BaseDirectory;
            var filePath = Path.Combine(outputDir, filename);

            File.WriteAllText(filePath, csvData);
            return filePath;
        }

        /// <summary>
        /// Estimates the approximate size in bytes for given record count and schema.
        /// </summary>
        public static long EstimateSize(int recordCount, CsvSchema schema)
        {
            var avgRecordSize = schema switch
            {
                CsvSchema.Employee => 120,      // ~120 bytes per record
                CsvSchema.Financial => 180,     // ~180 bytes per record
                CsvSchema.IoT => 80,           // ~80 bytes per record
                CsvSchema.Ecommerce => 200,    // ~200 bytes per record
                CsvSchema.LogData => 300,      // ~300 bytes per record
                _ => 120
            };

            return recordCount * avgRecordSize;
        }

        private static string GenerateEmployeeData(int recordCount)
        {
            var estimatedSize = (int)EstimateSize(recordCount, CsvSchema.Employee);
            var sb = new StringBuilder(estimatedSize);

            // CSV Header
            sb.AppendLine("Id,FirstName,LastName,Email,Age,Salary,Department,StartDate,IsActive");

            for (int i = 0; i < recordCount; i++)
            {
                var firstName = FirstNames[i % FirstNames.Length];
                var lastName = LastNames[(i * 7) % LastNames.Length]; // Different rotation
                var email = $"{firstName.ToLower()}.{lastName.ToLower()}@{EmailDomains[i % EmailDomains.Length]}";
                var age = 22 + (i % 43); // Ages 22-64
                var salary = 35000 + (i % 120000); // Salaries 35k-155k
                var department = Departments[i % Departments.Length];
                var startDate = GenerateRandomDate(2015, 2024, i);
                var isActive = (i % 7) != 0; // ~86% active rate

                sb.AppendLine($"{i},\"{firstName}\",\"{lastName}\",{email},{age},{salary},\"{department}\",{startDate},{isActive.ToString().ToLower()}");
            }

            return sb.ToString();
        }

        private static string GenerateFinancialData(int recordCount)
        {
            var sb = new StringBuilder(recordCount * 180);
            sb.AppendLine("TransactionId,AccountId,Amount,Currency,TransactionType,Timestamp,MerchantId,CardLast4");

            for (int i = 0; i < recordCount; i++)
            {
                var txnId = $"TXN-{i:D10}";
                var accountId = $"ACC-{(i % 10000):D6}";
                var amount = (decimal)(10.00 + (i % 5000.00)) / 100; // $0.10 - $50.00
                var currency = (i % 10) switch { 0 => "EUR", 1 => "GBP", 2 => "JPY", _ => "USD" };
                var txnType = (i % 4) switch { 0 => "DEBIT", 1 => "CREDIT", 2 => "TRANSFER", _ => "PAYMENT" };
                var timestamp = GenerateTimestamp(2024, i);
                var merchantId = $"MERCH-{(i % 1000):D4}";
                var cardLast4 = $"{(i % 9999):D4}";

                sb.AppendLine($"{txnId},{accountId},{amount:F2},{currency},{txnType},{timestamp},{merchantId},{cardLast4}");
            }

            return sb.ToString();
        }

        private static string GenerateIoTData(int recordCount)
        {
            var sb = new StringBuilder(recordCount * 80);
            sb.AppendLine("DeviceId,Timestamp,Temperature,Humidity,Pressure,BatteryLevel");

            for (int i = 0; i < recordCount; i++)
            {
                var deviceId = $"IOT-{(i % 500):D3}";
                var timestamp = GenerateTimestamp(2024, i);
                var temperature = 15.0 + (i % 500) * 0.1; // 15.0-65.0°C
                var humidity = 30.0 + (i % 700) * 0.1; // 30.0-100.0%
                var pressure = 950.0 + (i % 800) * 0.1; // 950.0-1030.0 hPa
                var battery = Math.Max(0, 100 - (i % 120)); // 0-100%

                sb.AppendLine($"{deviceId},{timestamp},{temperature:F1},{humidity:F1},{pressure:F1},{battery}");
            }

            return sb.ToString();
        }

        private static string GenerateEcommerceData(int recordCount)
        {
            var sb = new StringBuilder(recordCount * 200);
            sb.AppendLine("OrderId,CustomerId,ProductId,ProductName,Quantity,Price,Discount,ShippingAddress,OrderDate");

            for (int i = 0; i < recordCount; i++)
            {
                var orderId = $"ORD-{i:D8}";
                var customerId = $"CUST-{(i % 5000):D6}";
                var productId = $"PROD-{(i % 1000):D4}";
                var productName = $"\"Product {i % 100} - Category {i % 20}\"";
                var quantity = 1 + (i % 5);
                var price = (decimal)(5.99 + (i % 995.00));
                var discount = (i % 10 == 0) ? price * 0.1m : 0m;
                var address = $"\"123 Main St Apt {i % 999}, City {i % 50}, State {i % 50}\"";
                var orderDate = GenerateRandomDate(2023, 2024, i);

                sb.AppendLine($"{orderId},{customerId},{productId},{productName},{quantity},{price:F2},{discount:F2},{address},{orderDate}");
            }

            return sb.ToString();
        }

        private static string GenerateLogData(int recordCount)
        {
            var sb = new StringBuilder(recordCount * 300);
            sb.AppendLine("Timestamp,Level,Source,Message,UserId,SessionId,IpAddress,UserAgent");

            var levels = new[] { "DEBUG", "INFO", "WARN", "ERROR", "FATAL" };
            var sources = new[] { "WebAPI", "Database", "Cache", "Auth", "Payment", "Notification" };

            for (int i = 0; i < recordCount; i++)
            {
                var timestamp = GenerateTimestamp(2024, i);
                var level = levels[i % levels.Length];
                var source = sources[i % sources.Length];
                var message = $"\"Operation {i % 1000} completed with status {(i % 10 == 0 ? "ERROR" : "SUCCESS")}\"";
                var userId = (i % 3 == 0) ? $"USER-{(i % 10000):D6}" : "";
                var sessionId = $"SESS-{Guid.NewGuid():N}"[..16];
                var ipAddress = $"192.168.{i % 256}.{(i * 7) % 256}";
                var userAgent = $"\"Mozilla/5.0 Browser {i % 100}\"";

                sb.AppendLine($"{timestamp},{level},{source},{message},{userId},{sessionId},{ipAddress},{userAgent}");
            }

            return sb.ToString();
        }

        private static string GenerateQuotedFieldsStress(int recordCount)
        {
            var sb = new StringBuilder(recordCount * 150);
            sb.AppendLine("Id,QuotedField,CommaField,QuoteEscapeField,NewlineField");

            for (int i = 0; i < recordCount; i++)
            {
                sb.AppendLine($"{i}," +
                             $"\"Field with spaces {i}\"," +
                             $"\"Field, with, commas {i}\"," +
                             $"\"Field with \"\"embedded quotes\"\" {i}\"," +
                             $"\"Field with\nembedded newline {i}\"");
            }

            return sb.ToString();
        }

        private static string GenerateEscapeSequenceStress(int recordCount)
        {
            var sb = new StringBuilder(recordCount * 100);
            sb.AppendLine("Id,EscapeField,SpecialChars,UnicodeField");

            for (int i = 0; i < recordCount; i++)
            {
                sb.AppendLine($"{i}," +
                             $"\"Tab:\t Newline:\n Return:\r\"," +
                             $"\"Special: \\\"\\\\//\\b\\f\\r\\n\\t\"," +
                             $"\"Unicode: ñ café résumé 你好 🚀\"");
            }

            return sb.ToString();
        }

        private static string GenerateLargeFieldStress(int recordCount)
        {
            var sb = new StringBuilder(recordCount * 2000);
            sb.AppendLine("Id,SmallField,LargeField,HugeField");

            for (int i = 0; i < recordCount; i++)
            {
                var largeField = new string('A', 500 + (i % 1000)); // 500-1500 chars
                var hugeField = new string('B', 2000 + (i % 3000)); // 2000-5000 chars

                sb.AppendLine($"{i},Small{i},\"{largeField}\",\"{hugeField}\"");
            }

            return sb.ToString();
        }

        private static string GenerateManyColumnsStress(int recordCount)
        {
            var sb = new StringBuilder(recordCount * 500);

            // Generate header with 50 columns
            var headers = new List<string>();
            for (int col = 0; col < 50; col++)
            {
                headers.Add($"Column{col:D2}");
            }
            sb.AppendLine(string.Join(",", headers));

            // Generate data rows
            for (int i = 0; i < recordCount; i++)
            {
                var values = new List<string>();
                for (int col = 0; col < 50; col++)
                {
                    values.Add($"Value{i}-{col}");
                }
                sb.AppendLine(string.Join(",", values));
            }

            return sb.ToString();
        }

        private static string GenerateMixedLineEndingsStress(int recordCount)
        {
            var sb = new StringBuilder(recordCount * 100);
            sb.Append("Id,Name,Value\r\n"); // Windows CRLF header

            for (int i = 0; i < recordCount; i++)
            {
                var ending = (i % 3) switch
                {
                    0 => "\r\n", // Windows CRLF
                    1 => "\n",   // Unix LF
                    _ => "\r"    // Mac CR
                };

                sb.Append($"{i},Name{i},Value{i}{ending}");
            }

            return sb.ToString();
        }

        private static string GenerateFixedLengthRecord(int recordNumber, int totalLength)
        {
            // Generate fields that fit exactly in the fixed length
            var id = recordNumber.ToString().PadLeft(8, '0');                    // 8 chars
            var name = $"Employee{recordNumber % 1000}".PadRight(20).Substring(0, 20); // 20 chars
            var salary = (30000 + (recordNumber % 100000)).ToString().PadLeft(8, '0'); // 8 chars
            var date = GenerateRandomDate(2020, 2024, recordNumber);              // 10 chars

            var record = id + name + salary + date;

            // Pad or truncate to exact length
            if (record.Length > totalLength)
                return record.Substring(0, totalLength);
            else
                return record.PadRight(totalLength);
        }

        private static string GenerateRandomDate(int startYear, int endYear, int seed)
        {
            var year = startYear + (seed % (endYear - startYear + 1));
            var month = 1 + (seed % 12);
            var day = 1 + (seed % 28); // Use 28 to avoid month boundary issues
            return $"{year:D4}-{month:D2}-{day:D2}";
        }

        private static string GenerateTimestamp(int year, int seed)
        {
            var month = 1 + (seed % 12);
            var day = 1 + (seed % 28);
            var hour = seed % 24;
            var minute = (seed * 7) % 60;
            var second = (seed * 13) % 60;
            return $"{year:D4}-{month:D2}-{day:D2}T{hour:D2}:{minute:D2}:{second:D2}Z";
        }
    }

    /// <summary>
    /// CSV schema types for different test scenarios.
    /// </summary>
    public enum CsvSchema
    {
        Employee,     // Standard employee records
        Financial,    // Financial transaction data
        IoT,          // IoT sensor readings
        Ecommerce,    // E-commerce order data
        LogData       // Application log entries
    }

    /// <summary>
    /// Stress test types for challenging parsing scenarios.
    /// </summary>
    public enum StressTestType
    {
        QuotedFields,      // Heavy use of quoted fields
        EscapeSequences,   // Escape characters and special sequences
        LargeFields,       // Very large field values
        ManyColumns,       // Many columns (wide records)
        MixedLineEndings   // Mixed line ending formats
    }
}