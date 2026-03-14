namespace HeroParser;

/// <summary>
/// Triggers source generation of binder(s) for this record type.
/// The generator inspects properties: <see cref="TabularMapAttribute"/> produces a tabular binder
/// (CSV, Excel), <see cref="PositionalMapAttribute"/> produces a positional binder (FixedWidth).
/// If properties have both, both binders are generated.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class GenerateBinderAttribute : Attribute { }
