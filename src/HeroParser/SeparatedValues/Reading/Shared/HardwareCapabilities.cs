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
/// Overrides are stored in <see cref="AsyncLocal{T}"/> so concurrent tests in
/// different xUnit collections don't pollute each other's view of CPU capabilities —
/// critical on platforms where the underlying intrinsic is genuinely unavailable
/// (e.g. ARM macOS, where forcing <c>Avx2 = true</c> from one test would otherwise
/// trap any parallel test that hits the AVX2 SIMD branch).
/// </remarks>
internal static class HardwareCapabilities
{
    private static readonly AsyncLocal<bool?> avx2Override = new();
    private static readonly AsyncLocal<bool?> avx512BWOverride = new();
    private static readonly AsyncLocal<bool?> pclmulqdqOverride = new();

    /// <summary>True when AVX2 instructions can be used; mirrors <see cref="Avx2.IsSupported"/> unless a test override is active.</summary>
    public static bool Avx2IsSupported
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => avx2Override.Value ?? Avx2.IsSupported;
    }

    /// <summary>True when AVX-512BW instructions can be used; mirrors <see cref="Avx512BW.IsSupported"/> unless a test override is active.</summary>
    public static bool Avx512BWIsSupported
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => avx512BWOverride.Value ?? Avx512BW.IsSupported;
    }

    /// <summary>True when PCLMULQDQ instructions can be used; mirrors <see cref="Pclmulqdq.IsSupported"/> unless a test override is active.</summary>
    public static bool PclmulqdqIsSupported
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => pclmulqdqOverride.Value ?? Pclmulqdq.IsSupported;
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
    /// so tests should only force capabilities to <c>true</c> on hardware that actually
    /// supports them (guard the call with the corresponding <c>IsSupported</c> check).
    /// </remarks>
    internal static IDisposable Override(bool? avx2 = null, bool? avx512BW = null, bool? pclmulqdq = null)
    {
        var scope = new ResetScope(avx2Override.Value, avx512BWOverride.Value, pclmulqdqOverride.Value);
        if (avx2 is not null) avx2Override.Value = avx2;
        if (avx512BW is not null) avx512BWOverride.Value = avx512BW;
        if (pclmulqdq is not null) pclmulqdqOverride.Value = pclmulqdq;
        return scope;
    }

    private sealed class ResetScope(bool? prevAvx2, bool? prevAvx512BW, bool? prevPclmulqdq) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            avx2Override.Value = prevAvx2;
            avx512BWOverride.Value = prevAvx512BW;
            pclmulqdqOverride.Value = prevPclmulqdq;
        }
    }
}
