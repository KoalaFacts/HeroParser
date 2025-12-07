using HeroParser.SeparatedValues.Reading.Shared;

namespace HeroParser.Generators.Tests.Generated;

[CsvGenerateBinder]
internal sealed class GeneratedPerson
{
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
}
