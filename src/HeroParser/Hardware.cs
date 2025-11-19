namespace HeroParser;

/// <summary>
/// Factory for selecting the optimal SIMD parser based on hardware capabilities.
/// </summary>
public static class Hardware
{
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

        return caps.Count > 0
            ? $"SIMD: {string.Join(", ", caps)}"
            : $"No SIMD support";
    }
}
