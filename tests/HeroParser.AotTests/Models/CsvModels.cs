namespace HeroParser.AotTests.Models;

[GenerateBinder]
public class Person
{
    public string Name { get; set; } = "";
    public int Age { get; set; }
}

[GenerateBinder]
public class AttributedPerson
{
    [TabularMap(Name = "id", Index = 0)]
    public int Id { get; set; }

    [TabularMap(Name = "full_name", Index = 1)]
    public string Name { get; set; } = "";
}

[GenerateBinder]
public class NullableRecord
{
    public string Name { get; set; } = "";
    public int? Score { get; set; }
}
