namespace HeroParser.JsonLines.Reading.Data;

/// <summary>
/// Defines a single column for <see cref="JsonlDataReader"/> projection.
/// </summary>
/// <param name="Name">The column name.</param>
/// <param name="JsonPath">A dot/bracket path into the JSON object — e.g. <c>messages[0].content</c>.</param>
/// <param name="DataType">The CLR type of the value (e.g. <c>typeof(string)</c>, <c>typeof(long)</c>).</param>
public sealed record JsonlColumnDefinition(string Name, string JsonPath, Type DataType);
