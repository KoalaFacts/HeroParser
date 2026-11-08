---
name: run-benchmarks
description: Run the BenchmarkDotNet suite and judge the results with the measurement rules in CLAUDE.md
disable-model-invocation: true
---

# Run Benchmarks

Run the HeroParser benchmark suite locally to look for large effects or to profile.

## Steps

1. Run the benchmark suite (or one class with `--filter`):
```bash
dotnet run -c Release --project benchmarks/HeroParser.Benchmarks
```

2. Parse the results table from stdout.

3. Judge the results with CLAUDE.md, "Performance Optimization Lessons" → "How to measure":
   - Only the same-runner A/B in the PR benchmark job establishes a speedup or regression; local runs are too noisy for effects under ~20%.
   - The "Historical Benchmark Reference" table in CLAUDE.md is a historical observation on one CPU, not a baseline to compare a local run against.

4. Report what was run (CPU, runtime, benchmark filter), the raw table, and whether an effect is large enough to be worth a same-runner A/B. Never report a local run as a performance claim.
