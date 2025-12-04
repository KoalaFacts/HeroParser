using HeroParser.FixedWidths;
using HeroParser.FixedWidths.Records.Binding;

namespace HeroParser.AotTests.Models;

[FixedWidthGenerateBinder]
public class FixedWidthPerson
{
    [FixedWidthColumn(Start = 0, Length = 20)]
    public string Name { get; set; } = "";

    [FixedWidthColumn(Start = 20, Length = 10, Alignment = FieldAlignment.Right)]
    public int Age { get; set; }
}

[FixedWidthGenerateBinder]
public class FixedWidthAligned
{
    [FixedWidthColumn(Start = 0, Length = 5, Alignment = FieldAlignment.Right)]
    public int Id { get; set; }

    [FixedWidthColumn(Start = 5, Length = 15)]
    public string Name { get; set; } = "";
}
