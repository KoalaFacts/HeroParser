namespace HeroParser.Generators.Tests.Generated;

[GenerateBinder]
internal sealed class GeneratedAttributed
{
    [TabularMap(Name = "full_name", Index = 1)]
    public string Name { get; set; } = string.Empty;

    [TabularMap(Index = 0)]
    public int Id { get; set; }
}
