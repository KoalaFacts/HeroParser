namespace HeroParser.Tests.Generated;

[CsvGenerateBinder]
internal sealed class GeneratedAttributed
{
    [CsvColumn(Name = "full_name", Index = 1)]
    public string Name { get; set; } = string.Empty;

    [CsvColumn(Index = 0)]
    public int Id { get; set; }
}
