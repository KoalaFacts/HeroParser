```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-INMAZI : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

IterationCount=5  RunStrategy=Throughput  WarmupCount=3  

```
| Method                         | Rows   | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0      | Gen1      | Gen2      | Allocated   | Alloc Ratio |
|------------------------------- |------- |----------:|----------:|----------:|------:|--------:|----------:|----------:|----------:|------------:|------------:|
| **ReadFromText**                   | **10000**  |  **4.285 ms** | **0.2435 ms** | **0.0377 ms** |  **1.00** |    **0.01** |  **156.2500** |   **78.1250** |   **78.1250** |   **1614114 B** |        **1.00** |
| ReadFromStream                 | 10000  |  3.652 ms | 0.0541 ms | 0.0084 ms |  0.85 |    0.01 |  125.0000 |         - |         - |    968520 B |        0.60 |
| ReadFromFileAsync              | 10000  |  8.461 ms | 0.9849 ms | 0.2558 ms |  1.97 |    0.06 |  187.5000 |         - |         - |   1878152 B |        1.16 |
| WriteToText                    | 10000  |  3.345 ms | 0.1772 ms | 0.0460 ms |  0.78 |    0.01 |  199.2188 |  199.2188 |  199.2188 |   2573040 B |        1.59 |
| WriteToStream                  | 10000  |  2.797 ms | 0.1331 ms | 0.0346 ms |  0.65 |    0.01 |   62.5000 |   62.5000 |   62.5000 |           - |        0.00 |
| ReadFromText_SourceGenerated   | 10000  |  3.118 ms | 0.1519 ms | 0.0394 ms |  0.73 |    0.01 |  148.4375 |   85.9375 |   85.9375 |   4031016 B |        2.50 |
| ReadFromStream_SourceGenerated | 10000  |  2.730 ms | 0.0224 ms | 0.0058 ms |  0.64 |    0.01 |   46.8750 |         - |         - |    488520 B |        0.30 |
| WriteToText_SourceGenerated    | 10000  |  2.867 ms | 0.1069 ms | 0.0278 ms |  0.67 |    0.01 |  195.3125 |  195.3125 |  195.3125 |   2572836 B |        1.59 |
| WriteToStream_SourceGenerated  | 10000  |  2.047 ms | 0.1669 ms | 0.0258 ms |  0.48 |    0.01 |   62.5000 |   62.5000 |   62.5000 |    646754 B |        0.40 |
| ConvertCsvToJsonlFlat          | 10000  |  4.167 ms | 0.4607 ms | 0.0713 ms |  0.97 |    0.02 |  320.3125 |  296.8750 |  296.8750 |   3509178 B |        2.17 |
| ConvertJsonlToCsv              | 10000  |  8.234 ms | 0.4168 ms | 0.1082 ms |  1.92 |    0.03 |  359.3750 |  234.3750 |  140.6250 |   5910289 B |        3.66 |
|                                |        |           |           |           |       |         |           |           |           |             |             |
| **ReadFromText**                   | **100000** | **42.693 ms** | **2.2969 ms** | **0.5965 ms** |  **1.00** |    **0.02** | **1000.0000** |  **500.0000** |  **500.0000** |  **17082965 B** |        **1.00** |
| ReadFromStream                 | 100000 | 39.343 ms | 1.1513 ms | 0.2990 ms |  0.92 |    0.01 | 1000.0000 |         - |         - |  10328520 B |        0.60 |
| ReadFromFileAsync              | 100000 | 90.132 ms | 7.0174 ms | 1.8224 ms |  2.11 |    0.05 | 1500.0000 |         - |         - |  19878260 B |        1.16 |
| WriteToText                    | 100000 | 28.135 ms | 1.6910 ms | 0.4391 ms |  0.66 |    0.01 |  343.7500 |  343.7500 |  343.7500 | 111867508 B |        6.55 |
| WriteToStream                  | 100000 | 26.123 ms | 0.1070 ms | 0.0278 ms |  0.61 |    0.01 |  187.5000 |  187.5000 |  187.5000 |   6755991 B |        0.40 |
| ReadFromText_SourceGenerated   | 100000 | 30.800 ms | 1.1276 ms | 0.2928 ms |  0.72 |    0.01 |  875.0000 |  437.5000 |  437.5000 |  11563896 B |        0.68 |
| ReadFromStream_SourceGenerated | 100000 | 29.092 ms | 0.3950 ms | 0.0611 ms |  0.68 |    0.01 |  468.7500 |         - |         - |   4808520 B |        0.28 |
| WriteToText_SourceGenerated    | 100000 | 24.086 ms | 0.6137 ms | 0.1594 ms |  0.56 |    0.01 |  343.7500 |  343.7500 |  343.7500 |  26310187 B |        1.54 |
| WriteToStream_SourceGenerated  | 100000 | 20.192 ms | 0.3553 ms | 0.0550 ms |  0.47 |    0.01 |  187.5000 |  187.5000 |  187.5000 |   6755945 B |        0.40 |
| ConvertCsvToJsonlFlat          | 100000 |        NA |        NA |        NA |     ? |       ? |        NA |        NA |        NA |          NA |           ? |
| ConvertJsonlToCsv              | 100000 | 90.666 ms | 5.5525 ms | 1.4420 ms |  2.12 |    0.04 | 2000.0000 | 1000.0000 | 1000.0000 |  61574072 B |        3.60 |

Benchmarks with issues:
  JsonlBenchmark.ConvertCsvToJsonlFlat: Job-INMAZI(IterationCount=5, RunStrategy=Throughput, WarmupCount=3) [Rows=100000]
