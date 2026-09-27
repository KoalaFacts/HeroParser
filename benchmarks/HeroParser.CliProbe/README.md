# CLI streaming probe

Run from the repository root:

```bash
dotnet run -c Release -f net10.0 --project benchmarks/HeroParser.CliProbe -- query utf8 90000
dotnet run -c Release -f net10.0 --project benchmarks/HeroParser.CliProbe -- query utf16 90000
dotnet run -c Release -f net10.0 --project benchmarks/HeroParser.CliProbe -- query xlsx 90000
```

Arguments are `<schema|profile|query|translate> <utf8|utf16|xlsx> <data-rows>`. `schema` does not support `.xlsx`. The probe generates the same eight-column logical data for each format, then runs the CLI command in a fresh child process. Fixture generation and output-row verification are outside the timed section. Temporary files are deleted even when a run fails.

`query` uses a fixed local answer. `translate` uses a local model substitute that echoes every input row as JSONL, with a batch size of 100; the probe verifies the output row count. Neither result includes real model latency or token cost. The JSON result reports command time, baseline and peak process working set, cumulative managed allocation, input/output size, and model-call count. Working set is sampled every 5 ms, so very brief spikes can be missed. Compare runs on the same machine and build; do not interpret these numbers as parser-only throughput or a cross-platform ranking.

## Historical baseline before UTF-16 streaming (2026-09-26)

Windows, .NET SDK 10.0.401, AMD Ryzen AI 9 HX PRO 370, Release net10.0, merged PR #107 code. These values predate the UTF-16 streaming changes in PR #109 and are not representative of current `main`. Values below are medians of three fresh-process runs; time includes JIT and is sensitive to concurrent machine load.

| Command / rows | Input | Elapsed | Peak working set | Managed allocation |
|---|---|---:|---:|---:|
| `schema` / 1,000,000 | UTF-8 CSV | 333 ms | 34.7 MiB | 0.3 MiB |
| `schema` / 1,000,000 | UTF-16 CSV | 1,619 ms | 319.9 MiB | 282.8 MiB |
| `query` / 90,000 | UTF-8 CSV | 2,411 ms | 47.9 MiB | 29.4 MiB |
| `query` / 90,000 | UTF-16 CSV | 1,749 ms | 88.1 MiB | 63.8 MiB |
| `query` / 90,000 | XLSX | 3,000 ms | 61.9 MiB | 74.6 MiB |

Before PR #109, UTF-16 `profile`, `query`, and `translate` each failed at 100,000 data rows because their in-memory fallback included the header under the default 100,000-row parser limit. At 500,000 UTF-8 data rows, `query` peaked near 54 MiB, while `translate` peaked near 63 MiB with about 3.2 GiB of cumulative allocation from batch formatting and echoed output. These larger-run figures were single observations, not stable baselines.

## Current spot check after UTF-16 streaming (2026-09-27)

Windows, .NET SDK 10.0.401, Release net10.0, PR #108's probe branch updated to include `main` through PR #113. Each row below is one fresh-process run, not a median or a controlled before/after comparison. The old 100,000-row UTF-16 failure did not reproduce.

| Command / rows | Input | Elapsed | Peak working set | Managed allocation |
|---|---|---:|---:|---:|
| `schema` / 1,000,000 | UTF-16 CSV | 131 ms | 35.3 MiB | 0.6 MiB |
| `profile` / 100,000 | UTF-16 CSV | 446 ms | 38.5 MiB | 0.7 MiB |
| `query` / 100,000 | UTF-16 CSV | 336 ms | 38.3 MiB | 1.0 MiB |
| `translate` / 100,000 | UTF-16 CSV | 4,977 ms | 59.1 MiB | 653.0 MiB |

The `translate` run made 1,000 local echo-model calls and verified all 100,000 output rows. Its cumulative allocation includes prompt formatting and the echo substitute; it does not measure a real model or isolate parser allocations. Use repeated, same-machine runs before prioritizing further optimization. Excel still materializes rows in the CLI, but the historical 90,000-row Excel measurement alone does not establish the next bottleneck on current `main`.
