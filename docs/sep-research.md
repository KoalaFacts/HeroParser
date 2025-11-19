# Sep Parser Research & Migration Plan

## References

- `tempSep/src/Sep/Internals/SepParseMask.cs` (commit `c6b2b200e` copied into `tempSep/`) – primary SIMD state machine.
- `tempSep/src/Sep/Internals/SepMaskOperations.cs` – helper routines for quote masks, prefix XOR, and newline detection.

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

## Migration Plan

1. **Add UTF-8 entry point**
   - `Csv.ParseUtf8(ReadOnlySpan<byte> data, CsvParserOptions? options = null)` returning a reader that treats input as bytes.
   - Add `Utf8Buffer` helper to convert `string` input once per parse (pool-backed).

2. **Implement Streaming SIMD Parser**
   - Start with AVX2 (32 bytes). Follow Sep’s pattern: load bytes, compute delimiter mask, quote mask, newline mask, run prefix XOR for quotes, mask out delimiters inside quotes, and process the masks in order.
   - API: `ParseRow` returning `(columnCount, rowLength, charsConsumed)`.
   - Scalar fallback can share the same method signature (already coded).

3. **Reader Integration**
   - Replace `FindLineEnd` + `ParseLine` with a single call to `ParseRow`. `_position` advances by `charsConsumed`.
   - Remove `_rowBoundaries` batching; the streaming parser already amortizes work. (We can reintroduce batching later if needed.)
   - Drop `_forceEagerParsing`. All rows are eagerly parsed; lazy mode becomes “do not slice spans unless user touches them”.

4. **Benchmark Alignment**
   - Update `VsSepBenchmarks` to optionally feed UTF‑8 spans and ensure both Sep and HeroParser parse the same binary input.
   - Add micro-benchmarks for the SIMD parser alone to validate GB/s throughput.

5. **Validation**
   - Build a temporary harness mirroring Sep’s tests (quoted, CRLF, embedded quotes).
   - Track perf before/after each phase and keep the doc updated with results.

## Next Steps

### 1. Prototype Streaming Parser (isolated console app)
- [ ] Create `benchmarks/StreamingPrototype` (net8.0) that accepts `ReadOnlySpan<byte>` and runs an AVX2 streaming loop lifted from Sep.
- [ ] Implement the key routines:
  - `Vector256<byte> ClassifySeparators(...)` returning delimiter/newline masks.
  - Quote prefix computation (bitwise prefix XOR). Verify against Sep’s `_MaskPrefixXor` helper.
  - Mask post-processing to drop delimiters inside quotes.
- [ ] Expose `ParseRow` returning `(rowLength, charsConsumed, columnCount)`; re-use to parse entire buffer.
- [ ] Add tests covering:
  - Plain CSV rows (no quotes).
  - Quoted fields with escaped quotes (`""`).
  - CRLF + LF endings.
  - Empty lines and trailing rows without newline.

### 2. Benchmark Prototype vs. Sep
- [ ] Feed identical UTF‑8 buffers to Sep and the prototype (use `tempSep` as reference).
- [ ] Measure throughput (GB/s) and column count accuracy. Target ≤1.3× gap before integrating.

### 3. Integration Checklist
- [ ] Add `Csv.ParseUtf8` and a pooled UTF‑8 buffer adapter.
- [ ] Update `ISimdParser` to expose `ParseRow` (signature above); keep `ParseColumns` for compatibility until streaming fully replaces it.
- [ ] Replace `CsvCharSpanReader` hot path with the streaming parser, delete `_rowBoundaries`, and simplify lazy mode.
- [ ] Rerun `VsSepBenchmarks` (UTF‑8 mode) and compare ratios.

### 4. Research questions to resolve before coding
- Do we normalize all input to UTF‑8 or keep dual UTF‑16/UTF‑8 paths? (Pros/cons: transcode cost vs. simplicity.)
- Carry handling: Sep keeps the quote carry in a `SepParseMask` struct; where should we store it (parser vs. reader)?
- How do we map Sep’s `SepColInfo` metadata to our `CsvCharSpanRow` buffers? Define exact data we need (start, length, quoteParity).
