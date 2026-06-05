```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.300
  [Host]     : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3
  Job-INMAZI : .NET 10.0.8 (10.0.8, 10.0.826.23019), X64 RyuJIT x86-64-v3

IterationCount=5  RunStrategy=Throughput  WarmupCount=3  

```
| Method                         | Rows   | Mean      | Error      | StdDev    | Ratio | RatioSD | Gen0      | Gen1      | Gen2      | Allocated  | Alloc Ratio |
|------------------------------- |------- |----------:|-----------:|----------:|------:|--------:|----------:|----------:|----------:|-----------:|------------:|
| **ReadFromText**                   | **10000**  |  **4.106 ms** |  **0.1190 ms** | **0.0309 ms** |  **1.00** |    **0.01** |  **195.3125** |   **78.1250** |   **78.1250** |  **1613990 B** |        **1.00** |
| ReadFromStream                 | 10000  |  3.768 ms |  0.0518 ms | 0.0135 ms |  0.92 |    0.01 |  125.0000 |         - |         - |   968520 B |        0.60 |
| ReadFromFileAsync              | 10000  |  8.040 ms |  0.5231 ms | 0.1358 ms |  1.96 |    0.03 |  187.5000 |         - |         - |  1878152 B |        1.16 |
| WriteToText                    | 10000  |  3.699 ms |  0.3531 ms | 0.0917 ms |  0.90 |    0.02 |  296.8750 |  277.3438 |  277.3438 |  3389118 B |        2.10 |
| WriteToStream                  | 10000  |  2.593 ms |  0.0224 ms | 0.0035 ms |  0.63 |    0.00 |   66.4063 |   66.4063 |   66.4063 |          - |        0.00 |
| ReadFromText_SourceGenerated   | 10000  |  3.016 ms |  0.1271 ms | 0.0330 ms |  0.73 |    0.01 |  113.2813 |   66.4063 |   66.4063 |          - |        0.00 |
| ReadFromStream_SourceGenerated | 10000  |  2.719 ms |  0.0144 ms | 0.0037 ms |  0.66 |    0.00 |   46.8750 |         - |         - |   488520 B |        0.30 |
| WriteToText_SourceGenerated    | 10000  |  3.196 ms |  0.4654 ms | 0.1209 ms |  0.78 |    0.03 |  285.1563 |  265.6250 |  265.6250 |  3389036 B |        2.10 |
| WriteToStream_SourceGenerated  | 10000  |  2.024 ms |  0.0771 ms | 0.0119 ms |  0.49 |    0.00 |   62.5000 |   62.5000 |   62.5000 |   646608 B |        0.40 |
| ConvertCsvToJsonlFlat          | 10000  |  5.066 ms |  0.4491 ms | 0.1166 ms |  1.23 |    0.03 |  531.2500 |  312.5000 |  312.5000 |  5546367 B |        3.44 |
| ConvertJsonlToCsv              | 10000  |  8.297 ms |  0.4398 ms | 0.1142 ms |  2.02 |    0.03 |  359.3750 |  203.1250 |  125.0000 |  5910367 B |        3.66 |
|                                |        |           |            |           |       |         |           |           |           |            |             |
| **ReadFromText**                   | **100000** | **40.692 ms** |  **1.6533 ms** | **0.4294 ms** |  **1.00** |    **0.01** | **1916.6667** |  **916.6667** |  **916.6667** | **17083801 B** |        **1.00** |
| ReadFromStream                 | 100000 | 36.977 ms |  0.3920 ms | 0.1018 ms |  0.91 |    0.01 | 1000.0000 |         - |         - | 10328520 B |        0.60 |
| ReadFromFileAsync              | 100000 | 90.763 ms | 36.4588 ms | 9.4682 ms |  2.23 |    0.21 | 1500.0000 |         - |         - | 19878368 B |        1.16 |
| WriteToText                    | 100000 | 31.009 ms |  0.6463 ms | 0.1678 ms |  0.76 |    0.01 |  531.2500 |  531.2500 |  531.2500 | 30286859 B |        1.77 |
| WriteToStream                  | 100000 | 24.358 ms |  0.2618 ms | 0.0680 ms |  0.60 |    0.01 |  187.5000 |  187.5000 |  187.5000 |  6755668 B |        0.40 |
| ReadFromText_SourceGenerated   | 100000 | 30.848 ms |  0.5846 ms | 0.1518 ms |  0.76 |    0.01 |  875.0000 |  437.5000 |  437.5000 | 11562748 B |        0.68 |
| ReadFromStream_SourceGenerated | 100000 | 27.732 ms |  0.4410 ms | 0.1145 ms |  0.68 |    0.01 |  468.7500 |         - |         - |  4808520 B |        0.28 |
| WriteToText_SourceGenerated    | 100000 | 25.682 ms |  1.5267 ms | 0.3965 ms |  0.63 |    0.01 |  531.2500 |  531.2500 |  531.2500 | 30287331 B |        1.77 |
| WriteToStream_SourceGenerated  | 100000 | 19.238 ms |  0.3097 ms | 0.0804 ms |  0.47 |    0.00 |  187.5000 |  187.5000 |  187.5000 |  6755824 B |        0.40 |
| ConvertCsvToJsonlFlat          | 100000 |        NA |         NA |        NA |     ? |       ? |        NA |        NA |        NA |         NA |           ? |
| ConvertJsonlToCsv              | 100000 | 85.124 ms |  8.5229 ms | 1.3189 ms |  2.09 |    0.04 | 2000.0000 | 1000.0000 | 1000.0000 | 61567096 B |        3.60 |

Benchmarks with issues:
  JsonlBenchmark.ConvertCsvToJsonlFlat: Job-INMAZI(IterationCount=5, RunStrategy=Throughput, WarmupCount=3) [Rows=100000]
