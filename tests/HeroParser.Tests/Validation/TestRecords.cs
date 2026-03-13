using HeroParser.SeparatedValues.Reading.Shared;

namespace HeroParser.Tests.Validation;

[CsvGenerateBinder]
public class ValidatedTransaction
{
    [CsvColumn(Name = "Id", NotNull = true, NotEmpty = true)]
    public string TransactionId { get; set; } = "";

    [CsvColumn(Name = "Amount", Index = 1, NotNull = true, RangeMin = 0, RangeMax = 100000)]
    public decimal Amount { get; set; }

    [CsvColumn(Name = "Currency", Index = 2, NotNull = true, MinLength = 3, MaxLength = 3)]
    public string Currency { get; set; } = "";

    [CsvColumn(Name = "Reference", Index = 3, Pattern = @"^[A-Z]{2}\d{4}$")]
    public string Reference { get; set; } = "";
}

/// <summary>
/// Record with non-nullable value types but NO Required attribute — used to test
/// that empty/whitespace values throw a hard parse error instead of silently defaulting.
/// </summary>
[CsvGenerateBinder]
public class NonRequiredValueTypeRecord
{
    [CsvColumn(Name = "Name")]
    public string Name { get; set; } = "";

    [CsvColumn(Name = "Amount")]
    public decimal Amount { get; set; }

    [CsvColumn(Name = "Count")]
    public int Count { get; set; }

    [CsvColumn(Name = "Active")]
    public bool Active { get; set; }
}
