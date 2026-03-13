namespace HeroParser.AotTests.Models;

[GenerateBinder]
public class FixedWidthPerson
{
    [PositionalMap(Start = 0, Length = 20)]
    public string Name { get; set; } = "";

    [PositionalMap(Start = 20, Length = 10, Alignment = FieldAlignment.Right)]
    public int Age { get; set; }
}

[GenerateBinder]
public class FixedWidthAligned
{
    [PositionalMap(Start = 0, Length = 5, Alignment = FieldAlignment.Right)]
    public int Id { get; set; }

    [PositionalMap(Start = 5, Length = 15)]
    public string Name { get; set; } = "";
}
