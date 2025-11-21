namespace HeroParser.SeparatedValues.Records.Binding;

/// <summary>
/// Marks a record/class for source-generated CSV binder emission (AOT-friendly).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class CsvGenerateBinderAttribute : Attribute
{
}
