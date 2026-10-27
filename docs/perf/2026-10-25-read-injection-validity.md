# CSV injection-detection benchmark validity — 2026-10-25

## Failure and corrected contract

Source baseline: `927ae099055e94091585209a79c515dab842ea4e` on PR #157. `ReadInjectionProtectionBenchmarks` previously called `IsDangerousField`, `CsvRow.HasDangerousFields()`, and `CsvRow.IsDangerousColumn()` but **discarded every boolean** and returned only column counts. For the local pure predicate, the JIT could remove the work entirely; even if calls were executed, the measured checksum could not reveal a missed dangerous field. The original fixture also contained only nominal normal/signed/phone values and no positive detection case.

The new normal-named fixture is an intentionally **mixed** case: column 1 of rows 0, 8, 16, ... is `=cmd`, one dangerous field per flagged row. Remaining columns retain the original `Value{row}_{column}` format. Signed numeric and phone fixtures stay unchanged and must **not** be flagged. The raw baseline counts parsed cells only. Old span-based, whole-row, and per-column validation benchmarks now **consume their predicate results** in an observable return value: cells + flagged matches. On this controlled input at most one field per row is dangerous, so the whole-row and per-column methods perform an equivalent logical detection task.

In `GlobalSetup`, each of the three fixture variants is fully parsed before timing. Every header/data field, field count and data-row count is compared to independently constructed expected values. All 10 real timed methods are then invoked and their checksums verified, including the positive and negative cases. A new xUnit integration regression invokes these methods at 8, 9 and 1000 data rows and requires the same independent expected totals. **The test-first parent would have returned the cell total instead of the cell total + positive flags.**

## Performance and safety limits

This corrects an invalid benchmark, **not** production security behavior: no parser, injection classifier, validation option, package version, or public API was changed. No reduction in allocations, speedup, or security guarantee is claimed. The mixed fixture and observable checksum change the workload; prior benchmark timings are not comparable as an A/B speedup. An actual end-to-end injection policy test would require separately exercised application behavior.

Test-first Git object was created before the implementation; the local environment lacks dotnet and PowerShell. Do not claim the red test was executed locally, or that current-head .NET8/9/10 CI passed until the exact SHA's remote matrix completes and any real failure is resolved. Frozen historical CsvPipeABProbe timing run 36732093817 remains **FAILED**, ratio 1.052845684 > 1.05, remaining budget 0, with no changes or retrials. The independent performance methodology remains in docs/perf/2026-10-24-benchmark-validity.md.
