```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.401
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  Job-INMAZI : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

IterationCount=5  RunStrategy=Throughput  WarmupCount=3  

```
| Method                         | Rows   | Mean      | Error      | StdDev    | Ratio | RatioSD | Gen0      | Gen1      | Gen2      | Allocated  | Alloc Ratio |
|------------------------------- |------- |----------:|-----------:|----------:|------:|--------:|----------:|----------:|----------:|-----------:|------------:|
| **ReadFromText**                   | **10000**  |  **4.096 ms** |  **0.1485 ms** | **0.0386 ms** |  **1.00** |    **0.01** |  **164.0625** |   **70.3125** |   **70.3125** |  **1613992 B** |        **1.00** |
| ReadFromStream                 | 10000  |  3.692 ms |  0.0710 ms | 0.0184 ms |  0.90 |    0.01 |   93.7500 |         - |         - |   968520 B |        0.60 |
| ReadFromFileAsync              | 10000  |  8.103 ms |  0.5041 ms | 0.1309 ms |  1.98 |    0.03 |  187.5000 |         - |         - |  1878142 B |        1.16 |
| WriteToText                    | 10000  |  3.155 ms |  0.1506 ms | 0.0391 ms |  0.77 |    0.01 |  191.4063 |  191.4063 |  191.4063 |  2572964 B |        1.59 |
| WriteToStream                  | 10000  |  2.593 ms |  0.1267 ms | 0.0329 ms |  0.63 |    0.01 |   62.5000 |   62.5000 |   62.5000 |          - |        0.00 |
| ReadFromText_SourceGenerated   | 10000  |  3.087 ms |  0.0993 ms | 0.0258 ms |  0.75 |    0.01 |  113.2813 |   70.3125 |   70.3125 |  3281383 B |        2.03 |
| ReadFromStream_SourceGenerated | 10000  |  2.726 ms |  0.0295 ms | 0.0077 ms |  0.67 |    0.01 |   46.8750 |         - |         - |   488520 B |        0.30 |
| WriteToText_SourceGenerated    | 10000  |  2.759 ms |  0.2449 ms | 0.0379 ms |  0.67 |    0.01 |  199.2188 |  199.2188 |  199.2188 |  2572990 B |        1.59 |
| WriteToStream_SourceGenerated  | 10000  |  2.082 ms |  0.0960 ms | 0.0149 ms |  0.51 |    0.01 |   66.4063 |   66.4063 |   66.4063 |   646719 B |        0.40 |
| ConvertCsvToJsonlFlat          | 10000  |  4.052 ms |  0.3820 ms | 0.0992 ms |  0.99 |    0.02 |  320.3125 |  304.6875 |  304.6875 |  3509241 B |        2.17 |
| ConvertJsonlToCsv              | 10000  |  8.376 ms |  0.3664 ms | 0.0952 ms |  2.05 |    0.03 |  390.6250 |  203.1250 |  140.6250 |  5910470 B |        3.66 |
|                                |        |           |            |           |       |         |           |           |           |            |             |
| **ReadFromText**                   | **100000** | **42.513 ms** |  **1.7623 ms** | **0.2727 ms** |  **1.00** |    **0.01** | **1916.6667** |  **916.6667** |  **916.6667** | **17084294 B** |        **1.00** |
| ReadFromStream                 | 100000 | 38.687 ms |  0.5892 ms | 0.1530 ms |  0.91 |    0.01 | 1000.0000 |         - |         - | 10328520 B |        0.60 |
| ReadFromFileAsync              | 100000 | 86.465 ms | 10.4676 ms | 2.7184 ms |  2.03 |    0.06 | 1500.0000 |         - |         - | 19878260 B |        1.16 |
| WriteToText                    | 100000 | 29.271 ms |  0.9534 ms | 0.1475 ms |  0.69 |    0.01 |  343.7500 |  343.7500 |  343.7500 | 26309977 B |        1.54 |
| WriteToStream                  | 100000 | 24.627 ms |  0.1749 ms | 0.0271 ms |  0.58 |    0.00 |  187.5000 |  187.5000 |  187.5000 |  6756096 B |        0.40 |
| ReadFromText_SourceGenerated   | 100000 | 30.757 ms |  0.5024 ms | 0.1305 ms |  0.72 |    0.00 |  937.5000 |  468.7500 |  468.7500 | 11563457 B |        0.68 |
| ReadFromStream_SourceGenerated | 100000 | 27.898 ms |  0.3195 ms | 0.0494 ms |  0.66 |    0.00 |  468.7500 |         - |         - |  4808520 B |        0.28 |
| WriteToText_SourceGenerated    | 100000 | 24.986 ms |  1.1954 ms | 0.3104 ms |  0.59 |    0.01 |  375.0000 |  375.0000 |  375.0000 | 26309936 B |        1.54 |
| WriteToStream_SourceGenerated  | 100000 | 19.786 ms |  0.5446 ms | 0.1414 ms |  0.47 |    0.00 |  187.5000 |  187.5000 |  187.5000 |  6755960 B |        0.40 |
| ConvertCsvToJsonlFlat          | 100000 |        NA |         NA |        NA |     ? |       ? |        NA |        NA |        NA |         NA |           ? |
| ConvertJsonlToCsv              | 100000 | 87.520 ms |  5.0449 ms | 0.7807 ms |  2.06 |    0.02 | 2000.0000 | 1000.0000 | 1000.0000 | 61573064 B |        3.60 |

Benchmarks with issues:
  JsonlBenchmark.ConvertCsvToJsonlFlat: Job-INMAZI(IterationCount=5, RunStrategy=Throughput, WarmupCount=3) [Rows=100000]
