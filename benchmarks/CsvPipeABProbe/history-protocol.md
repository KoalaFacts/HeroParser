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

Implementation status: collection not yet verified. No new root cause or
performance acceptance is claimed.
