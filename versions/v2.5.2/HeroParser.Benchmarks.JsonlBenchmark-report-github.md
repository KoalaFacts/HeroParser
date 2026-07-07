```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
AMD EPYC 7763 2.45GHz, 1 CPU, 4 logical and 2 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v3
  Job-INMAZI : .NET 10.0.9 (10.0.9, 10.0.926.27113), X64 RyuJIT x86-64-v3

IterationCount=5  RunStrategy=Throughput  WarmupCount=3  

```
| Method                         | Rows   | Mean      | Error      | StdDev    | Ratio | RatioSD | Gen0      | Gen1      | Gen2      | Allocated  | Alloc Ratio |
|------------------------------- |------- |----------:|-----------:|----------:|------:|--------:|----------:|----------:|----------:|-----------:|------------:|
| **ReadFromText**                   | **10000**  |  **4.110 ms** |  **0.1921 ms** | **0.0499 ms** |  **1.00** |    **0.02** |  **171.8750** |   **78.1250** |   **78.1250** |  **1614087 B** |        **1.00** |
| ReadFromStream                 | 10000  |  3.830 ms |  0.0631 ms | 0.0164 ms |  0.93 |    0.01 |   97.6563 |    3.9063 |    3.9063 |  3347762 B |        2.07 |
| ReadFromFileAsync              | 10000  |  8.382 ms |  0.6411 ms | 0.1665 ms |  2.04 |    0.04 |  187.5000 |         - |         - |  1878162 B |        1.16 |
| WriteToText                    | 10000  |  3.288 ms |  0.1198 ms | 0.0311 ms |  0.80 |    0.01 |  195.3125 |  195.3125 |  195.3125 |  2572902 B |        1.59 |
| WriteToStream                  | 10000  |  2.612 ms |  0.0223 ms | 0.0058 ms |  0.64 |    0.01 |   62.5000 |   62.5000 |   62.5000 |          - |        0.00 |
| ReadFromText_SourceGenerated   | 10000  |  3.072 ms |  0.0945 ms | 0.0146 ms |  0.75 |    0.01 |  117.1875 |   70.3125 |   70.3125 |  3653736 B |        2.26 |
| ReadFromStream_SourceGenerated | 10000  |  2.774 ms |  0.0210 ms | 0.0054 ms |  0.68 |    0.01 |   46.8750 |         - |         - |   488520 B |        0.30 |
| WriteToText_SourceGenerated    | 10000  |  2.693 ms |  0.1596 ms | 0.0247 ms |  0.66 |    0.01 |  191.4063 |  191.4063 |  191.4063 |  2572922 B |        1.59 |
| WriteToStream_SourceGenerated  | 10000  |  2.067 ms |  0.0613 ms | 0.0159 ms |  0.50 |    0.01 |   62.5000 |   62.5000 |   62.5000 |   646682 B |        0.40 |
| ConvertCsvToJsonlFlat          | 10000  |  4.578 ms |  0.0477 ms | 0.0124 ms |  1.11 |    0.01 |  320.3125 |  296.8750 |  296.8750 |  3509082 B |        2.17 |
| ConvertJsonlToCsv              | 10000  |  8.173 ms |  0.4090 ms | 0.1062 ms |  1.99 |    0.03 |  375.0000 |  203.1250 |  140.6250 |          - |        0.00 |
|                                |        |           |            |           |       |         |           |           |           |            |             |
| **ReadFromText**                   | **100000** | **43.562 ms** |  **1.4883 ms** | **0.2303 ms** |  **1.00** |    **0.01** | **1916.6667** |  **916.6667** |  **916.6667** | **17083409 B** |        **1.00** |
| ReadFromStream                 | 100000 | 39.206 ms |  0.4608 ms | 0.1197 ms |  0.90 |    0.00 | 1000.0000 |         - |         - | 10328520 B |        0.60 |
| ReadFromFileAsync              | 100000 | 89.330 ms | 62.6442 ms | 9.6943 ms |  2.05 |    0.20 | 2000.0000 |         - |         - | 19878368 B |        1.16 |
| WriteToText                    | 100000 | 27.100 ms |  0.6430 ms | 0.1670 ms |  0.62 |    0.00 |  343.7500 |  343.7500 |  343.7500 | 26309752 B |        1.54 |
| WriteToStream                  | 100000 | 26.243 ms |  0.4755 ms | 0.0736 ms |  0.60 |    0.00 |  187.5000 |  187.5000 |  187.5000 |  6756086 B |        0.40 |
| ReadFromText_SourceGenerated   | 100000 | 30.607 ms |  0.9854 ms | 0.2559 ms |  0.70 |    0.01 |  906.2500 |  437.5000 |  437.5000 |          - |        0.00 |
| ReadFromStream_SourceGenerated | 100000 | 28.000 ms |  0.0825 ms | 0.0214 ms |  0.64 |    0.00 |  468.7500 |         - |         - |  4808520 B |        0.28 |
| WriteToText_SourceGenerated    | 100000 | 21.978 ms |  0.9991 ms | 0.1546 ms |  0.50 |    0.00 |  375.0000 |  375.0000 |  375.0000 | 26310144 B |        1.54 |
| WriteToStream_SourceGenerated  | 100000 | 20.293 ms |  0.2080 ms | 0.0540 ms |  0.47 |    0.00 |  187.5000 |  187.5000 |  187.5000 |  6755912 B |        0.40 |
| ConvertCsvToJsonlFlat          | 100000 |        NA |         NA |        NA |     ? |       ? |        NA |        NA |        NA |         NA |           ? |
| ConvertJsonlToCsv              | 100000 | 87.970 ms |  7.8568 ms | 2.0404 ms |  2.02 |    0.04 | 2000.0000 | 1000.0000 | 1000.0000 | 61574072 B |        3.60 |

Benchmarks with issues:
  JsonlBenchmark.ConvertCsvToJsonlFlat: Job-INMAZI(IterationCount=5, RunStrategy=Throughput, WarmupCount=3) [Rows=100000]
