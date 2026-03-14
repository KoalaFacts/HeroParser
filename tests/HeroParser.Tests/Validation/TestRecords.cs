namespace HeroParser.Tests.Validation;

[GenerateBinder]
public class ValidatedTransaction
{
    [TabularMap(Name = "Id")]
    [Validate(NotNull = true, NotEmpty = true)]
    public string TransactionId { get; set; } = "";

    [TabularMap(Name = "Amount", Index = 1)]
    [Validate(NotNull = true, RangeMin = 0, RangeMax = 100000)]
    public decimal Amount { get; set; }

    [TabularMap(Name = "Currency", Index = 2)]
    [Validate(NotNull = true, MinLength = 3, MaxLength = 3)]
    public string Currency { get; set; } = "";

    [TabularMap(Name = "Reference", Index = 3)]
    [Validate(Pattern = @"^[A-Z]{2}\d{4}$")]
    public string Reference { get; set; } = "";
}

/// <summary>
/// Record with non-nullable value types but NO Required attribute — used to test
/// that empty/whitespace values throw a hard parse error instead of silently defaulting.
/// </summary>
[GenerateBinder]
public class NonRequiredValueTypeRecord
{
    [TabularMap(Name = "Name")]
    public string Name { get; set; } = "";

    [TabularMap(Name = "Amount")]
    public decimal Amount { get; set; }

    [TabularMap(Name = "Count")]
    public int Count { get; set; }

    [TabularMap(Name = "Active")]
    public bool Active { get; set; }
}
