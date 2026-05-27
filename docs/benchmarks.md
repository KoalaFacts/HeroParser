# HeroParser Benchmarks & Performance Guide

This document details the performance benchmarks, memory profile characteristics, and head-to-head comparisons of **HeroParser** against leading .NET tabular libraries (`Sep`, `Sylvan`, and `CsvHelper`).

---

## Benchmark Environment
- **CPU**: AMD Ryzen AI 9 HX PRO 370 CPU (Zen 5 architecture)
- **Runtime**: .NET 10.0 (Release Build)
- **Instruction Sets**: AVX-512 enabled, SSE2/AVX2 fallbacks verified.
- **Validation tool**: BenchmarkDotNet v0.13.12+

---

## 1. Head-to-Head Reading Comparison (10,000 Rows x 25 Columns)

Measures throughput and memory allocations under `.NET 10.0`.

### Case A: Unquoted Data (`WithQuotes = False`)
* **Sep (Baseline)**: **2.092 ms** (Mean) | **3,952 B** (Allocated) | **1.00x** (Ratio)
* **Sylvan**: **2.293 ms** (Mean) | **43,528 B** (Allocated) | **1.10x** (Ratio)
* **CsvHelper**: **24.301 ms** (Mean) | **21,328 B** (Allocated) | **11.69x** (Ratio)
* **HeroParser UTF-8 (byte[])**: **1.832 ms** (Mean) | **112 B** (Allocated) | **0.88x** (Ratio) (**12% FASTER than Sep**)
* **HeroParser UTF-16 (string)**: **2.319 ms** (Mean) | **112 B** (Allocated) | **1.12x** (Ratio) (**Neck-and-neck with Sep (only 12% slower) while using 35x less memory!**)

### Case B: Quoted Data (`WithQuotes = True`)
* **Sep (Baseline)**: **3.440 ms** (Mean) | **4,048 B** (Allocated) | **1.00x** (Ratio)
* **Sylvan**: **11.178 ms** (Mean) | **43,531 B** (Allocated) | **3.25x** (Ratio)
* **CsvHelper**: **27.231 ms** (Mean) | **21,328 B** (Allocated) | **7.92x** (Ratio)
* **HeroParser UTF-8 (byte[])**: **3.036 ms** (Mean) | **112 B** (Allocated) | **0.88x** (Ratio) (**12% FASTER than Sep**)
* **HeroParser UTF-16 (string)**: **3.870 ms** (Mean) | **112 B** (Allocated) | **1.13x** (Ratio) (**Within 13% of Sep while allocating only 112 bytes!**)

---

## 2. Head-to-Head Writing Comparison (1,000 Rows x 25 Columns)

Measures sync writing throughput and memory allocations under `.NET 10.0`.

### Case A: Unquoted Data (`WithQuotes = False`)
* **Sep (Baseline)**: **4.455 ms** (Mean) | **1.98 MB** (Allocated) | **1.00x** (Ratio)
* **Sylvan**: **2.524 ms** (Mean) | **1.26 MB** (Allocated) | **0.57x** (Ratio)
* **HeroParser (row-by-row)**: **1.479 ms** (Mean) | **1.21 MB** (Allocated) | **0.33x** (Ratio) (**3.0x FASTER than Sep**)

### Case B: Quoted Data (`WithQuotes = True`)
* **Sep (Baseline)**: **3.306 ms** (Mean) | **1.98 MB** (Allocated) | **1.00x** (Ratio)
* **Sylvan**: **2.013 ms** (Mean) | **1.34 MB** (Allocated) | **0.61x** (Ratio)
* **HeroParser (row-by-row)**: **1.655 ms** (Mean) | **1.29 MB** (Allocated) | **0.50x** (Ratio) (**2.0x FASTER than Sep**)

---

## 3. Core Architectural Pillars & Memory Profile

- **Allocation-Free Hot Path**: HeroParser maintains a fixed allocation of **only 112 bytes** in its reading path, regardless of column counts or row counts, representing a **97% memory reduction** compared to Sep and **99.7% reduction** compared to Sylvan.
- **AVX-512 & AVX2 Quote-Aware SIMD**: Uses branchless PCLMULQDQ carry-less multiplication to mask quotes at maximum hardware throughput.
- **Register-Based Slow Path**: The UTF-16 parser uses register-to-register bitwise checks instead of memory reloads, maximizing memory bandwidth.

---

## How to Run Benchmarks Locally

To execute these benchmarks on your local hardware:

```bash
dotnet run -c Release --project benchmarks/HeroParser.Benchmarks --framework net10.0 -- --vs-sep-reading
```
