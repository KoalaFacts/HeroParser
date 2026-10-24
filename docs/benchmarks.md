# HeroParser Benchmarks & Performance Guide

This guide records benchmark scope, reproducible commands, and historical evidence. The current benchmark project references:
- **Sep** v0.17.1
- **Sylvan.Data.Csv** v1.4.4
- **CsvHelper** v33.1.0

---

Dependency versions are defined in [HeroParser.Benchmarks.csproj](../benchmarks/HeroParser.Benchmarks/HeroParser.Benchmarks.csproj). A benchmark result applies to its recorded source revision, fixture, runtime, and hardware; it is not a guarantee for other APIs or inputs.

The [2026-10-24 validity report](perf/2026-10-24-benchmark-validity.md) records the CSV/JSONL benchmark repairs, characterization tests, and the withdrawn wrapper candidate's complete local diagnostic evidence. It makes no parser speedup claim.

## Historical Benchmark Environment (2026-10-23)

The table below comes from the **Base `cba2bcd`** section of the [PR #156 CI benchmark report](https://github.com/KoalaFacts/HeroParser/pull/156#issuecomment-6355972696), using [this exact fixture](https://github.com/KoalaFacts/HeroParser/blob/cba2bcdbf57597d87a159316e4d06969c1278d01/benchmarks/HeroParser.Benchmarks/VsSepReadingBenchmarks.cs).

- **Runner**: GitHub Actions, Ubuntu 24.04.5 LTS, AMD EPYC 7763, 4 logical / 2 physical cores; x86-64-v3 (AVX2)
- **Runtime**: .NET 10.0.13, SDK 10.0.402, Release build
- **Validation tool**: BenchmarkDotNet v0.15.8
- **Job**: throughput strategy, 3 warmups, 5 measured iterations
- **Dependencies**: Sep 0.17.1, Sylvan.Data.Csv 1.4.4, CsvHelper 33.1.0

All libraries in this table ran in one job on the same runner. GitHub-hosted runner CPU models vary; compare base and head on the same runner with the same fixture and inspect the full report's error and standard deviation before claiming a change.

---

## 1. Historical Row/Column-Count Fixture (10,000 Rows × 25 Columns)

This legacy fixture generated input and UTF-8 bytes in `GlobalSetup`, outside the timed operation. Each method created a reader, iterated rows, and summed column counts. It did not consume every field's logical value, perform typed conversion, materialize application records, or include file/network I/O. `WithQuotes = true` wrapped exactly 50% of simple ASCII `value_r_c` fields in quotes; it did not exercise embedded delimiters, doubled quotes, multiline fields, or Unicode.

The old options were not equivalent: Sep and Sylvan retained their default header handling, CsvHelper explicitly disabled headers, and HeroParser disabled quote parsing for the unquoted case. These measurements are retained as historical observations, **not a fair library ranking**. The corrected fixture uses different options and validation; new results cannot be compared directly with this table to claim a parser speedup.

| Library / input | Unquoted mean | Unquoted allocated | Quoted mean | Quoted allocated |
|---|---:|---:|---:|---:|
| Sep | 632.9 µs | 3,952 B | 1,544.5 µs | 4,048 B |
| Sylvan.Data.Csv | 817.5 µs | 43,529 B | 4,652.1 µs | 43,530 B |
| CsvHelper | 13,497.1 µs | 21,328 B | 15,072.6 µs | 21,328 B |
| HeroParser UTF-8 (`byte[]`) | 588.9 µs | 176 B | 746.9 µs | 176 B |
| HeroParser UTF-16 (`string`) | 666.0 µs | 176 B | 853.0 µs | 176 B |

Allocated bytes are per benchmark operation for this workload, including reader/options setup. They do not imply zero-allocation typed binding or a fixed allocation budget for every input size. Pool growth and different APIs can change the profile. Both UTF-8 and UTF-16 span input are supported directly; avoid an encoding conversion when your input is already in a supported representation.

---

## 2. Writing Benchmarks

Use `--vs-sep-writing` for library comparisons and `--writer`, `--fixed-width-writer`, or `--jsonl` for the corresponding API workloads. Check that all methods produce equivalent output with the same quoting policy and output destination. String-returning, stream-writing, typed, and raw-row APIs perform different work and need separate results.

Earlier versions of this guide listed writing multipliers without a traceable run and complete environment. Those figures are not used as current performance claims. Publish new comparisons with the exact commit, dependency versions, fixtures, runtime, CPU, output validation, and BenchmarkDotNet report.

---

## 3. Write-Path Capacity Pre-allocation Optimization (ToText Facades)

Text writers estimate backing buffer capacity when collection counts are available. CSV and Fixed-Width use `StringBuilder` capacity estimates; JSONL uses a `MemoryStream` capacity estimate. This can reduce buffer growth and copying, but the estimate may be exceeded, and final output strings still allocate. Record contents, escaping, encoding, and selected serialization metadata affect both time and memory.

The former 35–64% and 1.9× claims are not maintained as current guarantees: they lacked a linked, reproducible comparison in this guide. Measure capacity changes independently with equivalent output and the same serialization path on both sides.

---

## 4. Core Architectural Pillars & Memory Profile

- **Span field access**: raw readers can expose fields without allocating a string for each field. Decoding to strings, constructing records or arrays, typed conversion, and owned PipeReader rows have separate costs.
- **Scan-Ahead Row Batches**: the scanner records column ends for batches of rows into pooled buffers; row iteration consumes those offsets.
- **Quote-Aware SIMD**: supported CPUs use vector comparisons and PCLMULQDQ quote masks; runtime capability checks and options select the applicable path or scalar fallback.
- **UTF-16 Pack-and-Saturate**: supported char scanning packs vectors with saturation and shares byte-mask analysis. This supports direct `string` scanning; it does not guarantee identical UTF-8 and UTF-16 timings.

See [`CsvRowBatchScanner`](../src/HeroParser/SeparatedValues/Reading/Shared/CsvRowBatchScanner.cs) for the current scanner and its option restrictions.

---

## Public Versioned Benchmark Portal

We host our benchmark results publicly to ensure complete transparency. The portal maintains reports for the **last 3 stable releases** so that consumers can track performance evolution:

* **Live URL**: [HeroParser Performance Portal](https://KoalaFacts.github.io/HeroParser/)

---

## How to Trigger the Benchmark Pages Deploy Manually

While the benchmarks deploy automatically upon successful release creation, you can trigger the pipeline manually at any time to update or republish reports:

1. Navigate to your **GitHub Repository** page on GitHub.
2. Click on the **Actions** tab at the top.
3. In the left-side workflow navigation list, click **"Deploy Performance Benchmarks to Pages"**.
4. Click the **"Run workflow"** dropdown button on the right.
5. *(Optional)* Provide a specific version tag in the **"Override version to deploy"** field (e.g. `2.3.0`), or leave it blank to auto-detect from the current version.
6. Click the green **"Run workflow"** button to start execution.

---

## How to Run Benchmarks Locally

To execute these benchmarks on your local hardware:

```bash
dotnet run -c Release --project benchmarks/HeroParser.Benchmarks --framework net10.0 -- --vs-sep-reading
```

This runs the fixture in your checkout. To reproduce the archived table, use a separate checkout of `cba2bcdbf57597d87a159316e4d06969c1278d01`; running the corrected fixture cannot reproduce the legacy workload. For a production-code A/B, run the **same corrected benchmark source** against both builds on the same machine, validate equivalent results before timing, and retain every sample. Report scan-only, field-decoding, typed-materialization, and I/O workloads separately.

### PipeReader Column Decoding A/B (September 2026)

`CsvPipeColumnDecodingBenchmarks` measures only column-to-string conversion, not
end-to-end pipe reading. It covers plain text, doubled quotes, explicit escapes,
Unicode, and long escaped fields, for both owned and two-segment borrowed columns.

Run the same benchmark fixture against both implementations on the same machine:

```bash
dotnet run -c Release -f net10.0 --project benchmarks/HeroParser.Benchmarks -- --csv-pipe --filter '*CsvPipeColumnDecodingBenchmarks*' --job short --artifacts BenchmarkDotNet.Artifacts/pipe-column-before
dotnet run -c Release -f net10.0 --project benchmarks/HeroParser.Benchmarks -- --csv-pipe --filter '*CsvPipeColumnDecodingBenchmarks*' --job short --artifacts BenchmarkDotNet.Artifacts/pipe-column-after
```

The first command must use the original decoder and the second the optimized
decoder; running both against the same checkout does not constitute an A/B test.
The fixture was added on top of base commit `626f8af`, before production changes,
for the baseline below.

Measured on Windows 11, AMD Ryzen AI 9 HX PRO 370, .NET SDK 10.0.401/runtime
10.0.12, BenchmarkDotNet 0.15.8, concurrent server GC. Both runs used ShortRun
(one launch, three warmups, three measured iterations) and the same fixtures.

| Column | Scenario | Before Mean | After Mean | Before Allocated | After Allocated |
|---|---|---:|---:|---:|---:|
| Owned | Plain | 88.27 ns | 39.35 ns | 96 B | 96 B |
| Segmented | Plain | 177.54 ns | 101.27 ns | 160 B | 96 B |
| Owned | Doubled quotes | 219.53 ns | 64.72 ns | 312 B | 88 B |
| Segmented | Doubled quotes | 712.18 ns | 75.22 ns | 368 B | 88 B |
| Owned | Explicit escapes | 225.31 ns | 68.94 ns | 312 B | 88 B |
| Segmented | Explicit escapes | 340.48 ns | 66.94 ns | 368 B | 88 B |
| Owned | Unicode with quotes | 148.45 ns | 37.00 ns | 192 B | 48 B |
| Segmented | Unicode with quotes | 208.72 ns | 80.41 ns | 240 B | 48 B |
| Owned | Long escaped | 15,511.65 ns | 5,232.54 ns | 23,672 B | 7,192 B |
| Segmented | Long escaped | 19,886.33 ns | 7,806.62 ns | 27,792 B | 7,192 B |

Allocation counts are the primary evidence. Several timing cases were noisy:
the original segmented doubled-quote standard deviation was 416.75 ns, and the
optimized owned doubled-quote standard deviation was 22.78 ns. ShortRun is not
enough to claim a precise throughput multiplier or an end-to-end speedup.
Use longer, repeated A/B runs before making such claims.

Both column types now share one decoder. Plain contiguous fields decode directly;
short scratch buffers use the stack, and larger or segmented scratch buffers use
`ArrayPool` and are returned in `finally`. Escaped fields are decoded before
unescaping to preserve invalid UTF-8 replacement semantics. Only the resulting
string remains allocated in these warmed-up cases; pool growth can allocate on
cold calls, and owned-row allocations are outside this microbenchmark.

### Checked End-to-End PipeReader A/B

The opt-in [paired probe](../benchmarks/CsvPipeABProbe/README.md) compares independent
parser/generator builds in one process, consumes complete records, verifies an
input-derived checksum, and retains every paired sample. It covers fixed
segmentation and real stream-backed pipe reading, with same-source A/A controls.
The [initial evidence](../benchmarks/CsvPipeABProbe/results.md) confirms allocation
reductions but rejects throughput claims under a noisy local environment. It also
exposes a generated-binding quoting discrepancy; invalid cases are not timed.
The same report records the follow-up logical-value repair and its validation.

An exploratory local study of [process-to-process variance](perf/csv-pipe-process-variance/README.md)
found that identical processes compile different optimized code under dynamic PGO, and that
dynamic PGO is worth roughly 16% on this path. It does not explain or waive the failed timing
gate; the noise on that machine is too large to link code shape to speed.
