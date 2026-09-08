# HeroParser Benchmarks & Performance Guide

This document details the performance benchmarks, memory profile characteristics, and head-to-head comparisons of **HeroParser** against leading .NET tabular libraries:
- **Sep** v0.17.0
- **Sylvan.Data.Csv** v1.4.4
- **CsvHelper** v33.1.0

---

## Benchmark Environment
- **Runner**: GitHub Actions `ubuntu-latest`, AMD EPYC 9V74 (Zen 4, AVX-512), 2 physical cores
- **Runtime**: .NET 10.0 (Release Build)
- **Validation tool**: BenchmarkDotNet v0.15.8
- **Method**: all libraries measured in the same job on the same runner. GitHub-hosted runners land on different CPU models from run to run, so numbers from different runs are not comparable with each other; every pull request that touches the parser gets a base-vs-head comparison on one runner, posted in the PR.

---

## 1. Head-to-Head Reading Comparison (10,000 Rows x 25 Columns)

Measures throughput and memory allocations under `.NET 10.0` compared to **Sep v0.17.0**, **Sylvan.Data.Csv v1.4.4**, and **CsvHelper v33.1.0** (v2.7.0).

### Case A: Unquoted Data (`WithQuotes = False`)
* **Sep (Baseline)**: **663.5 µs** (Mean) | **3,952 B** (Allocated) | **1.00x** (Ratio)
* **Sylvan**: **823.0 µs** (Mean) | **43,529 B** (Allocated) | **1.24x** (Ratio)
* **CsvHelper**: **14,128.5 µs** (Mean) | **21,328 B** (Allocated) | **21.29x** (Ratio)
* **HeroParser UTF-8 (byte[])**: **491.4 µs** (Mean) | **152 B** (Allocated) | **0.74x** (Ratio) (**26% faster than Sep**)
* **HeroParser UTF-16 (string)**: **534.4 µs** (Mean) | **152 B** (Allocated) | **0.81x** (Ratio) (**19% faster than Sep**)

### Case B: Quoted Data (`WithQuotes = True`)
* **Sep (Baseline)**: **1,343.2 µs** (Mean) | **4,048 B** (Allocated) | **1.00x** (Ratio)
* **Sylvan**: **5,018.5 µs** (Mean) | **43,530 B** (Allocated) | **3.74x** (Ratio)
* **CsvHelper**: **14,589.3 µs** (Mean) | **21,328 B** (Allocated) | **10.86x** (Ratio)
* **HeroParser UTF-8 (byte[])**: **898.8 µs** (Mean) | **152 B** (Allocated) | **0.67x** (Ratio) (**33% faster than Sep**)
* **HeroParser UTF-16 (string)**: **984.1 µs** (Mean) | **152 B** (Allocated) | **0.73x** (Ratio) (**27% faster than Sep**)

Both element types are first-class: `string` input no longer needs a UTF-8 conversion for performance. Prefer the `byte[]` APIs when the data is already bytes.

---

## 2. Head-to-Head Writing Comparison (1,000 Rows x 25 Columns)

Measures sync writing throughput and memory allocations under `.NET 10.0` compared to **Sep v0.14.1** and **Sylvan.Data.Csv v1.4.4**.

### Case A: Unquoted Data (`WithQuotes = False`)
* **Sep (Baseline)**: **4.455 ms** (Mean) | **1.98 MB** (Allocated) | **1.00x** (Ratio)
* **Sylvan**: **2.524 ms** (Mean) | **1.26 MB** (Allocated) | **0.57x** (Ratio)
* **HeroParser (row-by-row)**: **1.479 ms** (Mean) | **1.21 MB** (Allocated) | **0.33x** (Ratio) (**3.0x FASTER than Sep**)

### Case B: Quoted Data (`WithQuotes = True`)
* **Sep (Baseline)**: **3.306 ms** (Mean) | **1.98 MB** (Allocated) | **1.00x** (Ratio)
* **Sylvan**: **2.013 ms** (Mean) | **1.34 MB** (Allocated) | **0.61x** (Ratio)
* **HeroParser (row-by-row)**: **1.655 ms** (Mean) | **1.29 MB** (Allocated) | **0.50x** (Ratio) (**2.0x FASTER than Sep**)

---

## 3. Write-Path Capacity Pre-allocation Optimization (ToText Facades)

To eliminate GC allocations and backing buffer copy-doubling cycles when generating strings in memory, we implement backing capacity pre-allocation when writing collections:
- **CSV & Fixed-Width (`ToText`)**: Uses pre-allocated backing `StringBuilder` sizes based on estimated record counts. Delivers **35% to 64% throughput improvements** depending on column count and record volume.
- **JSONL (`ToText`)**: Uses pre-allocated backing `MemoryStream` sizes (`count * 128`). Delivers up to **1.9x speedup** on 100k records (taking only **17.01 ms** vs **32.73 ms** for standard reflection-based writing) with highly stable, predictable memory footprints.

---

## 4. Core Architectural Pillars & Memory Profile

- **Allocation-Free Hot Path**: HeroParser maintains a fixed allocation of **only 152 bytes** in its reading path, regardless of column counts or row counts, representing a **96% memory reduction** compared to Sep and **99.7% reduction** compared to Sylvan.
- **Scan-Ahead Row Batches**: one SIMD pass records the column ends of a batch of rows into a pooled buffer; advancing to the next row is index arithmetic rather than re-entering the parser. This removed the per-row fixed cost that dominated at 5-25 columns.
- **AVX-512 & AVX2 Quote-Aware SIMD**: Uses branchless PCLMULQDQ carry-less multiplication to mask quotes at maximum hardware throughput; chunks with quotes but no line ending are resolved inline.
- **UTF-16 Pack-and-Saturate**: each chunk of chars is packed into one byte vector with saturation and shares the UTF-8 dispatch, so `string` input runs at byte-path speed.

---

## Public Versioned Benchmark Portal

We host our benchmark results publicly to ensure complete transparency. The portal maintains reports for the **last 3 stable releases** so that consumers can track performance evolution:

* **Live URL**: [HeroParser Performance Portal](https://KoalaFacts.github.io/HeroParser/)

---

## How to Trigger the Benchmark Pages Deploy Manually

While the benchmarks deploy automatically upon successful release creation, you can trigger the pipeline manually at any time to update or republish reports:

1. Navigate to your **GitHub Repository** page on GitHub.
2. Click on the **Actions** tab at the top.
3. In the left-side workflow navigation list, click **"Deploy Performance Benchmarks to Pages"**.
4. Click the **"Run workflow"** dropdown button on the right.
5. *(Optional)* Provide a specific version tag in the **"Override version to deploy"** field (e.g. `2.3.0`), or leave it blank to auto-detect from the current version.
6. Click the green **"Run workflow"** button to start execution.

---

## How to Run Benchmarks Locally

To execute these benchmarks on your local hardware:

```bash
dotnet run -c Release --project benchmarks/HeroParser.Benchmarks --framework net10.0 -- --vs-sep-reading
```
