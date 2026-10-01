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

Initial status: diagnostic implementation awaiting CI; root cause unresolved.
The next step after evidence is a single predeclared, counterbalanced causal
intervention for the best-supported hypothesis, not a throughput patch or a
repeat-until-green control run. If no difference is reproduced, retain that
negative result and do not declare the earlier failure solved.
