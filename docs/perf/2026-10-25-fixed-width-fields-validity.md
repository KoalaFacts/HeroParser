# Fixed-width individual-field benchmark validity — 2026-10-25

## Old fixture defect

The 55-character fixture in `FixedWidthFieldParseBenchmarks` used two spaces
before the 19-character ISO timestamp, even though its date field begins at
offset 20 and is exactly 20 characters long. The timestamp's last character
(`6` in the seconds component) therefore spilled into the five-character
boolean field. The effective boolean field was `6TRUE`, not `TRUE `, and
could not be parsed as the intended true value. Returning only the date's
minute could not prove that the seconds had been preserved.

The fixture also includes `4294967295`, a valid `uint` value. The two timed
integer paths cast it to `int`, silently yielding -1 in the unchecked
configuration. That is a truncated checksum even if the parser itself reads
all input correctly.

## Corrected workload

- Each input column is now constructed at its exact width:
  ID 10, UInt32 10, date 20, boolean 5, double 10 (total 55).
  The date value is precisely `2025-11-20T12:34:56`, followed by one pad
  character **inside** its allocated 20-character column.
- `ParseAllFields` and `ParseIntsOnly` now accumulate in `long` and
  preserve the full unsigned value without narrowing to `int`.
  Their expected checksums are respectively **4,294,967,423** and
  **4,294,967,418**.
- Date and floating-point parsing use `CultureInfo.InvariantCulture` so the
  intended fixture means the same thing on different runners.
  `ParseDateTimeOnly` returns the second (56) rather than the minute (34):
  the old truncated timestamp can no longer masquerade as a valid result.
  `ParseDoubleOnly` still returns 3.
- `GlobalSetup` checks the exact fixture shape and invokes the actual timed
  methods before any benchmark sample. The xUnit benchmark regression asserts
  all four expected outputs and the 64-bit result types.

The test was committed before the correction, but **the failing state was not
executed locally** (no .NET SDK is installed in this tool runtime). Remote CI
for the exact final head must establish the compile/test result; do not count
the prior head's successful checks as validation.

These are benchmark and test changes only. They do not change the shipped
parser, prove a speedup, or validate any historical timing gate. The corrected
bytes, explicit culture and checksum work mean earlier numbers are not
directly comparable; establish a new same-runner baseline before performance
claims, with complete raw samples, stable A/A controls, and paired A/B.

The separate frozen CSV Pipe timing-acceptance gate remains **FAILED** and
its sampling budget is exhausted. Do not modify its thresholds, evidence, or
policy, or retry its original experiment.
