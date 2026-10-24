# HTB / CSV reading benchmark checksum validity — 2026-10-25

## Why the old comparison was insufficient

`HtbBenchmark` generates records with IDs 0 through R-1, including a 100,000-row workload.
The expected sum is `R * (R - 1) / 2`: **4,999,950,000** at 100,000 rows,
which exceeds `Int32.MaxValue`. The three timed reader methods originally returned
`int` sums, so the large case silently wrapped even when every row was read.

## Repair

- The timed CSV stream, HTB stream, and HTB asynchronous file readers now accumulate
  and return `long` / `Task<long>`; no source parser implementation is altered.
- Global setup compares **every** decoded ID, Name, Amount, and Active value against
  the independently generated source rows for both in-memory CSV and HTB reads;
  it also checks row counts and the actual timed-method checksums before measuring.
- xUnit integration tests call the three real timed methods at 7 and 100,000 rows,
  checking the independently computed 64-bit sum and return types.
- HTB asynchronous file reading is tested after the fixture has been written,
  using the benchmark's actual timed method. File I/O remains inside that method.

This is benchmark correctness work, **not** evidence of speedup or reduced memory
allocation. Any new throughput claim must be supported with exact source and assembly
hashes, the same bytes and logical work, runtime/CPU information, all raw paired
samples, stable same-source A/A controls, and a prespecified A/B analysis.

The separate historical CSV Pipe timing gate remains FAILED with sampling budget
exhausted; this fix neither changes its data nor requests a new sampling allowance.
