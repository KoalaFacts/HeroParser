using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Records;
using HeroParser.FixedWidths.Records.Binding;
using Xunit;

namespace HeroParser.Tests.FixedWidths;

/// <summary>
/// Simple synchronous progress reporter for testing.
/// </summary>
file sealed class TestProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}

public class FixedWidthBuilderTests
{
    [Fact]
    public void Builder_FromText_ReadsRecords()
    {
        // Arrange
        var data = "Record1\nRecord2\nRecord3";

        // Act
        var records = new List<string>();
        foreach (var row in FixedWidth.Read().FromText(data))
        {
            records.Add(row.RawRecord.ToString());
        }

        // Assert
        Assert.Equal(3, records.Count);
        Assert.Equal("Record1", records[0]);
        Assert.Equal("Record2", records[1]);
        Assert.Equal("Record3", records[2]);
    }

    [Fact]
    public void Builder_WithRecordLength_ParsesFixedLengthRecords()
    {
        // Arrange - No newlines, fixed 10-character records
        var data = "Record0001Record0002Record0003";

        // Act
        var records = new List<string>();
        foreach (var row in FixedWidth.Read()
            .WithRecordLength(10)
            .FromText(data))
        {
            records.Add(row.RawRecord.ToString());
        }

        // Assert
        Assert.Equal(3, records.Count);
        Assert.Equal("Record0001", records[0]);
        Assert.Equal("Record0002", records[1]);
        Assert.Equal("Record0003", records[2]);
    }

    [Fact]
    public void Builder_WithDefaultPadChar_AppliesToFields()
    {
        // Arrange - field with trailing asterisks (Left alignment trims trailing)
        var data = "Hello******";

        // Act
        string trimmed = "";
        foreach (var row in FixedWidth.Read()
            .WithDefaultPadChar('*')
            .WithDefaultAlignment(FieldAlignment.Left)
            .FromText(data))
        {
            trimmed = row.GetField(0, 11).ToString();
            break;
        }

        // Assert
        Assert.Equal("Hello", trimmed);
    }

    [Fact]
    public void Builder_TrackLineNumbers_PopulatesSourceLineNumber()
    {
        // Arrange
        var data = "Line1\nLine2\nLine3";

        // Act
        var lineNumbers = new List<int>();
        foreach (var row in FixedWidth.Read()
            .TrackLineNumbers()
            .FromText(data))
        {
            lineNumbers.Add(row.SourceLineNumber);
        }

        // Assert
        Assert.Equal([1, 2, 3], lineNumbers);
    }

    [Fact]
    public void Builder_SkipEmptyLines_SkipsEmptyByDefault()
    {
        // Arrange
        var data = "Line1\n\nLine2\n\nLine3";

        // Act
        var count = 0;
        foreach (var _ in FixedWidth.Read()
            .SkipEmptyLines()
            .FromText(data))
        {
            count++;
        }

        // Assert
        Assert.Equal(3, count);
    }

    [Fact]
    public void Builder_IncludeEmptyLines_IncludesEmpty()
    {
        // Arrange
        var data = "Line1\n\nLine2";

        // Act
        var count = 0;
        foreach (var _ in FixedWidth.Read()
            .IncludeEmptyLines()
            .FromText(data))
        {
            count++;
        }

        // Assert
        Assert.Equal(3, count); // Line1, empty, Line2
    }

    [Fact]
    public void Builder_WithMaxRecords_ThrowsWhenExceeded()
    {
        // Arrange
        var data = "R1\nR2\nR3\nR4\nR5";

        // Act & Assert
        var ex = Assert.Throws<FixedWidthException>(() =>
        {
            foreach (var _ in FixedWidth.Read()
                .WithMaxRecords(3)
                .FromText(data))
            {
                // Iterate through all
            }
        });

        Assert.Equal(FixedWidthErrorCode.TooManyRecords, ex.ErrorCode);
    }
}

public class FixedWidthRecordBindingTests
{
    [FixedWidthGenerateBinder]
    public class Employee
    {
        [FixedWidthColumn(Start = 0, Length = 10)]
        public string Id { get; set; } = "";

        [FixedWidthColumn(Start = 10, Length = 20)]
        public string Name { get; set; } = "";

        [FixedWidthColumn(Start = 30, Length = 10, Alignment = FieldAlignment.Right, PadChar = '0')]
        public decimal Salary { get; set; }
    }

    [FixedWidthGenerateBinder]
    public struct StructEmployee
    {
        [FixedWidthColumn(Start = 0, Length = 5, Alignment = FieldAlignment.Right, PadChar = '0')]
        public int Id { get; set; }

        [FixedWidthColumn(Start = 5, Length = 10)]
        public string? Name { get; set; }
    }

    public struct ReflectionStructEmployee
    {
        [FixedWidthColumn(Start = 0, Length = 5, Alignment = FieldAlignment.Right, PadChar = '0')]
        public int Id { get; set; }

        [FixedWidthColumn(Start = 5, Length = 10)]
        public string? Name { get; set; }
    }

    [Fact]
    public void GenericBuilder_FromText_BindsToRecords()
    {
        // Arrange
        var data =
            "0000000001John Doe            0000012345\n" +
            "0000000002Jane Smith          0000067890";

        // Act
        var employees = FixedWidth.Read<Employee>().FromText(data).ToList();

        // Assert
        Assert.Equal(2, employees.Count);

        Assert.Equal("0000000001", employees[0].Id);
        Assert.Equal("John Doe", employees[0].Name);
        Assert.Equal(12345m, employees[0].Salary);

        Assert.Equal("0000000002", employees[1].Id);
        Assert.Equal("Jane Smith", employees[1].Name);
        Assert.Equal(67890m, employees[1].Salary);
    }

    [Fact]
    public void GenericBuilder_FromText_BindsToStructRecords()
    {
        // Arrange
        var data =
            "00001Alice     \n" +
            "00002Bob       ";

        // Act
        var records = FixedWidth.Read<StructEmployee>().FromText(data).ToList();

        // Assert
        Assert.Equal(2, records.Count);
        Assert.Equal(1, records[0].Id);
        Assert.Equal("Alice", records[0].Name);
        Assert.Equal(2, records[1].Id);
        Assert.Equal("Bob", records[1].Name);
    }

    [Fact]
    public void ReflectionBinder_FromText_BindsToStructRecords()
    {
        // Arrange
        var data =
            "00001Alice     \n" +
            "00002Bob       ";

        // Act
        var records = FixedWidth.Read<ReflectionStructEmployee>().FromText(data).ToList();

        // Assert
        Assert.Equal(2, records.Count);
        Assert.Equal(1, records[0].Id);
        Assert.Equal("Alice", records[0].Name);
        Assert.Equal(2, records[1].Id);
        Assert.Equal("Bob", records[1].Name);
    }

    [Fact]
    public void ReflectionBinder_WithErrorHandler_SkipsInvalidStructRecords()
    {
        // Arrange
        var data =
            "00001Alice     \n" +
            "ABCDEBob       ";

        // Act
        var records = FixedWidth.Read<ReflectionStructEmployee>()
            .OnError((_, _) => FixedWidthDeserializeErrorAction.SkipRecord)
            .FromText(data)
            .ToList();

        // Assert
        Assert.Single(records);
        Assert.Equal(1, records[0].Id);
        Assert.Equal("Alice", records[0].Name);
    }

    [FixedWidthGenerateBinder]
    public class TypedRecord
    {
        [FixedWidthColumn(Start = 0, Length = 5, Alignment = FieldAlignment.Right, PadChar = '0')]
        public int IntValue { get; set; }

        [FixedWidthColumn(Start = 5, Length = 8, Format = "yyyyMMdd")]
        public DateTime DateValue { get; set; }

        [FixedWidthColumn(Start = 13, Length = 1)]
        public bool BoolValue { get; set; }
    }

    [Fact]
    public void GenericBuilder_BindsTypedFields()
    {
        // Arrange - "00123" + "20231225" + "1" = 14 characters
        var data = "00123202312251";

        // Act
        var records = FixedWidth.Read<TypedRecord>().FromText(data).ToList();

        // Assert
        Assert.Single(records);
        Assert.Equal(123, records[0].IntValue);
        Assert.Equal(new DateTime(2023, 12, 25), records[0].DateValue);
        Assert.True(records[0].BoolValue);
    }

    [FixedWidthGenerateBinder]
    public class BooleanRecord
    {
        [FixedWidthColumn(Start = 0, Length = 1)]
        public bool Flag { get; set; }
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("Y", true)]
    [InlineData("N", false)]
    [InlineData("T", true)]
    [InlineData("F", false)]
    public void GenericBuilder_BindsBooleanVariations(string input, bool expected)
    {
        // Act
        var records = FixedWidth.Read<BooleanRecord>().FromText(input).ToList();

        // Assert
        Assert.Single(records);
        Assert.Equal(expected, records[0].Flag);
    }

    [FixedWidthGenerateBinder]
    public class NullableRecord
    {
        [FixedWidthColumn(Start = 0, Length = 5)]
        public int? NullableInt { get; set; }
    }

    [Fact]
    public void GenericBuilder_BindsNullableFields_WhenEmpty()
    {
        // Arrange - empty field (all spaces)
        var data = "     ";

        // Act
        var records = FixedWidth.Read<NullableRecord>().FromText(data).ToList();

        // Assert
        Assert.Single(records);
        Assert.Null(records[0].NullableInt);
    }

    [Fact]
    public void GenericBuilder_BindsNullableFields_WhenHasValue()
    {
        // Arrange
        var data = "  123";

        // Act
        var records = FixedWidth.Read<NullableRecord>().FromText(data).ToList();

        // Assert
        Assert.Single(records);
        Assert.Equal(123, records[0].NullableInt);
    }

    public enum Status { Active, Inactive, Pending }

    [FixedWidthGenerateBinder]
    public class EnumRecord
    {
        [FixedWidthColumn(Start = 0, Length = 10)]
        public Status Status { get; set; }
    }

    [Theory]
    [InlineData("Active    ", Status.Active)]
    [InlineData("inactive  ", Status.Inactive)]  // case-insensitive
    [InlineData("PENDING   ", Status.Pending)]
    public void GenericBuilder_BindsEnumFields(string input, Status expected)
    {
        // Act
        var records = FixedWidth.Read<EnumRecord>().FromText(input).ToList();

        // Assert
        Assert.Single(records);
        Assert.Equal(expected, records[0].Status);
    }

    [Fact]
    public void GenericBuilder_OnError_SkipsRecord()
    {
        // Arrange - second record has invalid integer
        var data = "00123\nABCDE\n00456";
        var skippedCount = 0;

        // Act
        var records = FixedWidth.Read<NullableRecord>()
            .OnError((ctx, ex) =>
            {
                skippedCount++;
                return FixedWidthDeserializeErrorAction.SkipRecord;
            })
            .FromText(data)
            .ToList();

        // Assert
        Assert.Equal(2, records.Count); // First and third records
        Assert.Equal(1, skippedCount);
        Assert.Equal(123, records[0].NullableInt);
        Assert.Equal(456, records[1].NullableInt);
    }

    [Fact]
    public void GenericBuilder_OnError_ThrowsByDefault()
    {
        // Arrange - invalid integer
        var data = "ABCDE";

        // Act & Assert
        var ex = Assert.Throws<FixedWidthException>(() =>
            FixedWidth.Read<NullableRecord>().FromText(data).ToList());

        Assert.Equal(FixedWidthErrorCode.ParseError, ex.ErrorCode);
        Assert.Contains("NullableInt", ex.Message);
    }

    [FixedWidthGenerateBinder]
    public class NumericTypesRecord
    {
        [FixedWidthColumn(Start = 0, Length = 10)]
        public long LongValue { get; set; }

        [FixedWidthColumn(Start = 10, Length = 5)]
        public short ShortValue { get; set; }

        [FixedWidthColumn(Start = 15, Length = 3)]
        public byte ByteValue { get; set; }

        [FixedWidthColumn(Start = 18, Length = 10)]
        public double DoubleValue { get; set; }

        [FixedWidthColumn(Start = 28, Length = 10)]
        public float FloatValue { get; set; }
    }

    [Fact]
    public void GenericBuilder_BindsNumericTypes()
    {
        // Arrange: long(10) + short(5) + byte(3) + double(10) + float(10) = 38 chars
        // Positions: 0-9, 10-14, 15-17, 18-27, 28-37
        var data = "1234567890  123255   3.14159   2.71828";

        // Act
        var records = FixedWidth.Read<NumericTypesRecord>().FromText(data).ToList();

        // Assert
        Assert.Single(records);
        Assert.Equal(1234567890L, records[0].LongValue);
        Assert.Equal((short)123, records[0].ShortValue);
        Assert.Equal((byte)255, records[0].ByteValue);
        Assert.Equal(3.14159, records[0].DoubleValue, 5);
        Assert.Equal(2.71828f, records[0].FloatValue, 4);
    }

    [FixedWidthGenerateBinder]
    public class DateTypesRecord
    {
        [FixedWidthColumn(Start = 0, Length = 10, Format = "yyyy-MM-dd")]
        public DateOnly DateOnlyValue { get; set; }

        [FixedWidthColumn(Start = 10, Length = 8, Format = "HH:mm:ss")]
        public TimeOnly TimeOnlyValue { get; set; }

        [FixedWidthColumn(Start = 18, Length = 25, Format = "yyyy-MM-ddTHH:mm:sszzz")]
        public DateTimeOffset DateTimeOffsetValue { get; set; }
    }

    [Fact]
    public void GenericBuilder_BindsDateTypes()
    {
        // Arrange
        var data = "2023-12-2514:30:452023-12-25T14:30:45+05:00";

        // Act
        var records = FixedWidth.Read<DateTypesRecord>().FromText(data).ToList();

        // Assert
        Assert.Single(records);
        Assert.Equal(new DateOnly(2023, 12, 25), records[0].DateOnlyValue);
        Assert.Equal(new TimeOnly(14, 30, 45), records[0].TimeOnlyValue);
        Assert.Equal(new DateTimeOffset(2023, 12, 25, 14, 30, 45, TimeSpan.FromHours(5)), records[0].DateTimeOffsetValue);
    }

    [FixedWidthGenerateBinder]
    public class GuidRecord
    {
        [FixedWidthColumn(Start = 0, Length = 36)]
        public Guid GuidValue { get; set; }
    }

    [Fact]
    public void GenericBuilder_BindsGuid()
    {
        // Arrange
        var guid = Guid.Parse("12345678-1234-1234-1234-123456789abc");
        var data = "12345678-1234-1234-1234-123456789abc";

        // Act
        var records = FixedWidth.Read<GuidRecord>().FromText(data).ToList();

        // Assert
        Assert.Single(records);
        Assert.Equal(guid, records[0].GuidValue);
    }

    [FixedWidthGenerateBinder]
    public class AlignmentRecord
    {
        [FixedWidthColumn(Start = 0, Length = 10, Alignment = FieldAlignment.Left)]
        public string LeftAligned { get; set; } = "";

        [FixedWidthColumn(Start = 10, Length = 10, Alignment = FieldAlignment.Right)]
        public string RightAligned { get; set; } = "";

        [FixedWidthColumn(Start = 20, Length = 10, Alignment = FieldAlignment.None)]
        public string NoTrim { get; set; } = "";
    }

    [Fact]
    public void GenericBuilder_BindsWithDifferentAlignments()
    {
        // Arrange: Left trims trailing, Right trims leading, None keeps all
        // Field positions: 0-9 (LeftAligned), 10-19 (RightAligned), 20-29 (NoTrim)
        // Build string with exact field positions using concatenation:
        var data = "Hello".PadRight(10) +  // Chars 0-9:   "Hello     "
                   "World".PadLeft(10) +   // Chars 10-19: "     World"
                   "  Padded  ";           // Chars 20-29: "  Padded  "
        Assert.Equal(30, data.Length); // Verify exact length

        // Act
        var records = FixedWidth.Read<AlignmentRecord>().FromText(data).ToList();

        // Assert
        Assert.Single(records);
        Assert.Equal("Hello", records[0].LeftAligned);      // Trailing spaces trimmed
        Assert.Equal("World", records[0].RightAligned);     // Leading spaces trimmed
        Assert.Equal("  Padded  ", records[0].NoTrim);      // No trimming
    }

    [FixedWidthGenerateBinder]
    public class NullableTypesRecord
    {
        [FixedWidthColumn(Start = 0, Length = 10)]
        public long? NullableLong { get; set; }

        [FixedWidthColumn(Start = 10, Length = 10)]
        public DateTime? NullableDateTime { get; set; }

        [FixedWidthColumn(Start = 20, Length = 36)]
        public Guid? NullableGuid { get; set; }
    }

    [Fact]
    public void GenericBuilder_BindsNullableTypes_WhenEmpty()
    {
        // Arrange - all whitespace
        var data = new string(' ', 56);

        // Act
        var records = FixedWidth.Read<NullableTypesRecord>().FromText(data).ToList();

        // Assert
        Assert.Single(records);
        Assert.Null(records[0].NullableLong);
        Assert.Null(records[0].NullableDateTime);
        Assert.Null(records[0].NullableGuid);
    }

    [Fact]
    public void GenericBuilder_BindsNullableTypes_WhenHasValues()
    {
        // Arrange
        var guid = Guid.Parse("12345678-1234-1234-1234-123456789abc");
        var data = "123456789 2023-12-2512345678-1234-1234-1234-123456789abc";

        // Act
        var records = FixedWidth.Read<NullableTypesRecord>().FromText(data).ToList();

        // Assert
        Assert.Single(records);
        Assert.Equal(123456789L, records[0].NullableLong);
        Assert.Equal(new DateTime(2023, 12, 25), records[0].NullableDateTime);
        Assert.Equal(guid, records[0].NullableGuid);
    }

    [Theory]
    [InlineData("0         ", Status.Active)]    // Enum by integer value
    [InlineData("1         ", Status.Inactive)]
    [InlineData("2         ", Status.Pending)]
    public void GenericBuilder_BindsEnumByNumericValue(string input, Status expected)
    {
        // Act
        var records = FixedWidth.Read<EnumRecord>().FromText(input).ToList();

        // Assert
        Assert.Single(records);
        Assert.Equal(expected, records[0].Status);
    }

    [FixedWidthGenerateBinder]
    public class CustomPadCharRecord
    {
        [FixedWidthColumn(Start = 0, Length = 10, PadChar = '*', Alignment = FieldAlignment.Left)]
        public string StarPadded { get; set; } = "";

        [FixedWidthColumn(Start = 10, Length = 10, PadChar = '0', Alignment = FieldAlignment.Right)]
        public int ZeroPadded { get; set; }
    }

    [Fact]
    public void GenericBuilder_BindsWithCustomPadChars()
    {
        // Arrange
        var data = "Hello*****0000000123";

        // Act
        var records = FixedWidth.Read<CustomPadCharRecord>().FromText(data).ToList();

        // Assert
        Assert.Single(records);
        Assert.Equal("Hello", records[0].StarPadded);
        Assert.Equal(123, records[0].ZeroPadded);
    }

    #region WithNullValues Tests

    [FixedWidthGenerateBinder]
    public class NullValueTestRecord
    {
        [FixedWidthColumn(Start = 0, Length = 10)]
        public string? StringField { get; set; }

        [FixedWidthColumn(Start = 10, Length = 10)]
        public int? IntField { get; set; }
    }

    [Fact]
    public void GenericBuilder_WithNullValues_TreatsSpecificValuesAsNull()
    {
        // Arrange - "N/A" and "NULL" should be treated as null
        var data = "N/A       0000000123";

        // Act
        var records = FixedWidth.Read<NullValueTestRecord>()
            .WithNullValues("N/A", "NULL")
            .FromText(data)
            .ToList();

        // Assert
        Assert.Single(records);
        Assert.Null(records[0].StringField);
        Assert.Equal(123, records[0].IntField);
    }

    [Fact]
    public void GenericBuilder_WithNullValues_TreatsIntFieldAsNull()
    {
        // Arrange - "NULL" in int field should become null
        var data = "Value     NULL      ";

        // Act
        var records = FixedWidth.Read<NullValueTestRecord>()
            .WithNullValues("NULL")
            .FromText(data)
            .ToList();

        // Assert
        Assert.Single(records);
        Assert.Equal("Value", records[0].StringField);
        Assert.Null(records[0].IntField);
    }

    [Fact]
    public void GenericBuilder_WithNullValues_MultipleNullValueStrings()
    {
        // Arrange - Test multiple null value representations
        var data = """
            N/A       0000000001
            NULL      0000000002
            -         0000000003
            Value     0000000004
            """;

        // Act
        var records = FixedWidth.Read<NullValueTestRecord>()
            .WithNullValues("N/A", "NULL", "-")
            .FromText(data)
            .ToList();

        // Assert
        Assert.Equal(4, records.Count);
        Assert.Null(records[0].StringField);
        Assert.Null(records[1].StringField);
        Assert.Null(records[2].StringField);
        Assert.Equal("Value", records[3].StringField);
    }

    [Fact]
    public void GenericBuilder_WithNullValues_CaseSensitive()
    {
        // Arrange - "null" should NOT match "NULL"
        var data = "null      0000000001";

        // Act
        var records = FixedWidth.Read<NullValueTestRecord>()
            .WithNullValues("NULL")
            .FromText(data)
            .ToList();

        // Assert
        Assert.Single(records);
        Assert.Equal("null", records[0].StringField); // Not treated as null
    }

    #endregion

    #region WithProgress Tests

    [FixedWidthGenerateBinder]
    public class SimpleRecord
    {
        [FixedWidthColumn(Start = 0, Length = 10)]
        public string Value { get; set; } = "";
    }

    [Fact]
    public void GenericBuilder_WithProgress_ReportsProgress()
    {
        // Arrange - 5 records with interval of 2
        var data = """
            Record0001
            Record0002
            Record0003
            Record0004
            Record0005
            """;

        var progressReports = new List<FixedWidthProgress>();
        var progress = new TestProgress<FixedWidthProgress>(progressReports.Add);

        // Act
        var records = FixedWidth.Read<SimpleRecord>()
            .WithProgress(progress, intervalRows: 2)
            .FromText(data)
            .ToList();

        // Assert
        Assert.Equal(5, records.Count);
        // Progress reports: at 2, 4, and final (5)
        Assert.Equal(3, progressReports.Count);
        Assert.Equal(2, progressReports[0].RecordsProcessed);
        Assert.Equal(4, progressReports[1].RecordsProcessed);
        Assert.Equal(5, progressReports[2].RecordsProcessed);
    }

    [Fact]
    public void GenericBuilder_WithProgress_ReportsFinalProgress()
    {
        // Arrange - 3 records with interval of 10 (won't trigger interval reports)
        var data = """
            Record0001
            Record0002
            Record0003
            """;

        var progressReports = new List<FixedWidthProgress>();
        var progress = new TestProgress<FixedWidthProgress>(progressReports.Add);

        // Act
        var records = FixedWidth.Read<SimpleRecord>()
            .WithProgress(progress, intervalRows: 10)
            .FromText(data)
            .ToList();

        // Assert
        Assert.Equal(3, records.Count);
        // Should have only the final progress report (interval of 10 was never hit)
        Assert.Single(progressReports);
        Assert.Equal(3, progressReports[0].RecordsProcessed);
    }

    [Fact]
    public void GenericBuilder_WithProgress_DefaultInterval()
    {
        // Arrange
        var data = "Record0001";

        var progressReports = new List<FixedWidthProgress>();
        var progress = new TestProgress<FixedWidthProgress>(progressReports.Add);

        // Act
        var records = FixedWidth.Read<SimpleRecord>()
            .WithProgress(progress) // Uses default interval of 1000
            .FromText(data)
            .ToList();

        // Assert
        Assert.Single(records);
        Assert.Single(progressReports); // Final progress only
    }

    #endregion

    #region ForEachFromText Tests (Zero-Allocation API)

    [Fact]
    public void GenericBuilder_ForEachFromText_ReadsRecords()
    {
        // Arrange
        var data =
            "0000000001John Doe            0000012345\n" +
            "0000000002Jane Smith          0000067890";

        // Act
        var employees = new List<(string Id, string Name, decimal Salary)>();
        FixedWidth.Read<Employee>().ForEachFromText(data, record =>
        {
            // Copy values since the same instance is reused
            employees.Add((record.Id, record.Name, record.Salary));
        });

        // Assert
        Assert.Equal(2, employees.Count);
        Assert.Equal("0000000001", employees[0].Id);
        Assert.Equal("John Doe", employees[0].Name);
        Assert.Equal(12345m, employees[0].Salary);
        Assert.Equal("0000000002", employees[1].Id);
        Assert.Equal("Jane Smith", employees[1].Name);
        Assert.Equal(67890m, employees[1].Salary);
    }

    [Fact]
    public void GenericBuilder_ForEachFromText_ReusesInstance()
    {
        // Arrange
        var data =
            "0000000001John Doe            0000012345\n" +
            "0000000002Jane Smith          0000067890";

        // Act
        Employee? capturedInstance = null;
        var instancesSeen = new HashSet<Employee>(ReferenceEqualityComparer.Instance);

        FixedWidth.Read<Employee>().ForEachFromText(data, record =>
        {
            capturedInstance ??= record;
            instancesSeen.Add(record);
        });

        // Assert - only one instance should be seen (object reuse)
        Assert.Single(instancesSeen);
    }

    [Fact]
    public void GenericBuilder_ForEachFromText_AccumulatesValues()
    {
        // Arrange
        var data =
            "0000000001Person001           0000000100\n" +
            "0000000002Person002           0000000200\n" +
            "0000000003Person003           0000000300";

        // Act
        var totalSalary = 0m;
        FixedWidth.Read<Employee>().ForEachFromText(data, record =>
        {
            totalSalary += record.Salary;
        });

        // Assert
        Assert.Equal(600m, totalSalary);
    }

    #endregion

    #region AllowShortRows Tests

    [FixedWidthGenerateBinder]
    public class ShortRowRecord
    {
        [FixedWidthColumn(Start = 0, Length = 10)]
        public string Field1 { get; set; } = "";

        [FixedWidthColumn(Start = 10, Length = 10)]
        public string Field2 { get; set; } = "";

        [FixedWidthColumn(Start = 20, Length = 10)]
        public string Field3 { get; set; } = "";
    }

    [Fact]
    public void GenericBuilder_AllowShortRows_HandlesShortRowsGracefully()
    {
        // Arrange - First row is complete, second row is short (missing Field3)
        var data =
            "Field1    Field2    Field3    \n" +
            "Short1    Short2    ";

        // Act
        var records = FixedWidth.Read<ShortRowRecord>()
            .AllowShortRows()
            .FromText(data)
            .ToList();

        // Assert
        Assert.Equal(2, records.Count);
        Assert.Equal("Field1", records[0].Field1);
        Assert.Equal("Field2", records[0].Field2);
        Assert.Equal("Field3", records[0].Field3);
        Assert.Equal("Short1", records[1].Field1);
        Assert.Equal("Short2", records[1].Field2);
        Assert.Equal("", records[1].Field3);
    }

    [Fact]
    public void GenericBuilder_AllowShortRows_HandlesVeryShortRows()
    {
        // Arrange - Row only has first field
        var data = "OnlyField1";

        // Act
        var records = FixedWidth.Read<ShortRowRecord>()
            .AllowShortRows()
            .FromText(data)
            .ToList();

        // Assert
        Assert.Single(records);
        Assert.Equal("OnlyField1", records[0].Field1);
        Assert.Equal("", records[0].Field2);
        Assert.Equal("", records[0].Field3);
    }

    [Fact]
    public void GenericBuilder_AllowShortRows_False_ThrowsOnShortRows()
    {
        // Arrange - Row is short (missing Field3)
        var data = "Field1    Field2    ";

        // Act & Assert
        var ex = Assert.Throws<FixedWidthException>(() =>
            FixedWidth.Read<ShortRowRecord>()
                .AllowShortRows(false)
                .FromText(data)
                .ToList());

        Assert.Equal(FixedWidthErrorCode.FieldOutOfBounds, ex.ErrorCode);
        Assert.Contains("AllowShortRows", ex.Message);
    }

    [Fact]
    public void NonGenericBuilder_AllowShortRows_HandlesShortRowsGracefully()
    {
        // Arrange - Short row
        var data = "Short";

        // Act
        string? field1 = null;
        string? field2 = null;
        foreach (var row in FixedWidth.Read()
            .AllowShortRows()
            .FromText(data))
        {
            field1 = row.GetField(0, 10).ToString();
            field2 = row.GetField(10, 10).ToString();
        }

        // Assert
        Assert.Equal("Short", field1);
        Assert.Equal("", field2);
    }

    [Fact]
    public void NonGenericBuilder_AllowShortRows_False_ThrowsOnShortRows()
    {
        // Arrange
        var data = "Short";

        // Act & Assert
        var ex = Assert.Throws<FixedWidthException>(() =>
        {
            foreach (var row in FixedWidth.Read()
                .AllowShortRows(false)
                .FromText(data))
            {
                _ = row.GetField(0, 10); // This should throw
            }
        });

        Assert.Equal(FixedWidthErrorCode.FieldOutOfBounds, ex.ErrorCode);
    }

    #endregion

    #region End Property Tests

    [FixedWidthGenerateBinder]
    public class EndPropertyRecord
    {
        // Using End instead of Length
        [FixedWidthColumn(Start = 0, End = 10)]
        public string Field1 { get; set; } = "";

        [FixedWidthColumn(Start = 10, End = 25)]
        public string Field2 { get; set; } = "";

        // Mix of Length and End
        [FixedWidthColumn(Start = 25, Length = 5)]
        public string Field3 { get; set; } = "";
    }

    [Fact]
    public void GenericBuilder_EndProperty_WorksAsAlternativeToLength()
    {
        // Arrange: Field1 (0-10), Field2 (10-25), Field3 (25-30)
        var data = "Field1    Field2         F3   ";

        // Act
        var records = FixedWidth.Read<EndPropertyRecord>().FromText(data).ToList();

        // Assert
        Assert.Single(records);
        Assert.Equal("Field1", records[0].Field1);    // End=10 means length 10
        Assert.Equal("Field2", records[0].Field2);    // End=25 means length 15
        Assert.Equal("F3", records[0].Field3);        // Length=5
    }

    [FixedWidthGenerateBinder]
    public class EndPropertyNumericRecord
    {
        [FixedWidthColumn(Start = 0, End = 5, Alignment = FieldAlignment.Right, PadChar = '0')]
        public int Number { get; set; }
    }

    [Fact]
    public void GenericBuilder_EndProperty_WorksWithNumericTypes()
    {
        // Arrange: Field at 0-5 (length 5)
        var data = "00123";

        // Act
        var records = FixedWidth.Read<EndPropertyNumericRecord>().FromText(data).ToList();

        // Assert
        Assert.Single(records);
        Assert.Equal(123, records[0].Number);
    }

    [Fact]
    public void FixedWidthColumnAttribute_End_CalculatesLength()
    {
        // Verify the attribute calculates Length from End correctly
        var attr = new FixedWidthColumnAttribute { Start = 5, End = 15 };

        Assert.Equal(10, attr.Length);
        Assert.Equal(15, attr.End);
    }

    [Fact]
    public void FixedWidthColumnAttribute_Length_CalculatesEnd()
    {
        // Verify the attribute calculates End from Length correctly
        var attr = new FixedWidthColumnAttribute { Start = 5, Length = 10 };

        Assert.Equal(10, attr.Length);
        Assert.Equal(15, attr.End);
    }

    [Fact]
    public void FixedWidthColumnAttribute_LengthTakesPrecedenceOverEnd()
    {
        // When both are specified, Length takes precedence for determining field bounds
        // The source generator will use Length for the actual field extraction
        var attr = new FixedWidthColumnAttribute { Start = 0, Length = 10, End = 20 };

        Assert.Equal(10, attr.Length); // Length value is used for field extraction
        Assert.Equal(20, attr.End);    // End retains its set value (source generator ignores it when Length is set)
    }

    #endregion
}
