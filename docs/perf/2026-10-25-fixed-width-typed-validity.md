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
