# Benchmark validity repairs and wrapper experiment — 2026-10-24

## Outcome

This change repairs CSV and JSONL benchmark correctness, keeps the shared profiling
entry point working with the new fixture, adds forced-segment boundary coverage,
and corrects unsupported performance claims in the documentation.

The experimental `TryBindPipeSequenceRow` split-helper extraction was **withdrawn**.
The three predeclared local A/A controls were invalid, so no A/B comparison ran.
There is no throughput or allocation improvement claim for that candidate. The
production parser source remains identical to the baseline.

Baseline: [`cba2bcdbf57597d87a159316e4d06969c1278d01`](https://github.com/KoalaFacts/HeroParser/commit/cba2bcdbf57597d87a159316e4d06969c1278d01).
The existing historical CI timing gate and its exhausted sampling budget remain
unchanged. This new, explicitly bounded local diagnostic does not validate that
historical gate or authorize another repetition of this trial.

## Benchmark correctness

### CSV scanning

The generated CSV has no header. Previously, Sep and Sylvan used their default
header settings and consumed the first data row as a header. For a 12-by-5 fixture,
each reported 55 scanned cells instead of the expected 60. The regression tests
reproduced this with both quoted and unquoted input.

All five scan paths now use explicit comma separation and treat every row as data.
HeroParser remains quote-aware for both fixture variants. The timed methods still
only iterate rows and count columns; they do not materialize values or records.
Their public names and BenchmarkDotNet selectors are unchanged.

Before timing, `GlobalSetup` checks every decoded field and the complete row/column
shape for Sep, Sylvan, CsvHelper, HeroParser UTF-16, and HeroParser UTF-8. It also
calls each actual timed scan method and checks its returned cell count. Sep uses
unescaping for this logical-value validation; the timed count-only loop retains
its raw-scanning configuration.

The quoted fixture still quotes half its fields, but now includes embedded commas,
doubled quotes, line feeds, empty values, Chinese characters, and emoji. These
corrected options and changed bytes require a **new benchmark baseline**. A time
change relative to the old fixture cannot be attributed to parser optimization.

The same fixture is also used by `ProfileDriver`. Independent review reproduced a
newly exposed duplicate-header exception in `--profile sep-utf16-quoted` because
that caller still used Sep's header defaults. Its options are now aligned, and it
checks the expected 250,000 cells before entering warmup or the profiling loop.
The repaired quoted Sep command was rebuilt and smoke-tested successfully.

### JSONL source generation

The reflection path used `JsonSerializerDefaults.Web`, while the source-generated
context used different naming settings. On the seven-record regression fixture,
the generated reader returned an ID sum of 0 instead of 21, and the generated
writer emitted PascalCase property names instead of camelCase.

The generated context now also uses `JsonSerializerDefaults.Web`. Setup verifies
all four synchronous text/stream reading variants against every expected ID,
name, decimal amount, and boolean value, and verifies matching text-writer output.
Tests additionally check asynchronous file reading and actual camelCase JSON
properties. The existing timed benchmark bodies are unchanged.

The eight new fairness tests were run against the original implementation first:
six failed with these incorrect values. After the repairs, all eight passed.

### Integration-test dependency scope

`HeroParser.Tests` now references the benchmark project so these regressions exercise
the actual benchmark methods. This deliberately adds its competitor and
BenchmarkDotNet dependencies to the main test restore/build graph. It adds no
dependency to the shipped HeroParser package or the separate NativeAOT and source
generator test projects. Existing xUnit 4.0.2 and Microsoft.Testing.Platform
configuration are retained.

## Withdrawn parser candidate

The only production experiment moved the non-contiguous branches of
`TryBindPipeSequenceRow` into a private `NoInlining` helper. It preserved the
1024-byte stack threshold, pooled buffer ownership, copies, binder arguments,
and logical-field decoding. The hypothesis was that removing those branches from
the contiguous wrapper might improve generated binding; code layout alone was
not an acceptance criterion.

Four new characterization cases force rows of exactly 1024 and 1025 raw bytes to
split inside either a UTF-8 character or a doubled quote. These cases passed on
the original implementation before the experiment. The candidate also passed the
existing generated-value tests and a separate full 36-case probe verification
with 2,000 rows per case. These correctness checks do not establish performance.

### Fixed local protocol

- Existing isolated single-module worker and fixture/consumer, with normal parser
  and model assembly identities. Only the candidate parser DLL/PDB was replaced;
  model, consumer, dependencies, and runtime configuration were identical.
- Ubuntu 24.04.3, AMD EPYC 9V74, .NET SDK 10.0.402 / runtime 10.0.13, workstation GC.
  Both workers were pinned to allowed CPU 0 from startup on a shared machine.
- Exactly three fresh baseline/baseline controls, reversing launch order in the
  second. Each worker first validated the full 36-case matrix.
- Planned timing: Plain/Generated, 2,000 rows, Contiguous, Segmented128, Stream4096;
  30 alternating sequential pairs per transport, 100 ms minimum measured batches,
  at least 10 seconds and 10 pairs of warmup, 125 ms post-warmup calibration target,
  and a repeat cap of 65,536.
- Original control bounds: median ratio 0.95–1.05, p10 at least 0.90, p90 at most
  1.10. Only three valid, stable controls could enable one A/B and a final A/A.
  No retries or recalibration after measured samples began were allowed.
- Predeclared candidate criterion: median ratio at most 1.0 on every transport,
  at most 0.97 on Contiguous and Stream4096, and no increase in median or mean
  allocation. All four controls would also have to remain valid and stable.

### Actual result

| Control | First failing measured batch | Outcome |
|---|---:|---|
| A/A 1 | 99.7234 ms | Below the fixed 100 ms minimum; invalid |
| A/A 2 | 98.0395 ms | Below the fixed 100 ms minimum; invalid |
| A/A 3 | 34.0171 ms | Below the fixed 100 ms minimum; invalid |

All six control workers passed their 36 correctness checks. Each control then
stopped during Contiguous timing when the minimum-duration check failed. Thus no
control produced a complete transport summary, no Segmented128 or Stream4096
timing ran, and no candidate A/B or final A/A was attempted. The coordinator exited
2 with `state: inconclusive` and retained the invalid samples. Ratios from these
partial controls are not accepted performance evidence.

The helper extraction was removed from production source. Its patch is retained
with the raw evidence so future work can distinguish this unsuccessful measurement
attempt from an untested idea or a demonstrated parser regression.

## Evidence

[Raw diagnostic archive](evidence/2026-10-24-wrapper-split.tar.gz) contains the
fixed plan, exact coordinator source, runtime/host context, assembly/source
manifests, candidate patch, full correctness output, build logs, every worker
request/response, measured pairs including failures, and cleanup/final summaries.
It contains 103 files and no built parser assemblies.

| Artifact | SHA-256 |
|---|---|
| Archive | `a6d338559b5edd3dfd0651cdc455ff1bcfb61dd19e87f1ca2582773c1b204ac9` |
| Coordinator | `aad607b1d245ca3049accb4e0b3922a64fdc68af6a2b0f9f22b27262df5d083d` |
| Candidate parser patch | `077ce626f2a3c7accdc88ada7fdf3e830aeb8c04f4ff1f3190a84cd62c4a8ea3` |
| Baseline parser DLL | `7ecb760899319f817ff527a43a6911c0dd82b2dea30f2c605a6fcc028cb4d6d1` |
| Candidate parser DLL | `62322bcf93476899c6729a409b8cbf80d5e79aeee5b215d937008a3adfdf1438` |

The source refs in worker records are the actual base commit. The candidate was
an uncommitted patch, explicitly identified by the patch hash and complete source
manifest; those refs do not imply that both parser binaries were identical.

## Final validation

After withdrawing the production candidate, both test projects were rebuilt with
zero warnings and zero errors across all three supported frameworks. The complete
suites were then run again on that final source:

| Target | Core tests (Microsoft.Testing.Platform) | Generator tests | Failed / skipped |
|---|---:|---:|---:|
| .NET 8 | 4,541 passed | 138 passed | 0 / 0 |
| .NET 9 | 4,541 passed | 138 passed | 0 / 0 |
| .NET 10 | 4,541 passed | 138 passed | 0 / 0 |

The final source also passed `git diff --check`. Independent review accepted the
profiling correction and the evidence report after the initial blocking finding
was fixed. These local checks do not claim that remote CI or native AOT publishing
has run for this change.

### Commands

Build the core and generator test projects in Release with the repository's
warning-as-error policy. Run the main suite via Microsoft.Testing.Platform for
each supported framework:

```sh
dotnet build tests/HeroParser.Tests/HeroParser.Tests.csproj -c Release
dotnet build tests/HeroParser.Generators.Tests/HeroParser.Generators.Tests.csproj -c Release
dotnet test --project tests/HeroParser.Tests/HeroParser.Tests.csproj \
  --configuration Release --no-build --framework net10.0
dotnet exec tests/HeroParser.Generators.Tests/bin/Release/net10.0/HeroParser.Generators.Tests.dll
dotnet benchmarks/HeroParser.Benchmarks/bin/Release/net10.0/HeroParser.Benchmarks.dll \
  --profile sep-utf16-quoted 1
```

Repeat the test commands for `net8.0` and `net9.0`. The profiling command is a
correctness smoke check; its rough timing is not used as a benchmark result.

## JSONL reader checksum follow-up (2026-10-24)

The 100,000-record JSONL benchmark produces IDs 0 through 99,999. Their
correct sum is `4,999,950,000`, which does not fit in `int`.
The original five timed JSONL reader methods returned `int` sums and
silently wrapped in an unchecked build. An added 100,000-record regression
checks the actual reflection/source-generated text and stream methods,
plus the async file method, against this exact expected `long` sum.
All five timed readers now return `long`, preserving the number of records
and amount of parsing work while keeping the checksum exact.

This is a benchmark correctness fix, **not** evidence of an underlying parser
performance improvement. Reader return types changed, so publish a newly
validated benchmark baseline rather than treating older JSONL numbers as
fully interchangeable. The frozen CSV Pipe acceptance experiment, its failed
controls, fixed thresholds, and sample budget remain untouched.
