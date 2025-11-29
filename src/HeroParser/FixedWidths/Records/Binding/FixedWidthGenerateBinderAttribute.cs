namespace HeroParser.FixedWidths.Records.Binding;

/// <summary>
/// Indicates that the source generator should create an optimized binder for this type.
/// </summary>
/// <remarks>
/// When applied to a class or record, the source generator emits a registration call
/// that provides binding metadata without requiring runtime reflection. This enables
/// AOT-compatible deserialization.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class FixedWidthGenerateBinderAttribute : Attribute
{
}
