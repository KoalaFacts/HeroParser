using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace HeroParser.Internal;

/// <summary>
/// Compiles fast property getter delegates for reflection-based record writers.
/// </summary>
internal readonly struct PropertyAccessorCompiler(PropertyInfo property)
{
    [RequiresDynamicCode("Uses Expression.Compile to emit a property getter at runtime.")]
    internal Func<object, object?> CompileBoxedGetter()
    {
        // Expression: (object instance) => (object?)((TDeclaring)instance).Property
        var instanceParam = Expression.Parameter(typeof(object), "instance");
        var castInstance = Expression.Convert(instanceParam, property.DeclaringType!);
        var propertyAccess = Expression.Property(castInstance, property);

        Expression body = property.PropertyType.IsValueType
            ? Expression.Convert(propertyAccess, typeof(object))
            : Expression.TypeAs(propertyAccess, typeof(object));

        return Expression.Lambda<Func<object, object?>>(body, instanceParam).Compile();
    }

    [RequiresDynamicCode("Uses Expression.Compile to emit a property getter at runtime.")]
    internal Func<T, object?> CompileTypedGetter<T>()
    {
        var param = Expression.Parameter(typeof(T), "x");
        var access = Expression.Property(param, property);
        var convert = property.PropertyType.IsValueType
            ? Expression.Convert(access, typeof(object))
            : Expression.TypeAs(access, typeof(object));
        return Expression.Lambda<Func<T, object?>>(convert, param).Compile();
    }
}
