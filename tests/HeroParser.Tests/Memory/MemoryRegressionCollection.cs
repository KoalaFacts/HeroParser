using Xunit;

namespace HeroParser.Tests.Memory;

/// <summary>
/// Serializes memory regression tests so thread-local allocation checks are not disturbed by
/// concurrent JIT warmup, buffer pool churn, or SIMD capability overrides in other suites.
/// </summary>
[CollectionDefinition(NAME, DisableParallelization = true)]
public sealed class MemoryRegressionCollection
{
    /// <summary>The xUnit collection name applied to all memory regression test classes.</summary>
    public const string NAME = "MemoryRegression";
}
