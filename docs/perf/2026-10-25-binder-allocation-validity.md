# Binder overhead benchmark: object allocation validity (2026-10-25)

## Defect and scope

The `BinderOverheadBenchmarks.RawParse_CreateObjects` method constructed
`new SimpleRecord()` on each row but never used the reference. Because the
object did not escape, the .NET JIT could eliminate the allocation entirely.
The resulting timing and allocated bytes could resemble
`RawParse_CountColumns` even though this benchmark was labelled as the
object-allocation component. In the other three manual-binding methods,
only the Name length or parsed Value was used, so the unobserved object
and field assignments also lacked an explicit anti-elision sink.

## Correction

Create one `BenchmarkDotNet.Engines.Consumer` field per benchmark instance
(outside the timed methods), and pass each actual object to
`allocationConsumer.Consume(obj)` in the four object-creation methods:
`RawParse_CreateObjects`, `RawParse_CreateObjectsWithStrings`,
`RawParse_WithIntParse`, and `Manual_SkipHeader`. BenchmarkDotNet's
consumer writes the reference to a volatile holder and clears it, preventing
dead-code elimination without artificially retaining the entire dataset.

The xUnit integration test exercises the actual timed entrypoints, checks
the header-inclusive raw column count and numeric totals, warms the methods,
and compares `GC.GetAllocatedBytesForCurrentThread()` against matched raw
and string-only paths. It requires at least 16 bytes of additional managed
allocation per input row when object construction is included. This is an
allocation **presence** check, not a hard-coded byte-per-object guarantee,
a throughput gate, or a substitute for platform-wide CI. It deliberately
runs in the single calling thread so other test threads cannot inflate it.

## Interpretation and limitations

These four methods now perform observable work that the prior JIT was free
to omit. They include the consumer's volatile reference writes as part of
the measured operation, so they do not isolate the instruction cost of
`new` perfectly; the allocated-bytes diagnostic is the primary signal.
The count-only baseline also processes the CSV header whereas
`Manual_SkipHeader` and the typed binder skip it; these are diagnostic
stages, **not equal-work library ranking benchmarks**.

Changed workloads require fresh within-revision baselines. No parser or
source-generator production code, published API, xUnit/MTP version, or
.NET target framework is changed. No speedup or allocation reduction is
claimed. The historical CsvPipeABProbe FAILED evidence, thresholds, sampling
budget, and manual timing controls are unrelated and unchanged.

Validation status for this candidate: the CI regression is defined but
cannot be claimed as executed locally without a .NET SDK; the exact new
commit must pass .NET 8/9/10 GitHub Actions before publishing any result.
