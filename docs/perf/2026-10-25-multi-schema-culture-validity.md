# Multi-schema benchmark locale and checksum validity — 2026-10-25

## Failure mode

The `MultiSchemaBenchmarks` fixtures built decimal text with interpolated `i * 10.5m`, which uses the current culture. Under `fr-FR` (or other comma-decimal cultures), a value such as `10.5` becomes `10,5`, introducing an unintended CSV delimiter. This corrupts the intended four-column header/detail/trailer workload; a benchmark could fail or silently measure malformed input. This was a fixture defect, not evidence of a production parser defect or performance regression.

## Correction

- Four decimal-bearing detail/trailer lines now use `FormattableString.Invariant`, so the same source rows are generated regardless of the host culture. Header and all other source strings are unchanged.
- `GlobalSetup` checks the actual timed single-schema method against `Rows * (Rows - 1) / 2`. All four actual multi-schema methods (single-char discriminator, three-char discriminator, index-based and source-generated dispatch) must return the independently calculated truncated decimal sum `sum(21 * i / 2)` for `i = 0 .. Rows - 3`. These preflight calls are outside timing and fail closed if a reader omits data.
- The xUnit v3 integration regression runs these real timed methods for 1,000 and 10,000 rows with the current culture set to `fr-FR`, restoring it in a `finally` block. The test was committed first as `eb263fae343d6fd6d3fb5693b359ebf4d84d064b`; the old fixture would emit embedded commas, so it cannot satisfy the intended invariant four-column workload.

## Interpretation and limits

This is a **benchmark correctness repair**, not a claim that parser throughput or allocations improved. The old comma-decimal-culture fixture and the corrected one are different byte workloads; use a fresh baseline if comparing on those hosts. The single-schema and multi-schema cases also have different semantic records (all details versus a header/detail/trailer hierarchy), so their timings are not a fair equal-workload library ranking. The timed checksum is intentionally a truncated `decimal` amount sum; it does not prove all fields were materialized equivalently. No production parser, dependency, acceptance threshold, historical CSV Pipe samples or experiment budget was changed.

At authoring time the execution environment did not have `dotnet` or `pwsh`, so no local C# red/green, analyzer or runtime benchmark result is claimed. The patch must be verified on its exact new HEAD by GitHub's .NET 8/9/10 MTP tests, strict formatting/build matrix, and independent review before merge.
