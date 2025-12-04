using HeroParser.SeparatedValues.Records;
using HeroParser.SeparatedValues.Records.Binding;

namespace HeroParser.AotTests.Models;

[CsvGenerateBinder]
public class Person
{
    public string Name { get; set; } = "";
    public int Age { get; set; }
}

[CsvGenerateBinder]
public class AttributedPerson
{
    [CsvColumn(Name = "id", Index = 0)]
    public int Id { get; set; }

    [CsvColumn(Name = "full_name", Index = 1)]
    public string Name { get; set; } = "";
}

[CsvGenerateBinder]
public class NullableRecord
{
    public string Name { get; set; } = "";
    public int? Score { get; set; }
}
