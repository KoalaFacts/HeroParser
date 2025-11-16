namespace HeroParser.Simd;

/// <summary>
/// Factory for selecting the optimal SIMD parser based on hardware capabilities.
/// </summary>
public static class SimdParserFactory
{
    // Cached parser instance selected at startup
    private static readonly ISimdParser _parser = SelectParser();

    /// <summary>
    /// Get the optimal parser for the current hardware.
    /// </summary>
    public static ISimdParser GetParser() => _parser;

    private static ISimdParser SelectParser()
    {
        // Priority order: AVX-512 > AVX2 > NEON > Scalar
        // .NET 8+ always has SIMD support

        if (System.Runtime.Intrinsics.X86.Avx512F.IsSupported &&
            System.Runtime.Intrinsics.X86.Avx512BW.IsSupported)
        {
            // Best: AVX-512 processes 64 chars per iteration
            return Avx512Parser.Instance;
        }

        if (System.Runtime.Intrinsics.X86.Avx2.IsSupported)
        {
            // Good: AVX2 processes 32 chars per iteration
            return Avx2Parser.Instance;
        }

        if (System.Runtime.Intrinsics.Arm.AdvSimd.IsSupported)
        {
            // ARM NEON: processes 64 chars per iteration
            return NeonParser.Instance;
        }

        // Fallback: scalar implementation
        return ScalarParser.Instance;
    }

    /// <summary>
    /// Get hardware capabilities summary for diagnostics.
    /// </summary>
    public static string GetHardwareInfo()
    {
        var caps = new List<string>();

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
    }
}
