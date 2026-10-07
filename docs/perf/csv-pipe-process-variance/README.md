# Process-to-Process Variance in the CSV Pipe Probe (Local Exploration)

Exploratory, October 2026. **Not a timing acceptance run, and not evidence about
throughput.** It does not change, waive or retry the failed gate recorded in
[bias-investigation.md](../../../benchmarks/CsvPipeABProbe/bias-investigation.md); the
`CSV Pipe Correctness and Timing Controls` job keeps failing by design.

## Question

The same-source A/A control in run
[36732093817](https://github.com/KoalaFacts/HeroParser/actions/runs/36732093817) put one
fresh process 5.28% behind another on `Segmented128`. The retained same-run captures show
the two processes holding different optimized machine code (caller 10209 vs 5319 bytes).
Three narrower questions, answerable on one machine without a CI sampling budget:

1. Does an identical binary, input and CPU pin produce different optimized code per process?
2. Is that difference tied to dynamic PGO?
3. Does the code shape visibly track the measured time?

## Method

`replay.py` starts one fresh pinned worker process per run, from the frozen probe build, and
replays the policy of `run-isolated.ps1` against it: pilot calibration to 100 ms, warmup of
at least 10 batches and 10 s, calibration to 125 ms, then 30 measured batches, for
`Contiguous`, `Segmented128` and `Stream4096` in that order. Two arms alternate order every
run so machine drift hits both equally:

| Arm | Runtime setting |
| --- | --- |
| `default` | stock .NET 10.0.12 (dynamic PGO on) |
| `nopgo` | `DOTNET_TieredPGO=0`, **a discriminator only** |

16 processes per arm, 32 in total, about 49 s each. `DOTNET_JitDisasmSummary=1` with
`DOTNET_JitStdOutFile` records the tier and code size of every compiled method in every
process; `analyze.py` extracts the final fully optimized (non-OSR) version of
`<MoveNextSlowAsync>d__31:MoveNext()` ("caller") and `Csv:TryReadRow` ("reader").
`stats.py` produces every number below from `runs.ndjson` and `summary.json`.

`src` and `Directory.*` are byte-identical to the frozen revision `89c0681`; only the probe's
csproj and `IsolatedWorker.cs` differ, by diagnostic settings. Machine: 4 vCPU Intel Xeon
@ 2.10 GHz VM, .NET 10.0.12, worker pinned to one CPU.

## Results

**Optimized code differs between identical processes, and dynamic PGO is the source.**
This uses code sizes only, so it does not depend on timing noise.

| Arm | Distinct (caller, reader) shapes in 16 processes | Most common shape |
| --- | ---: | ---: |
| `default` | 13 (callers 5312 / 5332 / 6712 / 6952, ten reader sizes 5745-6559) | 3 of 16 |
| `nopgo` | 2 (callers 6203 / 6221, reader 5190) | 15 of 16 |

**The timing noise floor on this machine is larger than any plausible effect.**

| Quantity | `default` | `nopgo` |
| --- | ---: | ---: |
| Max/min of the 30 batches inside one process (median over processes) | 1.92 | 1.72 |
| Max/min across processes of the per-process minimum | 1.30 | 1.30 |
| Max/min across processes of the per-process median | 1.49 | 1.35 |

A 5% effect is three to six times smaller than the noise, so timing comparisons between
single processes cannot resolve it.

**Code shape does not visibly track speed, with almost no power to say so.** Spearman
correlation between per-process size and time (`default`, n = 16): caller size +0.07 and
reader size -0.09 against the per-process minimum, +0.01 and -0.13 against the median.
Median of per-process minima by caller size: 0.546 (5312, n=1), 0.540 (5332, n=5),
0.559 (6712, n=8), 0.535 (6952, n=2) ms. This is not evidence of absence: byte size is a
coarse proxy, and the noise above hides small effects. A slow process is only weakly slow on
every transport (`default` seg~contiguous +0.41, seg~stream +0.23; `nopgo` -0.37 and +0.22).

**Dynamic PGO is worth roughly 16% here.** Median of per-process minima: 0.543 ms with
default settings versus 0.633 ms with `TieredPGO=0`, ratio 1.165; `nopgo` is slower in 14 of
16 index-paired runs. This supports the existing rule against disabling PGO to make a
diagnostic look stable: it would buy code-shape stability at a real throughput cost.

## What this does and does not establish

- Established: the precondition of the CI observation (two identical processes holding
  different optimized code) is reproducible locally, and it goes away when dynamic PGO is
  off.
- Not established: that the code difference causes the CI timing bias. The CI failure was
  highly consistent inside the pair (p10 1.049, p90 1.056 around 1.053), unlike the noise
  seen here, so this machine is not reproducing that same phenomenon.
- Code sizes here (caller about 5.3-7.0 KB) differ from the CI captures (10209 vs 5319 bytes).
  Different CPU (Xeon here, AMD EPYC there) and instruction-set availability make the
  numbers non-comparable, and no claim is made that the mechanism is the same.

## Limitations

- One worker per process, with no alternating peer process, unlike the CI controls.
- Policy replay, not the exact 292-request history of `history-protocol.md`.
- Wall-clock on a shared VM with unrecorded frequency and steal time.
- `TieredPGO=0` changes everything downstream of tiering, not only inlining.
- 16 processes per arm; no multiple-comparison correction was applied.
- Raw JIT logs (about 3 MB) are not retained; `summary.json` keeps the extracted versions.

## Reproduce

```bash
dotnet build src/HeroParser/HeroParser.csproj -c Release -f net10.0
dotnet build benchmarks/CsvPipeABProbe/Models/CsvPipeABModels.csproj -c Release \
  -p:AssemblyName=CsvPipeABModels -p:ParserDll=$PWD/src/HeroParser/bin/Release/net10.0/HeroParser.dll \
  -p:BaseIntermediateOutputPath=obj/isolated/ -p:OutputPath=bin/Release/isolated/
dotnet build benchmarks/CsvPipeABProbe/CsvPipeABProbe.csproj -c Release -p:SingleModule=true \
  -p:CandidateModelsDll=$PWD/benchmarks/CsvPipeABProbe/Models/bin/Release/isolated/CsvPipeABModels.dll \
  -p:BaseIntermediateOutputPath=obj/isolated/ -p:OutputPath=bin/Release/isolated/

export STUDY_WORKER_DIR=$PWD/benchmarks/CsvPipeABProbe/bin/Release/isolated STUDY_OUT=/tmp/study
mkdir -p $STUDY_OUT
python3 -I docs/perf/csv-pipe-process-variance/replay.py 16   # about 26 min, needs Linux taskset
python3 -I docs/perf/csv-pipe-process-variance/analyze.py
python3 -I docs/perf/csv-pipe-process-variance/stats.py
```

Running `stats.py` with no `STUDY_OUT` reproduces the numbers above from the retained data.

## Predeclared CI experiment

Approved as a single fixed budget; this section and `decide.py` are committed before the first
dispatch so the outcome cannot be reinterpreted afterwards. It is exploratory and never
waives, relaxes or replaces the failed gate in `benchmarks.yml`.

**Design.** Workflow [`csv-pipe-variance.yml`](../../../.github/workflows/csv-pipe-variance.yml)
is manual only (`workflow_dispatch`, exact confirmation text, first attempt only, 30-minute
job limit). On `ubuntu-24.04`, from the frozen workload, it builds the probe, runs **20 fresh
`default`-arm processes** one after another pinned to CPU 0, records the code shape of every
process, then applies `decide.py`. One dispatch, no retry, no extension, no `TieredPGO=0` arm.
The job fails only when the data is invalid; every valid outcome below is a green job.

**Rule** (checked in this order; `decide.py` holds the constants):

| Quantity | Definition |
| --- | --- |
| noise | median over processes of max/min of the 30 `Segmented128` batches |
| spread | p90 / p10 of the per-process minima, minus 1 |
| shapes | distinct (caller, reader) final optimized code sizes |
| rho | larger of the absolute Spearman correlations of the per-process minimum with caller size and reader size |

| Order | Condition | Outcome |
| ---: | --- | --- |
| 0 | fewer than 20 complete processes, nonzero worker exit, missing optimized version | `invalid` (job fails) |
| 1 | noise > 1.30 | `runner-too-noisy-inconclusive` |
| 2 | spread <= 3% and shapes >= 4 | `shape-irrelevant-at-this-precision` |
| 3 | spread > 5% and rho >= 0.5 | `shape-plausibly-causal-candidate` |
| 4 | otherwise | `inconclusive` |

**What each outcome allows.** `shape-plausibly-causal-candidate` only justifies proposing one
single-variable code-shape intervention, which needs its own approval and sampling budget.
`shape-irrelevant-at-this-precision` points the search away from code shape. The other two
outcomes change nothing. No outcome approves a production change, a threshold change or
dropping the gate, and no result is rerun to obtain a different outcome.

The local VM here had noise of about 1.9, so a similar runner would end in
`runner-too-noisy-inconclusive`; that is a possible and acceptable result.

**Running it** (after this change is merged to `main`):

```bash
gh workflow run csv-pipe-variance.yml -f confirm=fixed-20-processes-one-dispatch
```

The artifact `csv-pipe-variance-<run>-1` keeps `runs.ndjson`, `summary.json`,
`decision.json`, the raw JIT logs and the runner context.
