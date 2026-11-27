#if !NET9_0_OR_GREATER
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace HeroParser.JsonLines;

/// <summary>
/// Resolves serialized CLR members for a <see cref="JsonTypeInfo"/> on .NET 8,
/// where source-generated <see cref="JsonPropertyInfo.AttributeProvider"/> is unavailable.
/// </summary>
internal sealed class JsonlContractMemberMap
{
    private readonly Dictionary<(string JsonName, Type PropertyType), MemberInfo> byNameAndType;

    private JsonlContractMemberMap(Dictionary<(string JsonName, Type PropertyType), MemberInfo> byNameAndType)
    {
        this.byNameAndType = byNameAndType;
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "Only executed on .NET 8 when System.Text.Json omits JsonPropertyInfo.AttributeProvider (added in .NET 9); callers treat missing members under NativeAOT trimming as unsupported metadata.")]
    public static JsonlContractMemberMap Create(JsonTypeInfo typeInfo)
    {
        var map = new Dictionary<(string JsonName, Type PropertyType), MemberInfo>(ContractMemberKeyComparer.Instance);
        var seenClrNames = new HashSet<string>(StringComparer.Ordinal);
        JsonNamingPolicy? namingPolicy = typeInfo.Options.PropertyNamingPolicy;
        const BindingFlags flags = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        for (Type? current = typeInfo.Type;
             current is not null && current != typeof(object) && current != typeof(ValueType);
             current = current.BaseType)
        {
            foreach (PropertyInfo prop in current.GetProperties(flags))
            {
                if (prop.GetIndexParameters().Length > 0)
                    continue;
                if (!seenClrNames.Add(prop.Name))
                    continue;
                if (IsAlwaysIgnored(prop))
                    continue;

                string jsonName = ResolveJsonName(prop, namingPolicy);
                map.TryAdd((jsonName, prop.PropertyType), prop);
            }

            foreach (FieldInfo field in current.GetFields(flags))
            {
                if (field.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
                    continue;
                if (!seenClrNames.Add(field.Name))
                    continue;
                if (IsAlwaysIgnored(field))
                    continue;

                string jsonName = ResolveJsonName(field, namingPolicy);
                map.TryAdd((jsonName, field.FieldType), field);
            }
        }

        return new JsonlContractMemberMap(map);
    }

    public bool TryGetMember(string jsonName, Type propertyType, [NotNullWhen(true)] out MemberInfo? member)
        => byNameAndType.TryGetValue((jsonName, propertyType), out member);

    private static bool IsAlwaysIgnored(MemberInfo member)
        => Attribute.GetCustomAttribute(member, typeof(JsonIgnoreAttribute), inherit: true) is JsonIgnoreAttribute { Condition: JsonIgnoreCondition.Always };

    private static string ResolveJsonName(MemberInfo member, JsonNamingPolicy? namingPolicy)
    {
        if (Attribute.GetCustomAttribute(member, typeof(JsonPropertyNameAttribute), inherit: true) is JsonPropertyNameAttribute jsonNameAttr)
            return jsonNameAttr.Name;

        return namingPolicy?.ConvertName(member.Name) ?? member.Name;
    }

    private sealed class ContractMemberKeyComparer : IEqualityComparer<(string JsonName, Type PropertyType)>
    {
        public static readonly ContractMemberKeyComparer Instance = new();

        public bool Equals((string JsonName, Type PropertyType) x, (string JsonName, Type PropertyType) y)
            => StringComparer.Ordinal.Equals(x.JsonName, y.JsonName) && x.PropertyType == y.PropertyType;

        public int GetHashCode((string JsonName, Type PropertyType) obj)
            => HashCode.Combine(StringComparer.Ordinal.GetHashCode(obj.JsonName), obj.PropertyType);
    }
}
#endif
