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
retaining a reproducing same-run control. That experiment is not implemented
or authorized here; the exhausted sampling budget is not extended.
