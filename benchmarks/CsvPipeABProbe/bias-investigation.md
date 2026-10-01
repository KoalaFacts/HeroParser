# Isolated Timing Bias: Root-Cause Investigation

## Question and Existing Evidence

Why do identical single-module workers diverge on Segmented128? This is separate
from the [verified native hotspot locations](native-source-map.md): an expensive
source operation does not explain why the same operation differs between PIDs.
No production optimization is accepted until the measurement boundary is understood.

Artifact-only analysis of all 270 pairs from failed same-CPU
[run 36732093817](https://github.com/KoalaFacts/HeroParser/actions/runs/36732093817)
stratifies Segmented128 by execution order and early/late measurement cohorts:

| Fresh process pair | All median B/A | B executes first | A executes first | First 10 | Last 10 | Pairs with B slower | Allocation mismatches |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | 1.042781 | 1.042187 | 1.043676 | 1.042650 | 1.041585 | 30/30 | 0/30 |
| 2, B launched first | 1.052846 | 1.054107 | 1.053117 | 1.052042 | 1.052089 | 30/30 | 0/30 |
| 3 | 1.014256 | 1.014966 | 1.014538 | 1.013939 | 1.015025 | 29/30 | 5/30 |

The corresponding Stream4096 medians are 0.998036, 0.995756 and 0.991575. Thus a
single transport-independent B/A multiplier is not a sufficient model. The failed
second pair's persistent offset is not explained by one isolated slow sample or
the simple rule that whichever side executes second is slower. Equal allocated
bytes in pairs one/two do not establish equal GC collection counts or pause time.
Launch order was reversed only once; no statistical exclusion of startup effects
or a general bias distribution follows from this small experiment.

To reproduce the table without running a parser, read each `isolated-aa-*.ndjson`
in the retained artifact, select `kind=pair`, group by transport/cycle and
`candidateFirst`, and use the middle two sorted values' average as the median.
Early/late cohorts are pair indices 0-9 and 20-29. Count allocation mismatches
using the stored `baseline.AllocatedBytes` and `candidate.AllocatedBytes`; do not
recompute timings or delete outliers. This table does not replace the unchanged
original percentile acceptance rules or waive their failure.

## Ranked Hypotheses and Missing Discriminators

| Hypothesis | Existing constraint | Next discriminator / remaining limit |
| --- | --- | --- |
| Module/consumer mismatch | Frozen source, normal identities and identical DLL hashes already verified. | Retain these checks in every new worker; not a necessary explanation of the failed run. |
| One-shot outlier / simple alternating-order effect | Persistent difference in both order strata and early/late cohorts. | Not a sufficient model; does not exclude correlated scheduling or frequency effects. |
| Off-CPU scheduling or VM contention | CPU affinity alone did not remove the difference. | Compare whole-process CPU accounting with its observer wall window, plus task scheduling and system CPU snapshots. Missing/disabled scheduling fields must remain gaps. |
| Different GC behavior or heap layout | Equal allocated bytes in two pairs, but no GC/pause/address evidence. | Native GC attribution can suggest a follow-up, but exact pause/count correlation still needs dedicated runtime events. No forced-GC or no-GC-region change yet. |
| Different tier/PGO code shape or preparation history | Previous native trace contains only worker A and used Segmented-only preparation. Failed controls run Contiguous before Segmented128. | Capture both PIDs with frozen binaries in both preparation-history conditions; compare sampled native versions, sizes, branches and code layout. Raw code hashes differ under relocation and are not sufficient. |
| Native code/data placement or frequency | No address-normalized comparison or hardware-counter evidence. | Retain maps and native bytes; use explicit relocation analysis before attributing a raw-byte difference. Frequency snapshots are contextual, not effective-frequency measurements. |

## Fixed CI Diagnostic, Not Another Acceptance Attempt

`pipe_native_profile=true` plus `pipe_native_history_probe=true` runs exactly two
conditions once each, always retaining partial failures:

1. `SegmentedOnly`: verify both workers' full 36-case matrix, prepare Segmented128,
   then alternate fixed 512-repeat warmup batches for at least 10 pairs/10 seconds.
2. `ContiguousPrelude`: the same frozen workload and diagnostic settings, with a
   fixed Contiguous prepare/warmup stage before the unchanged Segmented128 stage.

Each condition uses fresh worker PIDs pinned before runtime startup. Each PID has
one serial 65536-repeat diagnostic batch and one 30-second native CPU capture.
The peer stays idle. No A/A acceptance pairs, A/B, extended segmented warmup,
threshold relaxation, inlining/PGO override or production change is introduced.
The conditions differ in preparation history, but the prelude is not an exact
replay of the old calibration/measured Contiguous batches. They run in fixed
condition order, with A captured before B: temporal/order confounding remains.
A later counterbalanced intervention is required before claiming history is causal.

Each batch retains before/after process stat and per-task scheduler/status data,
plus contextual system CPU, cgroup-root CPU and available frequency snapshots.
Process deltas validate PID/start-time identity and monotonic counters, convert
CPU ticks using `getconf CLK_TCK`, and label their observer window explicitly.
The [kernel proc specification](https://www.kernel.org/doc/html/latest/filesystems/proc.html)
defines the process accounting/fault fields;
[scheduler statistics](https://www.kernel.org/doc/html/latest/scheduler/sched-stats.html)
describe per-task run and run-queue counters. Do not sum surviving-task snapshots
as complete process accounting: threads may exit and counters may be disabled.
The root cgroup snapshot is not automatically the worker's exact cgroup.

CPU accounting includes all process threads and kernel time over the observer
window, including command/report overhead; it is not the consumer stopwatch or
the 30-second user-space perf window. Raw snapshots are not atomic with each
other. Missing frequency/scheduler evidence cannot be treated as zero cost.
This diagnostic may identify a discriminating signal, but a new runner cannot
recover the old failing PIDs or prove their root cause by correlation alone.

## Completed Diagnostic: Bias Not Reproduced

[Run 36797617555](https://github.com/KoalaFacts/HeroParser/actions/runs/36797617555)
at diagnostic revision `a9fff81` completed both conditions on one runner, without
retries. The diagnostic self-tests passed 84 checks; six workload/model/consumer
build invocations reported zero warnings and errors. The four workers passed
144 correctness cases in total. All four parser/model/consumer SHA-256 values
match each other and the frozen failed-control binaries. Each native trace has
zero reported lost samples. Re-reading the downloaded manifests, correctness
responses, binary hashes and jitdump records also passes artifact validation.

| History | PID A / B | A / B diagnostic batch ms | Batch B/A | Process CPU B/A | Samples A / B | Unresolved period A / B |
| --- | --- | --- | ---: | ---: | --- | --- |
| SegmentedOnly | 3458 / 3468 | 51778.0447 / 51619.7877 | 0.996944 | 0.996909 | 5961 / 5964 | 8.3375% / 6.6566% |
| ContiguousPrelude | 3916 / 3925 | 52859.5890 / 52692.6302 | 0.996841 | 0.996973 | 5969 / 5956 | 5.8469% / 5.2888% |

These are single long, instrumented diagnostic batches, not 30-pair acceptance
distributions. Neither pair reproduces the old 1.052846 median. The negative
result does not make the failed control stable or establish a performance gain.
Process CPU/observer-wall ratios are 0.999363, 0.999514, 0.999563 and 0.999766,
respectively. They do not support a large whole-process CPU deficit in these
windows, but include all threads and cannot exclude concurrent runtime work,
GC, frequency changes or the old trial's off-CPU time. `sched_schedstats` is zero
in all four snapshots: disabled scheduler counters are unavailable evidence.
ContiguousPrelude B has 1991 minor faults versus A's 48, yet similar batch time;
fault counts alone are not a demonstrated explanation. Major-fault deltas are
zero in all four windows.

### Native Shape and Hotspot Location

| History / side | Reader Tier1 bytes | Binder Tier1 bytes | Consumer Tier1 bytes | Reader exclusive period | Separately sampled column-aware TryReadRow period |
| --- | ---: | ---: | ---: | ---: | ---: |
| SegmentedOnly A | 9823 | 12859 | 8571 | 36.81% | not in the retained top-hotspot list |
| SegmentedOnly B | 9838 | 12766 | 10356 | 38.03% | not in the retained top-hotspot list |
| ContiguousPrelude A | 5319 | 11057 | 8361 | 6.00% | 32.12% |
| ContiguousPrelude B | 5319 | 11054 | 8587 | 5.76% | 33.73% |

Select exact `[OptimizedTier1]` methods in each `jit-methods.json`, not similarly
named OSR versions; match annotations using their PID and code index. Both
histories contain a compiled column-aware `TryReadRow`, so its mere existence
does not prove whether a caller inlines it. The SegmentedOnly reader annotation
contains the scalar cursor loop, consistent with the earlier structural inline
match. The ContiguousPrelude A top annotation instead locates that loop directly
inside `Csv.TryReadRow`, code index 15569, size 5957, address `0x7effcb6f4a00`.
Its ELF entry is `0x80`; subtract `0x80` to obtain method-relative offsets.

| ContiguousPrelude A ELF offsets | Observed operations | Frozen source structural match |
| --- | --- | --- |
| `0x442..0x461` | Length initialization test, length minus consumed, exit test | `reader.Remaining > 0` at [Csv.PipeReader.cs:179](https://github.com/KoalaFacts/HeroParser/blob/89c06810e76c4623ebad3cfc89c4bcdef41acd59/src/HeroParser/Csv.PipeReader.cs#L179) |
| `0x467..0x4b9` | End/index checks, byte load, index/consumed increments, segment-end check | `reader.TryRead(out byte current)` at [Csv.PipeReader.cs:181](https://github.com/KoalaFacts/HeroParser/blob/89c06810e76c4623ebad3cfc89c4bcdef41acd59/src/HeroParser/Csv.PipeReader.cs#L181) |
| `0x4bf..0x4d6` | Subtract one, overflow branch, signed-int round-trip check | `checked((int)(reader.Consumed - 1))` at [Csv.PipeReader.cs:184](https://github.com/KoalaFacts/HeroParser/blob/89c06810e76c4623ebad3cfc89c4bcdef41acd59/src/HeroParser/Csv.PipeReader.cs#L184) |

This confirms that hotspot attribution depends on generated native shape and
that the earlier single-history reader percentage cannot simply be applied to
the failed controls. It does not establish history, PGO or inlining as the old
bias's cause. History conditions run in fixed order; early runtime profiling and
long diagnostic batches differ from the failing trial. Within SegmentedOnly,
the two consumer sizes differ substantially despite near-equal batch times:
native byte count or a relocated code hash is not a causal performance metric.
Instruction percentages remain sampled local period, not exact operation cost.

## Historical Evidence Boundary

The retained failed cycle-two command transcripts give a concrete mismatch to
remove: each worker ran 135 Contiguous batches totaling 22463 parses before
`prepare Segmented128` (command ID 138). Counts by repeat size are
`1x1, 2x1, 4x1, 8x1, 16x1, 32x1, 64x96, 128x1, 256x1, 512x31`.
The diagnostic's fixed 512-repeat prelude is not this calibration, warmup and
measured history. Matching counts cannot recreate past scheduling or JIT timing.

The follow-up requirement was to preserve the original request ordering,
calibration policy and measurement windows, with normal runtime settings and no
startup profiler. It must associate any reproduced bias with evidence from those
same PIDs, rather than compare a failed uninstrumented trial with another VM's
long profiled batch. Post-window native capture is a candidate; it records code
at capture time, not necessarily every version active during earlier timings.
Before publishing any memory dump, give workers a curated non-secret environment
and verify that CI credentials cannot be inherited into it. A dump is not GC
pause history; GC event collection needs its own explicit observer-effect check.

Predeclare a fixed process-pair budget and launch-order counterbalance, retain
every outcome, and label this investigation separately from acceptance. If bias
does not reproduce, report that result rather than rerun until it does. Only
after reproducible same-PID evidence discriminates a hypothesis should one
counterbalanced causal intervention be selected. Do not disable PGO merely to
make a non-reproducing diagnostic appear stable.

At the preceding diagnostic's completion, four-worker native/context evidence
was complete but bias was not reproduced. Old workers have
exited without native/GC capture; their missing process state cannot be recovered
from aggregate timing logs. No production optimization or merge is approved.

## Historical Same-PID Capture And Recovery

[Protocol and single-variable plan](history-protocol.md) now implement the exact
cycle-two command/repeat history, not new adaptive calibration. Four fixed pairs
retain 292 requests per worker, including the complete correctness matrix,
Contiguous's 135 batches / 22463 parses, all original segmented/stream requests,
and 30 measured pairs per transport. Elapsed history and scheduling cannot be
recreated. Reference conditions include boundary accounting and enabled scheduler
statistics; they are not pristine replicas of the old failed control.

[Capture 36807711817](https://github.com/KoalaFacts/HeroParser/actions/runs/36807711817)
at `7252c32` ran all four histories once. It retained a postprocessing failure on
blank perf separators and combined PID/TID formatting, not a workload failure.
[Artifact recovery 36808849164](https://github.com/KoalaFacts/HeroParser/actions/runs/36808849164)
at `6ed56f2` revalidated 288 correctness cases and 360 pairs / 720 batches,
decoded the same runtime traces and recovered native/request attribution without
new workers or sampling. The original failed manifest and all 210 source files
remain byte-identical. All eight workers match original parser/model/consumer
hashes, runtime .NET 10.0.12, workstation GC and CPU 0.

| Fixed condition | A PID | B PID | Contiguous median B/A | Segmented128 median B/A | Stream4096 median B/A |
| --- | --- | --- | --- | --- | --- |
| reference-b-first | 3674 | 3666 | 0.991894 | 0.993049 | 0.990934 |
| observed-a-first | 3724 | 3733 | 0.997355 | 1.020581 | 0.997290 |
| observed-b-first | 3824 | 3815 | 1.004805 | 1.065718 | 1.106059 |
| reference-a-first | 3909 | 3917 | 0.997878 | 1.001352 | 0.993741 |

Observed-b-first segmented p10/p90 are 1.057814/1.070424. Its B-side divergence
is not confined to one measured outlier. However, the two reference pairs do
not reproduce the old 5.28% bias; the GC/JIT/native/scheduler collection bundle,
fixed condition order, startup/JIT history and VM drift remain confounders.
These diagnostic ratios neither clear the original failed gate nor prove that
instrumentation is sufficient or necessary for the old failure.

### Same-PID Runtime And Scheduling Association

All four observed runtime traces report zero lost events, 956 GC starts each,
and 6013/6871/6118/6904 Method events respectively. Native captures have
7325/7797/7365/8292 samples; 6108/6505/6188/6792 match preceding same-PID JIT
ranges and request windows, and 4949/5273/5003/5532 belong to named workloads.
The remaining IPs are not assigned to JIT code by this mapper; they are not
silently charged to parser work. Kernel switch/wakeup records are retained.

The following sums cover the 30 Segmented128 measured request windows, not the
entire history. Batch time is per-parse consumer time multiplied by its original
repeat count, not the sum of normalized per-parse `Milliseconds`.

| Condition/side | Batch total ms | Runtime suspension overlap ms | Surviving-task run ms | Surviving-task run-queue ms | Process CPU ms |
| --- | --- | --- | --- | --- | --- |
| observed-a-first A / 3724 | 6014.2051 | 27.5413 | 6020.3455 | 33.7579 | 6020 |
| observed-a-first B / 3733 | 6132.1821 | 26.5480 | 6135.1895 | 35.3324 | 6130 |
| observed-b-first A / 3824 | 6127.3754 | 25.6034 | 6130.4481 | 29.3147 | 6130 |
| observed-b-first B / 3815 | 6521.2078 | 24.8454 | 6524.3142 | 29.8079 | 6530 |

In the larger divergence, batch difference is 393.8324 ms and recorded task-run
difference 393.8661 ms, versus 0.4932 ms extra run-queue time and 0.7580 ms *less*
runtime suspension overlap on B. These records constrain a model where extra
recorded suspension or queue waiting alone explains this pair; they do not
exclude GC-related on-CPU work, absent threads, CPU frequency or observer effects.
Runtime overlap is trace-UTC/coordinator-UTC approximate, scheduling sums cover
same-start-time survivors (8 or 9 tasks), and process CPU has 10 ms resolution.
Do not add overlap/queue/runtime counts as independent exclusive costs.

### Native Versions Active During The Same Requests

Segmented samples match versions loaded before those request windows. For
observed-b-first, A's separate `TryReadRow` is index 17258 / 6583 bytes and B's
17907 / 6590 bytes; both receive 389 sampled hits. `TryBindPipeSequenceRow` is
A index 15618 / 11024 bytes / 105 samples versus B 16771 / 11375 / 135.
The record enumerator receives 49 versus 103 hits, while consumer checksum
state-machine hits are 132 versus 112. Exclusive sampled hits are not inclusive
operation costs; neither size nor relocated code hash proves slower instructions.
Reader slow-path bodies remain separate (A 5334 bytes, B 5313 bytes).

This evidence narrows this observed pair to additional on-CPU/runtime/native work
without identifying a responsible source edit. Optimized inline/local maps remain
unavailable. Source ownership uses the same authenticated frozen source/PDB chain
as [the earlier map](native-source-map.md), not a claim of exact line-level cost.

The predeclared single-variable test changes only the EventPipe JIT keyword
(GC+JIT versus GC-only), preserving native code/GC/scheduler evidence and the
same history in four new counterbalanced pairs. It does not disable PGO or edit
the parser. Its setup and interpretation rules are in the protocol. No new
root-cause or throughput claim follows merely from collection success.

## Single-Variable Result

[Run 36809723846](https://github.com/KoalaFacts/HeroParser/actions/runs/36809723846)
at `25c95d4` completed its fixed four pairs once. 31 history and 84 native checks
passed; decoder and three frozen workload builds were warning/error-free. The
downloaded artifacts independently validate 288 correctness cases and 360 pairs /
720 batches, all original commands, frozen fingerprints, eight native/runtime
owners and the intended JIT-event treatment. Every trace has 956 GC starts and
zero reported loss; all four GC-only workers have zero Method events.

| Condition | A PID | B PID | Contiguous median B/A | Segmented128 median B/A | Stream4096 median B/A |
| --- | --- | --- | --- | --- | --- |
| GC+JIT / B-first | 3386 | 3377 | 0.988890 | 0.991572 | 0.987253 |
| GC-only / A-first | 3451 | 3460 | 1.086810 | 1.015000 | 1.004745 |
| GC-only / B-first | 3533 | 3524 | 0.985896 | 0.996265 | 0.984565 |
| GC+JIT / A-first | 3604 | 3613 | 1.166577 | 0.977545 | 1.000925 |

The predeclared outcome is **inconclusive**: the two GC+JIT segmented controls
do not reproduce the prior large B-side bias, so near-equal GC-only results do
not establish a fix or a JIT-keyword cause. Contiguous still diverges strongly
in one GC+JIT condition (p90 1.399485). No additional process pairs were run.

The control's runner is AMD EPYC 9V45, versus AMD EPYC 9V74 in the preceding
same-PID capture. Conditions within each job are same-runner; the two jobs are
not the same machine. Frozen counts now yield minimum batches around 103-120 ms,
not the old calibrated ~200 ms. This is diagnostic history replay, not a passed
adaptive timing protocol or evidence of a twofold parser speedup.

Next boundary: reproduce within a predeclared same-hardware/run budget and keep
the reproducing control alongside the causal arm. Current evidence supports
examining on-CPU/native/runtime differences, not blaming GC wait or a particular
source line. No production optimization, relaxed acceptance or merge is approved.
