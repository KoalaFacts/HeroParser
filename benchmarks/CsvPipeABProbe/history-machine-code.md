# Retained Same-Run Instruction Evidence

This is artifact-only analysis of [capture 36814110508](https://github.com/KoalaFacts/HeroParser/actions/runs/36814110508),
not a new experiment. The six-pair capture's failed reproduction gate and
`CausalComparisonQualified=false` remain unchanged. No worker, benchmark or
sampling process was started for this analysis. Production sources stay frozen
at `89c06810e76c4623ebad3cfc89c4bcdef41acd59`; capture revision is
`4bb4f48dd535bf1a501998ec5c41a810c4746347`.

## Method And Validation

`history-machine-code.ps1` extracts optimized slow-reader state machines and
standalone `TryReadRow` bodies from each observed owner's original x86-64
jitdump. It checks PID, index, address, body size and SHA-256 against the retained
method ledger. GNU objdump 2.42 decodes all bytes with contiguous instruction
coverage; invalid decoding fails closed. The script does not execute code bodies.

Only CPU IP samples within the same owner's original 30 measured Segmented128
request windows are counted. Selection uses the latest preceding JIT load
covering each IP, then the containing instruction's byte range. Counts are
cross-checked per request/code version against `native-request-windows.ndjson`.
Raw inputs are fingerprinted before/after analysis. Outputs include extracted
bytes, full assembly, instruction counts, input hashes, decoder version and
script/helper hashes. Partial output is not complete evidence until
`analysis.json` has `AnalysisComplete=true`. Existing output is never overwritten.

Reproduce against an already downloaded `study` directory, from the repository
root, with a new output directory:

```powershell
./benchmarks/CsvPipeABProbe/history-machine-code.ps1 -InputDirectory retained-study -OriginRun 36814110508 -OutputDirectory BenchmarkDotNet.Artifacts/offline-machine-code
```

On Windows with existing WSL/binutils, add `-ObjDump /usr/bin/objdump -UseWsl`.
`OriginRun` labels the retained download; it does not independently authenticate
GitHub provenance. CI downloads that exact run/artifact without substitution and
executes this offline analysis on PRs, never the collection step. No local .NET
build/test or synthetic test was run. Raw and derived binaries remain artifacts,
not committed source files. The original local derived output was retained
separately when provenance checks were added.

## Concrete Code Shape

All four observed conditions have A state machine index 15658, 10209 bytes /
2206 decoded instructions, versus B's 5319 bytes / 1230 instructions (index
16631, except full-keyword/B-first 16629). The caller's entry instruction at
`+0xa` reserves `0x4c8` (1224) stack bytes on A versus `0x2c8` (712) on B.
This is the `sub rsp` reservation, not total peak stack use or a GC allocation.
B's separately emitted optimized reader also reserves 712 bytes, except the
GC-only/B-first version's 728 bytes. A smaller caller alone does not demonstrate
less total stack or instruction-cache traffic.

Measured samples assigned to these exact versions:

| Condition | A caller | B caller | B optimized reader index / bytes | B reader samples |
| --- | ---: | ---: | --- | ---: |
| GC+JIT / A-first | 485 | 74 | 17881 / 6583 | 415 |
| GC-only / B-first | 843 | 92 | 17888 / 6772 | 739 |
| GC-only / A-first | 871 | 89 | 17874 / 6590 | 701 |
| GC+JIT / B-first | 834 | 99 | 17883 / 6579 | 675 |

A's scalar byte-reader operations are inside the optimized async state machine;
B's samples instead cover the separately emitted optimized reader. This is
consistent with different inlining/code-generation choices, not an authenticated
inline-tree report. Indirect RIP-relative calls expose pointer-slot addresses,
not retained slot contents; a slot must not be mistaken for a resolved callee.
No exact optimized native-to-IL mapping or inline-event evidence was captured.

## Cursor Loop And Byte Spill

Use GC-only/A-first as the concrete instruction example. A PID 3909's code
index 15658 starts at `0x7fe46fce18e0`; B PID 3919's reader index 17874 starts
at `0x7fa32e5841c0`. All offsets below are relative to those separate bodies.

| Operation | A caller offset / instruction | B reader offset / instruction |
| --- | --- | --- |
| Remaining-count subtraction | `+0x3e6 sub rsi,[rbp-0x2f0]` | `+0x84c sub rsi,[rbp-0x90]` |
| Load current byte | `+0x425 movzx ecx,BYTE PTR [rsi+rcx]` | `+0x886 movzx r12d,BYTE PTR [rsi+r8]` |
| Retain byte | `+0x429 mov [rbp-0x31c],ecx` | byte remains in `r12d` in this region |
| Reload current span length | `+0x44e mov esi,[rbp-0x2a0]` | `+0x8aa mov eax,[rbp-0x40]` |
| Consumed minus one | `+0x46c sub rdx,1` | `+0x8bc sub rax,1` |
| Later byte comparison | `+0x4bd mov ecx,[rbp-0x31c]`, then `cmp ecx,r14d` | `+0x916 cmp r12d,esi` |

Both regions update cursor index/consumed count, check span bounds and perform
overflow/sign-extension checks after subtracting one. Their operation-level
shape corresponds to `Csv.PipeReader.cs:179-184`: `Remaining`, `TryRead`, then
`checked((int)(Consumed - 1))`. The enclosing reader calls that scalar row path
from `Csv.PipeSequenceReader.cs:584` when comments are disabled. This is source
correspondence, not exact attribution of every optimized instruction/local.

A explicitly spills the current byte and reloads it for later comparisons;
B keeps it in a register in the shown path. In this pair, A's span-length
reload has 112 samples and its byte spill 60; B's length reload has 98 and its
cursor increment 43. A's corresponding spill/length operations remain sampled
in all four conditions, while B reader layout/offsets vary. These are concrete
register-allocation/code-shape differences to investigate, not measured latency
of the store/load instructions. CPU-clock samples can skid; counts are not
retired instructions, cycles, cache misses or a comparable absolute time scale
across conditions.

The caller's stack-zeroing loop at `+0x38..+0x4e` has 70 A versus 31 B samples
in this pair. The B reader has another 27 in its own corresponding entry loop.
Do not compare only the two caller loops or treat their sample difference as
proof that stack clearing explains the end-to-end gap.

## What This Does Not Explain

The earlier retained B-slower pair (capture 36807711817, recovered without
sampling in 36808849164) has optimized caller bodies of 5334 bytes on A PID
3824 and 5313 on B PID 3815, not the present 10209/5319 split. Its other
observed pair is 5298/5316 bytes. These jobs are not the same hardware and are
not interchangeable timing controls. Nevertheless, the old failure cannot be
declared explained simply by the newer large-A-caller observation.

The current evidence narrows the reverse difference to a concrete scalar
cursor/code-generation lead, including a byte spill. It does not prove why the
JIT chose that shape, whether it causes the timing difference, or whether
changing inlining or scanning would improve production throughput. Frequency,
other runtime CPU work, layout and observer/history effects remain uncontrolled.
The next causal proposal would need to isolate one code-shape variable while
retaining a reproducing same-run control. At that analysis revision the follow-up
was not implemented or authorized; the exhausted sampling budget was not extended.

## Preparation Boundary Follow-Up

Retained JIT timestamps place the optimized slow caller BEFORE segmented
preparation: all four observed A versions are loaded 6.46-6.48 seconds after
their verification request starts, after Contiguous warmup request 17. B
versions load at 7.92-8.01 seconds, around Contiguous warmup requests 32-39.
The separately emitted B optimized row reader follows segmented prepare
request 138. Identical commands/counts do not imply identical code-generation
history; merely lengthening segmented warmup does not undo an already emitted
optimized caller. These timestamps locate code-load events, not compiler-start
times or exact profile snapshots.

The timing driver executes a 36-case matrix first: four scenarios, three
transports, three paths. This exposes hot methods to verification workloads
before the Plain/Generated timing fixture. Dynamic PGO uses earlier instrumented
execution to optimize later code; see the [.NET 10.0.12 runtime design](https://github.com/dotnet/runtime/blob/v10.0.12/docs/design/features/DynamicPgo-InstrumentedTiers.md).
That establishes a shared profiling boundary in the harness, not a demonstrated
cause of the old timing failure. Register allocation and inlining consequences
are still candidates, not proven causal mechanisms.

`VerificationBoundaryControl` implements a candidate boundary repair without
enabling it by default: a separate process must successfully complete all 36
checks and exit before any timed worker starts. In the isolated treatment,
timed workers acknowledge this external verification without executing its
matrix. Preparation still validates eight Plain/Generated parses, and every
timed parse retains row/checksum validation. A fresh verifier's completed PID,
same parser/model hashes and revised driver hash are required. Normal legacy
acceptance rejects the diagnostic protocol; it cannot silently replace v4.

The dedicated, opt-in diagnostic fixes the scope BEFORE dispatch: matrix/B-first,
external/A-first, external/B-first, matrix/A-first; eight timed workers plus
one separate verifier, eight runtime traces, 180 full-matrix correctness cases,
360 measured pairs / 720 batches, the original 292 requests per timed worker,
10 hardware checkpoints, 30 job minutes, zero retries. All arms use the same
new driver and unchanged historical parser, generated models and consumer
source. Only verification location changes; runtime/PGO/tiering, observer
settings, target workload and historical non-verification commands stay intact.
Wall-clock history, heap state and mixed-input profile change together with
verification location, so this is not a JIT-only intervention.

Predeclared diagnostic prerequisite: BOTH matrix controls have segmented medians
outside [0.95, 1.05] in the SAME direction, with p10 > 1 for B-slower or p90 < 1
for B-faster. This new, explicitly two-direction diagnostic does not revise the
old one-direction study's failed decision. Missing/opposite/non-reproducing
controls remain inconclusive. Support for isolation additionally requires both
external arms, all three transports: median [0.98, 1.02], p10 >= 0.95,
p90 <= 1.05. No native code-size result alone qualifies improvement. The output
always reports `TimingAcceptancePassed=false`; even a supported treatment needs
independent confirmation and does not identify JIT versus heap/history effects.

Implementation/CI protocol tests are not sampling authorization. No new root
experiment is dispatched unless the user approves this separate fixed budget.
The old records, failed gates and exhausted six-pair budget remain untouched.

## Verification Boundary Setup Failure

The separately authorized single dispatch [37089514161](https://github.com/KoalaFacts/HeroParser/actions/runs/37089514161)
at `bcd47b1` failed before any timed worker or verification command. One external
verifier emitted its environment and was rejected on the parser hash. No matrix,
timed pair, batch or runtime trace was collected; no retry followed. Its raw
partial artifacts and `incomplete-verification-boundary` decision remain retained.
This is a setup failure, not a result supporting or rejecting PGO isolation.

The loaded parser/model version and PDB SourceLink pointed to the diagnostic
revision. That initially suggested rebuild drift, but the unmeasured regression
[37110752215](https://github.com/KoalaFacts/HeroParser/actions/runs/37110752215)
at `14ca65c` distinguishes the phases: frozen parser/models rebuilds exactly match
the historical SHA-256 hashes and `89c0681` SourceLink; the driver output copies
instead match the preceding HEAD builds. Source rebuilding was not the culprit.

The retained `diagnostic-driver.binlog` confirms that `ResolveAssemblyReference`
selected HEAD DLLs under `Models/bin/Release/boundary` through
`{CandidateAssemblyFiles}`, despite the correct frozen `HintPath` values.
The SDK searches candidate files before hint paths, and MSBuild supplies
`@(Content);@(None)` as candidates. Excluding model files from `Compile` alone
did not exclude their nested build outputs from implicit `None` items.
Both assemblies share the normal names/version `2.8.0.0`, so identity resolution
accepted the wrong builds before the runtime hash gate caught them.

The narrow repair removes `Models/**` from the probe's `None` items. The regression
deliberately leaves prior HEAD outputs in place, builds the frozen projects,
links the diagnostic driver and requires all source/copied DLL hashes to match
authenticated history. It records phase hashes, product versions, PDB SourceLink
and three binlogs while starting zero workers. A separate pre-start guard now
checks source and copied DLL hashes before even launching the external verifier.
No binary-hash check, old timing threshold or reproduction prerequisite is relaxed.
The failed regression is preserved; validation of the repair is recorded separately.

## Frozen Source Retrieval After Branch Cleanup

The newly authorized independent dispatch [37125865914](https://github.com/KoalaFacts/HeroParser/actions/runs/37125865914)
at merged revision `29f84b2` stopped in the frozen-build regression, before
collection: Git could not resolve the exact historical source `89c0681`.
The retained build summary records zero measured workers, zero batch commands
and no build fingerprints. This is another setup failure, not a verification
isolation result. That dispatch is retained without a retry.

The frozen commit is not an ancestor of the squash-merged default branch.
Deleting the old feature branch therefore makes a full advertised-history
checkout insufficient to obtain it. The history workflow now explicitly fetches
the full pinned SHA, verifies its exact commit identity and checks that retrieval
does not move diagnostic HEAD before any build or worker startup. Retrieval
provenance is uploaded even on failure. Missing source fails closed; there is
no substitute revision, source rebuild waiver or timing threshold change.
The existing frozen build/copy regression independently validates the retrieved
source's binaries. A green preparation check still does not authorize a new
sampling dispatch or establish the original timing failure's cause.
