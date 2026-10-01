# Same-Process Evidence With Historical Batch Replay

This is a diagnostic, not another attempt to pass failed timing controls. Run it
through the existing benchmark workflow with `pipe_history=true`, without other
manual modes. PRs validate reconstruction/decoding only; collection requires an
explicit workflow dispatch. All parser/model/consumer sources and binaries remain
frozen at `89c0681`. The original timing gate remains failed.

## Predeclared Replay

Only cycle two of [failed run 36732093817](https://github.com/KoalaFacts/HeroParser/actions/runs/36732093817)
is used. SHA-256 pins its two command transcripts and paired timing records.
Reconstruct every command and phase-local first-side alternation, including the
full correctness matrix, all three transport preparations, calibration requests,
warmup requests, 30 measured pairs per transport and final stop. Validate each
measured request ID against both original responses. Calibration is replayed, not
adaptively rerun: no extra warmup, repeats or process pairs are added.

The Contiguous prelude contains exactly 135 batches / 22463 parses per worker.
It includes seven pilot, 94 warmup, four post-warmup and 30 measured batches.
Segmented128 has nine pilot, 25 warmup, one post-warmup and 30 measured batches.
Stream4096 also retains its original history. Preserving request counts, repeat
sizes and order cannot recreate old scheduling, elapsed warmup or JIT timing.
Report these limitations; diagnostic ratios do not satisfy acceptance by proxy.

Exactly four fresh process pairs run in this fixed order, including failures:

| Condition | Startup order | Extra collection |
| --- | --- | --- |
| reference-b-first | B, A | request boundaries only |
| observed-a-first | A, B | runtime/JIT/native CPU/scheduler |
| observed-b-first | B, A | runtime/JIT/native CPU/scheduler |
| reference-a-first | A, B | request boundaries only |

Both workers inherit the same single-CPU affinity before runtime startup, use
normal assembly identities, and must match the original binary fingerprints.
Requests execute serially in their historical first-side order. Failures remain
retained; a condition is not retried, and all four are attempted unless a shared
prerequisite fails. There is no root-cause intervention or A/B in this protocol.

## Evidence And Observer Effect

Both conditions retain process CPU/fault accounting and per-task scheduling
counters at request boundaries. Scheduling statistics are explicitly enabled on
the disposable CI runner for all conditions; this and added boundary reads change
the old observation environment. A reference pair is not a pristine old control.
Surviving-thread deltas use TID plus start time, not just TID; exiting/new threads
remain gaps. Process CPU is whole-process, not exact consumer CPU.

Observed workers also write same-PID native jitdump and perf maps plus an EventPipe
GC/JIT trace from startup. Separate perf captures record native user-space CPU
sample IPs and kernel switch/wakeup events during the replay. No managed-stack
samples are represented as direct CPU evidence. GC/JIT tracing, mapping and perf
are an instrumentation bundle; reference/observed differences measure a possible
observer effect, not the isolated cost of one instrument or the old bias's cause.
Fixed ordering counterbalances startup order but does not eliminate VM/time drift.

Native correlation matches an IP to a same-PID code range whose JIT load precedes
the sample, then to the coordinator's monotonic request window. Retain code index,
address, size, hash and name. Address reuse selects the latest preceding load;
this is not an optimized inline/source-local map. Native bytes are retained in
jitdump; relocated byte hashes alone are not code-shape or causal evidence.

[EventPipe](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/eventpipe)
records runtime events, not kernel scheduling. The decoder exports raw GC/JIT
event identities, timestamps and selected payloads. Pair runtime suspension and
restart events by CLR instance; retain reasons instead of calling every suspension
a GC-only pause. Clip suspension overlap to request UTC windows, explicitly marked
approximate: trace-derived UTC and coordinator UTC are not exact consumer start/end
markers. Do not add overlapping runtime suspension and run-queue durations as
independent costs. Collection duration is not stop-the-world suspension time.

Missing owners/events, malformed windows, unmatched suspensions, missing native
request attribution or reported trace loss fail completeness checks. Zero lost
events means zero *reported* loss; absent counters/events do not prove zero cost.
Native sampled counts are not precise instruction costs or a speedup prediction.
Curated worker environments exclude CI credentials and uncontrolled runtime flags.
No process memory dump is published. Runtime decoder dependencies affect only
the out-of-process diagnostic tool, not the frozen measured consumer.

## Interpretation Gate

Compare the distributions and code versions within each same-PID pair. If bias
reproduces, examine whether per-request native shape, runtime suspensions or
observed scheduling distinguishes the slower side. If it does not, retain the
negative result and the original failure. Only then select one predeclared causal
intervention for a subsequent experiment; disabling PGO or changing cold paths
is not part of this capture. Neither collection success nor ordinary green CI
authorizes a performance claim or merge.

## Setup And Validation Record

PR validation run `36806718078` passed 24 history and 84 native checks, then
rejected use of a discouraged TraceEvent clock API and one style violation.
No parser or profiler ran. Revision `d2645dc` uses supported relative/UTC trace
timestamps; [validation 36806892762](https://github.com/KoalaFacts/HeroParser/actions/runs/36806892762)
passed checks, decoder build (zero warnings/errors) and formatting.

[Setup-only dispatch 36806977366](https://github.com/KoalaFacts/HeroParser/actions/runs/36806977366)
retained its tools/context artifact and failed before the frozen workload build
or worker startup because setup-dotnet supplied `DOTNET_MULTILEVEL_LOOKUP=0`.
The guard now recognizes only this SDK configuration, without passing it into
workers. [Official runtime-variable documentation](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-environment-variables#dotnet_multilevel_lookup)
limits its application to targets through .NET 6. Unknown flags and other values
remain rejected; workers must match the original .NET runtime/GC/processor count.
This is a repaired setup prerequisite, not a retrial of measured pairs.

[Setup-only dispatch 36807286588](https://github.com/KoalaFacts/HeroParser/actions/runs/36807286588)
also stopped before workload build or worker startup, on `DOTNET_NOLOGO`.
The guard now recognizes its documented boolean values without passing it into
workers, and reports all unknown flag names together (never their values).
Both setup failures remain recorded and contain no measured batches.

## Retained Capture And Artifact Recovery

[Capture 36807711817](https://github.com/KoalaFacts/HeroParser/actions/runs/36807711817)
at `7252c32` completed all four fixed measurement histories and retained raw native,
runtime and scheduling files. Original CI status remains failure: postprocessing
rejected the blank line emitted by `perf script`; its combined `PID/TID` format
also differed from the parser fixture. Neither error requires replacement sampling.
The parser now accepts blank separators and both explicitly supported PID/TID
formats, while still rejecting malformed samples and unknown owners.

Artifact-only recovery uses `pipe_history=true` and
`pipe_history_replay_run=36807711817` in the existing benchmark workflow. It skips
tools/scheduler setup and all measured worker startup, preserves the complete
original capture under `original/`, revalidates original command/response/binary
identities, decodes the same runtime traces and correlates original sample IPs.
New derived output never changes the old failed manifest or any timing. This is
analysis recovery, not a new trial, green acceptance or root-cause intervention.

[Artifact recovery 36808849164](https://github.com/KoalaFacts/HeroParser/actions/runs/36808849164)
at `6ed56f2` passed without new workloads. It revalidated 288 correctness cases,
360 measured pairs / 720 batches and recovered all four observed CPU/runtime
owners. All 210 original files match the downloaded capture byte-for-byte.
Four runtime traces report zero lost events and include GC/JIT events; native
workload attribution is 4949, 5273, 5003 and 5532 samples. The old capture failure
remains recorded, not changed to success.

## Single-Variable JIT Event Control

The first observer bundle has Segmented128 median B/A 0.993049 and 1.001352 in
reference pairs, versus 1.020581 and 1.065718 in observed pairs. Instrumentation,
fixed condition order and runtime/code variation cannot yet be separated. The
next test changes only the EventPipe JIT keyword: `0x11` (GC+JIT) versus `0x1`
(GC). It does not disable tiering/PGO, change GC mode, parser binaries, buffer size,
native mappings, sampling frequency, scheduling capture or historical requests.

Dispatch with `pipe_history=true` and `pipe_history_jit_control=true`, without
artifact replay or other modes. Exactly four new observed pairs run once:
GC+JIT/B-first, GC-only/A-first, GC-only/B-first, GC+JIT/A-first. Every result and
failure is retained. Both sides of each pair have the same provider keywords.
GC-only decoding requires GC events and zero Method events; native code-version
evidence still comes from same-PID jitdump and CPU samples, not EventPipe JIT events.

Predeclared hypothesis: the additional JIT keyword is necessary for persistent
side-B segmented bias in this observed setup. A GC-only pair whose Segmented128
median exceeds 1.05 and p10 exceeds 1.0 contradicts that necessity. Consistent
GC+JIT bias in both startup orders with both GC-only medians within [0.98, 1.02]
would support a keyword effect worth independent confirmation. Other results,
including failure to reproduce the GC+JIT condition, are inconclusive. Do not
average away divergent conditions or add process pairs. This small fixed design
does not eliminate time/VM drift or establish the old uninstrumented bias's cause.

## Single-Variable Result: Inconclusive

[Control 36809723846](https://github.com/KoalaFacts/HeroParser/actions/runs/36809723846)
at `25c95d4` completed all four fixed pairs once. CI passed 31 history and 84
native checks; decoder and three frozen workload builds had zero warnings/errors.
Independent artifact validation confirms 288 correctness cases, 360 pairs / 720
batches, all 292 historical requests per worker and the original binary/runtime
fingerprints. All eight runtime traces report zero loss and 956 GC starts each.
GC-only Method-event counts are exactly zero; GC+JIT counts are 6139/6079 and
6058/6106. Same-PID attributed workload samples range from 2668 to 2971.

| Fixed condition | Segmented128 median B/A | p10 | p90 |
| --- | --- | --- | --- |
| GC+JIT / B-first | 0.991572 | 0.984608 | 1.020160 |
| GC-only / A-first | 1.015000 | 0.991981 | 1.028927 |
| GC-only / B-first | 0.996265 | 0.983741 | 1.029785 |
| GC+JIT / A-first | 0.977545 | 0.950782 | 0.990602 |

Neither GC+JIT condition reproduced the earlier large side-B segmented bias.
Under the predeclared rules this is inconclusive, not evidence that removing JIT
events solved the old failure or that JIT events were its cause. The GC+JIT
A-first Contiguous median is 1.166577 (p90 1.399485); do not hide instability in
another transport by reporting only segmented medians. Minimum diagnostic batches
are about 103-120 ms, shorter than the old calibrated duration. No new calibration
or warmup was added, and these runs cannot substitute for timing acceptance.

All four conditions within the control shared one runner. Its recorded model
is AMD EPYC 9V45, whereas the preceding capture used AMD EPYC 9V74. Do not compare
absolute times across those jobs or describe them as the same physical machine.
Request history preservation is not elapsed-time/JIT-history reproduction. No
condition was rerun or added after seeing results.

Status: same-PID correlation and one single-variable test implemented and
validated; root cause unresolved, original timing acceptance failed. Further
causal work needs a reproducing control in the same hardware/run budget, not
another unqualified retry or a production cold-path optimization. No merge approved.

## Fixed Same-Run Control Budget

The next predeclared diagnostic uses `pipe_history=true` and
`pipe_history_same_run=true`, without any other experiment/replay flag. One job,
one runner boot, one pinned logical CPU and one frozen workload build serve all
six process pairs in this exact order:

1. Boundary-only reference (no EventPipe/perf), B-first.
2. Full GC+JIT observer control, A-first.
3. GC-only keyword intervention, B-first.
4. GC-only keyword intervention, A-first.
5. Full GC+JIT observer control, B-first.
6. Boundary-only reference (no EventPipe/perf), A-first.

References flank the study; full-keyword controls flank the GC-only conditions.
Both startup orders occur once per arm. Every worker replays the same 292
hash-pinned requests, including all historical preparation batches. Budget:
12 workers, 432 correctness cases, 540 measured pairs / 1080 measured batches,
eight runtime traces, maximum 30 job minutes, zero retries or extra calibration.
The plan, input hashes, ordered conditions, limits and hardware identity are
written before building/starting measured workers. All conditions are attempted
even when a distribution does not reproduce the bias; no result-driven stopping,
additional pairs, selective omission or replacing a failed condition is allowed.
Prerequisite failures can stop the study and retain partial evidence.

CPU vendor/family/model/name/stepping/flags, kernel, hashed boot identity,
coordinator allowed CPUs and pinned CPU must match before and after every pair
(12 checkpoints). Identity mismatch fails closed; it cannot start a substitute
runner. A hosted VM's recorded identity is not proof of exclusive physical
hardware, stable frequency, no VM migration or absence of neighboring workloads.
Only comparisons inside this job qualify; old jobs remain historical evidence,
not interchangeable hardware controls.

Predeclared gate: BOTH full GC+JIT Segmented128 distributions must have median
B/A > 1.05 and p10 > 1.0. Missing/incomplete evidence or zero/one reproducing
control makes the job fail with an inconclusive diagnostic outcome after the
fixed budget, while retaining every completed distribution and raw trace.
If both controls reproduce, a GC-only or reference condition meeting that bias
rule shows the JIT keyword is not necessary for observed bias. Otherwise, both
reference and both GC-only medians within [0.98, 1.02] support a keyword effect
requiring independent confirmation; remaining outcomes are inconclusive.
The `CausalComparisonQualified` flag means the positive-control prerequisite
passed, not a demonstrated root cause. Distributions for all three transports
remain published; no averaging across arms or hiding non-segmented instability.

`fixed-budget.json`, `runner-identity.json`, `hardware-checkpoints.ndjson` and
`same-run-decision.json` accompany the existing historical commands, binary
fingerprints, per-request windows, GC/JIT traces, native code versions and
CPU/scheduler samples. The original failed A/A gate stays failed regardless of
this diagnostic outcome. Production parser code and timing acceptance are
unchanged. The predeclared implementation was committed at `4bb4f48` before
the single dispatch below; no criteria or sampling budget changed afterward.

## Same-Run Result: No Reproducing Control

[Run 36814110508](https://github.com/KoalaFacts/HeroParser/actions/runs/36814110508)
at `4bb4f48` completed all six pairs in 14m26s, within the fixed 30-minute job
budget. It deliberately FAILED at the predeclared reproduction gate, not at
collection: `no-reproducing-same-run-control`, zero qualifying controls,
`CausalComparisonQualified=false`. No sampling retry was dispatched.

All 12 checkpoints match AMD EPYC 7763, kernel `6.17.0-1022-azure`, CPU 0,
coordinator allowed CPUs 0-3 and one hashed boot identity. Every worker reports
actual allowed CPUs 0, one processor and the original .NET 10.0.12 workstation
GC/binary fingerprints. This is within-job runner identity, not proof of an
exclusive or unmigrated physical host; it is not the old EPYC 9V74 machine.

Independent artifact validation confirms all three historical input hashes,
the six ordered conditions, all 292 requests/responses per worker, 432 verified
cases, 540 measured pairs / 1080 batches, unique persistent PIDs, original
alternation and recomputed per-transport percentiles. All eight runtime traces
report zero lost events and 956 GC starts each. GC-only Method counts are zero;
full-keyword counts are A/B 5977/6895 and 5998/6904. Same-PID workload-attributed
CPU samples range from 5335 to 6734. The raw capture and decision artifacts are
retained together, with an operator-local offline copy separate from git.

| Fixed condition | Segmented median B/A | p10 | p90 | Contiguous median | Stream4096 median |
| --- | --- | --- | --- | --- | --- |
| Reference / B-first | 0.956523 | 0.939608 | 0.962882 | 0.999214 | 0.983473 |
| GC+JIT / A-first | 0.930464 | 0.923399 | 0.939802 | 1.010148 | 1.003765 |
| GC-only / B-first | 0.930775 | 0.926861 | 0.934537 | 0.984096 | 0.985608 |
| GC-only / A-first | 0.911461 | 0.908561 | 0.915247 | 0.997474 | 0.984920 |
| GC+JIT / B-first | 0.938820 | 0.935837 | 0.961803 | 1.003475 | 0.998234 |
| Reference / A-first | 1.006985 | 0.999434 | 1.012756 | 1.020404 | 1.024273 |

The original side-B-slower hypothesis did not reproduce. All four observed
segmented conditions instead have a substantial opposite-direction difference,
including both GC-only conditions and both startup orders. The first reference
is also outside the predeclared [0.98, 1.02] stable-reference interval. Do not flip
the reproduction rule after seeing results, average away that reference, treat
GC-only as a fix, or call the reverse difference a throughput improvement:
both sides have identical production binaries. This preserves useful negative
evidence but does not determine the old failure's cause or qualify causal
interpretation under this protocol. Diagnostic minimum batches range from
195.225 to 346.500 ms across condition/transport summaries; old elapsed-time/JIT
history and calibrated timing acceptance still have not been recreated.

CI passed 81 historical correlation and 84 native checks. Decoder plus three
frozen workload builds reported zero warnings/errors; decoder formatting passed.
[Ordinary build/test CI at the measured revision](https://github.com/KoalaFacts/HeroParser/actions/runs/36813982693)
also passed all 24 jobs, separately from the deliberately failed measurement
gate. Further sampling requires a new approved, predeclared design; this budget
is exhausted. Existing same-PID artifacts can still be analyzed without launching
workers. Original A/A acceptance stays failed; production optimization and merge
remain unauthorized.
