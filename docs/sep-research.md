# Sep Parser Research & Migration Plan

> **Note**: This document captures the research and learning from studying the [Sep CSV parser](https://github.com/nietras/Sep) by nietras, which served as the primary inspiration for HeroParser's SIMD architecture. Sep is one of the fastest CSV parsers for .NET and pioneered many of the techniques used in modern high-performance CSV parsing.

## References

- **[Sep GitHub Repository](https://github.com/nietras/Sep)** - The primary reference for SIMD CSV parsing techniques
- Key source files studied:
  - `src/Sep/Internals/SepParseMask.cs` - Primary SIMD state machine
  - `src/Sep/Internals/SepMaskOperations.cs` - Helper routines for quote masks, prefix XOR, and newline detection

## Key Findings From Sep

1. **Single Streaming Pass**  
   Sep feeds the parser with UTF-8 bytes and keeps one SIMD loop that simultaneously detects commas, quotes, and CR/LF. The loop maintains `quoteCount` (parity) so delimiters inside quotes are ignored. When a newline hits, the parser already has every column boundary (see `ParseSeparatorsLineEndingsMasks` around lines 128‑223).  
   → Our reader currently performs *two* scans per row (newline search then column parse) and even the “single-pass” experiment still used UTF‑16 packing. We need the same streaming mask approach.

2. **Byte-Oriented Data**  
   Sep never packs UTF‑16. The parser reads `Span<byte>` and relies on `MoveMask`/bitmasks over the byte lanes. Packing our UTF‑16 chars to bytes (`PackUnsignedSaturate` + `Permute4x64`) costs ~30‑40% of the per-row work.  
   → Introduce a byte-oriented path (`Csv.Parse(ReadOnlySpan<byte> utf8)`) and transcode strings once into a pooled buffer.

3. **Minimal Lazy State**  
   Sep records column offsets immediately; there is no lazy “parse later” path beyond optional projection. All consumers pay one pass. Our ColumnCount/Adaptive knobs add branches and require us to keep `_forceEagerParsing`.  
   → Default to eager streaming; keep lazy mode only if demanded and implement it as “don’t materialize spans”, not “rerun the parser”.

4. **Vector-Friendly Quote Masks**  
   Sep precomputes quote masks per vector and stores carries in `SepParseMask`. Carry propagation lets the next block know if it starts inside quotes.  
   → Our new parser must maintain cross-vector state (1 bit) and reuse it between iterations.

5. **Row metadata + newline handling**  
   In Sep every row stores `(startIndex, quoteCount)` plus flags for CR/LF. `ParseSeparatorLineEndingChar` (lines 226‑239) shows how CRLF is handled without rescanning the text.  
   → Move CR/LF handling into the streaming parser and stop special-casing CRLF in `CsvCharSpanReader`.
