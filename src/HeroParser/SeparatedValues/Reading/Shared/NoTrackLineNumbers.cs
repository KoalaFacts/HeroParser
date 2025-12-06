namespace HeroParser.SeparatedValues.Reading.Shared;

/// <summary>
/// Marker struct for compile-time specialization: skip line number tracking.
/// Using generic type parameters allows the JIT to eliminate dead branches.
/// </summary>
internal readonly struct NoTrackLineNumbers { }
