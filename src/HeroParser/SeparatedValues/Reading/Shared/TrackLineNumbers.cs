namespace HeroParser.SeparatedValues.Reading.Shared;

/// <summary>
/// Marker struct for compile-time specialization: track line numbers.
/// Using generic type parameters allows the JIT to eliminate dead branches.
/// </summary>
internal readonly struct TrackLineNumbers { }
