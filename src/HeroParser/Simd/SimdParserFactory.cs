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
#if NET6_0_OR_GREATER
        // Priority order: AVX-512 > AVX2 > NEON > Scalar

        if (System.Runtime.Intrinsics.X86.Avx512F.IsSupported &&
            System.Runtime.Intrinsics.X86.Avx512BW.IsSupported)
        {
            // Best: AVX-512 processes 64 chars per iteration (30+ GB/s)
            return Avx512Parser.Instance;
        }

        if (System.Runtime.Intrinsics.X86.Avx2.IsSupported)
        {
            // Good: AVX2 processes 32 chars per iteration (20+ GB/s)
            return Avx2Parser.Instance;
        }

        if (System.Runtime.Intrinsics.Arm.AdvSimd.IsSupported)
        {
            // ARM NEON: processes 64 chars per iteration (12+ GB/s)
            return NeonParser.Instance;
        }
#endif
        // Fallback: scalar implementation (works on all frameworks, 2-5 GB/s)
        return ScalarParser.Instance;
    }

    /// <summary>
    /// Get hardware capabilities summary for diagnostics.
    /// </summary>
    public static string GetHardwareInfo()
    {
#if NET6_0_OR_GREATER
        var caps = new System.Collections.Generic.List<string>();

        if (System.Runtime.Intrinsics.X86.Avx512F.IsSupported)
            caps.Add("AVX-512F");
        if (System.Runtime.Intrinsics.X86.Avx512BW.IsSupported)
            caps.Add("AVX-512BW");
        if (System.Runtime.Intrinsics.X86.Avx2.IsSupported)
            caps.Add("AVX2");
        if (System.Runtime.Intrinsics.Arm.AdvSimd.IsSupported)
            caps.Add("ARM-NEON");
        if (System.Runtime.Intrinsics.X86.Sse2.IsSupported)
            caps.Add("SSE2");

        var parser = _parser.GetType().Name;

        return caps.Count > 0
            ? $"SIMD: {string.Join(", ", caps)} | Using: {parser}"
            : $"No SIMD support | Using: {parser}";
#else
        return $"Using: {_parser.GetType().Name} (netstandard - no SIMD)";
#endif
    }
}
