using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;

namespace HeroParser.SeparatedValues.Reading.Shared;

/// <summary>
/// Indirection over <see cref="Avx2.IsSupported"/>, <see cref="Avx512BW.IsSupported"/>,
/// and <see cref="Pclmulqdq.IsSupported"/> so that tests can disable individual SIMD
/// branches on otherwise-capable hardware to validate fallback paths.
/// </summary>
/// <remarks>
/// In production no overrides are set, so the JIT can still fold the property reads
/// to compile-time constants for the default <c>IsSupported</c> values.
/// </remarks>
internal static class HardwareCapabilities
{
    private static bool? avx2Override;
    private static bool? avx512BWOverride;
    private static bool? pclmulqdqOverride;

    /// <summary>True when AVX2 instructions can be used; mirrors <see cref="Avx2.IsSupported"/> unless a test override is active.</summary>
    public static bool Avx2IsSupported
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => avx2Override ?? Avx2.IsSupported;
    }

    /// <summary>True when AVX-512BW instructions can be used; mirrors <see cref="Avx512BW.IsSupported"/> unless a test override is active.</summary>
    public static bool Avx512BWIsSupported
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => avx512BWOverride ?? Avx512BW.IsSupported;
    }

    /// <summary>True when PCLMULQDQ instructions can be used; mirrors <see cref="Pclmulqdq.IsSupported"/> unless a test override is active.</summary>
    public static bool PclmulqdqIsSupported
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => pclmulqdqOverride ?? Pclmulqdq.IsSupported;
    }

    /// <summary>
    /// Test hook: temporarily forces specific SIMD capabilities to a chosen value.
    /// </summary>
    /// <param name="avx2">When non-null, overrides <see cref="Avx2IsSupported"/>.</param>
    /// <param name="avx512BW">When non-null, overrides <see cref="Avx512BWIsSupported"/>.</param>
    /// <param name="pclmulqdq">When non-null, overrides <see cref="PclmulqdqIsSupported"/>.</param>
    /// <returns>An <see cref="IDisposable"/> that restores the previous overrides when disposed.</returns>
    /// <remarks>
    /// Overrides only constrain SIMD branches downward — forcing <c>true</c> on a CPU that
    /// genuinely lacks the instruction set will still trap when the SIMD code executes,
    /// so tests should only force capabilities to <c>false</c> for fallback validation.
    /// </remarks>
    internal static IDisposable Override(bool? avx2 = null, bool? avx512BW = null, bool? pclmulqdq = null)
    {
        var scope = new ResetScope(avx2Override, avx512BWOverride, pclmulqdqOverride);
        if (avx2 is not null) avx2Override = avx2;
        if (avx512BW is not null) avx512BWOverride = avx512BW;
        if (pclmulqdq is not null) pclmulqdqOverride = pclmulqdq;
        return scope;
    }

    private sealed class ResetScope(bool? prevAvx2, bool? prevAvx512BW, bool? prevPclmulqdq) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            avx2Override = prevAvx2;
            avx512BWOverride = prevAvx512BW;
            pclmulqdqOverride = prevPclmulqdq;
        }
    }
}
