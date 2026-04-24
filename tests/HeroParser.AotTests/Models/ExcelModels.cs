namespace HeroParser.AotTests.Models;

[GenerateBinder]
public class ExcelPerson
{
    public string Name { get; set; } = "";
    public int Age { get; set; }
}

[GenerateBinder]
public class ExcelOrder
{
    [TabularMap(Name = "OrderId", Index = 0)]
    public int Id { get; set; }

    [TabularMap(Name = "Customer", Index = 1)]
    public string Customer { get; set; } = "";

    [TabularMap(Name = "Amount", Index = 2)]
    public decimal Amount { get; set; }
}

[GenerateBinder]
public class ExcelNullableRecord
{
    public string Name { get; set; } = "";
    public int? Score { get; set; }
}
