using HeroParser;

namespace HeroParser.Generators.Tests.Generated;

[GenerateBinder]
internal sealed class GeneratedAiModel
{
    [TabularMap(Name = "user_name")]
    [Validate(NotEmpty = true, Pattern = @"^[A-Za-z0-9]+$")]
    public string Username { get; set; } = string.Empty;

    [TabularMap(Name = "user_age")]
    [Validate(RangeMin = 18.0, RangeMax = 120.0)]
    public int Age { get; set; }
}
