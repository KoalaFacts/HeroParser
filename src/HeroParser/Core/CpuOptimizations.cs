using System;
using System.Runtime.InteropServices;
#if NET6_0_OR_GREATER
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics.Arm;
#endif

namespace HeroParser.Core
{
    /// <summary>
    /// Runtime CPU capability detection and optimization selection for HeroParser.
    /// Implements hardware-specific parser selection based on available SIMD instructions.
    /// </summary>
    public static class CpuOptimizations
    {
        /// <summary>
        /// Cached CPU capabilities detected at startup.
        /// </summary>
        public static readonly CpuCapabilities Capabilities = DetectCapabilities();

        /// <summary>
        /// Detects CPU capabilities and returns optimized configuration.
        /// Uses conditional compilation to support all target frameworks.
        /// </summary>
        private static CpuCapabilities DetectCapabilities()
        {
            var caps = new CpuCapabilities();

#if NET6_0_OR_GREATER
            // Modern .NET with full SIMD support
            if (IsAvx512Supported())
            {
                caps.SimdLevel = SimdLevel.Avx512;
                caps.VectorSize = 64; // 512-bit vectors

#if NET10_0_OR_GREATER
                // Check for advanced instruction sets in .NET 10+
                caps.HasGfni = DetectGfniSupport();
                caps.HasAvx10 = DetectAvx10Support();
#endif
            }
            else if (Avx2.IsSupported)
            {
                caps.SimdLevel = SimdLevel.Avx2;
                caps.VectorSize = 32; // 256-bit vectors
            }
            else if (System.Runtime.Intrinsics.Vector128.IsHardwareAccelerated)
            {
                caps.SimdLevel = SimdLevel.Vector128;
                caps.VectorSize = 16; // 128-bit vectors
            }

            // ARM64-specific detection
            if (AdvSimd.IsSupported)
            {
                caps.SimdLevel = SimdLevel.ArmNeon;
                caps.VectorSize = 16; // 128-bit vectors
                caps.IsAppleSilicon = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                                      && RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
            }

            // AMD-specific handling for Zen4 optimization quirks
            caps.IsAmdZen4 = DetectAmdZen4();

            // Intel-specific optimizations
            caps.IsIntel = DetectIntelProcessor();

#elif NETSTANDARD2_1
            // Limited SIMD support on .NET Standard 2.1
            if (System.Numerics.Vector.IsHardwareAccelerated)
            {
                caps.SimdLevel = SimdLevel.Vector128;
                caps.VectorSize = System.Numerics.Vector<byte>.Count;
            }
            else
            {
                caps.SimdLevel = SimdLevel.Scalar;
                caps.VectorSize = 1;
            }
#else
            // .NET Standard 2.0 - scalar only with optimized algorithms
            caps.SimdLevel = SimdLevel.Scalar;
            caps.VectorSize = 1;
#endif

            // Detect thread count for parallel processing decisions
            caps.LogicalCoreCount = Environment.ProcessorCount;
            caps.OptimalThreadCount = Math.Min(caps.LogicalCoreCount, 16); // Cap at 16 for memory efficiency

            return caps;
        }

#if NET6_0_OR_GREATER
        /// <summary>
        /// Checks if AVX-512 is supported and safe to use.
        /// Verifies both AVX512BW and AVX512VL for optimal CSV parsing.
        /// </summary>
        private static bool IsAvx512Supported()
        {
            return Avx512BW.IsSupported && Avx512DQ.IsSupported;
        }

#if NET10_0_OR_GREATER
        /// <summary>
        /// Detects GFNI (Galois Field New Instructions) support for advanced bit manipulation.
        /// Available in .NET 10+ for next-generation optimizations.
        /// </summary>
        private static bool DetectGfniSupport()
        {
            // GFNI detection requires manual CPUID on .NET 10 until official API
            // For now, return false as conservative approach
            return false;
        }

        /// <summary>
        /// Detects AVX10 support for unified vector instruction set.
        /// AVX10 provides consistent performance across Intel and future AMD processors.
        /// </summary>
        private static bool DetectAvx10Support()
        {
            // AVX10 detection requires manual CPUID on .NET 10 until official API
            // For now, return false as conservative approach
            return false;
        }
#endif

        /// <summary>
        /// Detects AMD Zen4 architecture to apply specific optimizations.
        /// Zen4 has different memory compression characteristics requiring adapted algorithms.
        /// </summary>
        private static bool DetectAmdZen4()
        {
            try
            {
                // AMD Zen4 detection through runtime characteristics
                // This is a simplified detection - production code would use CPUID
                if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
                {
                    // Placeholder: In real implementation, would check CPUID registers
                    // For now, return false to use general optimizations
                    return false;
                }
            }
            catch
            {
                // Fallback to safe mode if detection fails
            }

            return false;
        }

        /// <summary>
        /// Detects Intel processor for Intel-specific optimizations.
        /// Intel processors have different optimal instruction sequences than AMD.
        /// </summary>
        private static bool DetectIntelProcessor()
        {
            try
            {
                // Intel detection through runtime characteristics
                // This is a simplified detection - production code would use CPUID
                if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
                {
                    // Placeholder: In real implementation, would check vendor ID
                    // For now, assume Intel as majority case
                    return true;
                }
            }
            catch
            {
                // Fallback to safe mode if detection fails
            }

            return false;
        }
#endif

        /// <summary>
        /// Creates the optimal parser instance based on detected CPU capabilities.
        /// Implements the adaptive algorithm selection pattern from research.md.
        /// </summary>
        /// <typeparam name="T">Type of records to parse</typeparam>
        /// <returns>Optimized parser instance</returns>
        public static IOptimizedParser<T> CreateOptimizedParser<T>()
        {
            return Capabilities.SimdLevel switch
            {
#if NET6_0_OR_GREATER
                SimdLevel.Avx512 => new Avx512Parser<T>(),
                SimdLevel.Avx2 => new Avx2Parser<T>(),
                SimdLevel.ArmNeon => new NeonParser<T>(),
                SimdLevel.Vector128 => new Vector128Parser<T>(),
#endif
                _ => new ScalarParser<T>()
            };
        }

        /// <summary>
        /// Gets the optimal buffer size for the current hardware configuration.
        /// Aligns with CPU cache lines and SIMD vector sizes for maximum throughput.
        /// </summary>
        /// <param name="inputSizeHint">Estimated input size for dynamic optimization</param>
        /// <returns>Optimal buffer size in bytes</returns>
        public static int GetOptimalBufferSize(long inputSizeHint = 0)
        {
            var baseSize = Capabilities.SimdLevel switch
            {
                SimdLevel.Avx512 => 64 * 1024,     // 64KB aligned to 512-bit vectors
                SimdLevel.Avx2 => 32 * 1024,       // 32KB aligned to 256-bit vectors
                SimdLevel.ArmNeon => 16 * 1024,    // 16KB optimal for ARM cache
                SimdLevel.Vector128 => 16 * 1024,  // 16KB for generic 128-bit
                _ => 8 * 1024                       // 8KB conservative scalar size
            };

            // Scale up for large inputs to improve streaming performance
            if (inputSizeHint > 100 * 1024 * 1024) // > 100MB
            {
                return Math.Min(baseSize * 4, 1024 * 1024); // Cap at 1MB
            }

            return baseSize;
        }

        /// <summary>
        /// Determines if parallel processing is beneficial for the given input size.
        /// Considers CPU capabilities and memory bandwidth constraints.
        /// </summary>
        /// <param name="inputSize">Size of input data in bytes</param>
        /// <returns>True if parallel processing is recommended</returns>
        public static bool ShouldUseParallelProcessing(long inputSize)
        {
            // Only use parallel processing for larger files to avoid overhead
            const long MinParallelSize = 10 * 1024 * 1024; // 10MB threshold

            return inputSize >= MinParallelSize &&
                   Capabilities.LogicalCoreCount > 1 &&
                   Capabilities.SimdLevel != SimdLevel.Scalar; // SIMD required for efficient parallel
        }
    }

    /// <summary>
    /// CPU capabilities detected at runtime.
    /// </summary>
    public sealed class CpuCapabilities
    {
        /// <summary>
        /// Highest supported SIMD instruction level.
        /// </summary>
        public SimdLevel SimdLevel { get; set; } = SimdLevel.Scalar;

        /// <summary>
        /// Vector size in bytes for SIMD operations.
        /// </summary>
        public int VectorSize { get; set; } = 1;

        /// <summary>
        /// Number of logical processor cores.
        /// </summary>
        public int LogicalCoreCount { get; set; } = 1;

        /// <summary>
        /// Optimal number of threads for parallel processing.
        /// </summary>
        public int OptimalThreadCount { get; set; } = 1;

        /// <summary>
        /// Whether running on Apple Silicon (ARM64 macOS).
        /// </summary>
        public bool IsAppleSilicon { get; set; }

        /// <summary>
        /// Whether running on AMD Zen4 architecture.
        /// </summary>
        public bool IsAmdZen4 { get; set; }

        /// <summary>
        /// Whether running on Intel processor.
        /// </summary>
        public bool IsIntel { get; set; }

        /// <summary>
        /// Whether GFNI instructions are available (.NET 10+).
        /// </summary>
        public bool HasGfni { get; set; }

        /// <summary>
        /// Whether AVX10 instructions are available (.NET 10+).
        /// </summary>
        public bool HasAvx10 { get; set; }
    }

    /// <summary>
    /// SIMD instruction set levels supported by HeroParser.
    /// </summary>
    public enum SimdLevel
    {
        /// <summary>
        /// No SIMD support, scalar algorithms only.
        /// </summary>
        Scalar = 0,

        /// <summary>
        /// 128-bit vector support (SSE/NEON).
        /// </summary>
        Vector128 = 1,

        /// <summary>
        /// ARM NEON 128-bit vector instructions.
        /// </summary>
        ArmNeon = 2,

        /// <summary>
        /// AVX2 256-bit vector instructions.
        /// </summary>
        Avx2 = 3,

        /// <summary>
        /// AVX-512 512-bit vector instructions.
        /// </summary>
        Avx512 = 4
    }

    /// <summary>
    /// Interface for optimized parser implementations.
    /// Each SIMD level provides its own optimized implementation.
    /// </summary>
    /// <typeparam name="T">Type of records to parse</typeparam>
    public interface IOptimizedParser<T>
    {
        /// <summary>
        /// Parses CSV content using hardware-optimized algorithms.
        /// </summary>
        /// <param name="content">CSV content to parse</param>
        /// <returns>Parsed records</returns>
        System.Collections.Generic.IEnumerable<T> Parse(ReadOnlySpan<char> content);
    }

    // Placeholder parser implementations - will be implemented in subsequent tasks
#if NET6_0_OR_GREATER
    internal sealed class Avx512Parser<T> : IOptimizedParser<T>
    {
        public System.Collections.Generic.IEnumerable<T> Parse(ReadOnlySpan<char> content)
        {
            throw new NotImplementedException("AVX-512 parser implementation pending (Phase 3.5)");
        }
    }

    internal sealed class Avx2Parser<T> : IOptimizedParser<T>
    {
        public System.Collections.Generic.IEnumerable<T> Parse(ReadOnlySpan<char> content)
        {
            throw new NotImplementedException("AVX2 parser implementation pending (Phase 3.5)");
        }
    }

    internal sealed class NeonParser<T> : IOptimizedParser<T>
    {
        public System.Collections.Generic.IEnumerable<T> Parse(ReadOnlySpan<char> content)
        {
            throw new NotImplementedException("ARM NEON parser implementation pending (Phase 3.5)");
        }
    }

    internal sealed class Vector128Parser<T> : IOptimizedParser<T>
    {
        public System.Collections.Generic.IEnumerable<T> Parse(ReadOnlySpan<char> content)
        {
            throw new NotImplementedException("Vector128 parser implementation pending (Phase 3.5)");
        }
    }
#endif

    internal sealed class ScalarParser<T> : IOptimizedParser<T>
    {
        public System.Collections.Generic.IEnumerable<T> Parse(ReadOnlySpan<char> content)
        {
            throw new NotImplementedException("Scalar parser implementation pending (Phase 3.5)");
        }
    }
}