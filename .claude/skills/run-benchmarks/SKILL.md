---
name: run-benchmarks
description: Run BenchmarkDotNet suite and compare results against Sep baseline documented in CLAUDE.md
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

3. Compare against the baseline in CLAUDE.md under "Benchmark Baseline (vs Sep 0.12.1)":
   - Standard (10k rows x 25 cols): HeroParser should be ~0.79x Sep (quoted), ~0.93x (unquoted)
   - Wide CSVs: 25-45% faster than Sep
   - Allocations: 4 KB fixed

4. Report:
   - Whether performance improved, regressed, or held steady vs the documented baseline
   - Any allocation changes
   - If results changed significantly, propose an update to the CLAUDE.md baseline numbers
