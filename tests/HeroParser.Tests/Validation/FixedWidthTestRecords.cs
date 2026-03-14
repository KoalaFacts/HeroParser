namespace HeroParser.Tests.Validation;

[GenerateBinder]
public class ValidatedEmployee
{
    [PositionalMap(Start = 0, Length = 5)]
    [Validate(NotNull = true)]
    public int EmployeeId { get; set; }

    [PositionalMap(Start = 5, Length = 20)]
    [Validate(NotNull = true, NotEmpty = true)]
    public string Name { get; set; } = "";

    [PositionalMap(Start = 25, Length = 10)]
    [Validate(NotNull = true, RangeMin = 20000, RangeMax = 500000)]
    public decimal Salary { get; set; }

    [PositionalMap(Start = 35, Length = 3)]
    [Validate(NotNull = true, MinLength = 2, MaxLength = 3)]
    public string Department { get; set; } = "";

    [PositionalMap(Start = 38, Length = 12)]
    [Validate(Pattern = @"^\d{3}-\d{3}-\d{4}$")]
    public string Phone { get; set; } = "";
}
