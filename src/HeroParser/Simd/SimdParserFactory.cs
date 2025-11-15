using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics.Arm;

namespace HeroParser.Simd;

/// <summary>
/// Factory for selecting the optimal SIMD parser based on hardware capabilities.
/// </summary>
internal static class SimdParserFactory
{
    // Cached parser instance selected at startup
    private static readonly ISimdParser _parser = SelectParser();

    /// <summary>
    /// Get the optimal parser for the current hardware.
    /// </summary>
    public static ISimdParser GetParser() => _parser;

    private static ISimdParser SelectParser()
    {
        // Priority order: AVX-512 > AVX2 > NEON > SSE2 > Scalar

        if (Avx512F.IsSupported && Avx512BW.IsSupported)
        {
            // Best: AVX-512 processes 64 chars per iteration
            return Avx512Parser.Instance;
        }

        if (Avx2.IsSupported)
        {
            // Good: AVX2 processes 32 chars per iteration
            return Avx2Parser.Instance;
        }

        if (AdvSimd.IsSupported)
        {
            // ARM NEON: processes 64 chars per iteration (8x16-byte vectors)
            return NeonParser.Instance;
        }

        if (Sse2.IsSupported)
        {
            // Fallback: SSE2 processes 16 chars per iteration
            // Not implemented yet - use scalar
            return ScalarParser.Instance;
        }

        // Last resort: scalar implementation
        return ScalarParser.Instance;
    }

    /// <summary>
    /// Get hardware capabilities summary for diagnostics.
    /// </summary>
    public static string GetHardwareInfo()
    {
        var caps = new List<string>();

        if (Avx512F.IsSupported) caps.Add("AVX-512F");
        if (Avx512BW.IsSupported) caps.Add("AVX-512BW");
        if (Avx2.IsSupported) caps.Add("AVX2");
        if (AdvSimd.IsSupported) caps.Add("ARM-NEON");
        if (Sse2.IsSupported) caps.Add("SSE2");

        var parser = _parser.GetType().Name;

        return caps.Count > 0
            ? $"SIMD: {string.Join(", ", caps)} | Using: {parser}"
            : $"No SIMD support | Using: {parser}";
    }
}
