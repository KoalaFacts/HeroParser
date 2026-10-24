# Fixed-width typed PipeReader benchmark fixture validity — 2026-10-25

## Root cause

The `FixedWidthTypedPipeReaderBenchmarks` fixture generated IDs using
`{i:D4}`. `D4` specifies a **minimum**, not a maximum, of four digits. At
record 10,000 the ID therefore occupied five bytes; at record 100,000 it
occupied six. The benchmark map continued to treat the ID as four characters
and the Name as the following six. From record 10,000 onwards, fields were
shifted/truncated. This was a benchmark input/mapping defect, not evidence
of a production parser failure.

The prior timed methods also returned `int` checksums. For the intended
100,000-record IDs 1..100,000, the ID sum alone is 5,000,050,000 and exceeds
`Int32.MaxValue`. Results could wrap even without the field corruption.

## Repair and contract

- Use a fixed six-digit ID (`000001` to `100000`) and a fixed seven-character
  Name (`N000001` to `N100000`) for every row. The file has no header.
- Update both source-generated `[PositionalMap]` attributes and the explicit
  `FixedWidthMap`: ID starts at 0 and spans 6, Name starts at 6 and spans 7.
- In `GlobalSetup`, read every generated logical record and validate its
  exact ID, complete Name, and total row count before any timed operation.
- Accumulate all four timed reader variants with `long` / `Task<long>`.
  The independent checksum for `R` rows is
  `R * (R + 1) / 2 + 7 * R`, evaluated with 64-bit arithmetic.
  For 100,000 rows the expected result is **5,000,750,000**.
- An integration regression invokes the actual text/source-generated, static
  PipeReader, builder PipeReader, and explicit-map PipeReader benchmark methods
  for 10,000 and 100,000 records and checks the independent checksum.

This is **a measurement-validity repair**, not a parser speedup. It changes
fixture bytes and the amount/shape of input for the fixed-width typed
benchmark. Do not compare the corrected timings to the old timings to claim
a performance gain or regression. New performance claims require complete
source/assembly fingerprints, equivalent input and logical work, paired
runs on the same runtime/hardware, stable A/A controls, and raw samples.

The separate historical CSV Pipe timing gate remains FAILED with its
sampling budget exhausted. This repair does not alter or waive that gate.

## Independent fixed-width record binding checksum (2026-10-25)

The separate `FixedWidthBenchmarks` binding fixture generates IDs 1..R and
integer Values equal to 100 times each ID. Its two timed methods
(`BindToRecords_SourceGenerated` and `BindToRecords_ForEach`) previously
accumulated an `int` checksum. The expected total is
`101 * R * (R + 1) / 2`, evaluated as a 64-bit value. At 10,000 records
this is **5,050,505,000**; at 100,000 records it is **505,005,050,000**,
both above `Int32.MaxValue`.

The timed methods now return `long` and accumulate with 64-bit totals.
Before any timing, setup checks every ID, complete Name (ignoring field
padding), Value, record count, and both actual timed checksums. A new
xUnit integration regression invokes both timed methods at 1,000, 10,000
and 100,000 rows and compares them against the independent formula.

The typed fixture bytes and parser API are unchanged, but the previous
wrapped 32-bit benchmark results were not trustworthy as correctness
checks. This is a **benchmark validity fix, not a parser optimization**.
Any timing comparison after the result-type change needs new, controlled
same-runner evidence; no throughput gain is claimed. The frozen historical
CSV Pipe gate remains FAILED and is not modified by this work.
