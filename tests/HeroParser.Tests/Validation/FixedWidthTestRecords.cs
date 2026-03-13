using HeroParser.FixedWidths.Records.Binding;

namespace HeroParser.Tests.Validation;

[FixedWidthGenerateBinder]
public class ValidatedEmployee
{
    [FixedWidthColumn(Start = 0, Length = 5, NotNull = true)]
    public int EmployeeId { get; set; }

    [FixedWidthColumn(Start = 5, Length = 20, NotNull = true, NotEmpty = true)]
    public string Name { get; set; } = "";

    [FixedWidthColumn(Start = 25, Length = 10, NotNull = true, RangeMin = 20000, RangeMax = 500000)]
    public decimal Salary { get; set; }

    [FixedWidthColumn(Start = 35, Length = 3, NotNull = true, MinLength = 2, MaxLength = 3)]
    public string Department { get; set; } = "";

    [FixedWidthColumn(Start = 38, Length = 12, Pattern = @"^\d{3}-\d{3}-\d{4}$")]
    public string Phone { get; set; } = "";
}
