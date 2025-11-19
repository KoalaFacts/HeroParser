namespace HeroParser;

/// <summary>
/// Provides helper utilities for inspecting hardware capabilities relevant to HeroParser.
/// </summary>
public static class Hardware
{
    /// <summary>
    /// Returns a short, human-readable summary of SIMD instruction sets available on the current process.
    /// </summary>
    /// <remarks>
    /// This is primarily intended for diagnostics and benchmark output so that parsing results can be tied
    /// to the processor features that were enabled.
    /// </remarks>
    /// <returns>A descriptive string such as <c>"SIMD: AVX2, SSE2"</c> or <c>"No SIMD support"</c>.</returns>
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
