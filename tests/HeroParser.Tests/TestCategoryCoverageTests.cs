using System.Reflection;
using Xunit;

namespace HeroParser.Tests;

/// <summary>
/// Guards the trait that decides whether a test runs in CI at all.
///
/// CI executes this assembly twice, once per category filter, so a test with no
/// Category trait is discovered, reported as part of the total, and never run by
/// any job. That failure is silent: the suite stays green precisely because the
/// test never executes. This fixture fails instead, naming the offenders.
/// </summary>
public class TestCategoryCoverageTests
{
    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void EveryTestMethodDeclaresACategory()
    {
        var offenders = typeof(TestCategoryCoverageTests).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                | BindingFlags.Instance | BindingFlags.Static
                                                | BindingFlags.DeclaredOnly))
            .Where(IsTestMethod)
            .Where(method => !DeclaresCategory(method))
            .Select(method => $"{method.DeclaringType?.FullName}.{method.Name}")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"{offenders.Count} test method(s) declare no [Trait(TestCategories.CATEGORY, ...)], so no CI job runs them. "
            + "Add the trait to the method, or to its class when the whole class belongs to one category:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    [Trait(TestCategories.CATEGORY, TestCategories.UNIT)]
    public void OnlyKnownCategoryValuesAreUsed()
    {
        string[] known = [TestCategories.UNIT, TestCategories.INTEGRATION];

        var unknown = typeof(TestCategoryCoverageTests).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                | BindingFlags.Instance | BindingFlags.Static
                                                | BindingFlags.DeclaredOnly))
            .Where(IsTestMethod)
            .SelectMany(method => CategoryValues(method).Select(value => (method, value)))
            .Where(pair => !known.Contains(pair.value, StringComparer.Ordinal))
            .Select(pair => $"{pair.method.DeclaringType?.FullName}.{pair.method.Name} -> '{pair.value}'")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        // A typo'd or invented category filters into neither CI job, which is the
        // same silent skip as declaring no category at all.
        Assert.True(
            unknown.Count == 0,
            $"{unknown.Count} test method(s) use a Category outside {string.Join(" / ", known)}, "
            + "so no CI job runs them:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, unknown));
    }

    private static bool IsTestMethod(MethodInfo method) =>
        method.GetCustomAttributesData().Any(attribute => IsFactOrDerived(attribute.AttributeType));

    private static bool IsFactOrDerived(Type? attributeType)
    {
        // TheoryAttribute derives from FactAttribute; walking the chain also covers
        // any custom Fact-derived attribute this assembly might grow later.
        for (var current = attributeType; current is not null; current = current.BaseType)
        {
            if (current.FullName == "Xunit.FactAttribute") return true;
        }

        return false;
    }

    private static bool DeclaresCategory(MethodInfo method) => CategoryValues(method).Any();

    private static IEnumerable<string> CategoryValues(MethodInfo method)
    {
        foreach (var value in CategoryValuesOn(method)) yield return value;

        // A class-level trait applies to every test in the class, so a method
        // carrying none is still covered. Base types can supply it too.
        for (var type = method.DeclaringType; type is not null; type = type.BaseType)
        {
            foreach (var value in CategoryValuesOn(type)) yield return value;
        }
    }

    private static IEnumerable<string> CategoryValuesOn(MemberInfo member) =>
        member.GetCustomAttributesData()
            .Where(attribute => attribute.AttributeType.FullName == "Xunit.TraitAttribute"
                                && attribute.ConstructorArguments.Count == 2
                                && attribute.ConstructorArguments[0].Value as string == TestCategories.CATEGORY)
            .Select(attribute => (string?)attribute.ConstructorArguments[1].Value)
            .Where(value => value is not null)
            .Select(value => value!);
}
