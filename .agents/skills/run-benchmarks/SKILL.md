---
name: run-benchmarks
description: Run BenchmarkDotNet suite and compare results against Sep baseline documented in AGENTS.md
disable-model-invocation: true
---

# Run Benchmarks

Run the HeroParser benchmark suite and compare against the documented baseline.

## Steps

1. Run the benchmark suite:
```bash
dotnet run -c Release --project benchmarks/HeroParser.Benchmarks
```

2. Parse the results table from stdout.

3. Compare against the baseline in AGENTS.md under "Benchmark Baseline (vs Sep 0.17.0)":
   - Standard (10k rows x 25 cols): HeroParser is faster than Sep (quoted) and matches/exceeds Sep (unquoted)
   - Wide CSVs: 25-45% faster than Sep
   - Allocations: 112 B fixed (vs Sep's ~4 KB)

4. Report:
   - Whether performance improved, regressed, or held steady vs the documented baseline
   - Any allocation changes
   - If results changed significantly, propose an update to the AGENTS.md baseline numbers
