# CLI streaming probe

Run from the repository root:

```bash
dotnet run -c Release -f net10.0 --project benchmarks/HeroParser.CliProbe -- query utf8 90000
dotnet run -c Release -f net10.0 --project benchmarks/HeroParser.CliProbe -- query utf16 90000
dotnet run -c Release -f net10.0 --project benchmarks/HeroParser.CliProbe -- query xlsx 90000
```

Arguments are `<schema|profile|query|translate> <utf8|utf16|xlsx> <data-rows>`. `schema` does not support `.xlsx`. The probe generates the same eight-column logical data for each format, then runs the CLI command in a fresh child process. Fixture generation and output-row verification are outside the timed section. Temporary files are deleted even when a run fails.

`query` uses a fixed local answer. `translate` uses a local model substitute that echoes every input row as JSONL, with a batch size of 100; the probe verifies the output row count. Neither result includes real model latency or token cost. The JSON result reports command time, baseline and peak process working set, cumulative managed allocation, input/output size, and model-call count. Working set is sampled every 5 ms, so very brief spikes can be missed. Compare runs on the same machine and build; do not interpret these numbers as parser-only throughput or a cross-platform ranking.

## Observed baseline (2026-09-26)

Windows, .NET SDK 10.0.401, AMD Ryzen AI 9 HX PRO 370, Release net10.0, merged PR #107 code. Values below are medians of three fresh-process runs; time includes JIT and is sensitive to concurrent machine load.

| Command / rows | Input | Elapsed | Peak working set | Managed allocation |
|---|---|---:|---:|---:|
| `schema` / 1,000,000 | UTF-8 CSV | 333 ms | 34.7 MiB | 0.3 MiB |
| `schema` / 1,000,000 | UTF-16 CSV | 1,619 ms | 319.9 MiB | 282.8 MiB |
| `query` / 90,000 | UTF-8 CSV | 2,411 ms | 47.9 MiB | 29.4 MiB |
| `query` / 90,000 | UTF-16 CSV | 1,749 ms | 88.1 MiB | 63.8 MiB |
| `query` / 90,000 | XLSX | 3,000 ms | 61.9 MiB | 74.6 MiB |

At 100,000 data rows, UTF-16 `profile`, `query`, and `translate` each returned failure: their in-memory fallback includes the header under the default 100,000-row parser limit. At 500,000 UTF-8 data rows, `query` peaked near 54 MiB, while `translate` peaked near 63 MiB with about 3.2 GiB of cumulative allocation from batch formatting and echoed output. These larger-run figures were single observations, not stable baselines.

**Next priority:** remove the UTF-16 in-memory fallback and its row-count limit before optimizing Excel. Excel still materializes rows in the CLI, but its observed 90,000-row peak was below UTF-16, and it did not hit the CSV parser limit in these runs.
