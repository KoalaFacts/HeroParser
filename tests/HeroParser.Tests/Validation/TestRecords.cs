using HeroParser.SeparatedValues.Reading.Shared;

namespace HeroParser.Tests.Validation;

[CsvGenerateBinder]
public class ValidatedTransaction
{
    [CsvColumn(Name = "Id", Required = true, NotEmpty = true)]
    public string TransactionId { get; set; } = "";

    [CsvColumn(Name = "Amount", Index = 1, Required = true, RangeMin = 0, RangeMax = 100000)]
    public decimal Amount { get; set; }

    [CsvColumn(Name = "Currency", Index = 2, Required = true, MinLength = 3, MaxLength = 3)]
    public string Currency { get; set; } = "";

    [CsvColumn(Name = "Reference", Index = 3, Pattern = @"^[A-Z]{2}\d{4}$")]
    public string Reference { get; set; } = "";
}
