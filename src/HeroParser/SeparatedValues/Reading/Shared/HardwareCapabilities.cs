using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace HeroParser.SeparatedValues.Reading.Shared;

/// <summary>
/// Indirection over SIMD hardware intrinsics (<see cref="Avx2"/>, <see cref="Avx512BW"/>,
/// <see cref="Pclmulqdq"/>, <see cref="AdvSimd.Arm64"/>, and portable <see cref="Vector128"/>)
/// so that tests can disable individual SIMD branches on otherwise-capable hardware to validate
/// fallback paths.
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
internal readonly struct HardwareCapabilities
{
    private static int activeOverrideCount;
    private static readonly AsyncLocal<bool?> avx2Override = new();
    private static readonly AsyncLocal<bool?> avx512BWOverride = new();
    private static readonly AsyncLocal<bool?> pclmulqdqOverride = new();
    private static readonly AsyncLocal<bool?> advSimdOverride = new();
    private static readonly AsyncLocal<bool?> vector128Override = new();

    /// <summary>Gets the current hardware capabilities view.</summary>
    internal static HardwareCapabilities Current => default;

    /// <summary>True when AVX2 instructions can be used; mirrors <see cref="Avx2.IsSupported"/> unless a test override is active.</summary>
    internal bool Avx2IsSupported
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => activeOverrideCount > 0 ? (avx2Override.Value ?? Avx2.IsSupported) : Avx2.IsSupported;
    }

    /// <summary>True when AVX-512BW instructions can be used; mirrors <see cref="Avx512BW.IsSupported"/> unless a test override is active.</summary>
    internal bool Avx512BWIsSupported
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => activeOverrideCount > 0 ? (avx512BWOverride.Value ?? Avx512BW.IsSupported) : Avx512BW.IsSupported;
    }

    /// <summary>True when PCLMULQDQ instructions can be used; mirrors <see cref="Pclmulqdq.IsSupported"/> unless a test override is active.</summary>
    internal bool PclmulqdqIsSupported
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => activeOverrideCount > 0 ? (pclmulqdqOverride.Value ?? Pclmulqdq.IsSupported) : Pclmulqdq.IsSupported;
    }

    /// <summary>True when ARM64 AdvSimd instructions can be used; mirrors <see cref="AdvSimd.Arm64.IsSupported"/> unless a test override is active.</summary>
    internal bool AdvSimdIsSupported
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => activeOverrideCount > 0 ? (advSimdOverride.Value ?? AdvSimd.Arm64.IsSupported) : AdvSimd.Arm64.IsSupported;
    }

    /// <summary>True when hardware-accelerated 128-bit SIMD vectors can be used; mirrors <see cref="Vector128.IsHardwareAccelerated"/> unless a test override is active.</summary>
    internal bool Vector128IsSupported
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => activeOverrideCount > 0 ? (vector128Override.Value ?? Vector128.IsHardwareAccelerated) : Vector128.IsHardwareAccelerated;
    }

    /// <summary>
    /// Test hook: temporarily forces specific SIMD capabilities to a chosen value.
    /// </summary>
    /// <param name="avx2">When non-null, overrides <see cref="Avx2IsSupported"/>.</param>
    /// <param name="avx512BW">When non-null, overrides <see cref="Avx512BWIsSupported"/>.</param>
    /// <param name="pclmulqdq">When non-null, overrides <see cref="PclmulqdqIsSupported"/>.</param>
    /// <param name="advSimd">When non-null, overrides <see cref="AdvSimdIsSupported"/>.</param>
    /// <param name="vector128">When non-null, overrides <see cref="Vector128IsSupported"/>.</param>
    /// <returns>An <see cref="IDisposable"/> that restores the previous overrides when disposed.</returns>
    /// <remarks>
    /// Overrides only constrain SIMD branches downward — forcing <c>true</c> on a CPU that
    /// genuinely lacks the instruction set will still trap when the SIMD code executes,
    /// so tests should only force capabilities to <c>true</c> on hardware that actually
    /// supports them (guard the call with the corresponding <c>IsSupported</c> check).
    /// </remarks>
    internal static IDisposable Override(
        bool? avx2 = null,
        bool? avx512BW = null,
        bool? pclmulqdq = null,
        bool? advSimd = null,
        bool? vector128 = null)
    {
        Interlocked.Increment(ref activeOverrideCount);
        var scope = new ResetScope(
            avx2Override.Value,
            avx512BWOverride.Value,
            pclmulqdqOverride.Value,
            advSimdOverride.Value,
            vector128Override.Value);
        if (avx2 is not null) avx2Override.Value = avx2;
        if (avx512BW is not null) avx512BWOverride.Value = avx512BW;
        if (pclmulqdq is not null) pclmulqdqOverride.Value = pclmulqdq;
        if (advSimd is not null) advSimdOverride.Value = advSimd;
        if (vector128 is not null) vector128Override.Value = vector128;
        return scope;
    }

    private sealed class ResetScope(
        bool? prevAvx2,
        bool? prevAvx512BW,
        bool? prevPclmulqdq,
        bool? prevAdvSimd,
        bool? prevVector128) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            avx2Override.Value = prevAvx2;
            avx512BWOverride.Value = prevAvx512BW;
            pclmulqdqOverride.Value = prevPclmulqdq;
            advSimdOverride.Value = prevAdvSimd;
            vector128Override.Value = prevVector128;
            Interlocked.Decrement(ref activeOverrideCount);
        }
    }
}
